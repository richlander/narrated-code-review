namespace NarratedCodeReviewer.Domain;

/// <summary>
/// Base record for all log entries.
/// </summary>
public abstract record Entry(
    string Uuid,
    DateTime Timestamp,
    string SessionId,
    string? ProjectPath
);

/// <summary>
/// A user message entry.
/// </summary>
public record UserEntry(
    string Uuid,
    DateTime Timestamp,
    string SessionId,
    string? ProjectPath,
    string Content
) : Entry(Uuid, Timestamp, SessionId, ProjectPath);

/// <summary>
/// An assistant response entry.
/// </summary>
public record AssistantEntry(
    string Uuid,
    DateTime Timestamp,
    string SessionId,
    string? ProjectPath,
    string? TextContent,
    IReadOnlyList<ToolUse> ToolUses,
    TokenUsage? Usage
) : Entry(Uuid, Timestamp, SessionId, ProjectPath);

/// <summary>
/// A tool invocation by the assistant.
/// </summary>
public record ToolUse(
    string Name,
    string? FilePath,
    string? Content,
    string? OldContent,
    string? Command
);

/// <summary>
/// Token usage statistics.
/// </summary>
public record TokenUsage(
    int InputTokens,
    int OutputTokens
);
