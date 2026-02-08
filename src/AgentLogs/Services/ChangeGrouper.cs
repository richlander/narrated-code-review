using AgentLogs.Domain;

namespace AgentLogs.Services;

/// <summary>
/// Groups consecutive tool uses into logical changes.
/// </summary>
public class ChangeGrouper
{
    private readonly TimeSpan _maxGap;

    public ChangeGrouper(TimeSpan? maxGap = null)
    {
        _maxGap = maxGap ?? TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Groups entries into logical changes.
    /// </summary>
    public IReadOnlyList<LogicalChange> GroupChanges(IEnumerable<Entry> entries)
    {
        var changes = new List<LogicalChange>();
        var orderedEntries = entries.OrderBy(e => e.Timestamp).ToList();

        var currentToolUses = new List<(ToolUse Tool, DateTime Timestamp)>();
        DateTime? groupStartTime = null;
        string? currentSessionId = null;

        foreach (var entry in orderedEntries)
        {
            // User message interrupts current change group
            if (entry is UserEntry)
            {
                if (currentToolUses.Count > 0 && groupStartTime.HasValue && currentSessionId != null)
                {
                    changes.Add(CreateChange(currentToolUses, groupStartTime.Value, currentSessionId));
                }
                currentToolUses.Clear();
                groupStartTime = null;
                currentSessionId = entry.SessionId;
                continue;
            }

            if (entry is AssistantEntry assistant && assistant.ToolUses.Count > 0)
            {
                // Check if we should start a new group
                var shouldStartNewGroup =
                    currentToolUses.Count == 0 ||
                    currentSessionId != entry.SessionId ||
                    (currentToolUses.Count > 0 &&
                     entry.Timestamp - currentToolUses[^1].Timestamp > _maxGap);

                if (shouldStartNewGroup && currentToolUses.Count > 0 && groupStartTime.HasValue && currentSessionId != null)
                {
                    changes.Add(CreateChange(currentToolUses, groupStartTime.Value, currentSessionId));
                    currentToolUses.Clear();
                }

                if (currentToolUses.Count == 0)
                {
                    groupStartTime = entry.Timestamp;
                    currentSessionId = entry.SessionId;
                }

                foreach (var tool in assistant.ToolUses)
                {
                    currentToolUses.Add((tool, entry.Timestamp));
                }
            }
        }

        // Don't forget the last group
        if (currentToolUses.Count > 0 && groupStartTime.HasValue && currentSessionId != null)
        {
            changes.Add(CreateChange(currentToolUses, groupStartTime.Value, currentSessionId));
        }

        return changes;
    }

    private LogicalChange CreateChange(
        List<(ToolUse Tool, DateTime Timestamp)> toolUses,
        DateTime startTime,
        string sessionId)
    {
        var tools = toolUses.Select(t => t.Tool).ToList();
        var endTime = toolUses.Max(t => t.Timestamp);
        var changeType = DetermineChangeType(tools);
        var description = GenerateDescription(tools, changeType);

        return new LogicalChange(
            Id: Guid.NewGuid().ToString("N")[..8],
            StartTime: startTime,
            EndTime: endTime,
            SessionId: sessionId,
            Description: description,
            Tools: tools,
            Type: changeType
        );
    }

    private static ChangeType DetermineChangeType(IReadOnlyList<ToolUse> tools)
    {
        var hasWrite = tools.Any(t => t.Name.Equals("Write", StringComparison.OrdinalIgnoreCase));
        var hasEdit = tools.Any(t => t.Name.Equals("Edit", StringComparison.OrdinalIgnoreCase));
        var hasBash = tools.Any(t => t.Name.Equals("Bash", StringComparison.OrdinalIgnoreCase));
        var hasExplore = tools.Any(t =>
            t.Name.Equals("Read", StringComparison.OrdinalIgnoreCase) ||
            t.Name.Equals("Grep", StringComparison.OrdinalIgnoreCase) ||
            t.Name.Equals("Glob", StringComparison.OrdinalIgnoreCase));

        var modifyCount = (hasWrite ? 1 : 0) + (hasEdit ? 1 : 0) + (hasBash ? 1 : 0);

        if (modifyCount > 1)
            return ChangeType.Mixed;
        if (hasWrite)
            return ChangeType.Write;
        if (hasEdit)
            return ChangeType.Edit;
        if (hasBash)
            return ChangeType.Execute;
        if (hasExplore)
            return ChangeType.Explore;

        return ChangeType.Mixed;
    }

    private static string GenerateDescription(IReadOnlyList<ToolUse> tools, ChangeType changeType)
    {
        var files = tools
            .Where(t => t.FilePath != null)
            .Select(t => Path.GetFileName(t.FilePath!))
            .Distinct()
            .ToList();

        var fileCount = files.Count;

        return changeType switch
        {
            ChangeType.Explore when fileCount == 0 => "Explored codebase",
            ChangeType.Explore => fileCount == 1
                ? $"Read {files[0]}"
                : $"Explored {fileCount} files",
            ChangeType.Write when fileCount == 1 => $"Created {files[0]}",
            ChangeType.Write => $"Created {fileCount} files",
            ChangeType.Edit when fileCount == 1 => $"Edited {files[0]}",
            ChangeType.Edit => $"Edited {fileCount} files",
            ChangeType.Execute => tools.FirstOrDefault(t =>
                t.Name.Equals("Bash", StringComparison.OrdinalIgnoreCase))?.Command is { } cmd
                ? $"Ran: {TruncateCommand(cmd)}"
                : "Executed commands",
            ChangeType.Mixed when fileCount > 0 => $"Modified {fileCount} files",
            _ => $"Performed {tools.Count} operations"
        };
    }

    private static string TruncateCommand(string command)
    {
        var firstLine = command.Split('\n')[0].Trim();
        return firstLine.Length > 40 ? firstLine[..37] + "..." : firstLine;
    }
}
