using AgentLogs.Domain;
using AgentLogs.Services;
using AgentTrace.Services;
using Markout;

namespace AgentTrace.Commands;

/// <summary>
/// Dumps session data as plain text to stdout — no ANSI, no ITerminal.
/// Designed for LLM consumption and piping to head/tail/grep.
/// </summary>
public static class DumpCommand
{
    /// <summary>
    /// Prints a markdown session list via Markout.
    /// </summary>
    public static void ListSessionsMarkdown(SessionManager sessionManager, string? projectFilter = null,
        string? projectDir = null, BookmarkStore? bookmarkStore = null, string? grepTerm = null,
        TagStore? tagStore = null, string? tagFilter = null)
    {
        var sessions = SessionHelper.FilterSessions(sessionManager, projectFilter);
        var bookmarks = bookmarkStore?.Load();
        var allTags = tagStore?.LoadAllResolved(sessions.Select(s => s.Id));

        if (bookmarks != null)
            sessions = sessions.Where(s => bookmarks.Contains(s.Id)).ToList();

        if (tagFilter != null && allTags != null)
            sessions = sessions.Where(s => allTags.TryGetValue(s.Id, out var t) && t.Contains(tagFilter, StringComparer.OrdinalIgnoreCase)).ToList();

        // Grep filtering: only show sessions containing the term
        EntryMatcher? matcher = grepTerm != null ? new EntryMatcher(grepTerm) : null;
        Dictionary<string, int>? matchCounts = null;
        if (matcher != null)
        {
            matchCounts = new Dictionary<string, int>();
            sessions = sessions.Where(s =>
            {
                var entries = sessionManager.GetSessionEntries(s.Id);
                var count = matcher.CountMatches(entries);
                if (count > 0) matchCounts[s.Id] = count;
                return count > 0;
            }).ToList();
        }

        var writer = new MarkoutWriter(Console.Out);

        var heading = projectDir != null
            ? $"Sessions ({sessions.Count})"
            : $"All Sessions ({sessions.Count})";
        writer.WriteHeading(1, heading);

        if (projectDir != null)
            writer.WriteField("Scope", projectDir);
        if (grepTerm != null)
            writer.WriteField("Grep", grepTerm);

        // Build header columns
        var headers = new List<string>();
        if (bookmarks != null) headers.Add("★");
        headers.AddRange(["ID", "Status", "Project", "Messages", "Tools", "Duration", "Date"]);
        if (allTags != null) headers.Add("Tags");
        if (matchCounts != null) headers.Add("Matches");
        writer.WriteTableStart(headers.ToArray());

        foreach (var session in sessions)
        {
            var shortId = session.Id.Length > 7 ? session.Id[..7] : session.Id;
            var status = session.IsActive ? "active" : "done";
            var project = session.ProjectName ?? "Unknown";
            var messages = $"{session.UserMessageCount}u/{session.AssistantMessageCount}a";
            var duration = Formatting.FormatDuration(session.Duration);
            var date = session.StartTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

            var row = new List<string>();
            if (bookmarks != null) row.Add("★");
            row.AddRange([shortId, status, project, messages, session.ToolCallCount.ToString(), duration, date]);
            if (allTags != null) row.Add(allTags.TryGetValue(session.Id, out var t) ? string.Join(", ", t) : "");
            if (matchCounts != null) row.Add(matchCounts.TryGetValue(session.Id, out var c) ? c.ToString() : "0");
            writer.WriteTableRow(row.ToArray());
        }

        writer.WriteTableEnd();
        writer.Flush();
    }

