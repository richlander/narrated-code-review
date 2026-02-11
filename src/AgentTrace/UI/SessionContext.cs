namespace AgentTrace.UI;

/// <summary>
/// Carries session identity into pagers for display in the status bar.
/// </summary>
public record SessionContext(string Id, string? ProjectName, DateTimeOffset StartTime, int Index, int TotalSessions);
