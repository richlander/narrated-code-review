using AgentTrace.Services;

namespace AgentTrace.Commands;

/// <summary>
/// Emits a structured stamp block with session, git, and timestamp metadata.
/// Stamps use guillemet delimiters for zero-false-positive searching.
/// </summary>
public static class StampCommand
{
    public static void Execute(string? projectLogPath, string? projectPath, string? message)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

        Console.WriteLine($"«stamp:{timestamp}»");

        // Session ID: most recently modified .jsonl in project log path
        var sessionId = StampHelper.DetectSessionId(projectLogPath);
        if (sessionId != null)
            Console.WriteLine($"  session: {sessionId}");

        // Git context
        if (!string.IsNullOrEmpty(projectPath))
        {
            var commit = GitRunner.RunGit(projectPath, "log --oneline -1");
            if (commit != null)
                Console.WriteLine($"  commit: {commit.Trim()}");

            var branch = GitRunner.RunGit(projectPath, "rev-parse --abbrev-ref HEAD");
            if (branch != null)
                Console.WriteLine($"  branch: {branch.Trim()}");

            var staged = GitRunner.RunGit(projectPath, "diff --name-only --cached");
            Console.WriteLine($"  staged: {Formatting.FormatFileList(staged)}");

            var modified = GitRunner.RunGit(projectPath, "diff --name-only");
            Console.WriteLine($"  modified: {Formatting.FormatFileList(modified)}");

            var untracked = GitRunner.RunGit(projectPath, "ls-files --others --exclude-standard");
            Console.WriteLine($"  untracked: {Formatting.FormatFileList(untracked)}");
        }

        if (!string.IsNullOrEmpty(message))
            Console.WriteLine($"  message: {message}");

        Console.WriteLine("«/stamp»");
    }
}