    /// <summary>
    /// Prints a tab-separated session list (original format).
    /// </summary>
    public static void ListSessionsTsv(SessionManager sessionManager, string? projectFilter = null,
        BookmarkStore? bookmarkStore = null, string? grepTerm = null,
        TagStore? tagStore = null, string? tagFilter = null)
    {
        var sessions = SessionHelper.FilterSessions(sessionManager, projectFilter);
        var bookmarks = bookmarkStore?.Load();
        var allTags = tagStore?.LoadAllResolved(sessions.Select(s => s.Id));

        if (bookmarks != null)
            sessions = sessions.Where(s => bookmarks.Contains(s.Id)).ToList();

        if (tagFilter != null && allTags != null)
            sessions = sessions.Where(s => allTags.TryGetValue(s.Id, out var t) && t.Contains(tagFilter, StringComparer.OrdinalIgnoreCase)).ToList();

        EntryMatcher? matcher = grepTerm != null ? new EntryMatcher(grepTerm) : null;
        Dictionary<string, int>? matchCounts = null;
        if (matcher != null)
        {
            matchCounts = new Dictionary<string, int>();
            sessions = sessions.Where(s =>
            {
                var entries = sessionManager.GetSessionEntries(s.Id);
                var count = matcher.CountMatches(entries);
                if (count > 0) matchCounts[s.Id] = count;
                return count > 0;
            }).ToList();
        }

        var header = "";
        if (bookmarks != null) header += "★\t";
        header += "ID\tStatus\tProject\tMessages\tTools\tDuration\tDate";
        if (allTags != null) header += "\tTags";
        if (matchCounts != null) header += "\tMatches";
        Console.Out.WriteLine(header);

        foreach (var session in sessions)
        {
            var status = session.IsActive ? "active" : "done";
            var project = session.ProjectName ?? "Unknown";
            var messages = $"{session.UserMessageCount}u/{session.AssistantMessageCount}a";
            var duration = Formatting.FormatDuration(session.Duration);
            var date = session.StartTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

            var row = "";
            if (bookmarks != null) row += "★\t";
            row += $"{session.Id}\t{status}\t{project}\t{messages}\t{session.ToolCallCount}\t{duration}\t{date}";
            if (allTags != null) row += "\t" + (allTags.TryGetValue(session.Id, out var t) ? string.Join(",", t) : "");
            if (matchCounts != null) row += "\t" + (matchCounts.TryGetValue(session.Id, out var c) ? c.ToString() : "0");
            Console.Out.WriteLine(row);
        }
    }

    /// <summary>
    /// Prints session metadata as markdown without dumping content.
    /// </summary>
    public static void PrintInfo(SessionManager sessionManager, string sessionId,
        TurnSlice turnSlice = default, BookmarkStore? bookmarkStore = null, TagStore? tagStore = null)
    {
        var (session, conversation) = SessionHelper.ResolveSession(sessionManager, sessionId);
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
        writer.WriteField("Duration", Formatting.FormatDuration(session.Duration));
        writer.WriteField("Turns", conversation.Turns.Count);
        writer.WriteField("Messages", $"{session.UserMessageCount}u/{session.AssistantMessageCount}a");
        writer.WriteField("Tool calls", session.ToolCallCount);
        writer.WriteField("Lines", $"{lineCount} (estimated from dump)");

        // Bookmark status
        if (bookmarkStore?.IsBookmarked(session.Id) == true)
            writer.WriteField("Bookmarked", "yes");

        // Tags
        var tags = tagStore?.GetTags(session.Id);
        if (tags != null && tags.Count > 0)
            writer.WriteField("Tags", string.Join(", ", tags.Order()));

        // Continuation detection
        var firstUserMsg = conversation.Turns
            .Select(t => t.UserMessage)
            .FirstOrDefault(m => !string.IsNullOrWhiteSpace(m));
        if (ContinuationDetector.IsContinuation(firstUserMsg))
            writer.WriteField("Type", "continuation");

        // Git branch from entry metadata
        var gitBranch = conversation.Entries
            .Select(e => e.GitBranch)
            .FirstOrDefault(b => !string.IsNullOrEmpty(b));
        if (gitBranch != null)
            writer.WriteField("Branch", gitBranch);

        // Git commits during session
        PrintGitCommits(writer, session);

        writer.Flush();
    }

