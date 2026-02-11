using AgentLogs.Domain;
using AgentLogs.Services;
using AgentTrace.Services;
using Markout;

namespace AgentTrace.Commands;

/// <summary>
/// Selects a slice of turns: either the last N or a 1-indexed range M..N.
/// </summary>
public readonly record struct TurnSlice(int? Last, int? From, int? To)
{
    public static TurnSlice Parse(string value)
    {
        var dotIdx = value.IndexOf("..", StringComparison.Ordinal);
        if (dotIdx >= 0)
        {
            var fromStr = value[..dotIdx];
            var toStr = value[(dotIdx + 2)..];
            if (int.TryParse(fromStr, out var from) && int.TryParse(toStr, out var to))
                return new TurnSlice(null, from, to);
        }

        if (int.TryParse(value, out var last))
            return new TurnSlice(last, null, null);

        return default;
    }

    public IReadOnlyList<Turn> Apply(IReadOnlyList<Turn> turns)
    {
        if (From.HasValue && To.HasValue)
        {
            // 1-indexed inclusive range → 0-indexed
            var from = Math.Max(0, From.Value - 1);
            var to = Math.Min(turns.Count, To.Value);
            if (from >= to) return [];
            return turns.Skip(from).Take(to - from).ToList();
        }

        if (Last.HasValue && Last.Value > 0 && Last.Value < turns.Count)
            return turns.Skip(turns.Count - Last.Value).ToList();

        return turns;
    }

    public string Describe()
    {
        if (From.HasValue && To.HasValue)
            return $"turns {From.Value}..{To.Value}";
        if (Last.HasValue)
            return $"last {Last.Value} turns";
        return "all turns";
    }

    public bool IsSet => Last.HasValue || (From.HasValue && To.HasValue);
}

/// <summary>
/// Dumps session data as plain text to stdout — no ANSI, no ITerminal.
/// Designed for LLM consumption and piping to head/tail/grep.
/// </summary>
public static class DumpCommand
{
    /// <summary>
    /// Prints a markdown session list via Markout.
    /// </summary>
    public static void ListSessionsMarkdown(SessionManager sessionManager, string? projectFilter = null, string? projectDir = null, BookmarkStore? bookmarkStore = null)
    {
        var sessions = FilterSessions(sessionManager, projectFilter);
        var bookmarks = bookmarkStore?.Load();

        if (bookmarks != null)
            sessions = sessions.Where(s => bookmarks.Contains(s.Id)).ToList();

        var writer = new MarkoutWriter(Console.Out);

        var heading = projectDir != null
            ? $"Sessions ({sessions.Count})"
            : $"All Sessions ({sessions.Count})";
        writer.WriteHeading(1, heading);

        if (projectDir != null)
            writer.WriteField("Scope", projectDir);

        if (bookmarks != null)
            writer.WriteTableStart("★", "ID", "Status", "Project", "Messages", "Tools", "Duration", "Date");
        else
            writer.WriteTableStart("ID", "Status", "Project", "Messages", "Tools", "Duration", "Date");

        foreach (var session in sessions)
        {
            var shortId = session.Id.Length > 7 ? session.Id[..7] : session.Id;
            var status = session.IsActive ? "active" : "done";
            var project = session.ProjectName ?? "Unknown";
            var messages = $"{session.UserMessageCount}u/{session.AssistantMessageCount}a";
            var duration = FormatDuration(session.Duration);
            var date = session.StartTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

            if (bookmarks != null)
                writer.WriteTableRow("★", shortId, status, project, messages, session.ToolCallCount.ToString(), duration, date);
            else
                writer.WriteTableRow(shortId, status, project, messages, session.ToolCallCount.ToString(), duration, date);
        }

        writer.WriteTableEnd();
        writer.Flush();
    }

    /// <summary>
    /// Prints a tab-separated session list (original format).
    /// </summary>
    public static void ListSessionsTsv(SessionManager sessionManager, string? projectFilter = null, BookmarkStore? bookmarkStore = null)
    {
        var sessions = FilterSessions(sessionManager, projectFilter);
        var bookmarks = bookmarkStore?.Load();

        if (bookmarks != null)
            sessions = sessions.Where(s => bookmarks.Contains(s.Id)).ToList();

        if (bookmarks != null)
            Console.Out.WriteLine("★\tID\tStatus\tProject\tMessages\tTools\tDuration\tDate");
        else
            Console.Out.WriteLine("ID\tStatus\tProject\tMessages\tTools\tDuration\tDate");

        foreach (var session in sessions)
        {
            var status = session.IsActive ? "active" : "done";
            var project = session.ProjectName ?? "Unknown";
            var messages = $"{session.UserMessageCount}u/{session.AssistantMessageCount}a";
            var duration = FormatDuration(session.Duration);
            var date = session.StartTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

            if (bookmarks != null)
                Console.Out.WriteLine($"★\t{session.Id}\t{status}\t{project}\t{messages}\t{session.ToolCallCount}\t{duration}\t{date}");
            else
                Console.Out.WriteLine($"{session.Id}\t{status}\t{project}\t{messages}\t{session.ToolCallCount}\t{duration}\t{date}");
        }
    }

