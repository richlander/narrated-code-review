using Microsoft.Extensions.Terminal;
using AgentLogs.Domain;
using AgentLogs.Services;

namespace AgentTrace.Commands;

/// <summary>
/// Lists sessions in a table format.
/// </summary>
public static class ListCommand
{
    public static void Execute(SessionManager sessionManager, ITerminal terminal, string? projectFilter = null)
    {
        var sessions = sessionManager.GetAllSessions();

        if (projectFilter != null)
        {
            sessions = sessions
                .Where(s => s.ProjectName != null &&
                    s.ProjectName.Contains(projectFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        terminal.AppendLine();
        terminal.SetColor(TerminalColor.Blue);
        terminal.AppendLine("  AgentTrace - Session List");
        terminal.SetColor(TerminalColor.Gray);
        terminal.AppendLine($"  {new string('─', 92)}");
        terminal.ResetColor();

        terminal.AppendLine($"  {"#",-4} {"",2} {"Project",-25} {"Messages",-12} {"Tools",-8} {"Duration",-12} {"Date",-20}");
        terminal.SetColor(TerminalColor.Gray);
        terminal.AppendLine($"  {new string('─', 92)}");
        terminal.ResetColor();

        var index = 1;
        foreach (var session in sessions.Take(50))
        {
            var duration = FormatDuration(session.Duration);
            var date = session.StartTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

            terminal.Append($"  {index,-4} ");
            terminal.SetColor(session.IsActive ? TerminalColor.Green : TerminalColor.Gray);
            terminal.Append(session.IsActive ? "● " : "○ ");
            terminal.SetColor(TerminalColor.White);
            terminal.Append($"{Truncate(session.ProjectName ?? "Unknown", 25),-25} ");
            terminal.ResetColor();
            terminal.Append($"{session.UserMessageCount}↔{session.AssistantMessageCount,-7} ");
            terminal.Append($"{session.ToolCallCount,-8} ");
            terminal.SetColor(TerminalColor.Gray);
            terminal.Append($"{duration,-12} ");
            terminal.AppendLine($"{date,-20}");
            terminal.ResetColor();

            index++;
        }

        terminal.AppendLine();
        terminal.SetColor(TerminalColor.Gray);
        terminal.AppendLine($"  {sessions.Count} session(s) found");
        terminal.ResetColor();
        terminal.AppendLine();
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalSeconds < 60)
            return $"{(int)duration.TotalSeconds}s";
        if (duration.TotalMinutes < 60)
            return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
        return $"{(int)duration.TotalHours}h {duration.Minutes}m";
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Length <= maxLength ? text : text[..(maxLength - 3)] + "...";
    }
}
