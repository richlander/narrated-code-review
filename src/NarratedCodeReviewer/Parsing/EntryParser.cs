using System.Globalization;
using System.Text.Json;
using NarratedCodeReviewer.Domain;

namespace NarratedCodeReviewer.Parsing;

/// <summary>
/// Parses JSONL log files line-by-line into Entry records.
/// </summary>
public static class EntryParser
{
    /// <summary>
    /// Parses all entries from a JSONL file.
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

        while (await reader.ReadLineAsync() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var entry = ParseLine(line);
            if (entry != null)
                yield return entry;
        }
    }

    /// <summary>
    /// Parses a single JSONL line into an Entry.
    /// </summary>
    public static Entry? ParseLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        try
        {
            var raw = JsonSerializer.Deserialize(line, LogJsonContext.Default.RawLogEntry);
            if (raw == null)
                return null;

            return raw.Type?.ToLowerInvariant() switch
            {
                "user" => ParseUserEntry(raw),
                "assistant" => ParseAssistantEntry(raw),
                _ => null // Skip summary and other types for now
            };
        }
        catch (JsonException)
        {
            // Skip malformed JSON lines
            return null;
        }
    }

    private static UserEntry? ParseUserEntry(RawLogEntry raw)
    {
        if (raw.Uuid == null || raw.SessionId == null || raw.Message == null)
            return null;

        var timestamp = ParseTimestamp(raw.Timestamp);
        var content = ExtractUserContent(raw.Message.Content);

        return new UserEntry(
            Uuid: raw.Uuid,
            Timestamp: timestamp,
            SessionId: raw.SessionId,
            ProjectPath: raw.Cwd,
            Content: content ?? string.Empty
        );
    }

    private static AssistantEntry? ParseAssistantEntry(RawLogEntry raw)
    {
        if (raw.Uuid == null || raw.SessionId == null || raw.Message == null)
            return null;

        var timestamp = ParseTimestamp(raw.Timestamp);
        var (textContent, toolUses) = ExtractAssistantContent(raw.Message.Content);
        var usage = raw.Message.Usage != null
            ? new TokenUsage(raw.Message.Usage.InputTokens, raw.Message.Usage.OutputTokens)
            : null;

        return new AssistantEntry(
            Uuid: raw.Uuid,
            Timestamp: timestamp,
            SessionId: raw.SessionId,
            ProjectPath: raw.Cwd,
            TextContent: textContent,
            ToolUses: toolUses,
            Usage: usage
        );
    }

    private static DateTime ParseTimestamp(string? timestamp)
    {
        if (string.IsNullOrEmpty(timestamp))
            return DateTime.UtcNow;

        // Claude logs use ISO 8601 format
        if (DateTime.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
            return dt;

        return DateTime.UtcNow;
    }

    private static string? ExtractUserContent(JsonElement content)
    {
        // User content can be a string or array
        if (content.ValueKind == JsonValueKind.String)
            return content.GetString();

        if (content.ValueKind == JsonValueKind.Array)
        {
            var texts = new List<string>();
            foreach (var item in content.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    texts.Add(item.GetString() ?? string.Empty);
                }
                else if (item.TryGetProperty("text", out var textProp) && textProp.ValueKind == JsonValueKind.String)
                {
                    texts.Add(textProp.GetString() ?? string.Empty);
                }
            }
            return string.Join("\n", texts);
        }

        return null;
    }

    private static (string? TextContent, List<ToolUse> ToolUses) ExtractAssistantContent(JsonElement content)
    {
        var toolUses = new List<ToolUse>();
        var textParts = new List<string>();

        if (content.ValueKind != JsonValueKind.Array)
            return (null, toolUses);

        foreach (var item in content.EnumerateArray())
        {
            try
            {
                var block = JsonSerializer.Deserialize(item.GetRawText(), LogJsonContext.Default.RawContentBlock);
                if (block == null) continue;

                if (block.Type == "text" && !string.IsNullOrEmpty(block.Text))
                {
                    textParts.Add(block.Text);
                }
                else if (block.Type == "tool_use")
                {
                    var toolUse = ToolUseExtractor.Extract(block);
                    if (toolUse != null)
                        toolUses.Add(toolUse);
                }
            }
            catch (JsonException)
            {
                // Skip malformed content blocks
            }
        }

        var textContent = textParts.Count > 0 ? string.Join("\n", textParts) : null;
        return (textContent, toolUses);
    }
}
