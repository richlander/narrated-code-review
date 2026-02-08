using System.Globalization;
using System.Text.Json;
using AgentLogs.Domain;

namespace AgentLogs.Parsing;

/// <summary>
/// Result of parsing a JSONL file, including entries and any parse errors.
/// </summary>
public record ParseResult(
    IReadOnlyList<Entry> Entries,
    IReadOnlyList<ParseError> Errors
);

/// <summary>
/// A parse error encountered while processing a JSONL line.
/// </summary>
public record ParseError(
    int LineNumber,
    string Message,
    string? RawLine
);

/// <summary>
/// Parses JSONL log files line-by-line into Entry records.
/// Supports two-pass parsing: first parse all lines, then resolve tool results.
/// </summary>
public static class EntryParser
{
    /// <summary>
    /// Parses all entries from a JSONL file (streaming, single-pass, backward compatible).
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
    /// Two-pass parse: reads entire file, parses all entry types, resolves tool results.
    /// Returns entries and collected errors.
    /// </summary>
    public static async Task<ParseResult> ParseFileFullAsync(string filePath)
    {
        if (!File.Exists(filePath))
            return new ParseResult([], []);

        var entries = new List<Entry>();
        var errors = new List<ParseError>();
        var lineNumber = 0;

        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 4096,
            useAsync: true);

        using var reader = new StreamReader(stream);

