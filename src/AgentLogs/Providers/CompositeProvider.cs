using AgentLogs.Domain;

namespace AgentLogs.Providers;

/// <summary>
/// Combines multiple log providers into a single provider.
/// </summary>
public class CompositeProvider : ILogProvider
{
    private readonly ILogProvider[] _providers;
    private readonly ILogProvider _primaryProvider;

    public CompositeProvider(params ILogProvider[] providers)
    {
        if (providers.Length == 0)
            throw new ArgumentException("At least one provider is required", nameof(providers));

        _providers = providers;
        _primaryProvider = providers[0];
    }

    public string Name => string.Join(" + ", _providers.Select(p => p.Name));

    public string BasePath => _primaryProvider.BasePath;

    public IEnumerable<string> DiscoverLogFiles()
    {
        foreach (var provider in _providers)
        {
            foreach (var file in provider.DiscoverLogFiles())
                yield return file;
        }
    }

    public async IAsyncEnumerable<Entry> GetAllEntriesAsync()
    {
        foreach (var provider in _providers)
        {
            await foreach (var entry in provider.GetAllEntriesAsync())
                yield return entry;
        }
    }

    public IAsyncEnumerable<Entry> GetEntriesFromFileAsync(string filePath)
    {
        var provider = FindProviderForFile(filePath);
        return provider.GetEntriesFromFileAsync(filePath);
    }

    public string? ExtractProjectName(string filePath)
    {
        var provider = FindProviderForFile(filePath);
        return provider.ExtractProjectName(filePath);
    }

    public Func<string, Entry?> CreateLineParser()
    {
        // For live tailing, we need to know which provider's parser to use
        // This is determined by the file being tailed
        return _primaryProvider.CreateLineParser();
    }

    public string? FindProjectDir(string workingDirectory)
    {
        foreach (var provider in _providers)
        {
            var result = provider.FindProjectDir(workingDirectory);
            if (result != null)
                return result;
        }
        return null;
    }

    public string GetProjectLogPath(string? projectDirName)
    {
        return _primaryProvider.GetProjectLogPath(projectDirName);
    }

    public string ExtractSessionId(string filePath)
    {
        var provider = FindProviderForFile(filePath);
        return provider.ExtractSessionId(filePath);
    }

    /// <summary>
    /// Gets the appropriate line parser for a specific file.
    /// </summary>
    public Func<string, Entry?> CreateLineParserForFile(string filePath)
    {
        var provider = FindProviderForFile(filePath);
        return provider.CreateLineParser();
    }

    private ILogProvider FindProviderForFile(string filePath)
    {
        // Check which provider's base path contains this file
        foreach (var provider in _providers)
        {
            if (filePath.StartsWith(provider.BasePath, StringComparison.OrdinalIgnoreCase))
                return provider;
        }
        return _primaryProvider;
    }
}
