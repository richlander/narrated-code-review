using AgentLogs.Domain;

namespace AgentLogs.Providers;

/// <summary>
/// Interface for log providers (Claude Code, Copilot, etc.).
/// </summary>
public interface ILogProvider
{
    /// <summary>
    /// Gets the name of this provider.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the base path for logs.
    /// </summary>
    string BasePath { get; }

    /// <summary>
    /// Discovers all log files.
    /// </summary>
    IEnumerable<string> DiscoverLogFiles();

    /// <summary>
    /// Parses entries from all discovered log files.
    /// </summary>
    IAsyncEnumerable<Entry> GetAllEntriesAsync();

    /// <summary>
    /// Parses entries from a specific log file.
    /// </summary>
    IAsyncEnumerable<Entry> GetEntriesFromFileAsync(string filePath);

    /// <summary>
    /// Extracts project name from a log file path.
    /// </summary>
    string? ExtractProjectName(string filePath);
}
