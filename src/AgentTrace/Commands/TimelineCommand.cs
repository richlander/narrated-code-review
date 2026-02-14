using System.Text.RegularExpressions;
using AgentLogs.Domain;
using AgentLogs.Services;
using AgentTrace.Services;
using Markout;

namespace AgentTrace.Commands;

/// <summary>
/// Cross-session timeline: chronological view of turns across sessions.
/// </summary>
public static partial class TimelineCommand
{
    public static void Execute(SessionManager sessionManager, string? projectFilter = null,
        string? afterFilter = null)
    {
        var sessions = sessionManager.GetAllSessions();

        if (projectFilter != null)
        {
            sessions = sessions
                .Where(s => s.ProjectName != null &&
                    s.ProjectName.Contains(projectFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        DateTime? afterTime = afterFilter != null ? ParseRelativeTime(afterFilter) : null;

        var timelineEntries = new List<TimelineEntry>();

        foreach (var session in sessions)
        {
            var entries = sessionManager.GetSessionEntries(session.Id);
            if (entries.Count == 0) continue;

            var conversation = new Conversation(session.Id, entries);
            var shortId = session.Id.Length > 7 ? session.Id[..7] : session.Id;

            foreach (var turn in conversation.Turns)
            {
                if (afterTime.HasValue && turn.StartTime < afterTime.Value)
                    continue;

                var content = turn.UserMessage;
                if (content != null)
                {
                    var contInfo = ContinuationDetector.Parse(content);
                    if (contInfo.IsContinuation)
                        content = contInfo.SubstantiveContent ?? "[continuation]";
                }
                content ??= turn.AssistantMessages
                    .Select(a => a.TextContent)
                    .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));

                timelineEntries.Add(new TimelineEntry(
                    turn.StartTime,
                    shortId,
                    turn.Number + 1,
                    turn.ToolUses.Count,
                    turn.Duration,
                    Formatting.Truncate(content ?? "", 60)));
            }
        }

        // Sort chronologically
        timelineEntries.Sort((a, b) => a.Time.CompareTo(b.Time));

        var writer = new MarkoutWriter(Console.Out);
        writer.WriteHeading(1, $"Timeline ({timelineEntries.Count} turns)");
        if (afterFilter != null)
            writer.WriteField("After", afterFilter);

        writer.WriteTableStart("Time", "Session", "Turn", "Tools", "Duration", "Content");

        foreach (var entry in timelineEntries)
        {
            writer.WriteTableRow(
                entry.Time.ToLocalTime().ToString("MM-dd HH:mm"),
                entry.SessionId,
                entry.Turn.ToString(),
                entry.Tools.ToString(),
                Formatting.FormatDuration(entry.Duration),
                entry.Content);
        }

        writer.WriteTableEnd();
        writer.Flush();
    }

    /// <summary>
    /// Parses relative time expressions like "2h ago", "1d ago", "30m ago"
    /// or absolute dates like "2026-02-12".
    /// </summary>
    public static DateTime? ParseRelativeTime(string input)
    {
        var match = RelativeTimeRegex().Match(input);
        if (match.Success)
        {
            var amount = int.Parse(match.Groups[1].Value);
            var unit = match.Groups[2].Value;
            var offset = unit switch
            {
                "m" => TimeSpan.FromMinutes(amount),
                "h" => TimeSpan.FromHours(amount),
                "d" => TimeSpan.FromDays(amount),
                "w" => TimeSpan.FromDays(amount * 7),
                _ => TimeSpan.Zero
            };
            return DateTime.UtcNow - offset;
        }

        if (DateTime.TryParse(input, out var dt))
            return dt.ToUniversalTime();

        return null;
    }

    [GeneratedRegex(@"^(\d+)\s*(m|h|d|w)\s*ago$", RegexOptions.IgnoreCase)]
    private static partial Regex RelativeTimeRegex();

    private record TimelineEntry(DateTime Time, string SessionId, int Turn, int Tools, TimeSpan Duration, string Content);
}