    /// <summary>
    /// Runs git log for the session's time range and prints commits.
    /// </summary>
    private static void PrintGitCommits(MarkoutWriter writer, Session session)
    {
        if (string.IsNullOrEmpty(session.ProjectPath))
            return;

        try
        {
            var after = session.StartTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
            var before = session.LastActivityTime.ToUniversalTime().AddMinutes(1).ToString("yyyy-MM-ddTHH:mm:ssZ");

            var output = GitRunner.RunGit(session.ProjectPath, $"log --oneline --after=\"{after}\" --before=\"{before}\"");
            if (string.IsNullOrWhiteSpace(output))
                return;

            writer.WriteHeading(2, "Commits during session");
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                writer.WriteListItem(line.Trim());
            }
        }
        catch
        {
            // Git not available or not a git repo — silent failure
        }
    }

    /// <summary>
    /// Prints the full conversation for a session as plain text to stdout.
    /// Supports --head, --tail, and --turns options.
    /// </summary>
    public static void PrintConversation(SessionManager sessionManager, string sessionId,
        int? headLines = null, int? tailLines = null, TurnSlice turnSlice = default,
        string? speakerFilter = null, bool compact = false)
    {
        var (session, conversation) = SessionHelper.ResolveSession(sessionManager, sessionId);
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
    /// Prints a table of contents — one line per turn.
    /// </summary>
    public static void PrintToc(SessionManager sessionManager, string sessionId)
    {
        var (session, conversation) = SessionHelper.ResolveSession(sessionManager, sessionId);
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
            var duration = Formatting.FormatDuration(turn.Duration);

            // Content preview: first user message, or first assistant text
            var preview = turn.UserMessage;
            var contPrefix = "";
            if (preview != null)
            {
                var contInfo = ContinuationDetector.Parse(preview);
                if (contInfo.IsContinuation)
                {
                    contPrefix = "[continued] ";
                    // Use substantive content, or fall back to next user entry / assistant text
                    preview = contInfo.SubstantiveContent;
                    if (string.IsNullOrWhiteSpace(preview))
                    {
                        preview = turn.Entries.OfType<UserEntry>().Skip(1)
                            .Select(u => u.Content)
                            .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));
                    }
                    if (string.IsNullOrWhiteSpace(preview))
                    {
                        preview = turn.AssistantMessages
                            .Select(a => a.TextContent)
                            .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
                    }
                }
            }
            if (string.IsNullOrWhiteSpace(preview))
            {
                preview = turn.AssistantMessages
                    .Select(a => a.TextContent)
                    .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
            }
            preview = contPrefix + Formatting.Truncate(preview ?? "", 60 - contPrefix.Length);

            writer.WriteTableRow(turnNum, messages, tools, duration, preview);
        }

        writer.WriteTableEnd();
        writer.Flush();
    }

    /// <summary>
    /// Prints a compact digest of the N most recent sessions.
    /// </summary>
    public static void PrintBrief(SessionManager sessionManager, string? projectFilter = null,
        BookmarkStore? bookmarkStore = null, TagStore? tagStore = null, int count = 5)
    {
        var sessions = SessionHelper.FilterSessions(sessionManager, projectFilter);
        var bookmarks = bookmarkStore?.Load();
        var allTags = tagStore?.LoadAllResolved(sessions.Select(s => s.Id));

        // Take most recent N
        sessions = sessions.Take(count).ToList();

        var writer = new MarkoutWriter(Console.Out);
        writer.WriteHeading(1, $"Recent Sessions ({sessions.Count})");

        foreach (var session in sessions)
        {
            var shortId = session.Id.Length > 7 ? session.Id[..7] : session.Id;
            var status = session.IsActive ? "active" : "done";
            var age = Formatting.FormatAge(DateTime.UtcNow - session.StartTime);
            var bookmark = bookmarks?.Contains(session.Id) == true ? " ★" : "";
            var tags = allTags != null && allTags.TryGetValue(session.Id, out var t) ? $" [{string.Join(", ", t)}]" : "";

            writer.WriteHeading(2, $"{shortId}{bookmark}{tags}");
            writer.WriteFieldList(
                new MarkoutField("Age", age),
                new MarkoutField("Status", status),
                new MarkoutField("Turns", SessionHelper.GetTurnCount(sessionManager, session.Id).ToString()),
                new MarkoutField("Messages", $"{session.UserMessageCount}u/{session.AssistantMessageCount}a"),
                new MarkoutField("Tools", session.ToolCallCount.ToString()));

            // Goal: first real user message (skip continuation preambles)
            var entries = sessionManager.GetSessionEntries(session.Id);
            var conversation = new Conversation(session.Id, entries);
            var goal = SessionHelper.GetGoal(conversation, 100);

            if (goal != "(no user message)")
                writer.WriteField("Goal", goal);

            // Last: last assistant message
            var lastAssistant = conversation.Turns
                .SelectMany(t2 => t2.AssistantMessages)
                .Select(a => a.TextContent)
                .LastOrDefault(t2 => !string.IsNullOrWhiteSpace(t2));

            if (lastAssistant != null)
                writer.WriteField("Last", Formatting.Truncate(lastAssistant, 100));
        }

        writer.Flush();
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
            if (compact && ContinuationDetector.IsContinuation(user.Content))
            {
                var contInfo = ContinuationDetector.Parse(user.Content);
                output.WriteLine($"[continuation summary: {contInfo.PreambleCharCount:N0} chars]");
                if (!string.IsNullOrWhiteSpace(contInfo.SubstantiveContent))
                    output.WriteLine(contInfo.SubstantiveContent);
            }
            else
            {
                output.WriteLine(user.Content);
            }
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

    private static int CountDumpLines(Session session, Conversation conversation, TurnSlice turnSlice)
    {
        using var counter = new StringWriter();
        RenderConversation(session, conversation, counter, turnSlice);
        return counter.ToString().Split('\n').Length;
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
