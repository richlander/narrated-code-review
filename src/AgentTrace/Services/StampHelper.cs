namespace AgentTrace.Services;

/// <summary>
/// Shared stamp utilities — session detection used by StampCommand and DecisionCommand.
/// </summary>
public static class StampHelper
{
    /// <summary>
    /// Detects the most recently modified session ID (short form) from the project log path.
    /// </summary>
    public static string? DetectSessionId(string? projectLogPath)
    {
        if (string.IsNullOrEmpty(projectLogPath) || !Directory.Exists(projectLogPath))
            return null;

        var mostRecent = Directory.EnumerateFiles(projectLogPath, "*.jsonl")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault();

        if (mostRecent == null)
            return null;

        var fullId = Path.GetFileNameWithoutExtension(mostRecent.Name);
        return fullId.Length > 7 ? fullId[..7] : fullId;
    }
}
