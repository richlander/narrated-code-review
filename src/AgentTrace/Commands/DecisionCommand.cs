using AgentTrace.Services;

namespace AgentTrace.Commands;

/// <summary>
/// Emits a structured decision block recording an architectural/design decision.
/// Uses guillemet delimiters for zero-false-positive searching.
/// </summary>
public static class DecisionCommand
{
    public static void Execute(string? projectLogPath, string? projectPath, string chose, string? over, string? because)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

        Console.WriteLine($"«decision:{timestamp}»");
        Console.WriteLine($"  chose: {chose}");

        if (!string.IsNullOrEmpty(over))
            Console.WriteLine($"  over: {over}");

        if (!string.IsNullOrEmpty(because))
            Console.WriteLine($"  because: {because}");

        // Session ID
        var sessionId = StampHelper.DetectSessionId(projectLogPath);
        if (sessionId != null)
            Console.WriteLine($"  session: {sessionId}");

        // Git context (lighter than stamps — just branch + commit)
        if (!string.IsNullOrEmpty(projectPath))
        {
            var branch = GitRunner.RunGit(projectPath, "rev-parse --abbrev-ref HEAD");
            if (branch != null)
                Console.WriteLine($"  branch: {branch.Trim()}");

            var commit = GitRunner.RunGit(projectPath, "log --oneline -1");
            if (commit != null)
                Console.WriteLine($"  commit: {commit.Trim()}");
        }

        Console.WriteLine("«/decision»");
    }
}
