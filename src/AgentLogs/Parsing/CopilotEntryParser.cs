using System.Globalization;
using System.Text.Json;
using AgentLogs.Domain;

namespace AgentLogs.Parsing;

/// <summary>
/// Parses GitHub Copilot CLI JSONL log files into Entry records.
/// </summary>
public static class CopilotEntryParser
{
    /// <summary>
    /// Parses all entries from a Copilot JSONL file.
    /// </summary>
    public static async IAsyncEnumerable<Entry> ParseFileAsync(string filePath)
    {
        if (!File.Exists(filePath))
            yield break;

        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 4096,
            useAsync: true);

        using var reader = new StreamReader(stream);

        string? sessionId = null;

        while (await reader.ReadLineAsync() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var entry = ParseLine(line, ref sessionId);
            if (entry != null)
                yield return entry;
        }
    }

    /// <summary>
    /// Parses a single JSONL line into an Entry.
    /// </summary>
    public static Entry? ParseLine(string line, ref string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        try
        {
            var evt = JsonSerializer.Deserialize(line, CopilotJsonContext.Default.CopilotLogEvent);
            if (evt == null)
                return null;

            // Extract session ID from session.start event
            if (evt.Type == "session.start")
            {
                var startData = JsonSerializer.Deserialize(
                    evt.Data.GetRawText(),
                    CopilotJsonContext.Default.CopilotSessionStartData);
                sessionId = startData?.SessionId;
                return null; // Don't emit session.start as an Entry
            }

            var timestamp = ParseTimestamp(evt.Timestamp);
            var effectiveSessionId = sessionId ?? evt.Id ?? "unknown";

            return evt.Type switch
            {
                "user.message" => ParseUserMessage(evt, timestamp, effectiveSessionId),
                "assistant.message" => ParseAssistantMessage(evt, timestamp, effectiveSessionId),
                _ => null // Skip other event types
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static UserEntry? ParseUserMessage(CopilotLogEvent evt, DateTime timestamp, string sessionId)
    {
        if (evt.Id == null)
            return null;

        try
        {
            var data = JsonSerializer.Deserialize(
                evt.Data.GetRawText(),
                CopilotJsonContext.Default.CopilotUserMessageData);

            if (data == null)
                return null;

            // Use original content (not transformed which has datetime injected)
            var content = data.Content ?? data.TransformedContent ?? string.Empty;

            return new UserEntry(
                Uuid: evt.Id,
                Timestamp: timestamp,
                SessionId: sessionId,
                ProjectPath: null, // Copilot doesn't include project path in individual events
                Content: content
            );
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static AssistantEntry? ParseAssistantMessage(CopilotLogEvent evt, DateTime timestamp, string sessionId)
    {
        if (evt.Id == null)
            return null;

        try
        {
            var data = JsonSerializer.Deserialize(
                evt.Data.GetRawText(),
                CopilotJsonContext.Default.CopilotAssistantMessageData);

            if (data == null)
                return null;

            var toolUses = ExtractToolUses(data.ToolRequests);

            return new AssistantEntry(
                Uuid: evt.Id,
                Timestamp: timestamp,
                SessionId: sessionId,
                ProjectPath: null,
                TextContent: data.Content,
                ToolUses: toolUses,
                Usage: null // Copilot doesn't include token usage in events
            );
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static List<ToolUse> ExtractToolUses(List<CopilotToolRequest>? requests)
    {
        var toolUses = new List<ToolUse>();

        if (requests == null)
            return toolUses;

        foreach (var req in requests)
        {
            if (req.Name == null)
                continue;

            var toolUse = ExtractToolUse(req);
            if (toolUse != null)
                toolUses.Add(toolUse);
        }

        return toolUses;
    }

    private static ToolUse? ExtractToolUse(CopilotToolRequest req)
    {
        if (req.Name == null)
            return null;

        string? filePath = null;
        string? content = null;
        string? oldContent = null;
        string? command = null;

        if (req.Arguments.ValueKind == JsonValueKind.Object)
        {
            // Extract common tool arguments
            if (req.Arguments.TryGetProperty("path", out var pathProp))
                filePath = pathProp.GetString();
            else if (req.Arguments.TryGetProperty("file_path", out var filePathProp))
                filePath = filePathProp.GetString();

            if (req.Arguments.TryGetProperty("content", out var contentProp))
                content = contentProp.GetString();
            else if (req.Arguments.TryGetProperty("file_text", out var fileTextProp))
                content = fileTextProp.GetString();
            else if (req.Arguments.TryGetProperty("new_str", out var newStrProp))
                content = newStrProp.GetString();

            if (req.Arguments.TryGetProperty("old_str", out var oldStrProp))
                oldContent = oldStrProp.GetString();
            else if (req.Arguments.TryGetProperty("old_string", out var oldStringProp))
                oldContent = oldStringProp.GetString();

            if (req.Arguments.TryGetProperty("command", out var cmdProp))
                command = cmdProp.GetString();
        }

        return new ToolUse(
            Name: req.Name,
            FilePath: filePath,
            Content: content,
            OldContent: oldContent,
            Command: command
        );
    }

    private static DateTime ParseTimestamp(string? timestamp)
    {
        if (string.IsNullOrEmpty(timestamp))
            return DateTime.UtcNow;

        if (DateTime.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
            return dt;

        return DateTime.UtcNow;
    }
}
