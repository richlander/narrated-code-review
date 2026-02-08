using AgentLogs.Domain;

namespace AgentLogs.Services;

/// <summary>
/// Aggregates statistics across sessions.
/// </summary>
public class StatsAggregator
{
    /// <summary>
    /// Computes aggregate statistics from sessions.
    /// </summary>
    public Stats ComputeStats(IEnumerable<Session> sessions)
    {
        var sessionList = sessions.ToList();

        var toolUsage = new Dictionary<string, int>();
        var hourlyActivity = new Dictionary<int, int>();

        foreach (var session in sessionList)
        {
            // Count tool usage
            foreach (var change in session.Changes)
            {
                foreach (var tool in change.Tools)
                {
                    var name = tool.Name.ToLowerInvariant();
                    toolUsage.TryGetValue(name, out var count);
                    toolUsage[name] = count + 1;
                }
            }

            // Track hourly activity
            var hour = session.StartTime.ToLocalTime().Hour;
            hourlyActivity.TryGetValue(hour, out var hourCount);
            hourlyActivity[hour] = hourCount + 1;
        }

        return new Stats(
            TotalSessions: sessionList.Count,
            ActiveSessions: sessionList.Count(s => s.IsActive),
            TotalUserMessages: sessionList.Sum(s => s.UserMessageCount),
            TotalAssistantMessages: sessionList.Sum(s => s.AssistantMessageCount),
            TotalToolCalls: sessionList.Sum(s => s.ToolCallCount),
            TotalInputTokens: sessionList.Sum(s => (long)s.TotalInputTokens),
            TotalOutputTokens: sessionList.Sum(s => (long)s.TotalOutputTokens),
            ToolUsageCounts: toolUsage,
            HourlyActivity: hourlyActivity
        );
    }

    /// <summary>
    /// Computes daily statistics.
    /// </summary>
    public IReadOnlyList<DailyStats> ComputeDailyStats(IEnumerable<Session> sessions, int days = 7)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var sessionList = sessions.ToList();

        var result = new List<DailyStats>();

        for (int i = 0; i < days; i++)
        {
            var date = today.AddDays(-i);
            var daySessions = sessionList
                .Where(s => DateOnly.FromDateTime(s.StartTime.ToLocalTime()) == date)
                .ToList();

            result.Add(new DailyStats(
                Date: date,
                SessionCount: daySessions.Count,
                UserMessages: daySessions.Sum(s => s.UserMessageCount),
                AssistantMessages: daySessions.Sum(s => s.AssistantMessageCount),
                ToolCalls: daySessions.Sum(s => s.ToolCallCount),
                InputTokens: daySessions.Sum(s => (long)s.TotalInputTokens),
                OutputTokens: daySessions.Sum(s => (long)s.TotalOutputTokens)
            ));
        }

        return result;
    }
}
