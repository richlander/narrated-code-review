using System.Diagnostics;
using AgentLogs.Domain;
using AgentLogs.Services;
using AgentTrace.Services;
using Markout;

namespace AgentTrace.Commands;

/// <summary>
/// Single-call session orientation digest. Combines recent sessions, previous session
/// detail, turn summary, uncommitted state, and breadcrumbs into ~500-800 tokens.
/// </summary>
public static class OrientCommand
{
    public static void Execute(SessionManager sessionManager, string? projectPath, string? projectFilter = null,
        BookmarkStore? bookmarkStore = null, TagStore? tagStore = null)
    {
        var sessions = DumpCommand.FilterSessions(sessionManager, projectFilter);
        var writer = new MarkoutWriter(Console.Out);
        writer.WriteHeading(1, "Orientation");

        if (sessions.Count == 0)
        {
            writer.WriteField("Status", "No sessions found");
            writer.Flush();
            return;
        }

        // --- Recent Activity (one line per session, up to 5) ---
        writer.WriteHeading(2, "Recent Activity");
        foreach (var session in sessions.Take(5))
        {
            var shortId = session.Id.Length > 7 ? session.Id[..7] : session.Id;
            var status = session.IsActive ? "active" : "done";
            var age = FormatAge(DateTime.UtcNow - session.StartTime);
            var goal = GetGoal(sessionManager, session.Id);
            writer.WriteListItem($"{shortId} ({age}, {status}): {goal}");
        }

        // --- Previous Session detail ---
        // Pick the most interesting recent session: skip active sessions with ≤2 turns (likely the current agent warming up)
        var previousSession = sessions
            .FirstOrDefault(s => !(s.IsActive && GetTurnCount(sessionManager, s.Id) <= 2));
        previousSession ??= sessions[0]; // fallback to most recent

        var prevEntries = sessionManager.GetSessionEntries(previousSession.Id);
        var prevConversation = new Conversation(previousSession.Id, prevEntries);
        var prevShortId = previousSession.Id.Length > 7 ? previousSession.Id[..7] : previousSession.Id;

        writer.WriteHeading(2, $"Previous Session: {prevShortId}");
        writer.WriteCompactFields(
            new MarkoutField("Goal", GetGoal(sessionManager, previousSession.Id)),
            new MarkoutField("Turns", prevConversation.Turns.Count.ToString()),
            new MarkoutField("Duration", FormatDuration(previousSession.Duration)),
            new MarkoutField("Tools", previousSession.ToolCallCount.ToString()));

        // Git commits during previous session
        var commits = GetGitCommits(previousSession);
        if (commits.Count > 0)
        {
            writer.WriteField("Commits", string.Join("; ", commits.Take(5)));
        }

        // Turn summary: one line per turn
        if (prevConversation.Turns.Count > 0)
        {
            writer.WriteHeading(3, "Turn Summary");
            foreach (var turn in prevConversation.Turns)
            {
                var turnNum = turn.Number + 1;
                var preview = GetTurnPreview(turn);
                writer.WriteListItem($"{turnNum}. {preview}");
            }
        }

        // --- Uncommitted State ---
        if (!string.IsNullOrEmpty(projectPath))
        {
            var gitStatus = GetUncommittedState(projectPath);
            if (gitStatus.Modified.Count > 0 || gitStatus.New.Count > 0 || gitStatus.Staged.Count > 0)
            {
                writer.WriteHeading(2, "Uncommitted State");
                if (gitStatus.Staged.Count > 0)
                    writer.WriteField("Staged", string.Join(", ", gitStatus.Staged));
                if (gitStatus.Modified.Count > 0)
                    writer.WriteField("Modified", string.Join(", ", gitStatus.Modified));
                if (gitStatus.New.Count > 0)
                    writer.WriteField("New", string.Join(", ", gitStatus.New));
            }
        }

        // --- Breadcrumbs (stamps + commit mentions, compact) ---
        var breadcrumbs = GatherBreadcrumbs(sessions, sessionManager, projectPath);
        if (breadcrumbs.Count > 0)
        {
            writer.WriteHeading(2, "Breadcrumbs");
            foreach (var crumb in breadcrumbs.Take(5))
                writer.WriteListItem(crumb);
        }

        writer.Flush();
    }

    private static string GetGoal(SessionManager sessionManager, string sessionId)
    {
        var entries = sessionManager.GetSessionEntries(sessionId);
        if (entries.Count == 0) return "(empty)";
        var conversation = new Conversation(sessionId, entries);

        var goalMessage = conversation.Turns
            .Select(t => t.UserMessage)
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m =>
            {
                var cont = ContinuationDetector.Parse(m);
                return cont.IsContinuation ? cont.SubstantiveContent : m;
            })
            .FirstOrDefault(m => !string.IsNullOrWhiteSpace(m));

