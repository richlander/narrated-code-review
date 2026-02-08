using AgentLogs.Domain;

namespace AgentLogs.Parsing;

/// <summary>
/// Resolves tool result blocks by mapping tool_use_id to tool names.
/// Two-pass: first collect tool_use_id → name mappings from assistant entries,
/// then resolve tool names in user entries' tool result blocks.
/// </summary>
public static class ToolResultResolver
{
    /// <summary>
    /// Builds a map of tool_use_id to tool name from assistant entries.
    /// </summary>
    public static Dictionary<string, string> BuildToolNameMap(IEnumerable<Entry> entries)
    {
        var map = new Dictionary<string, string>();

        foreach (var entry in entries)
        {
            if (entry is not AssistantEntry assistant)
                continue;

            foreach (var block in assistant.ContentBlocks)
            {
                if (block is ToolUseBlock toolUse && !string.IsNullOrEmpty(toolUse.ToolUseId))
                {
                    map[toolUse.ToolUseId] = toolUse.Name;
                }
            }

            // Also check ToolUses for backward compat
            foreach (var toolUse in assistant.ToolUses)
            {
                if (!string.IsNullOrEmpty(toolUse.ToolUseId) && !map.ContainsKey(toolUse.ToolUseId))
                {
                    map[toolUse.ToolUseId] = toolUse.Name;
                }
            }
        }

        return map;
    }

    /// <summary>
    /// Resolves tool names in tool result blocks using the tool name map.
    /// Returns a new list of entries with resolved tool result blocks.
    /// </summary>
    public static IReadOnlyList<Entry> ResolveToolResults(
        IReadOnlyList<Entry> entries,
        Dictionary<string, string> toolNameMap)
    {
        var resolved = new List<Entry>(entries.Count);

        foreach (var entry in entries)
        {
            if (entry is UserEntry user && user.ContentBlocks.Count > 0)
            {
                var resolvedBlocks = ResolveBlocks(user.ContentBlocks, toolNameMap);
                if (resolvedBlocks != null)
                {
                    resolved.Add(user with { ContentBlocks = resolvedBlocks });
                    continue;
                }
            }

            resolved.Add(entry);
        }

        return resolved;
    }

    private static IReadOnlyList<ContentBlock>? ResolveBlocks(
        IReadOnlyList<ContentBlock> blocks,
        Dictionary<string, string> toolNameMap)
    {
        var anyChanged = false;
        var result = new List<ContentBlock>(blocks.Count);

        foreach (var block in blocks)
        {
            if (block is ToolResultBlock toolResult &&
                toolResult.ToolName == null &&
                !string.IsNullOrEmpty(toolResult.ToolUseId) &&
                toolNameMap.TryGetValue(toolResult.ToolUseId, out var name))
            {
                result.Add(toolResult with { ToolName = name });
                anyChanged = true;
            }
            else
            {
                result.Add(block);
            }
        }

        return anyChanged ? result : null;
    }
}
