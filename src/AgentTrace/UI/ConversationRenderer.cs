using AgentLogs.Domain;

namespace AgentTrace.UI;

/// <summary>
/// Converts a Conversation into styled lines for display.
/// </summary>
public class ConversationRenderer
{
    private bool _showToolDetails;
    private bool _showThinking;

    public bool ShowToolDetails
    {
        get => _showToolDetails;
        set => _showToolDetails = value;
    }

    public bool ShowThinking
    {
        get => _showThinking;
        set => _showThinking = value;
    }

    /// <summary>
    /// Renders a conversation into styled lines.
    /// </summary>
    public List<StyledLine> Render(Conversation conversation)
    {
        var lines = new List<StyledLine>();

        foreach (var turn in conversation.Turns)
        {
            RenderTurn(turn, lines);
        }

        return lines;
    }

    private void RenderTurn(Turn turn, List<StyledLine> lines)
    {
        // Turn separator
        var separator = $"──── Turn {turn.Number + 1} ────";
        lines.Add(new StyledLine(separator, LineStyle.Separator, turn.Number));

        var assistantPrefixShown = false;

        foreach (var entry in turn.Entries)
        {
            switch (entry)
            {
                case UserEntry user:
                    RenderUserEntry(user, lines);
                    break;
                case AssistantEntry assistant:
                    RenderAssistantEntry(assistant, lines, ref assistantPrefixShown);
                    break;
                case SystemEntry system:
                    RenderSystemEntry(system, lines);
                    break;
                case SummaryEntry summary:
                    RenderSummaryEntry(summary, lines);
                    break;
            }
        }

        lines.Add(new StyledLine("", LineStyle.Empty, turn.Number));
    }

    private void RenderUserEntry(UserEntry user, List<StyledLine> lines)
    {
        var hasText = !string.IsNullOrWhiteSpace(user.Content);
        var hasToolResults = user.ContentBlocks.Any(b => b is ToolResultBlock);

        // Tool-result-only user entries: skip [user] header, just show tool results
        if (!hasText && !hasToolResults)
            return; // Nothing to render at all

        if (hasText)
        {
            lines.Add(new StyledLine("[user]", LineStyle.UserPrefix, -1));
            foreach (var textLine in user.Content.Split('\n'))
            {
                lines.Add(new StyledLine(textLine, LineStyle.UserText, -1));
            }
        }

        // Render tool results in user content blocks
        foreach (var block in user.ContentBlocks)
        {
            if (block is ToolResultBlock result)
            {
                var status = result.IsError ? "ERROR" : "OK";
                var name = result.ToolName ?? "tool";
                lines.Add(new StyledLine($"  < {name} [{status}]", LineStyle.ToolResult, -1));

                if (_showToolDetails && result.Content != null)
                {
                    foreach (var line in result.Content.Split('\n').Take(20))
                    {
                        lines.Add(new StyledLine($"    {line}", LineStyle.ToolResultContent, -1));
                    }
                }
            }
        }

        if (hasText)
            lines.Add(new StyledLine("", LineStyle.Empty, -1));
    }

    private void RenderAssistantEntry(AssistantEntry assistant, List<StyledLine> lines, ref bool prefixShown)
    {
        var hasContent = assistant.ThinkingBlocks.Count > 0
            || !string.IsNullOrWhiteSpace(assistant.TextContent)
            || assistant.ToolUses.Count > 0;

        // Show [assistant] prefix once per turn, before the first content
        if (hasContent && !prefixShown)
        {
            lines.Add(new StyledLine("[assistant]", LineStyle.AssistantPrefix, -1));
            prefixShown = true;
        }

        // Thinking blocks
        foreach (var thinking in assistant.ThinkingBlocks)
        {
            if (_showThinking)
            {
                lines.Add(new StyledLine($"  [thinking] {thinking.CharCount:N0} chars", LineStyle.ThinkingHeader, -1));
                foreach (var line in thinking.Text.Split('\n'))
                {
                    lines.Add(new StyledLine($"  {line}", LineStyle.ThinkingText, -1));
                }
            }
            else
            {
                lines.Add(new StyledLine($"  [thinking] {thinking.CharCount:N0} chars", LineStyle.ThinkingCollapsed, -1));
            }
        }

        // Text content
        if (!string.IsNullOrWhiteSpace(assistant.TextContent))
        {
            foreach (var textLine in assistant.TextContent.Split('\n'))
            {
                lines.Add(new StyledLine(textLine, LineStyle.AssistantText, -1));
            }
        }

        // Tool uses
        foreach (var tool in assistant.ToolUses)
        {
            RenderToolUse(tool, lines);
        }
    }

