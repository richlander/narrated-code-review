using System.Diagnostics;
using AgentLogs.Domain;
using AgentLogs.Services;
using AgentTrace.Services;
using Markout;

namespace AgentTrace.Commands;

/// <summary>
/// Automatically gathers context clues (latest commits, branch, etc.) and searches
/// for breadcrumbs across sessions. Helps agents orient after compaction.
/// </summary>
public static class AutoSearchCommand
{
    public static void Execute(SessionManager sessionManager, string? projectPath, string? projectFilter = null,
        BookmarkStore? bookmarkStore = null, TagStore? tagStore = null)
    {
        var sessions = DumpCommand.FilterSessions(sessionManager, projectFilter);
        var writer = new MarkoutWriter(Console.Out);
        writer.WriteHeading(1, "Auto-Search Results");

        // Gather clues from git
        var clues = GatherGitClues(projectPath);

        // Gather clues from bookmarks/tags
        var bookmarks = bookmarkStore?.Load();
        var allTags = tagStore?.LoadAllResolved(sessions.Select(s => s.Id));

        // Report bookmarked sessions
        if (bookmarks != null && bookmarks.Count > 0)
        {
            var bookmarkedSessions = sessions.Where(s => bookmarks.Contains(s.Id)).ToList();
            if (bookmarkedSessions.Count > 0)
            {
                writer.WriteHeading(2, $"Bookmarked Sessions ({bookmarkedSessions.Count})");
                foreach (var s in bookmarkedSessions.Take(5))
                {
                    var shortId = s.Id.Length > 7 ? s.Id[..7] : s.Id;
                    var age = FormatAge(DateTime.UtcNow - s.StartTime);
                    var entries = sessionManager.GetSessionEntries(s.Id);
                    var conversation = new Conversation(s.Id, entries);
                    var goal = GetGoal(conversation);
                    writer.WriteListItem($"{shortId} ({age}) — {goal}");
                }
            }
        }

        // Report tagged sessions
        if (allTags != null && allTags.Count > 0)
        {
            var taggedSessions = sessions.Where(s => allTags.ContainsKey(s.Id)).ToList();
            if (taggedSessions.Count > 0)
            {
                writer.WriteHeading(2, $"Tagged Sessions ({taggedSessions.Count})");
                foreach (var s in taggedSessions.Take(5))
                {
                    var shortId = s.Id.Length > 7 ? s.Id[..7] : s.Id;
                    var tags = allTags.TryGetValue(s.Id, out var t) ? string.Join(", ", t) : "";
                    var age = FormatAge(DateTime.UtcNow - s.StartTime);
                    writer.WriteListItem($"{shortId} ({age}) [{tags}]");
                }
            }
        }

        // Search for each git clue
        if (clues.Count > 0)
        {
            writer.WriteHeading(2, "Git Context");
            if (clues.TryGetValue("branch", out var branch))
                writer.WriteField("Branch", branch);

            if (clues.TryGetValue("commits", out var commits))
            {
                writer.WriteHeading(3, "Recent Commits");
                foreach (var line in commits.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    writer.WriteListItem(line.Trim());
            }

            // Search for recent commit hashes in sessions
            if (clues.TryGetValue("commit_hashes", out var hashesStr))
            {
                var hashes = hashesStr.Split(',', StringSplitOptions.RemoveEmptyEntries);
                var foundSessions = new Dictionary<string, List<string>>();

                foreach (var hash in hashes)
                {
                    var matcher = new EntryMatcher(hash);
                    foreach (var session in sessions)
                    {
                        var entries = sessionManager.GetSessionEntries(session.Id);
                        if (matcher.CountMatches(entries) > 0)
                        {
                            var shortId = session.Id.Length > 7 ? session.Id[..7] : session.Id;
                            if (!foundSessions.TryGetValue(shortId, out var list))
                            {
                                list = [];
                                foundSessions[shortId] = list;
                            }
                            list.Add(hash);
                        }
                    }
                }

                if (foundSessions.Count > 0)
                {
                    writer.WriteHeading(3, "Commit Breadcrumbs Found");
                    foreach (var (sid, hashes2) in foundSessions)
                    {
                        writer.WriteListItem($"Session {sid}: mentions {string.Join(", ", hashes2)}");
                    }
                }
            }
        }

        // Search for stamps
        SearchStamps(sessions, sessionManager, writer);

        // Search for structured breadcrumbs
        var breadcrumbPatterns = new[]
        {
            ("Milestone:", "Milestones"),
            ("Decision:", "Decisions"),
            ("Root cause:", "Root Causes"),
        };

        foreach (var (pattern, label) in breadcrumbPatterns)
        {
            var matcher = new EntryMatcher(pattern);
            var hits = new List<(string SessionId, string Context)>();

            foreach (var session in sessions)
            {
                var entries = sessionManager.GetSessionEntries(session.Id);
                foreach (var entry in entries)
                {
                    var ctx = matcher.FindMatch(entry);
                    if (ctx != null)
                    {
                        var shortId = session.Id.Length > 7 ? session.Id[..7] : session.Id;
                        hits.Add((shortId, ctx));
                    }
                }
            }

            if (hits.Count > 0)
            {
                writer.WriteHeading(2, $"{label} ({hits.Count})");
                foreach (var (sid, ctx) in hits.Take(10))
                {
                    var clean = ctx.Replace('\n', ' ').Replace('\r', ' ');
                    if (clean.Length > 80) clean = clean[..80] + "...";
                    writer.WriteListItem($"{sid}: {clean}");
                }
            }
        }

        writer.Flush();
    }

    private static Dictionary<string, string> GatherGitClues(string? projectPath)
    {
        var clues = new Dictionary<string, string>();
        if (string.IsNullOrEmpty(projectPath))
            return clues;

        try
        {
            // Current branch
            var branch = RunGit(projectPath, "rev-parse --abbrev-ref HEAD");
            if (branch != null)
                clues["branch"] = branch.Trim();

            // Recent commits (last 5)
            var log = RunGit(projectPath, "log --oneline -5");
            if (log != null)
            {
                clues["commits"] = log.Trim();
                // Extract hashes for breadcrumb search
                var hashes = log.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => line.Split(' ')[0])
                    .Where(h => h.Length >= 7);
                clues["commit_hashes"] = string.Join(",", hashes);
            }
        }
        catch
        {
            // Git not available — silent
        }

        return clues;
    }

