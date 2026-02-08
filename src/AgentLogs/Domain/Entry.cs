namespace AgentLogs.Domain;

/// <summary>
/// Base record for all log entries.
/// </summary>
public abstract record Entry(
    string Uuid,
    DateTime Timestamp,
    string SessionId,
    string? ProjectPath
)
{
    /// <summary>UUID of the parent entry in the conversation tree.</summary>
    public string? ParentUuid { get; init; }

    /// <summary>Schema version of the log entry.</summary>
    public string? Version { get; init; }

    /// <summary>Git branch active when this entry was logged.</summary>
    public string? GitBranch { get; init; }

    /// <summary>Whether this entry is part of a sidechain (e.g. a sub-agent).</summary>
    public bool IsSidechain { get; init; }
}

/// <summary>
/// A user message entry.
/// </summary>
public record UserEntry(
    string Uuid,
    DateTime Timestamp,
    string SessionId,
    string? ProjectPath,
    string Content
) : Entry(Uuid, Timestamp, SessionId, ProjectPath)
{
    /// <summary>Structured content blocks (text + tool results).</summary>
    public IReadOnlyList<ContentBlock> ContentBlocks { get; init; } = [];
}

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
) : Entry(Uuid, Timestamp, SessionId, ProjectPath)
{
    /// <summary>Message ID from the API response.</summary>
    public string? MessageId { get; init; }

    /// <summary>Structured content blocks.</summary>
    public IReadOnlyList<ContentBlock> ContentBlocks { get; init; } = [];

    /// <summary>Thinking/reasoning blocks from the response.</summary>
    public IReadOnlyList<ThinkingBlock> ThinkingBlocks { get; init; } = [];

    /// <summary>Stop reason from the API (e.g. "end_turn", "tool_use").</summary>
    public string? StopReason { get; init; }

    /// <summary>Model ID used for this response.</summary>
    public string? Model { get; init; }
}

/// <summary>
/// A system prompt entry.
/// </summary>
public record SystemEntry(
    string Uuid,
    DateTime Timestamp,
    string SessionId,
    string? ProjectPath,
    string Content
) : Entry(Uuid, Timestamp, SessionId, ProjectPath);

/// <summary>
/// A conversation summary entry (context window compression).
/// </summary>
public record SummaryEntry(
    string Uuid,
    DateTime Timestamp,
    string SessionId,
    string? ProjectPath,
    string Summary
) : Entry(Uuid, Timestamp, SessionId, ProjectPath);

/// <summary>
/// A metadata entry for non-conversation events (e.g. file-history-snapshot, queue-operation, turn_end).
/// </summary>
public record MetadataEntry(
    string Uuid,
    DateTime Timestamp,
    string SessionId,
    string? ProjectPath,
    string EntryType
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
)
{
    /// <summary>Tool use ID for correlating with tool results.</summary>
    public string? ToolUseId { get; init; }
}

/// <summary>
/// Token usage statistics.
/// </summary>
public record TokenUsage(
    int InputTokens,
    int OutputTokens
)
{
    /// <summary>Tokens used to create cache entries.</summary>
    public int CacheCreationInputTokens { get; init; }

    /// <summary>Tokens read from cache.</summary>
    public int CacheReadInputTokens { get; init; }

    /// <summary>Service tier used (e.g. "default", "priority").</summary>
    public string? ServiceTier { get; init; }
}
