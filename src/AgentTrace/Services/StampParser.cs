using System.Text.RegularExpressions;
using AgentLogs.Domain;

namespace AgentTrace.Services;

/// <summary>
/// Shared stamp parsing utilities — eliminates duplication across commands.
/// </summary>
public static partial class StampParser
{
    /// <summary>
    /// Extracts the full stamp text from an entry (checks user content blocks, user content, assistant text).
    /// </summary>
    public static string? ExtractStampText(Entry entry, EntryMatcher matcher)
    {
        if (entry is UserEntry user)
        {
            foreach (var block in user.ContentBlocks)
            {
                if (block is ToolResultBlock toolResult && toolResult.Content != null
                    && matcher.IsMatch(toolResult.Content) && toolResult.Content.Contains("«/stamp»"))
                    return toolResult.Content;
            }
            if (user.Content != null && matcher.IsMatch(user.Content) && user.Content.Contains("«/stamp»"))
                return user.Content;
        }

        if (entry is AssistantEntry assistant && assistant.TextContent != null
            && matcher.IsMatch(assistant.TextContent) && assistant.TextContent.Contains("«/stamp»"))
            return assistant.TextContent;

        return null;
    }

    /// <summary>
    /// Parses a field value from stamp text (e.g., "session: abc123" → "abc123").
    /// </summary>
    public static string? ParseStampField(string text, string field)
    {
        var match = Regex.Match(text,
            $@"^\s*{field}:\s*(.+)$", RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    /// <summary>
    /// Parses the timestamp from a stamp block (e.g., «stamp:2026-02-13T08:15:00Z»).
    /// </summary>
    public static string? ParseStampTimestamp(string text)
    {
        var match = StampTimestampRegex().Match(text);
        return match.Success ? match.Groups[1].Value : null;
    }

    [GeneratedRegex(@"«stamp:([^»]+)»")]
    private static partial Regex StampTimestampRegex();

    /// <summary>
    /// Extracts the full decision text from an entry (same structure as ExtractStampText but for decisions).
    /// </summary>
    public static string? ExtractDecisionText(Entry entry, EntryMatcher matcher)
    {
        if (entry is UserEntry user)
        {
            foreach (var block in user.ContentBlocks)
            {
                if (block is ToolResultBlock toolResult && toolResult.Content != null
                    && matcher.IsMatch(toolResult.Content) && toolResult.Content.Contains("«/decision»"))
                    return toolResult.Content;
            }
            if (user.Content != null && matcher.IsMatch(user.Content) && user.Content.Contains("«/decision»"))
                return user.Content;
        }

        if (entry is AssistantEntry assistant && assistant.TextContent != null
            && matcher.IsMatch(assistant.TextContent) && assistant.TextContent.Contains("«/decision»"))
            return assistant.TextContent;

        return null;
    }

    /// <summary>
    /// Parses the timestamp from a decision block (e.g., «decision:2026-02-13T14:30:00Z»).
    /// </summary>
    public static string? ParseDecisionTimestamp(string text)
    {
        var match = DecisionTimestampRegex().Match(text);
        return match.Success ? match.Groups[1].Value : null;
    }

    [GeneratedRegex(@"«decision:([^»]+)»")]
    private static partial Regex DecisionTimestampRegex();
}
