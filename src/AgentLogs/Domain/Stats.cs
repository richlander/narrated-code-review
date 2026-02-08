namespace AgentLogs.Domain;

/// <summary>
/// Aggregated statistics across sessions.
/// </summary>
public record Stats(
    int TotalSessions,
    int ActiveSessions,
    int TotalUserMessages,
    int TotalAssistantMessages,
    int TotalToolCalls,
    long TotalInputTokens,
    long TotalOutputTokens,
    IReadOnlyDictionary<string, int> ToolUsageCounts,
    IReadOnlyDictionary<int, int> HourlyActivity // Hour (0-23) -> count
);

/// <summary>
/// Daily statistics.
/// </summary>
public record DailyStats(
    DateOnly Date,
    int SessionCount,
    int UserMessages,
    int AssistantMessages,
    int ToolCalls,
    long InputTokens,
    long OutputTokens
);
