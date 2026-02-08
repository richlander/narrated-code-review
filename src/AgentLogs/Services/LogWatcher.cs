using AgentLogs.Domain;
using AgentLogs.Providers;

namespace AgentLogs.Services;

/// <summary>
/// Watches log directories for changes and streams new entries.
/// </summary>
public class LogWatcher : IDisposable
{
    private readonly ILogProvider _provider;
    private readonly SessionManager _sessionManager;
    private FileSystemWatcher? _watcher;
    private readonly Dictionary<string, long> _filePositions = new();
    private readonly object _lock = new();
    private bool _disposed;

    public event Action? OnDataChanged;

    public LogWatcher(ILogProvider provider, SessionManager sessionManager)
    {
        _provider = provider;
        _sessionManager = sessionManager;
    }

    /// <summary>
    /// Starts watching for log file changes.
    /// </summary>
    public void Start()
    {
        if (!Directory.Exists(_provider.BasePath))
            return;

        // Record initial file positions
        foreach (var file in _provider.DiscoverLogFiles())
        {
            if (File.Exists(file))
            {
                _filePositions[file] = new FileInfo(file).Length;
            }
        }

        _watcher = new FileSystemWatcher(_provider.BasePath)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
            Filter = "*.jsonl",
            IncludeSubdirectories = true,
            EnableRaisingEvents = true
        };

        _watcher.Changed += OnFileChanged;
        _watcher.Created += OnFileCreated;
    }

    /// <summary>
    /// Stops watching for changes.
    /// </summary>
    public void Stop()
    {
        _watcher?.Dispose();
        _watcher = null;
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (e.ChangeType != WatcherChangeTypes.Changed)
            return;

        _ = ProcessFileChangesAsync(e.FullPath);
    }

    private void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        lock (_lock)
        {
            _filePositions[e.FullPath] = 0;
        }

        _ = ProcessFileChangesAsync(e.FullPath);
    }

    private async Task ProcessFileChangesAsync(string filePath)
    {
        if (!File.Exists(filePath) || !filePath.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
            return;

        long startPosition;
        lock (_lock)
        {
            _filePositions.TryGetValue(filePath, out startPosition);
        }

        try
        {
            var newEntries = new List<Entry>();

            await using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 4096,
                useAsync: true);

            // Seek to last known position
            if (startPosition > 0 && startPosition < stream.Length)
            {
                stream.Seek(startPosition, SeekOrigin.Begin);
            }
            else if (startPosition >= stream.Length)
            {
                // File was truncated, read from beginning
                stream.Seek(0, SeekOrigin.Begin);
            }

            using var reader = new StreamReader(stream);

            while (await reader.ReadLineAsync() is { } line)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var entry = Parsing.EntryParser.ParseLine(line);
                if (entry != null)
                {
                    newEntries.Add(entry);
                }
            }

            // Update position
            lock (_lock)
            {
                _filePositions[filePath] = stream.Position;
            }

            // Add entries to session manager
            foreach (var entry in newEntries)
            {
                _sessionManager.AddEntry(entry);
            }

            if (newEntries.Count > 0)
            {
                OnDataChanged?.Invoke();
            }
        }
        catch (IOException)
        {
            // File is likely still being written to, will retry on next change
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _watcher?.Dispose();
        GC.SuppressFinalize(this);
    }
}
