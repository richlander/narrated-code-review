using System.Diagnostics;

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
        var sessionId = DetectSessionId(projectLogPath);
        if (sessionId != null)
            Console.WriteLine($"  session: {sessionId}");

        // Git context
        if (!string.IsNullOrEmpty(projectPath))
        {
            var commit = RunGit(projectPath, "log --oneline -1");
            if (commit != null)
                Console.WriteLine($"  commit: {commit.Trim()}");

            var branch = RunGit(projectPath, "rev-parse --abbrev-ref HEAD");
            if (branch != null)
                Console.WriteLine($"  branch: {branch.Trim()}");

            var staged = RunGit(projectPath, "diff --name-only --cached");
            Console.WriteLine($"  staged: {FormatFileList(staged)}");

            var modified = RunGit(projectPath, "diff --name-only");
            Console.WriteLine($"  modified: {FormatFileList(modified)}");

            var untracked = RunGit(projectPath, "ls-files --others --exclude-standard");
            Console.WriteLine($"  untracked: {FormatFileList(untracked)}");
        }

        if (!string.IsNullOrEmpty(message))
            Console.WriteLine($"  message: {message}");

        Console.WriteLine("«/stamp»");
    }

    private static string? DetectSessionId(string? projectLogPath)
    {
        if (string.IsNullOrEmpty(projectLogPath) || !Directory.Exists(projectLogPath))
            return null;

        var mostRecent = Directory.EnumerateFiles(projectLogPath, "*.jsonl")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault();

        if (mostRecent == null)
            return null;

        var fullId = Path.GetFileNameWithoutExtension(mostRecent.Name);
        return fullId.Length > 7 ? fullId[..7] : fullId;
    }

    private static string FormatFileList(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return "(none)";

        var files = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(f => f.Trim())
            .Where(f => f.Length > 0)
            .ToArray();

        return files.Length == 0 ? "(none)" : string.Join(", ", files);
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
}