    /// <summary>
    /// Prints session metadata as markdown without dumping content.
    /// </summary>
    public static void PrintInfo(SessionManager sessionManager, string sessionId, TurnSlice turnSlice = default)
    {
        var (session, conversation) = ResolveSession(sessionManager, sessionId);
        if (session == null || conversation == null)
            return;

        // Count dump lines by rendering to a counting writer
        var lineCount = CountDumpLines(session, conversation, turnSlice);

        var writer = new MarkoutWriter(Console.Out);
        var shortId = session.Id.Length > 7 ? session.Id[..7] : session.Id;
        writer.WriteHeading(1, $"Session {shortId}");

        writer.WriteField("ID", session.Id);
        writer.WriteField("Project", session.ProjectName ?? "Unknown");
        writer.WriteField("Status", session.IsActive ? "active" : "done");
        writer.WriteField("Started", session.StartTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
        writer.WriteField("Duration", FormatDuration(session.Duration));
        writer.WriteField("Turns", conversation.Turns.Count);
        writer.WriteField("Messages", $"{session.UserMessageCount}u/{session.AssistantMessageCount}a");
        writer.WriteField("Tool calls", session.ToolCallCount);
        writer.WriteField("Lines", $"{lineCount} (estimated from dump)");

        writer.Flush();
    }

    /// <summary>
    /// Prints the full conversation for a session as plain text to stdout.
    /// Supports --head, --tail, and --turns options.
    /// </summary>
    public static void PrintConversation(SessionManager sessionManager, string sessionId,
        int? headLines = null, int? tailLines = null, TurnSlice turnSlice = default,
        string? speakerFilter = null, bool compact = false)
    {
        var (session, conversation) = ResolveSession(sessionManager, sessionId);
        if (session == null || conversation == null)
            return;

        // Determine which turns to print
        var turns = turnSlice.Apply(conversation.Turns);

        // Header via Markout
        var writer = new MarkoutWriter(Console.Out);
        var shortId = session.Id.Length > 7 ? session.Id[..7] : session.Id;
        writer.WriteHeading(1, $"Session {shortId}");
        writer.WriteField("Project", session.ProjectName ?? "Unknown");
        writer.WriteField("Started", session.StartTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
        writer.WriteField("Turns", conversation.Turns.Count);
        if (turnSlice.IsSet)
            writer.WriteField("Showing", $"{turnSlice.Describe()} ({turns.Count} shown)");
        writer.Flush();
        Console.Out.WriteLine();

        // Handle --head: write to a line-counting wrapper that stops after N lines
        if (headLines.HasValue)
        {
            var remaining = headLines.Value;
            using var limited = new LineCountingWriter(Console.Out, remaining);
            try
            {
                foreach (var turn in turns)
                    PrintTurn(turn, limited, speakerFilter, compact);
            }
            catch (LineLimitReachedException)
            {
                // Expected — we hit the limit
            }
            return;
        }

        // Handle --tail: render to buffer, then output last N lines
        if (tailLines.HasValue)
        {
            using var buffer = new StringWriter();
            foreach (var turn in turns)
                PrintTurn(turn, buffer, speakerFilter, compact);

            var allLines = buffer.ToString().Split('\n');
            var start = Math.Max(0, allLines.Length - tailLines.Value);
            for (var i = start; i < allLines.Length; i++)
                Console.Out.WriteLine(allLines[i]);
            return;
        }

        // Default: print all turns
        foreach (var turn in turns)
            PrintTurn(turn, Console.Out, speakerFilter, compact);
    }

    /// <summary>
    /// Renders a conversation to a TextWriter — used by PrintConversation and SummaryCommand.
    /// </summary>
    public static void RenderConversation(Session session, Conversation conversation, TextWriter output,
        TurnSlice turnSlice = default, string? speakerFilter = null, bool compact = false)
    {
        var turns = turnSlice.Apply(conversation.Turns);

        var writer = new MarkoutWriter(output);
        var shortId = session.Id.Length > 7 ? session.Id[..7] : session.Id;
        writer.WriteHeading(1, $"Session {shortId}");
        writer.WriteField("Project", session.ProjectName ?? "Unknown");
        writer.WriteField("Started", session.StartTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
        writer.WriteField("Turns", conversation.Turns.Count);
        writer.Flush();
        output.WriteLine();

        foreach (var turn in turns)
            PrintTurn(turn, output, speakerFilter, compact);
    }

    /// <summary>
    /// Resolves a session ID (with prefix matching) to session + conversation.
    /// </summary>
    public static (Session? Session, Conversation? Conversation) ResolveSession(SessionManager sessionManager, string sessionId)
    {
        var entries = sessionManager.GetSessionEntries(sessionId);

        if (entries.Count == 0)
        {
            var match = sessionManager.GetAllSessions()
                .FirstOrDefault(s => s.Id.StartsWith(sessionId, StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                entries = sessionManager.GetSessionEntries(match.Id);
                sessionId = match.Id;
            }
        }

        if (entries.Count == 0)
        {
            Console.Error.WriteLine($"Session not found: {sessionId}");
            return (null, null);
        }

        var session = sessionManager.GetSession(sessionId);
        var conversation = new Conversation(sessionId, entries);
        return (session, conversation);
    }

    /// <summary>
    /// Prints a table of contents — one line per turn.
    /// </summary>
    public static void PrintToc(SessionManager sessionManager, string sessionId)
    {
        var (session, conversation) = ResolveSession(sessionManager, sessionId);
        if (session == null || conversation == null)
            return;

        var writer = new MarkoutWriter(Console.Out);
        var shortId = session.Id.Length > 7 ? session.Id[..7] : session.Id;
        writer.WriteHeading(1, $"Session {shortId} — Table of Contents");

        writer.WriteTableStart("Turn", "Messages", "Tools", "Duration", "Content");

        foreach (var turn in conversation.Turns)
        {
            var turnNum = (turn.Number + 1).ToString();
            var userCount = turn.Entries.OfType<UserEntry>().Count();
            var assistantCount = turn.Entries.OfType<AssistantEntry>().Count();
            var messages = $"{userCount}u/{assistantCount}a";
            var tools = turn.ToolUses.Count.ToString();
            var duration = FormatDuration(turn.Duration);

            // Content preview: first user message, or first assistant text
            var preview = turn.UserMessage;
            if (string.IsNullOrWhiteSpace(preview))
            {
                preview = turn.AssistantMessages
                    .Select(a => a.TextContent)
                    .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
            }
            preview = Truncate(preview ?? "", 60);

            writer.WriteTableRow(turnNum, messages, tools, duration, preview);
        }

        writer.WriteTableEnd();
        writer.Flush();
    }

    private static string Truncate(string text, int maxLength)
    {
        // Collapse to single line
        var singleLine = text.ReplaceLineEndings(" ");
        if (singleLine.Length <= maxLength)
            return singleLine;
        return string.Concat(singleLine.AsSpan(0, maxLength), "...");
    }

    private static void PrintTurn(Turn turn, TextWriter output, string? speakerFilter = null, bool compact = false)
    {
        output.WriteLine($"--- Turn {turn.Number + 1} ---");

        foreach (var entry in turn.Entries)
        {
            switch (entry)
            {
                case UserEntry user when speakerFilter is null or "user":
                    PrintUserEntry(user, output, compact);
                    break;
                case AssistantEntry assistant when speakerFilter is null or "assistant":
                    PrintAssistantEntry(assistant, output, speakerFilter);
                    break;
                case SystemEntry system when speakerFilter is null:
                    if (!string.IsNullOrWhiteSpace(system.Content))
                    {
                        output.WriteLine("[system]");
                        output.WriteLine(system.Content);
                        output.WriteLine();
                    }
                    break;
                case SummaryEntry summary when speakerFilter is null:
                    output.WriteLine("[summary]");
                    output.WriteLine(summary.Summary);
                    output.WriteLine();
                    break;
            }
        }
    }

    private static void PrintUserEntry(UserEntry user, TextWriter output, bool compact = false)
    {
        if (!string.IsNullOrWhiteSpace(user.Content))
        {
            output.WriteLine("[user]");
            output.WriteLine(user.Content);
            output.WriteLine();
        }

        foreach (var block in user.ContentBlocks)
        {
            if (block is ToolResultBlock result)
            {
                var status = result.IsError ? "ERROR" : "OK";
                var name = result.ToolName ?? "tool";

                if (compact && result.Content != null && result.Content.Length > 200)
                {
                    var firstLine = result.Content.AsSpan();
                    var newlineIdx = firstLine.IndexOf('\n');
                    if (newlineIdx >= 0) firstLine = firstLine[..newlineIdx];
                    if (firstLine.Length > 60) firstLine = firstLine[..60];
                    output.WriteLine($"  < {name} [{status}] ({result.Content.Length:N0} chars) {firstLine.TrimEnd()}...");
                    continue;
                }

                output.WriteLine($"  < {name} [{status}]");

                if (result.Content != null)
                {
                    foreach (var line in result.Content.Split('\n'))
                    {
                        output.WriteLine($"    {line}");
                    }
                }
            }
        }
    }

    private static void PrintAssistantEntry(AssistantEntry assistant, TextWriter output, string? speakerFilter = null)
    {
        var assistantOnly = speakerFilter == "assistant";

        var hasContent = (!assistantOnly && assistant.ThinkingBlocks.Count > 0)
            || !string.IsNullOrWhiteSpace(assistant.TextContent)
            || (!assistantOnly && assistant.ToolUses.Count > 0);

        if (!hasContent)
            return;

        output.WriteLine("[assistant]");

        if (!assistantOnly)
        {
            foreach (var thinking in assistant.ThinkingBlocks)
            {
                output.WriteLine($"  [thinking: {thinking.CharCount:N0} chars]");
            }
        }

        if (!string.IsNullOrWhiteSpace(assistant.TextContent))
        {
            output.WriteLine(assistant.TextContent);
        }

        if (!assistantOnly)
        {
            foreach (var tool in assistant.ToolUses)
            {
                PrintToolUse(tool, output);
            }
        }

        output.WriteLine();
    }

    private static void PrintToolUse(ToolUse tool, TextWriter output)
    {
        var target = tool.FilePath ?? tool.Command ?? "";
        output.WriteLine($"  > {tool.Name} ({target})");

        switch (tool.Name.ToLowerInvariant())
        {
            case "edit":
                if (!string.IsNullOrEmpty(tool.OldContent))
                {
                    foreach (var line in tool.OldContent.Split('\n'))
                        output.WriteLine($"    - {line}");
                }
                if (!string.IsNullOrEmpty(tool.Content))
                {
                    foreach (var line in tool.Content.Split('\n'))
                        output.WriteLine($"    + {line}");
                }
                break;

            case "write":
                if (!string.IsNullOrEmpty(tool.Content))
                {
                    output.WriteLine("    [NEW FILE]");
                    foreach (var line in tool.Content.Split('\n'))
                        output.WriteLine($"    {line}");
                }
                break;

            case "bash":
                if (!string.IsNullOrEmpty(tool.Command))
                    output.WriteLine($"    $ {tool.Command}");
                break;
        }
    }

    private static IReadOnlyList<Session> FilterSessions(SessionManager sessionManager, string? projectFilter)
    {
        var sessions = sessionManager.GetAllSessions();

        if (projectFilter != null)
        {
            sessions = sessions
                .Where(s => s.ProjectName != null &&
                    s.ProjectName.Contains(projectFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return sessions;
    }

    private static int CountDumpLines(Session session, Conversation conversation, TurnSlice turnSlice)
    {
        using var counter = new StringWriter();
        RenderConversation(session, conversation, counter, turnSlice);
        return counter.ToString().Split('\n').Length;
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalSeconds < 60)
            return $"{(int)duration.TotalSeconds}s";
        if (duration.TotalMinutes < 60)
            return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
        return $"{(int)duration.TotalHours}h {duration.Minutes}m";
    }

    /// <summary>
    /// TextWriter that stops after a given number of lines.
    /// </summary>
    private sealed class LineCountingWriter(TextWriter inner, int maxLines) : TextWriter
    {
        private int _lineCount;

        public override System.Text.Encoding Encoding => inner.Encoding;

        public override void WriteLine(string? value)
        {
            if (_lineCount >= maxLines)
                throw new LineLimitReachedException();
            inner.WriteLine(value);
            _lineCount++;
        }

        public override void Write(string? value) => inner.Write(value);
    }

    private sealed class LineLimitReachedException : Exception;
}
