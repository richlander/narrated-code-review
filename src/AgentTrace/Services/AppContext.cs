using AgentLogs.Providers;

namespace AgentTrace.Services;

/// <summary>
/// Shared provider/store context — replaces the 35 lines of setup in Program.cs.
/// </summary>
public record TraceContext(
    ILogProvider BaseProvider,
    ILogProvider ScopedProvider,
    string? DetectedProjectDir,
    BookmarkStore? BookmarkStore,
    TagStore? TagStore);

public static class TraceContextFactory
{
    /// <summary>
    /// Creates the app context. Returns null and prints an error if targetDir is specified but not found.
    /// By default, includes both Claude Code and Copilot logs.
    /// </summary>
    public static TraceContext? Create(string? customPath, string? targetDir, bool showAll, string? projectFilter, bool claudeOnly = false, bool copilotOnly = false)
    {
        if (copilotOnly)
            return CreateCopilot(customPath, targetDir);

        if (claudeOnly)
            return CreateClaudeCode(customPath, targetDir, showAll, projectFilter);

        // Default: both providers
        return CreateCombined(customPath, targetDir, showAll, projectFilter);
    }

    private static TraceContext? CreateCombined(string? customPath, string? targetDir, bool showAll, string? projectFilter)
    {
        var claudeBase = customPath != null
            ? new ClaudeCodeProvider(customPath)
            : new ClaudeCodeProvider();

        var copilotBase = new CopilotProvider();

        string? detectedProjectDir = null;
        if (targetDir != null)
        {
            detectedProjectDir = claudeBase.FindProjectDir(targetDir);
            // Don't fail if Claude Code project not found — Copilot might still have sessions
        }
        else if (!showAll && projectFilter == null && customPath == null)
        {
            detectedProjectDir = claudeBase.FindProjectDir(Environment.CurrentDirectory);
        }

        // Build scoped providers
        ILogProvider claudeScoped = detectedProjectDir != null
            ? new ClaudeCodeProvider(claudeBase.BasePath) { ProjectDirFilter = detectedProjectDir }
            : claudeBase;

        ILogProvider copilotScoped = targetDir != null
            ? new CopilotProvider(copilotBase.BasePath) { WorkingDirectoryFilter = Path.GetFullPath(targetDir) }
            : copilotBase;

        var baseProvider = new CompositeProvider(claudeBase, copilotBase);
        var scopedProvider = new CompositeProvider(claudeScoped, copilotScoped);

        BookmarkStore? bookmarkStore = detectedProjectDir != null
            ? new BookmarkStore(claudeBase.GetProjectLogPath(detectedProjectDir))
            : null;

        TagStore? tagStore = detectedProjectDir != null
            ? new TagStore(claudeBase.GetProjectLogPath(detectedProjectDir))
            : null;

        return new TraceContext(baseProvider, scopedProvider, detectedProjectDir, bookmarkStore, tagStore);
    }

    private static TraceContext? CreateClaudeCode(string? customPath, string? targetDir, bool showAll, string? projectFilter)
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

    private static TraceContext CreateCopilot(string? customPath, string? targetDir)
    {
        var baseProvider = customPath != null
            ? new CopilotProvider(customPath)
            : new CopilotProvider();

        // When -C is specified, create a scoped provider that filters sessions
        // by scanning for file paths under the target directory
        var scopedProvider = targetDir != null
            ? new CopilotProvider(baseProvider.BasePath) { WorkingDirectoryFilter = Path.GetFullPath(targetDir) }
            : baseProvider;

        // Copilot has no project grouping — bookmarks/tags stored under BasePath
        var bookmarkStore = new BookmarkStore(baseProvider.BasePath);
        var tagStore = new TagStore(baseProvider.BasePath);

        return new TraceContext(baseProvider, scopedProvider, null, bookmarkStore, tagStore);
    }
}
