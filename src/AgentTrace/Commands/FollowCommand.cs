using AgentLogs.Domain;
using AgentLogs.Parsing;
using AgentLogs.Providers;
using AgentTrace.Services;
using AgentTrace.UI;
using Microsoft.Extensions.Terminal;

namespace AgentTrace.Commands;

/// <summary>
/// Follows the active session for a project, tailing new entries as they arrive.
/// </summary>
public static class FollowCommand
{
    public static async Task<int> RunAsync(
        ClaudeCodeProvider provider,
        string projectDirName,
        ITerminal terminal,
        string? watchPattern = null)
    {
        var projectLogPath = provider.GetProjectLogPath(projectDirName);

        // Find the most recently modified JSONL file
        var activeFile = FindActiveFile(projectLogPath);
        if (activeFile == null)
        {
            terminal.SetColor(TerminalColor.Red);
            terminal.AppendLine("No active session found for this project.");
            terminal.ResetColor();
            terminal.SetColor(TerminalColor.Gray);
            terminal.AppendLine($"  project: {projectLogPath}");
            terminal.ResetColor();
            return 1;
        }

        var fileInfo = new FileInfo(activeFile);
        var age = DateTime.UtcNow - fileInfo.LastWriteTimeUtc;
        if (age > TimeSpan.FromMinutes(10))
        {
            terminal.SetColor(TerminalColor.Yellow);
            terminal.AppendLine($"Warning: Most recent session was last active {Formatting.FormatAge(age, withSuffix: false)} ago.");
            terminal.ResetColor();
            terminal.AppendLine($"  file: {Path.GetFileName(activeFile)}");
            terminal.AppendLine();
        }

        var sessionId = Path.GetFileNameWithoutExtension(activeFile);

        terminal.SetColor(TerminalColor.Blue);
        terminal.Append("Following session ");
        terminal.SetColor(TerminalColor.White);
        terminal.Append(sessionId[..8]);
        terminal.SetColor(TerminalColor.Gray);
        terminal.AppendLine($"  (Ctrl+C or q to stop)");
        terminal.ResetColor();

        // Initial parse
        var result = await EntryParser.ParseFileFullAsync(activeFile);
        var conversation = new Conversation(sessionId, result.Entries);

        // Build session context (single session in follow mode)
        var ctx = new SessionContext(sessionId, null, fileInfo.CreationTimeUtc, 0, 1);

        // Launch live pager
        var pager = new LiveConversationPager(conversation, terminal, activeFile, ctx)
        {
            WatchPattern = watchPattern
        };
        await pager.RunAsync();

        // Watch mode: exit with code 2 and print the match
        if (watchPattern != null && pager.WatchMatchContext != null)
        {
            terminal.SetColor(TerminalColor.Red);
            terminal.Append("WATCH TRIGGERED: ");
            terminal.ResetColor();
            terminal.AppendLine(pager.WatchMatchContext);
            return 2;
        }

        return 0;
    }

    private static string? FindActiveFile(string projectLogPath)
    {
        if (!Directory.Exists(projectLogPath))
            return null;

        return Directory.EnumerateFiles(projectLogPath, "*.jsonl")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault()
            ?.FullName;
    }
}
