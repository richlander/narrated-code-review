using AgentLogs.Domain;
using AgentLogs.Parsing;

namespace AgentLogs.Providers;

/// <summary>
/// Log provider for GitHub Copilot CLI sessions.
/// Reads from ~/.copilot/session-state/
/// </summary>
public class CopilotProvider : ILogProvider
{
    public string Name => "GitHub Copilot";

    public string BasePath { get; }

    /// <summary>
    /// When set, only discover sessions that contain file paths under this directory.
    /// Uses a fast text scan of the raw JSONL — no full parse needed.
    /// </summary>
    public string? WorkingDirectoryFilter { get; init; }

    public CopilotProvider()
        : this(GetDefaultBasePath())
    {
    }

    public CopilotProvider(string basePath)
    {
        BasePath = basePath;
    }

    private static string GetDefaultBasePath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".copilot", "session-state");
    }

    public IEnumerable<string> DiscoverLogFiles()
    {
        if (!Directory.Exists(BasePath))
            yield break;

        foreach (var subdir in Directory.EnumerateDirectories(BasePath))
        {
            var eventsFile = Path.Combine(subdir, "events.jsonl");
            if (!File.Exists(eventsFile))
                continue;

            if (WorkingDirectoryFilter != null && !SessionMatchesDirectory(eventsFile, WorkingDirectoryFilter))
                continue;

            yield return eventsFile;
        }
    }

    /// <summary>
    /// Fast text scan: checks whether a session file contains any absolute file paths
    /// under the given directory. Looks for "path":"/dir/..." patterns in raw JSONL.
    /// </summary>
    private static bool SessionMatchesDirectory(string filePath, string directory)
    {
        var normalized = directory.TrimEnd('/');
        var searchPattern = $"\"{normalized}/";

        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 4096);
        using var reader = new StreamReader(stream);

        while (reader.ReadLine() is { } line)
        {
            if (line.Contains(searchPattern, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    public async IAsyncEnumerable<Entry> GetAllEntriesAsync()
    {
        var seenIds = new HashSet<string>();

        foreach (var filePath in DiscoverLogFiles())
        {
            await foreach (var entry in GetEntriesFromFileAsync(filePath))
            {
                // Deduplicate by UUID
                if (seenIds.Add(entry.Uuid))
                {
                    yield return entry;
                }
            }
        }
    }

    public IAsyncEnumerable<Entry> GetEntriesFromFileAsync(string filePath)
    {
        return CopilotEntryParser.ParseFileAsync(filePath);
    }

    public string? ExtractProjectName(string filePath)
    {
        // For Copilot, we need to read the session.start event to get working directory
        // For now, return the session ID as the identifier
        return Path.GetFileNameWithoutExtension(filePath);
    }

    public Func<string, Entry?> CreateLineParser()
    {
        var state = new CopilotLineParserState();
        return state.ParseLine;
    }

    /// <summary>
    /// Extracts session ID from a log file path (UUID/events.jsonl format).
    /// </summary>
    public string ExtractSessionId(string filePath)
    {
        var parentDir = Path.GetDirectoryName(filePath);
        return parentDir != null ? Path.GetFileName(parentDir) : Path.GetFileNameWithoutExtension(filePath);
    }
}
