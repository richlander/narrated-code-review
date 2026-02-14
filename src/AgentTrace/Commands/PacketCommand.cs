using AgentLogs.Domain;
using AgentLogs.Services;
using AgentTrace.Services;

namespace AgentTrace.Commands;

/// <summary>
/// Dense structured context packet for agent consumption.
/// No markdown prose — pure key:value pairs in --- section --- delimited blocks.
/// </summary>
public static class PacketCommand
{
    public static void Execute(SessionManager sessionManager, string? projectPath, string? projectFilter, int depth)
    {
        var sessions = SessionHelper.FilterSessions(sessionManager, projectFilter);

        // --- project ---
        var effectivePath = projectPath ?? sessions.FirstOrDefault()?.ProjectPath ?? Environment.CurrentDirectory;
        Console.WriteLine("--- project ---");
        Console.WriteLine($"path: {effectivePath}");
        Console.WriteLine($"name: {Path.GetFileName(effectivePath)}");
        Console.WriteLine();

        // --- git ---
        if (!string.IsNullOrEmpty(effectivePath))
        {
            Console.WriteLine("--- git ---");
            var branch = GitRunner.RunGit(effectivePath, "rev-parse --abbrev-ref HEAD");
            Console.WriteLine($"branch: {branch?.Trim() ?? "(unknown)"}");

            var commit = GitRunner.RunGit(effectivePath, "log --oneline -1");
            Console.WriteLine($"commit: {commit?.Trim() ?? "(none)"}");

            var staged = GitRunner.RunGit(effectivePath, "diff --name-only --cached");
            Console.WriteLine($"staged: {Formatting.FormatFileList(staged)}");

            var modified = GitRunner.RunGit(effectivePath, "diff --name-only");
            Console.WriteLine($"modified: {Formatting.FormatFileList(modified)}");

            var untracked = GitRunner.RunGit(effectivePath, "ls-files --others --exclude-standard");
            Console.WriteLine($"untracked: {Formatting.FormatFileList(untracked)}");
            Console.WriteLine();
        }

        // --- sessions ---
        var limitedSessions = sessions.Take(depth).ToList();
        Console.WriteLine($"--- sessions ({limitedSessions.Count}) ---");
        foreach (var session in limitedSessions)
        {
            var shortId = session.Id.Length > 7 ? session.Id[..7] : session.Id;
            var age = Formatting.FormatAge(DateTime.UtcNow - session.StartTime);
            var status = session.IsActive ? "active" : "done";
            var entries = sessionManager.GetSessionEntries(session.Id);
            var conversation = new Conversation(session.Id, entries);
            var turns = conversation.Turns.Count;
            var tools = session.ToolCallCount;
            var goal = SessionHelper.GetGoal(conversation, 60);
            Console.WriteLine($"{shortId}  {age,-10} {status,-8} {turns} turns  {tools,3} tools  \"{goal}\"");
        }
        Console.WriteLine();

        // --- decisions ---
        var decisions = GatherDecisions(sessions, sessionManager);
        if (decisions.Count > 0)
        {
            Console.WriteLine($"--- decisions ({decisions.Count}) ---");
            foreach (var (ts, chose, over) in decisions)
            {
                var parts = $"chose: {chose}";
                if (over != null)
                    parts += $" | over: {over}";
                Console.WriteLine($"{ts}  {parts}");
            }
            Console.WriteLine();
        }

        // --- stamps ---
        var stamps = GatherStamps(sessions, sessionManager);
        if (stamps.Count > 0)
        {
            Console.WriteLine($"--- stamps ({stamps.Count}) ---");
            foreach (var (ts, stampSession, message) in stamps)
            {
                var parts = new List<string>();
                if (stampSession != null) parts.Add($"session:{stampSession}");
                if (message != null) parts.Add($"\"{message}\"");
                Console.WriteLine($"{ts}  {string.Join("  ", parts)}");
            }
            Console.WriteLine();
        }

        // --- files ---
        var files = GatherFiles(limitedSessions, sessionManager);
        if (files.Count > 0)
        {
            Console.WriteLine($"--- files ({Math.Min(files.Count, 10)}) ---");
            foreach (var (path, sessionCount, ops) in files.Take(10))
            {
                Console.WriteLine($"{path,-50} {sessionCount} sessions  {string.Join(",", ops)}");
            }
            Console.WriteLine();
        }
    }