        if (goalMessage == null) return "(no user message)";
        var single = goalMessage.ReplaceLineEndings(" ");
        return single.Length > 80 ? single[..80] + "..." : single;
    }

    private static int GetTurnCount(SessionManager sessionManager, string sessionId)
    {
        var entries = sessionManager.GetSessionEntries(sessionId);
        if (entries.Count == 0) return 0;
        var conversation = new Conversation(sessionId, entries);
        return conversation.Turns.Count;
    }

    private static string GetTurnPreview(Turn turn)
    {
        // Try user message first
        var preview = turn.UserMessage;
        if (preview != null)
        {
            var contInfo = ContinuationDetector.Parse(preview);
            if (contInfo.IsContinuation)
            {
                preview = contInfo.SubstantiveContent;
                if (string.IsNullOrWhiteSpace(preview))
                    preview = null;
            }
        }

        // Fall back to assistant text
        if (string.IsNullOrWhiteSpace(preview))
        {
            preview = turn.AssistantMessages
                .Select(a => a.TextContent)
                .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
        }

        if (string.IsNullOrWhiteSpace(preview))
            return "(tool calls only)";

        var single = preview.ReplaceLineEndings(" ");
        return single.Length > 80 ? single[..80] + "..." : single;
    }

    private static List<string> GetGitCommits(Session session)
    {
        var results = new List<string>();
        if (string.IsNullOrEmpty(session.ProjectPath))
            return results;

        try
        {
            var after = session.StartTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
            var before = session.LastActivityTime.ToUniversalTime().AddMinutes(1).ToString("yyyy-MM-ddTHH:mm:ssZ");
            var output = RunGit(session.ProjectPath, $"log --oneline --after=\"{after}\" --before=\"{before}\"");
            if (output != null)
            {
                foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    results.Add(line.Trim());
            }
        }
        catch
        {
            // Git not available
        }

        return results;
    }

    private record GitStatus(List<string> Staged, List<string> Modified, List<string> New);

    private static GitStatus GetUncommittedState(string projectPath)
    {
        var staged = ParseFileList(RunGit(projectPath, "diff --name-only --cached"));
        var modified = ParseFileList(RunGit(projectPath, "diff --name-only"));
        var untracked = ParseFileList(RunGit(projectPath, "ls-files --others --exclude-standard"));
        return new GitStatus(staged, modified, untracked);
    }

    private static List<string> ParseFileList(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return [];
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(f => f.Trim())
            .Where(f => f.Length > 0)
            .ToList();
    }

    private static List<string> GatherBreadcrumbs(IReadOnlyList<Session> sessions,
        SessionManager sessionManager, string? projectPath)
    {
        var results = new List<string>();

        // Find stamps (most recent first, limit to 3)
        var stampMatcher = new EntryMatcher("«stamp:");
        foreach (var session in sessions.Take(5))
        {
            var entries = sessionManager.GetSessionEntries(session.Id);
            foreach (var entry in entries)
            {
                var ctx = stampMatcher.FindMatch(entry);
                if (ctx == null) continue;

                var fullText = ExtractStampText(entry, stampMatcher);
                if (fullText == null) continue;

                var message = ParseStampField(fullText, "message");
                if (message == null) continue;

                var shortId = session.Id.Length > 7 ? session.Id[..7] : session.Id;
                results.Add($"Stamp: \"{message}\" (session {shortId})");
                if (results.Count >= 3) break;
            }
            if (results.Count >= 3) break;
        }

        // Commit breadcrumbs: check if recent git commits are mentioned in sessions
        if (!string.IsNullOrEmpty(projectPath))
        {
            var log = RunGit(projectPath, "log --oneline -5");
            if (log != null)
            {
                var hashes = log.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => line.Split(' ')[0])
                    .Where(h => h.Length >= 7)
                    .ToList();

                foreach (var hash in hashes)
                {
                    var matcher = new EntryMatcher(hash);
                    var found = new List<string>();
                    foreach (var session in sessions.Take(10))
                    {
                        var entries = sessionManager.GetSessionEntries(session.Id);
                        if (matcher.CountMatches(entries) > 0)
                        {
                            var shortId = session.Id.Length > 7 ? session.Id[..7] : session.Id;
                            found.Add(shortId);
                        }
                    }
                    if (found.Count > 0)
                    {
                        results.Add($"Commit {hash} mentioned in sessions {string.Join(", ", found)}");
                    }
                }
            }
        }

        return results;
    }

    private static string? ExtractStampText(Entry entry, EntryMatcher matcher)
    {
        if (entry is UserEntry user)
        {
            foreach (var block in user.ContentBlocks)
            {
                if (block is ToolResultBlock toolResult && toolResult.Content != null
                    && matcher.IsMatch(toolResult.Content) && toolResult.Content.Contains("«/stamp»"))
                    return toolResult.Content;
            }
            if (user.Content != null && matcher.IsMatch(user.Content) && user.Content.Contains("«/stamp»"))
                return user.Content;
        }

        if (entry is AssistantEntry assistant && assistant.TextContent != null
            && matcher.IsMatch(assistant.TextContent) && assistant.TextContent.Contains("«/stamp»"))
            return assistant.TextContent;

        return null;
    }

    private static string? ParseStampField(string text, string field)
    {
        var match = System.Text.RegularExpressions.Regex.Match(text,
            $@"^\s*{field}:\s*(.+)$", System.Text.RegularExpressions.RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static string? RunGit(string workingDirectory, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo("git")
            {
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return null;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            return process.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }

    private static string FormatAge(TimeSpan age)
    {
        if (age.TotalMinutes < 60)
            return $"{(int)age.TotalMinutes}m ago";
        if (age.TotalHours < 24)
            return $"{(int)age.TotalHours}h ago";
        return $"{(int)age.TotalDays}d ago";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalSeconds < 60)
            return $"{(int)duration.TotalSeconds}s";
        if (duration.TotalMinutes < 60)
            return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
        return $"{(int)duration.TotalHours}h {duration.Minutes}m";
    }
}
