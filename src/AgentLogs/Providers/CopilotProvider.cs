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

        // Find all .jsonl files in the session-state directory
        foreach (var jsonlFile in Directory.EnumerateFiles(BasePath, "*.jsonl"))
        {
            yield return jsonlFile;
        }
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
}
