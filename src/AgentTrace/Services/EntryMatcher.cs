using System.Text.RegularExpressions;
using AgentLogs.Domain;

namespace AgentTrace.Services;

/// <summary>
/// Reusable entry matching logic — regex with literal fallback.
/// Extracted from SearchCommand for use by grep filtering and search.
/// </summary>
public class EntryMatcher
{
    private readonly string _searchTerm;
    private readonly Regex? _regex;

    public EntryMatcher(string searchTerm)
    {
        _searchTerm = searchTerm;
        try
        {
            _regex = new Regex(searchTerm, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }
        catch (RegexParseException)
        {
            // Fall back to literal search
        }
    }

    /// <summary>
    /// Returns true if the text matches the search term.
    /// </summary>
    public bool IsMatch(string text)
    {
        if (_regex != null)
            return _regex.IsMatch(text);
        return text.Contains(_searchTerm, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Searches an entry for matches. Returns context string or null.
    /// </summary>
    public string? FindMatch(Entry entry)
    {
        string? text = entry switch
        {
            UserEntry u => u.Content,
            AssistantEntry a => a.TextContent,
            _ => null
        };

        if (text != null && IsMatch(text))
            return ExtractContext(text);

        // Search tool results in user entry content blocks
        if (entry is UserEntry user)
        {
            foreach (var block in user.ContentBlocks)
            {
                if (block is ToolResultBlock toolResult && toolResult.Content != null && IsMatch(toolResult.Content))
                    return ExtractContext(toolResult.Content);
            }
        }

        // Also search tool calls
        if (entry is AssistantEntry assistant)
        {
            foreach (var tool in assistant.ToolUses)
            {
                var toolText = $"{tool.Name}: {tool.FilePath ?? tool.Command ?? tool.Content ?? ""}";
                if (IsMatch(toolText))
                    return ExtractContext(toolText);
            }
        }

        return null;
    }

    /// <summary>
    /// Counts matches in a list of entries.
    /// </summary>
    public int CountMatches(IReadOnlyList<Entry> entries)
    {
        var count = 0;
        foreach (var entry in entries)
        {
            if (FindMatch(entry) != null)
                count++;
        }
        return count;
    }

    /// <summary>
    /// Extracts context around the first match in the text.
    /// </summary>
    public string ExtractContext(string text)
    {
        int matchIndex;
        if (_regex != null)
        {
            var match = _regex.Match(text);
            matchIndex = match.Success ? match.Index : 0;
        }
        else
        {
            matchIndex = text.IndexOf(_searchTerm, StringComparison.OrdinalIgnoreCase);
        }

        if (matchIndex < 0) matchIndex = 0;

        var start = Math.Max(0, matchIndex - 30);
        var end = Math.Min(text.Length, matchIndex + _searchTerm.Length + 40);
        var context = text[start..end].Replace('\n', ' ').Replace('\r', ' ');

        if (start > 0) context = "..." + context;
        if (end < text.Length) context += "...";

        return context;
    }
}
