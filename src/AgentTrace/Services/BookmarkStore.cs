namespace AgentTrace.Services;

/// <summary>
/// Reads and writes a .bookmarks file (one session ID per line) in a project log directory.
/// </summary>
public class BookmarkStore(string projectLogPath)
{
    private readonly string _filePath = Path.Combine(projectLogPath, ".bookmarks");

    public HashSet<string> Load()
    {
        if (!File.Exists(_filePath))
            return [];

        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in File.ReadAllLines(_filePath))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0)
                set.Add(trimmed);
        }
        return set;
    }

    public void Save(HashSet<string> bookmarks)
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllLines(_filePath, bookmarks.Order());
    }

    /// <summary>
    /// Toggles the bookmark state of a session. Returns true if now bookmarked, false if removed.
    /// </summary>
    public bool Toggle(string sessionId)
    {
        var bookmarks = Load();
        bool added;
        if (bookmarks.Contains(sessionId))
        {
            bookmarks.Remove(sessionId);
            added = false;
        }
        else
        {
            bookmarks.Add(sessionId);
            added = true;
        }
        Save(bookmarks);
        return added;
    }

    public bool IsBookmarked(string sessionId) => Load().Contains(sessionId);
}
