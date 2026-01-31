using System.Text.RegularExpressions;
using NarratedCodeReviewer.Domain;
using NarratedCodeReviewer.Parsing;

namespace NarratedCodeReviewer.Providers;

/// <summary>
/// Log provider for Claude Code sessions.
/// Reads from ~/.claude/projects/
/// </summary>
public partial class ClaudeCodeProvider : ILogProvider
{
    public string Name => "Claude Code";

    public string BasePath { get; }

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

    public IEnumerable<string> DiscoverLogFiles()
    {
        if (!Directory.Exists(BasePath))
            yield break;

        // Find all .jsonl files in project directories
        foreach (var projectDir in Directory.EnumerateDirectories(BasePath))
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

    public IAsyncEnumerable<Entry> GetEntriesFromFileAsync(string filePath)
    {
        return EntryParser.ParseFileAsync(filePath);
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
