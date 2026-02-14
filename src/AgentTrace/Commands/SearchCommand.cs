using Microsoft.Extensions.Terminal;
using AgentLogs.Domain;
using AgentLogs.Services;
using AgentTrace.Services;

namespace AgentTrace.Commands;

/// <summary>
/// Cross-session search command.
/// </summary>
public static class SearchCommand
{
    public static void Execute(
        SessionManager sessionManager,
        ITerminal terminal,
        string searchTerm,
        string? projectFilter = null)
    {
        var sessions = sessionManager.GetAllSessions();

        if (projectFilter != null)
        {
            sessions = sessions
                .Where(s => s.ProjectName != null &&
                    s.ProjectName.Contains(projectFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var matcher = new EntryMatcher(searchTerm);

        terminal.AppendLine();
        terminal.SetColor(TerminalColor.Blue);
        terminal.Append("  Search: ");
        terminal.SetColor(TerminalColor.Yellow);
        terminal.AppendLine(searchTerm);
        terminal.SetColor(TerminalColor.Gray);
        terminal.AppendLine($"  {new string('─', 80)}");
        terminal.ResetColor();

        var totalMatches = 0;

        foreach (var session in sessions)
        {
            var entries = sessionManager.GetSessionEntries(session.Id);
            var sessionMatches = new List<(Entry Entry, string Context)>();

            foreach (var entry in entries)
            {
                var matchContext = matcher.FindMatch(entry);
                if (matchContext != null)
                {
                    sessionMatches.Add((entry, matchContext));
                }
            }

            if (sessionMatches.Count == 0)
                continue;

            totalMatches += sessionMatches.Count;

            // Print session header
            terminal.AppendLine();
            terminal.SetColor(TerminalColor.White);
            terminal.Append($"  {session.ProjectName ?? "Unknown"}");
            terminal.SetColor(TerminalColor.Gray);
            terminal.AppendLine($"  ({sessionMatches.Count} matches)");
            terminal.ResetColor();

            foreach (var (entry, context) in sessionMatches.Take(10))
            {
                var role = entry switch
                {
                    UserEntry => "user",
                    AssistantEntry => "assistant",
                    _ => entry.GetType().Name
                };
                var time = entry.Timestamp.ToLocalTime().ToString("HH:mm:ss");

                terminal.Append("    ");
                terminal.SetColor(TerminalColor.Gray);
                terminal.Append($"[{time}] ");
                terminal.SetColor(role == "user" ? TerminalColor.Cyan : TerminalColor.Green);
                terminal.Append($"{role}: ");
                terminal.ResetColor();
                terminal.AppendLine(Formatting.Truncate(context, 70));
            }

            if (sessionMatches.Count > 10)
            {
                terminal.SetColor(TerminalColor.Gray);
                terminal.AppendLine($"    ... and {sessionMatches.Count - 10} more matches");
                terminal.ResetColor();
            }
        }

        terminal.AppendLine();
        terminal.SetColor(TerminalColor.Gray);
        terminal.AppendLine($"  {totalMatches} match(es) across {sessions.Count} session(s)");
        terminal.ResetColor();
        terminal.AppendLine();
    }
}
