namespace AgentLogs.Domain;

/// <summary>
/// Base type for structured content blocks in messages.
/// </summary>
public abstract record ContentBlock;

/// <summary>
/// A text content block.
/// </summary>
public sealed record TextBlock(string Text) : ContentBlock;

/// <summary>
/// A tool use content block (assistant requesting a tool call).
/// </summary>
public sealed record ToolUseBlock(
    string ToolUseId,
    string Name,
    string? InputJson
) : ContentBlock;

/// <summary>
/// A tool result content block (user message containing tool output).
/// </summary>
public sealed record ToolResultBlock(
    string ToolUseId,
    string? ToolName,
    string? Content,
    bool IsError
) : ContentBlock;

/// <summary>
/// A thinking/reasoning block from the assistant.
/// </summary>
public sealed record ThinkingBlock(
    string Text,
    string? Signature
) : ContentBlock
{
    /// <summary>Character count of the thinking text.</summary>
    public int CharCount => Text.Length;
}

/// <summary>
/// An image content block.
/// </summary>
public sealed record ImageBlock(
    string MediaType,
    string Source
) : ContentBlock;