    private static string? RunGit(string workingDirectory, string arguments)
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

    private static string GetGoal(Conversation conversation)
    {
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

    private static void SearchStamps(IReadOnlyList<Session> sessions, SessionManager sessionManager, MarkoutWriter writer)
    {
        var matcher = new EntryMatcher("«stamp:");
        var stamps = new List<(string SessionId, string Timestamp, string? Session, string? Commit, string? Message)>();

        foreach (var session in sessions)
        {
            var entries = sessionManager.GetSessionEntries(session.Id);
            foreach (var entry in entries)
            {
                var ctx = matcher.FindMatch(entry);
                if (ctx == null) continue;

                // Found a stamp — now extract the full content to parse fields
                var fullText = ExtractStampText(entry, matcher);
                if (fullText == null) continue;

                var timestamp = ParseStampTimestamp(fullText);
                if (timestamp == null || timestamp.Length < 10) continue; // Not a real stamp block

                var stampSession = ParseStampField(fullText, "session");
                var commit = ParseStampField(fullText, "commit");
                var message = ParseStampField(fullText, "message");

                var shortId = session.Id.Length > 7 ? session.Id[..7] : session.Id;
                stamps.Add((shortId, timestamp, stampSession, commit, message));
            }
        }

        if (stamps.Count > 0)
        {
            writer.WriteHeading(2, $"Stamps ({stamps.Count})");
            foreach (var (sid, ts, stampSess, commit, message) in stamps.Take(10))
            {
                var parts = new List<string> { ts };
                if (stampSess != null) parts.Add($"session:{stampSess}");
                if (commit != null) parts.Add(commit.Length > 50 ? commit[..50] + "..." : commit);
                if (message != null) parts.Add($"\"{message}\"");
                writer.WriteListItem($"{sid}: {string.Join("  ", parts)}");
            }
        }
    }

    private static string? ExtractStampText(Entry entry, EntryMatcher matcher)
    {
        // Check user entry content blocks (tool results) — stamps land here via Bash output
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

    private static string? ParseStampTimestamp(string text)
    {
        // Match «stamp:2026-02-13T08:15:00Z»
        var match = System.Text.RegularExpressions.Regex.Match(text, @"«stamp:([^»]+)»");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? ParseStampField(string text, string field)
    {
        // Match "  field: value" lines
        var match = System.Text.RegularExpressions.Regex.Match(text, $@"^\s*{field}:\s*(.+)$", System.Text.RegularExpressions.RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static string FormatAge(TimeSpan age)
    {
        if (age.TotalMinutes < 60)
            return $"{(int)age.TotalMinutes}m ago";
        if (age.TotalHours < 24)
            return $"{(int)age.TotalHours}h ago";
        return $"{(int)age.TotalDays}d ago";
    }
}
