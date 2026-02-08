using AgentLogs.Domain;

namespace AgentLogs.Providers;

/// <summary>
/// Interface for live log providers that stream events in real-time.
/// Extends ILogProvider with streaming capabilities for active sessions.
/// </summary>
public interface ILiveLogProvider
{
    /// <summary>
    /// Gets the name of this provider.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets whether the provider is connected and available.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Lists active sessions from the provider.
    /// </summary>
    Task<IReadOnlyList<LiveSessionInfo>> ListSessionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Watches a session for new entries in real-time.
    /// </summary>
    /// <param name="sessionId">The session ID to watch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of entries as they occur.</returns>
    IAsyncEnumerable<Entry> WatchSessionAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets buffered entries from a session (historical data).
    /// </summary>
    Task<IReadOnlyList<Entry>> GetBufferedEntriesAsync(string sessionId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Information about a live session.
/// </summary>
public record LiveSessionInfo(
    string Id,
    string Command,
    string? WorkingDirectory,
    string State,
    DateTime Created,
    int? ExitCode
);
