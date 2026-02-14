using AgentLogs.Providers;

namespace AgentTrace.Services;

/// <summary>
/// Shared provider/store context — replaces the 35 lines of setup in Program.cs.
/// </summary>
public record TraceContext(
    ClaudeCodeProvider BaseProvider,
    ClaudeCodeProvider ScopedProvider,
    string? DetectedProjectDir,
    BookmarkStore? BookmarkStore,
    TagStore? TagStore);

public static class TraceContextFactory
{
    /// <summary>
    /// Creates the app context. Returns null and prints an error if targetDir is specified but not found.
    /// </summary>
    public static TraceContext? Create(string? customPath, string? targetDir, bool showAll, string? projectFilter)
    {
        var baseProvider = customPath != null
            ? new ClaudeCodeProvider(customPath)
            : new ClaudeCodeProvider();

        string? detectedProjectDir = null;
        if (targetDir != null)
        {
            detectedProjectDir = baseProvider.FindProjectDir(targetDir);
            if (detectedProjectDir == null)
            {
                Console.Error.WriteLine($"No Claude Code sessions found for: {targetDir}");
                Console.Error.WriteLine($"  expected: {baseProvider.BasePath}/{ClaudeCodeProvider.EncodeProjectPath(targetDir)}");
                return null;
            }
        }
        else if (!showAll && projectFilter == null && customPath == null)
        {
            detectedProjectDir = baseProvider.FindProjectDir(Environment.CurrentDirectory);
        }

        var scopedProvider = detectedProjectDir != null
            ? new ClaudeCodeProvider(baseProvider.BasePath) { ProjectDirFilter = detectedProjectDir }
            : baseProvider;

        BookmarkStore? bookmarkStore = detectedProjectDir != null
            ? new BookmarkStore(baseProvider.GetProjectLogPath(detectedProjectDir))
            : null;

        TagStore? tagStore = detectedProjectDir != null
            ? new TagStore(baseProvider.GetProjectLogPath(detectedProjectDir))
            : null;

        return new TraceContext(baseProvider, scopedProvider, detectedProjectDir, bookmarkStore, tagStore);
    }
}
