namespace NarratedCodeReviewer.Domain;

/// <summary>
/// Represents a Claude Code session.
/// </summary>
public record Session(
    string Id,
    string? ProjectPath,
    string? ProjectName,
    DateTime StartTime,
    DateTime LastActivityTime,
    bool IsActive,
    int UserMessageCount,
    int AssistantMessageCount,
    int ToolCallCount,
    IReadOnlyList<LogicalChange> Changes
)
{
    /// <summary>
    /// Gets the total token count for this session.
    /// </summary>
    public int TotalInputTokens { get; init; }
    public int TotalOutputTokens { get; init; }

    /// <summary>
    /// Duration of the session.
    /// </summary>
    public TimeSpan Duration => LastActivityTime - StartTime;
}
