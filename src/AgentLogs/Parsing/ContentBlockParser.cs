using System.Text.Json;
using AgentLogs.Domain;

namespace AgentLogs.Parsing;

/// <summary>
/// Converts RawContentBlock instances into typed ContentBlock domain objects.
/// </summary>
public static class ContentBlockParser
{
    /// <summary>
    /// Parses a raw content block into a typed ContentBlock.
    /// </summary>
    public static ContentBlock? Parse(RawContentBlock block)
    {
        return block.Type switch
        {
            "text" => ParseTextBlock(block),
            "tool_use" => ParseToolUseBlock(block),
            "tool_result" => ParseToolResultBlock(block),
            "thinking" => ParseThinkingBlock(block),
            "image" => ParseImageBlock(block),
            _ => null
        };
    }

    private static TextBlock? ParseTextBlock(RawContentBlock block)
    {
        if (string.IsNullOrEmpty(block.Text))
            return null;

        return new TextBlock(block.Text);
    }

    private static ToolUseBlock? ParseToolUseBlock(RawContentBlock block)
    {
        if (string.IsNullOrEmpty(block.Name))
            return null;

        var id = block.Id ?? "";
        string? inputJson = null;

        if (block.Input.ValueKind != JsonValueKind.Undefined &&
            block.Input.ValueKind != JsonValueKind.Null)
        {
            inputJson = block.Input.GetRawText();
        }

        return new ToolUseBlock(id, block.Name, inputJson);
    }

    private static ToolResultBlock? ParseToolResultBlock(RawContentBlock block)
    {
        var toolUseId = block.ToolUseId ?? "";
        var isError = block.IsError ?? false;

        string? content = ExtractToolResultContent(block.ContentElement);

        return new ToolResultBlock(toolUseId, ToolName: null, content, isError);
    }

    private static ThinkingBlock? ParseThinkingBlock(RawContentBlock block)
    {
        var text = block.Thinking ?? block.Text ?? "";
        if (string.IsNullOrEmpty(text))
            return null;

        return new ThinkingBlock(text, block.Signature);
    }

    private static ImageBlock? ParseImageBlock(RawContentBlock block)
    {
        if (block.Source.ValueKind != JsonValueKind.Object)
            return null;

        var mediaType = "image/png";
        var source = "";

        if (block.Source.TryGetProperty("media_type", out var mt))
            mediaType = mt.GetString() ?? mediaType;

        if (block.Source.TryGetProperty("data", out var data))
            source = data.GetString() ?? "";

        return new ImageBlock(mediaType, source);
    }

    private static string? ExtractToolResultContent(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String)
            return content.GetString();

        if (content.ValueKind == JsonValueKind.Array)
        {
            var parts = new List<string>();
            foreach (var item in content.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    parts.Add(item.GetString() ?? "");
                }
                else if (item.TryGetProperty("text", out var textProp))
                {
                    parts.Add(textProp.GetString() ?? "");
                }
            }
            return parts.Count > 0 ? string.Join("\n", parts) : null;
        }

        return null;
    }
}
