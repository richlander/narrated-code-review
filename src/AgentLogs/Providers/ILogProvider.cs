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

    /// <summary>
    /// Creates a line parser for incremental (live-tail) parsing.
    /// </summary>
    Func<string, Entry?> CreateLineParser() => _ => null;

    /// <summary>
    /// Creates a line parser for a specific file (used by composite providers).
    /// </summary>
    Func<string, Entry?> CreateLineParserForFile(string filePath) => CreateLineParser();

    /// <summary>
    /// Finds the provider-specific project directory for a given working directory.
    /// Returns null if the provider doesn't support project grouping.
    /// </summary>
    string? FindProjectDir(string workingDirectory) => null;

    /// <summary>
    /// Gets the full filesystem path to a project's log directory.
    /// Returns BasePath when projectDirName is null.
    /// </summary>
    string GetProjectLogPath(string? projectDirName) => BasePath;

    /// <summary>
    /// Extracts session ID from a log file path.
    /// Default implementation uses the file name without extension.
    /// </summary>
    string ExtractSessionId(string filePath) => Path.GetFileNameWithoutExtension(filePath);
}