    private static List<(string Timestamp, string Chose, string? Over)> GatherDecisions(
        IReadOnlyList<Session> sessions, SessionManager sessionManager)
    {
        var results = new List<(string, string, string?)>();
        var matcher = new EntryMatcher("«decision:");

        foreach (var session in sessions.Take(10))
        {
            var entries = sessionManager.GetSessionEntries(session.Id);
            foreach (var entry in entries)
            {
                var ctx = matcher.FindMatch(entry);
                if (ctx == null) continue;

                var fullText = StampParser.ExtractDecisionText(entry, matcher);
                if (fullText == null) continue;

                var ts = StampParser.ParseDecisionTimestamp(fullText) ?? "unknown";
                var chose = StampParser.ParseStampField(fullText, "chose");
                if (chose == null) continue;

                var over = StampParser.ParseStampField(fullText, "over");
                results.Add((ts, chose, over));
            }
        }

        return results;
    }

    private static List<(string Timestamp, string? Session, string? Message)> GatherStamps(
        IReadOnlyList<Session> sessions, SessionManager sessionManager)
    {
        var results = new List<(string, string?, string?)>();
        var matcher = new EntryMatcher("«stamp:");

        foreach (var session in sessions.Take(10))
        {
            var entries = sessionManager.GetSessionEntries(session.Id);
            foreach (var entry in entries)
            {
                var ctx = matcher.FindMatch(entry);
                if (ctx == null) continue;

                var fullText = StampParser.ExtractStampText(entry, matcher);
                if (fullText == null) continue;

                var ts = StampParser.ParseStampTimestamp(fullText);
                if (ts == null || ts.Length < 10) continue;

                var stampSession = StampParser.ParseStampField(fullText, "session");
                var message = StampParser.ParseStampField(fullText, "message");
                results.Add((ts, stampSession, message));
            }
        }

        return results;
    }

    private static List<(string Path, int SessionCount, HashSet<string> Ops)> GatherFiles(
        IReadOnlyList<Session> sessions, SessionManager sessionManager)
    {
        // Map tool names to operations
        static string? MapToolOp(string toolName) => toolName switch
        {
            "Read" or "read" => "read",
            "Write" or "write" => "write",
            "Edit" or "edit" => "edit",
            "Glob" or "glob" or "Grep" or "grep" => null, // skip search tools
            _ => null
        };

        var fileMap = new Dictionary<string, (HashSet<string> Sessions, HashSet<string> Ops)>();

        foreach (var session in sessions)
        {
            var shortId = session.Id.Length > 7 ? session.Id[..7] : session.Id;
            var entries = sessionManager.GetSessionEntries(session.Id);

            foreach (var entry in entries)
            {
                if (entry is not AssistantEntry assistant) continue;

                foreach (var tool in assistant.ToolUses)
                {
                    var op = MapToolOp(tool.Name);
                    if (op == null || string.IsNullOrEmpty(tool.FilePath)) continue;

                    if (!fileMap.TryGetValue(tool.FilePath, out var info))
                    {
                        info = (new HashSet<string>(), new HashSet<string>());
                        fileMap[tool.FilePath] = info;
                    }

                    info.Sessions.Add(shortId);
                    info.Ops.Add(op);
                }
            }
        }

        return fileMap
            .OrderByDescending(kv => kv.Value.Sessions.Count)
            .ThenBy(kv => kv.Key)
            .Select(kv => (kv.Key, kv.Value.Sessions.Count, kv.Value.Ops))
            .ToList();
    }
}