    private void RenderToolUse(ToolUse tool, List<StyledLine> lines)
    {
        var target = tool.FilePath ?? tool.Command ?? "";
        if (target.Length > 60)
        {
            // Show just filename for long paths
            target = tool.FilePath != null ? Path.GetFileName(tool.FilePath) : target[..57] + "...";
        }

        var summary = $"  > {tool.Name} ({target})";
        lines.Add(new StyledLine(summary, LineStyle.ToolUse, -1));

        if (_showToolDetails)
        {
            RenderToolDetails(tool, lines);
        }
    }

    private void RenderToolDetails(ToolUse tool, List<StyledLine> lines)
    {
        var name = tool.Name.ToLowerInvariant();

        switch (name)
        {
            case "edit":
                if (!string.IsNullOrEmpty(tool.OldContent))
                {
                    foreach (var line in tool.OldContent.Split('\n').Take(10))
                    {
                        lines.Add(new StyledLine($"    - {line}", LineStyle.DiffRemoved, -1));
                    }
                }
                if (!string.IsNullOrEmpty(tool.Content))
                {
                    foreach (var line in tool.Content.Split('\n').Take(10))
                    {
                        lines.Add(new StyledLine($"    + {line}", LineStyle.DiffAdded, -1));
                    }
                }
                break;

            case "write":
                if (!string.IsNullOrEmpty(tool.Content))
                {
                    lines.Add(new StyledLine("    [NEW FILE]", LineStyle.DiffAdded, -1));
                    var writeLines = tool.Content.Split('\n');
                    for (var i = 0; i < Math.Min(writeLines.Length, 10); i++)
                    {
                        lines.Add(new StyledLine($"    {i + 1,3} {writeLines[i]}", LineStyle.ToolDetailContent, -1));
                    }
                    if (writeLines.Length > 10)
                    {
                        lines.Add(new StyledLine($"    ... ({writeLines.Length - 10} more lines)", LineStyle.ToolDetailMuted, -1));
                    }
                }
                break;

            case "bash":
                if (!string.IsNullOrEmpty(tool.Command))
                {
                    lines.Add(new StyledLine($"    $ {tool.Command}", LineStyle.ToolDetailContent, -1));
                }
                break;

            case "grep" or "glob":
                if (!string.IsNullOrEmpty(tool.Command))
                {
                    lines.Add(new StyledLine($"    Pattern: {tool.Command}", LineStyle.ToolDetailContent, -1));
                }
                if (!string.IsNullOrEmpty(tool.FilePath))
                {
                    lines.Add(new StyledLine($"    Path: {tool.FilePath}", LineStyle.ToolDetailContent, -1));
                }
                break;

            case "read":
                if (!string.IsNullOrEmpty(tool.FilePath))
                {
                    lines.Add(new StyledLine($"    {tool.FilePath}", LineStyle.ToolDetailMuted, -1));
                }
                break;
        }
    }

    private void RenderSystemEntry(SystemEntry system, List<StyledLine> lines)
    {
        if (string.IsNullOrWhiteSpace(system.Content))
            return;

        lines.Add(new StyledLine("[system]", LineStyle.SystemPrefix, -1));
        var preview = system.Content.Length > 100 ? system.Content[..97] + "..." : system.Content;
        lines.Add(new StyledLine(preview, LineStyle.SystemText, -1));
    }

    private void RenderSummaryEntry(SummaryEntry summary, List<StyledLine> lines)
    {
        lines.Add(new StyledLine("[summary]", LineStyle.SummaryPrefix, -1));
        lines.Add(new StyledLine(summary.Summary, LineStyle.SummaryText, -1));
    }
}

/// <summary>
/// A styled line for display in the pager.
/// </summary>
public record StyledLine(string Text, LineStyle Style, int TurnNumber);

/// <summary>
/// Line styles for rendering.
/// </summary>
public enum LineStyle
{
    Empty,
    Separator,
    UserPrefix,
    UserText,
    AssistantPrefix,
    AssistantText,
    ToolUse,
    ToolResult,
    ToolResultContent,
    ToolDetailContent,
    ToolDetailMuted,
    ThinkingHeader,
    ThinkingText,
    ThinkingCollapsed,
    DiffAdded,
    DiffRemoved,
    SystemPrefix,
    SystemText,
    SummaryPrefix,
    SummaryText
}
