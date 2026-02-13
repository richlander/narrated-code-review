namespace AgentTrace.Services;

/// <summary>
/// Reads and writes a .tags file (sessionId\ttag1,tag2 per line) in a project log directory.
/// Follows the BookmarkStore pattern.
/// </summary>
public class TagStore(string projectLogPath)
{
    private readonly string _filePath = Path.Combine(projectLogPath, ".tags");

    /// <summary>
    /// Loads all tags, returning a dictionary of session ID → tag set.
    /// </summary>
    public Dictionary<string, HashSet<string>> LoadAll()
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        if (!File.Exists(_filePath))
            return result;

        foreach (var line in File.ReadAllLines(_filePath))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;

            var tabIdx = trimmed.IndexOf('\t');
            if (tabIdx < 0) continue;

            var sessionId = trimmed[..tabIdx];
            var tags = trimmed[(tabIdx + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (tags.Length > 0)
                result[sessionId] = new HashSet<string>(tags, StringComparer.OrdinalIgnoreCase);
        }

        return result;
    }

    /// <summary>
    /// Gets tags for a specific session, with prefix matching.
    /// If the stored key is a prefix of the session ID (or vice versa), it matches.
    /// </summary>
    public HashSet<string> GetTags(string sessionId)
    {
        var all = LoadAll();
        if (all.TryGetValue(sessionId, out var tags))
            return tags;

        // Prefix match: stored key might be a prefix of sessionId, or sessionId a prefix of key
        foreach (var (key, value) in all)
        {
            if (sessionId.StartsWith(key, StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith(sessionId, StringComparison.OrdinalIgnoreCase))
                return value;
        }

        return [];
    }

    /// <summary>
    /// Loads all tags, keyed by full session IDs where possible.
    /// Resolves prefix-stored keys against a list of known full session IDs.
    /// </summary>
    public Dictionary<string, HashSet<string>> LoadAllResolved(IEnumerable<string> knownSessionIds)
    {
        var raw = LoadAll();
        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var (key, tags) in raw)
        {
            // Try to resolve prefix key to full ID
            var fullId = knownSessionIds.FirstOrDefault(id =>
                id.Equals(key, StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith(key, StringComparison.OrdinalIgnoreCase));

            result[fullId ?? key] = tags;
        }

        return result;
    }

    /// <summary>
    /// Adds a tag to a session. Returns true if the tag was added (not already present).
    /// </summary>
    public bool AddTag(string sessionId, string tag)
    {
        var all = LoadAll();
        if (!all.TryGetValue(sessionId, out var tags))
        {
            tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            all[sessionId] = tags;
        }

        var added = tags.Add(tag);
        if (added) Save(all);
        return added;
    }

    /// <summary>
    /// Removes a tag from a session. Returns true if the tag was removed.
    /// </summary>
    public bool RemoveTag(string sessionId, string tag)
    {
        var all = LoadAll();
        if (!all.TryGetValue(sessionId, out var tags))
            return false;

        var removed = tags.Remove(tag);
        if (removed)
        {
            if (tags.Count == 0)
                all.Remove(sessionId);
            Save(all);
        }
        return removed;
    }

    private void Save(Dictionary<string, HashSet<string>> allTags)
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var lines = allTags
            .Where(kv => kv.Value.Count > 0)
            .OrderBy(kv => kv.Key)
            .Select(kv => $"{kv.Key}\t{string.Join(",", kv.Value.Order())}");

        File.WriteAllLines(_filePath, lines);
    }
}
