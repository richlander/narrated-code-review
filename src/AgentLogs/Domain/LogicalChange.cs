namespace AgentLogs.Domain;

/// <summary>
/// A logical grouping of related tool uses.
/// </summary>
public record LogicalChange(
    string Id,
    DateTime StartTime,
    DateTime EndTime,
    string SessionId,
    string Description,
    IReadOnlyList<ToolUse> Tools,
    ChangeType Type
)
{
    /// <summary>
    /// Gets the affected file paths in this change.
    /// </summary>
    public IReadOnlyList<string> AffectedFiles =>
        Tools.Where(t => t.FilePath != null)
             .Select(t => t.FilePath!)
             .Distinct()
             .ToList();

    /// <summary>
    /// Duration of this change.
    /// </summary>
    public TimeSpan Duration => EndTime - StartTime;
}

/// <summary>
/// Type of logical change.
/// </summary>
public enum ChangeType
{
    Explore,    // Read, Grep, Glob only
    Write,      // New file creation
    Edit,       // Modifications to existing files
    Execute,    // Bash commands
    Mixed       // Combination
}
