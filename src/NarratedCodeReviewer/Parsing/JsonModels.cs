using System.Text.Json;
using System.Text.Json.Serialization;

namespace NarratedCodeReviewer.Parsing;

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
