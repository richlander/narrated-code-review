using System.Text.RegularExpressions;
using AgentLogs.Domain;
using AgentLogs.Parsing;

namespace AgentLogs.Providers;

/// <summary>
/// Log provider for Claude Code sessions.
/// Reads from ~/.claude/projects/
/// </summary>
public partial class ClaudeCodeProvider : ILogProvider
{
    public string Name => "Claude Code";

    public string BasePath { get; }

    /// <summary>
    /// When set, only discover log files from this project directory name
    /// (e.g. "-home-rich-git-foo").
    /// </summary>
    public string? ProjectDirFilter { get; init; }

    public ClaudeCodeProvider()
        : this(GetDefaultBasePath())
    {
    }

    public ClaudeCodeProvider(string basePath)
    {
        BasePath = basePath;
    }

    private static string GetDefaultBasePath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".claude", "projects");
    }

    /// <summary>
    /// Encodes a directory path into Claude's project directory name format.
    /// E.g., /home/rich/git/foo → -home-rich-git-foo
    /// </summary>
    public static string EncodeProjectPath(string directoryPath)
    {
        var normalized = Path.GetFullPath(directoryPath).TrimEnd(Path.DirectorySeparatorChar);
        return normalized.Replace(Path.DirectorySeparatorChar, '-');
    }

    /// <summary>
    /// Finds the Claude project directory name for a given working directory.
    /// Returns the encoded directory name (e.g. "-home-rich-git-foo"), or null if not found.
    /// </summary>
    public string? FindProjectDir(string workingDirectory)
    {
        var encoded = EncodeProjectPath(workingDirectory);
        var projectDir = Path.Combine(BasePath, encoded);
        return Directory.Exists(projectDir) ? encoded : null;
    }

    /// <summary>
    /// Gets the full filesystem path to a project's log directory.
    /// </summary>
    public string GetProjectLogPath(string? projectDirName)
    {
        return projectDirName != null ? Path.Combine(BasePath, projectDirName) : BasePath;
    }

    public Func<string, Entry?> CreateLineParser() => EntryParser.ParseLineFull;

    public IEnumerable<string> DiscoverLogFiles()
    {
        if (!Directory.Exists(BasePath))
            yield break;

        IEnumerable<string> projectDirs;
        if (ProjectDirFilter != null)
        {
            var dir = Path.Combine(BasePath, ProjectDirFilter);
            projectDirs = Directory.Exists(dir) ? [dir] : [];
        }
        else
        {
            projectDirs = Directory.EnumerateDirectories(BasePath);
        }

        foreach (var projectDir in projectDirs)
        {
            foreach (var jsonlFile in Directory.EnumerateFiles(projectDir, "*.jsonl"))
            {
                yield return jsonlFile;
            }
        }
    }

    public async IAsyncEnumerable<Entry> GetAllEntriesAsync()
    {
        var seenUuids = new HashSet<string>();

        foreach (var filePath in DiscoverLogFiles())
        {
            await foreach (var entry in GetEntriesFromFileAsync(filePath))
            {
                // Deduplicate by UUID
                if (seenUuids.Add(entry.Uuid))
                {
                    yield return entry;
                }
            }
        }
    }

    public async IAsyncEnumerable<Entry> GetEntriesFromFileAsync(string filePath)
    {
        var result = await EntryParser.ParseFileFullAsync(filePath);
        foreach (var entry in result.Entries)
            yield return entry;
    }

    public string? ExtractProjectName(string filePath)
    {
        // Path format: ~/.claude/projects/-Users-rich-git-projectname/sessionid.jsonl
        // Extract project name from the directory name

        var dirName = Path.GetFileName(Path.GetDirectoryName(filePath));
        if (string.IsNullOrEmpty(dirName))
            return null;

        // The directory name is the path with separators replaced by dashes
        // e.g., "-Users-rich-git-myproject" -> "myproject"
        var parts = dirName.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0)
        {
            // Return the last part as the project name
            return parts[^1];
        }

        return dirName;
    }

    /// <summary>
    /// Gets the full project path from the directory name.
    /// </summary>
    public string? ExtractProjectPath(string filePath)
    {
        var dirName = Path.GetFileName(Path.GetDirectoryName(filePath));
        if (string.IsNullOrEmpty(dirName))
            return null;

        // Convert "-Users-rich-git-project" back to "/Users/rich/git/project"
        if (dirName.StartsWith('-'))
        {
            return "/" + dirName[1..].Replace('-', '/');
        }

        return dirName.Replace('-', '/');
    }
}