        // Pass 1: Parse all lines
        while (await reader.ReadLineAsync() is { } line)
        {
            lineNumber++;

            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                var entry = ParseLineFull(line);
                if (entry != null)
                    entries.Add(entry);
            }
            catch (JsonException ex)
            {
                errors.Add(new ParseError(lineNumber, ex.Message, TruncateForError(line)));
            }
        }

        // Pass 2: Resolve tool results
        var toolNameMap = ToolResultResolver.BuildToolNameMap(entries);
        var resolved = ToolResultResolver.ResolveToolResults(entries, toolNameMap);

        // Deduplicate by message.id for token usage
        resolved = DeduplicateByMessageId(resolved);

        return new ParseResult(resolved, errors);
    }

    /// <summary>
    /// Parses a single JSONL line into an Entry (backward compatible - user/assistant only).
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

    /// <summary>
    /// Parses a single JSONL line into an Entry (full - all 7 entry types).
    /// </summary>
    public static Entry? ParseLineFull(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        var raw = JsonSerializer.Deserialize(line, LogJsonContext.Default.RawLogEntry);
        if (raw == null)
            return null;

        var entry = raw.Type?.ToLowerInvariant() switch
        {
            "user" => ParseUserEntryFull(raw),
            "assistant" => ParseAssistantEntryFull(raw),
            "system" => ParseSystemEntry(raw),
            "summary" => ParseSummaryEntry(raw),
            _ => ParseMetadataEntry(raw)
        };

        return entry;
    }

    private static Entry? SetBaseProperties(Entry? entry, RawLogEntry raw)
    {
        if (entry == null)
            return null;

        return entry with
        {
            ParentUuid = raw.ParentUuid,
            Version = raw.Version,
            GitBranch = raw.GitBranch,
            IsSidechain = raw.IsSidechain ?? false
        };
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

    private static Entry? ParseUserEntryFull(RawLogEntry raw)
    {
        if (raw.Uuid == null || raw.SessionId == null || raw.Message == null)
            return null;

        var timestamp = ParseTimestamp(raw.Timestamp);
        var content = ExtractUserContent(raw.Message.Content);
        var contentBlocks = ParseContentBlocks(raw.Message.Content);

        var entry = new UserEntry(
            Uuid: raw.Uuid,
            Timestamp: timestamp,
            SessionId: raw.SessionId,
            ProjectPath: raw.Cwd,
            Content: content ?? string.Empty
        )
        {
            ContentBlocks = contentBlocks
        };

        return SetBaseProperties(entry, raw);
    }

    private static AssistantEntry? ParseAssistantEntry(RawLogEntry raw)
    {
        if (raw.Uuid == null || raw.SessionId == null || raw.Message == null)
            return null;

        var timestamp = ParseTimestamp(raw.Timestamp);
        var (textContent, toolUses) = ExtractAssistantContent(raw.Message.Content);
        var usage = ParseTokenUsage(raw.Message.Usage);

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

    private static Entry? ParseAssistantEntryFull(RawLogEntry raw)
    {
        if (raw.Uuid == null || raw.SessionId == null || raw.Message == null)
            return null;

        var timestamp = ParseTimestamp(raw.Timestamp);
        var (textContent, toolUses) = ExtractAssistantContent(raw.Message.Content);
        var usage = ParseTokenUsage(raw.Message.Usage);
        var contentBlocks = ParseContentBlocks(raw.Message.Content);
        var thinkingBlocks = ExtractThinkingBlocks(contentBlocks);

        var entry = new AssistantEntry(
            Uuid: raw.Uuid,
            Timestamp: timestamp,
            SessionId: raw.SessionId,
            ProjectPath: raw.Cwd,
            TextContent: textContent,
            ToolUses: toolUses,
            Usage: usage
        )
        {
            MessageId = raw.Message.Id,
            ContentBlocks = contentBlocks,
            ThinkingBlocks = thinkingBlocks,
            StopReason = raw.Message.StopReason,
            Model = raw.Message.Model
        };

        return SetBaseProperties(entry, raw);
    }

    private static Entry? ParseSystemEntry(RawLogEntry raw)
    {
        if (raw.Uuid == null || raw.SessionId == null)
            return null;

        var timestamp = ParseTimestamp(raw.Timestamp);
        var content = raw.Message != null
            ? ExtractUserContent(raw.Message.Content) ?? string.Empty
            : string.Empty;

        var entry = new SystemEntry(
            Uuid: raw.Uuid,
            Timestamp: timestamp,
            SessionId: raw.SessionId,
            ProjectPath: raw.Cwd,
            Content: content
        );

        return SetBaseProperties(entry, raw);
    }

    private static Entry? ParseSummaryEntry(RawLogEntry raw)
    {
        if (raw.Uuid == null || raw.SessionId == null)
            return null;

        var timestamp = ParseTimestamp(raw.Timestamp);
        var summary = raw.Summary ?? string.Empty;

        if (string.IsNullOrEmpty(summary) && raw.Message != null)
        {
            summary = ExtractUserContent(raw.Message.Content) ?? string.Empty;
        }

        var entry = new SummaryEntry(
            Uuid: raw.Uuid,
            Timestamp: timestamp,
            SessionId: raw.SessionId,
            ProjectPath: raw.Cwd,
            Summary: summary
        );

        return SetBaseProperties(entry, raw);
    }

    private static Entry? ParseMetadataEntry(RawLogEntry raw)
    {
        if (raw.Uuid == null || raw.SessionId == null || raw.Type == null)
            return null;

        var timestamp = ParseTimestamp(raw.Timestamp);

        var entry = new MetadataEntry(
            Uuid: raw.Uuid,
            Timestamp: timestamp,
            SessionId: raw.SessionId,
            ProjectPath: raw.Cwd,
            EntryType: raw.Type
        );

        return SetBaseProperties(entry, raw);
    }

    private static TokenUsage? ParseTokenUsage(RawUsage? raw)
    {
        if (raw == null)
            return null;

        return new TokenUsage(raw.InputTokens, raw.OutputTokens)
        {
            CacheCreationInputTokens = raw.CacheCreationInputTokens ?? 0,
            CacheReadInputTokens = raw.CacheReadInputTokens ?? 0,
            ServiceTier = raw.ServiceTier
        };
    }

    private static IReadOnlyList<ContentBlock> ParseContentBlocks(JsonElement content)
    {
        if (content.ValueKind != JsonValueKind.Array)
            return [];

        var blocks = new List<ContentBlock>();

        foreach (var item in content.EnumerateArray())
        {
            try
            {
                var raw = JsonSerializer.Deserialize(item.GetRawText(), LogJsonContext.Default.RawContentBlock);
                if (raw == null) continue;

                var block = ContentBlockParser.Parse(raw);
                if (block != null)
                    blocks.Add(block);
            }
            catch (JsonException)
            {
                // Skip malformed content blocks
            }
        }

        return blocks;
    }

    private static IReadOnlyList<ThinkingBlock> ExtractThinkingBlocks(IReadOnlyList<ContentBlock> blocks)
    {
        var thinking = new List<ThinkingBlock>();
        foreach (var block in blocks)
        {
            if (block is ThinkingBlock tb)
                thinking.Add(tb);
        }
        return thinking;
    }

    private static IReadOnlyList<Entry> DeduplicateByMessageId(IReadOnlyList<Entry> entries)
    {
        var seenMessageIds = new HashSet<string>();
        var result = new List<Entry>(entries.Count);

        foreach (var entry in entries)
        {
            if (entry is AssistantEntry assistant && !string.IsNullOrEmpty(assistant.MessageId))
            {
                if (!seenMessageIds.Add(assistant.MessageId))
                {
                    // Duplicate message ID — keep entry but clear usage to avoid double-counting
                    result.Add(assistant with { Usage = null });
                    continue;
                }
            }

            result.Add(entry);
        }

        return result;
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
                    {
                        // Enrich with tool_use ID
                        if (!string.IsNullOrEmpty(block.Id))
                        {
                            toolUse = toolUse with { ToolUseId = block.Id };
                        }
                        toolUses.Add(toolUse);
                    }
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

    private static string TruncateForError(string line)
    {
        return line.Length > 200 ? line[..197] + "..." : line;
    }
}
