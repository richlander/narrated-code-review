namespace AgentTrace.Services;

/// <summary>
/// Shared formatting utilities — eliminates duplication across commands.
/// </summary>
public static class Formatting
{
    public static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalSeconds < 60)
            return $"{(int)duration.TotalSeconds}s";
        if (duration.TotalMinutes < 60)
            return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
        return $"{(int)duration.TotalHours}h {duration.Minutes}m";
    }

    /// <summary>
    /// Formats a time span as a human-readable age string.
    /// With suffix: "5m ago", "3h ago", "2d ago"
    /// Without suffix: "5m", "3h 15m", "2d"
    /// </summary>
    public static string FormatAge(TimeSpan age, bool withSuffix = true)
    {
        var suffix = withSuffix ? " ago" : "";
        if (age.TotalMinutes < 60)
            return $"{(int)age.TotalMinutes}m{suffix}";
        if (age.TotalHours < 24)
        {
            if (withSuffix)
                return $"{(int)age.TotalHours}h{suffix}";
            return $"{(int)age.TotalHours}h {age.Minutes}m";
        }
        return $"{(int)age.TotalDays}d{suffix}";
    }

    /// <summary>
    /// Formats git file list output as comma-separated or "(none)".
    /// </summary>
    public static string FormatFileList(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return "(none)";

        var files = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(f => f.Trim())
            .Where(f => f.Length > 0)
            .ToArray();

        return files.Length == 0 ? "(none)" : string.Join(", ", files);
    }

    /// <summary>
    /// Collapses text to a single line and truncates with "..." if longer than maxLength.
    /// </summary>
    public static string Truncate(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var singleLine = text.ReplaceLineEndings(" ");
        if (singleLine.Length <= maxLength)
            return singleLine;
        return string.Concat(singleLine.AsSpan(0, maxLength), "...");
    }
}
