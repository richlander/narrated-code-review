using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentLogs.Parsing;

/// <summary>
/// Raw JSON entry from Claude Code JSONL logs.
/// </summary>
public class RawLogEntry
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("uuid")]
    public string? Uuid { get; set; }

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }

    [JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }

    [JsonPropertyName("cwd")]
    public string? Cwd { get; set; }

    [JsonPropertyName("message")]
    public RawMessage? Message { get; set; }

    [JsonPropertyName("parentUuid")]
    public string? ParentUuid { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("gitBranch")]
    public string? GitBranch { get; set; }

    [JsonPropertyName("isSidechain")]
    public bool? IsSidechain { get; set; }

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }
}

/// <summary>
/// Raw message structure.
/// </summary>
public class RawMessage
{
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("content")]
    public JsonElement Content { get; set; }

    [JsonPropertyName("usage")]
    public RawUsage? Usage { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("stop_reason")]
    public string? StopReason { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }
}

/// <summary>
/// Usage statistics from assistant messages.
/// </summary>
public class RawUsage
{
    [JsonPropertyName("input_tokens")]
    public int InputTokens { get; set; }

    [JsonPropertyName("output_tokens")]
    public int OutputTokens { get; set; }

    [JsonPropertyName("cache_creation_input_tokens")]
    public int? CacheCreationInputTokens { get; set; }

    [JsonPropertyName("cache_read_input_tokens")]
    public int? CacheReadInputTokens { get; set; }

    [JsonPropertyName("service_tier")]
    public string? ServiceTier { get; set; }
}

/// <summary>
/// Content block in assistant messages.
/// </summary>
public class RawContentBlock
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("input")]
    public JsonElement Input { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("thinking")]
    public string? Thinking { get; set; }

    [JsonPropertyName("signature")]
    public string? Signature { get; set; }

    [JsonPropertyName("tool_use_id")]
    public string? ToolUseId { get; set; }

    [JsonPropertyName("content")]
    public JsonElement ContentElement { get; set; }

    [JsonPropertyName("is_error")]
    public bool? IsError { get; set; }

    [JsonPropertyName("source")]
    public JsonElement Source { get; set; }
}

/// <summary>
/// Tool input for file operations.
/// </summary>
public class RawToolInput
{
    [JsonPropertyName("file_path")]
    public string? FilePath { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("command")]
    public string? Command { get; set; }

    [JsonPropertyName("old_string")]
    public string? OldString { get; set; }

    [JsonPropertyName("new_string")]
    public string? NewString { get; set; }

    [JsonPropertyName("pattern")]
    public string? Pattern { get; set; }
}

/// <summary>
/// Source generator context for AOT-compatible JSON serialization.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(RawLogEntry))]
[JsonSerializable(typeof(RawMessage))]
[JsonSerializable(typeof(RawUsage))]
[JsonSerializable(typeof(RawContentBlock))]
[JsonSerializable(typeof(RawToolInput))]
[JsonSerializable(typeof(List<RawContentBlock>))]
internal partial class LogJsonContext : JsonSerializerContext
{
}
