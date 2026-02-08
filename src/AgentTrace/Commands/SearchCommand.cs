using System.Text.RegularExpressions;
using Microsoft.Extensions.Terminal;
using AgentLogs.Domain;
using AgentLogs.Services;

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

        Regex? regex = null;
        try
        {
            regex = new Regex(searchTerm, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }
        catch (RegexParseException)
        {
            // Fall back to literal search
        }

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
                var matchContext = FindMatch(entry, searchTerm, regex);
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
                terminal.AppendLine(Truncate(context, 70));
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

    private static string? FindMatch(Entry entry, string searchTerm, Regex? regex)
    {
        string? text = entry switch
        {
            UserEntry u => u.Content,
            AssistantEntry a => a.TextContent,
            _ => null
        };

        if (text != null && IsMatch(text, searchTerm, regex))
            return ExtractContext(text, searchTerm, regex);

        // Also search tool calls
        if (entry is AssistantEntry assistant)
        {
            foreach (var tool in assistant.ToolUses)
            {
                var toolText = $"{tool.Name}: {tool.FilePath ?? tool.Command ?? tool.Content ?? ""}";
                if (IsMatch(toolText, searchTerm, regex))
                    return ExtractContext(toolText, searchTerm, regex);
            }
        }

        return null;
    }

    private static bool IsMatch(string text, string searchTerm, Regex? regex)
    {
        if (regex != null)
            return regex.IsMatch(text);
        return text.Contains(searchTerm, StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractContext(string text, string searchTerm, Regex? regex)
    {
        int matchIndex;
        if (regex != null)
        {
            var match = regex.Match(text);
            matchIndex = match.Success ? match.Index : 0;
        }
        else
        {
            matchIndex = text.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase);
        }

        if (matchIndex < 0) matchIndex = 0;

        // Extract a window around the match
        var start = Math.Max(0, matchIndex - 30);
        var end = Math.Min(text.Length, matchIndex + searchTerm.Length + 40);
        var context = text[start..end].Replace('\n', ' ').Replace('\r', ' ');

        if (start > 0) context = "..." + context;
        if (end < text.Length) context += "...";

        return context;
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var firstLine = text.Replace('\n', ' ').Replace('\r', ' ');
        return firstLine.Length <= maxLength ? firstLine : firstLine[..(maxLength - 3)] + "...";
    }
}
