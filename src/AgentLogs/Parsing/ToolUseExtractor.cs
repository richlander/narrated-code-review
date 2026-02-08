using System.Text.Json;
using AgentLogs.Domain;

namespace AgentLogs.Parsing;

/// <summary>
/// Extracts structured tool use information from raw JSON input.
/// </summary>
public static class ToolUseExtractor
{
    /// <summary>
    /// Extracts a ToolUse record from a content block.
    /// </summary>
    public static ToolUse? Extract(RawContentBlock block)
    {
        if (block.Type != "tool_use" || string.IsNullOrEmpty(block.Name))
            return null;

        var input = ParseToolInput(block.Input);
        var filePath = ExtractFilePath(block.Name, input);
        var content = ExtractContent(block.Name, input);
        var oldContent = ExtractOldContent(block.Name, input);
        var command = ExtractCommand(block.Name, input);

        return new ToolUse(
            Name: block.Name,
            FilePath: filePath,
            Content: content,
            OldContent: oldContent,
            Command: command
        );
    }

    private static RawToolInput? ParseToolInput(JsonElement input)
    {
        if (input.ValueKind == JsonValueKind.Undefined || input.ValueKind == JsonValueKind.Null)
            return null;

        try
        {
            return JsonSerializer.Deserialize(input.GetRawText(), LogJsonContext.Default.RawToolInput);
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractFilePath(string toolName, RawToolInput? input)
    {
        if (input == null) return null;

        return toolName.ToLowerInvariant() switch
        {
            "read" or "write" or "edit" or "notebookedit" => input.FilePath ?? input.Path,
            "glob" or "grep" => input.Path,
            _ => input.FilePath ?? input.Path
        };
    }

    private static string? ExtractContent(string toolName, RawToolInput? input)
    {
        if (input == null) return null;

        return toolName.ToLowerInvariant() switch
        {
            "write" => input.Content,
            "edit" => input.NewString,
            _ => null
        };
    }

    private static string? ExtractOldContent(string toolName, RawToolInput? input)
    {
        if (input == null) return null;

        return toolName.ToLowerInvariant() switch
        {
            "edit" => input.OldString,
            _ => null
        };
    }

    private static string? ExtractCommand(string toolName, RawToolInput? input)
    {
        if (input == null) return null;

        return toolName.ToLowerInvariant() switch
        {
            "bash" => input.Command,
            "grep" => input.Pattern,
            "glob" => input.Pattern,
            _ => null
        };
    }
}
