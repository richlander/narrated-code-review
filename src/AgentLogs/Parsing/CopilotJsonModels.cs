using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentLogs.Parsing;

/// <summary>
/// Base event from Copilot JSONL logs.
/// </summary>
public class CopilotLogEvent
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }

    [JsonPropertyName("parentId")]
    public string? ParentId { get; set; }

    [JsonPropertyName("data")]
    public JsonElement Data { get; set; }
}

/// <summary>
/// Session start event data.
/// </summary>
public class CopilotSessionStartData
{
    [JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("producer")]
    public string? Producer { get; set; }

    [JsonPropertyName("copilotVersion")]
    public string? CopilotVersion { get; set; }

    [JsonPropertyName("startTime")]
    public string? StartTime { get; set; }
}

/// <summary>
/// User message event data.
/// </summary>
public class CopilotUserMessageData
{
    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("transformedContent")]
    public string? TransformedContent { get; set; }

    [JsonPropertyName("attachments")]
    public JsonElement Attachments { get; set; }
}

/// <summary>
/// Tool request in an assistant message.
/// </summary>
public class CopilotToolRequest
{
    [JsonPropertyName("toolCallId")]
    public string? ToolCallId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("arguments")]
    public JsonElement Arguments { get; set; }
}

/// <summary>
/// Assistant message event data.
/// </summary>
public class CopilotAssistantMessageData
{
    [JsonPropertyName("messageId")]
    public string? MessageId { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("toolRequests")]
    public List<CopilotToolRequest>? ToolRequests { get; set; }
}

/// <summary>
/// Tool execution start event data.
/// </summary>
public class CopilotToolExecutionStartData
{
    [JsonPropertyName("toolCallId")]
    public string? ToolCallId { get; set; }

    [JsonPropertyName("toolName")]
    public string? ToolName { get; set; }

    [JsonPropertyName("arguments")]
    public JsonElement Arguments { get; set; }
}

/// <summary>
/// Tool execution complete event data.
/// </summary>
public class CopilotToolExecutionCompleteData
{
    [JsonPropertyName("toolCallId")]
    public string? ToolCallId { get; set; }

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("result")]
    public JsonElement Result { get; set; }
}

/// <summary>
/// Source generator context for AOT-compatible JSON serialization of Copilot logs.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CopilotLogEvent))]
[JsonSerializable(typeof(CopilotSessionStartData))]
[JsonSerializable(typeof(CopilotUserMessageData))]
[JsonSerializable(typeof(CopilotAssistantMessageData))]
[JsonSerializable(typeof(CopilotToolRequest))]
[JsonSerializable(typeof(CopilotToolExecutionStartData))]
[JsonSerializable(typeof(CopilotToolExecutionCompleteData))]
[JsonSerializable(typeof(List<CopilotToolRequest>))]
internal partial class CopilotJsonContext : JsonSerializerContext
{
}
