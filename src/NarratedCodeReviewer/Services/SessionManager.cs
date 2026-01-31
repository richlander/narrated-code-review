using System.Collections.Concurrent;
using NarratedCodeReviewer.Domain;
using NarratedCodeReviewer.Parsing;
using NarratedCodeReviewer.Providers;

namespace NarratedCodeReviewer.Services;

/// <summary>
/// Manages session state and provides session queries.
/// </summary>
public class SessionManager
{
    private readonly ConcurrentDictionary<string, SessionState> _sessions = new();
    private readonly ChangeGrouper _changeGrouper;
    private readonly TimeSpan _activeThreshold;

    public SessionManager(TimeSpan? activeThreshold = null)
    {
        _changeGrouper = new ChangeGrouper();
        _activeThreshold = activeThreshold ?? TimeSpan.FromMinutes(5);
    }

    /// <summary>
    /// Adds an entry to the session manager.
    /// </summary>
    public void AddEntry(Entry entry)
    {
        var state = _sessions.GetOrAdd(entry.SessionId, _ => new SessionState(entry.SessionId));
        state.AddEntry(entry);
    }

    /// <summary>
    /// Loads all entries from a provider.
    /// </summary>
    public async Task LoadFromProviderAsync(ILogProvider provider)
    {
        await foreach (var entry in provider.GetAllEntriesAsync())
        {
            AddEntry(entry);
        }
    }

    /// <summary>
    /// Gets all sessions.
    /// </summary>
    public IReadOnlyList<Session> GetAllSessions()
    {
        return _sessions.Values
            .Select(BuildSession)
            .OrderByDescending(s => s.LastActivityTime)
            .ToList();
    }

    /// <summary>
    /// Gets active sessions (activity within threshold).
    /// </summary>
    public IReadOnlyList<Session> GetActiveSessions()
    {
        var cutoff = DateTime.UtcNow - _activeThreshold;
        return GetAllSessions()
            .Where(s => s.LastActivityTime > cutoff)
            .ToList();
    }

    /// <summary>
    /// Gets recent sessions (most recent N).
    /// </summary>
    public IReadOnlyList<Session> GetRecentSessions(int count = 20)
    {
        return GetAllSessions()
            .Take(count)
            .ToList();
    }

    /// <summary>
    /// Gets a specific session by ID.
    /// </summary>
    public Session? GetSession(string sessionId)
    {
        return _sessions.TryGetValue(sessionId, out var state)
            ? BuildSession(state)
            : null;
    }

    /// <summary>
    /// Gets entries for a specific session.
    /// </summary>
    public IReadOnlyList<Entry> GetSessionEntries(string sessionId)
    {
        return _sessions.TryGetValue(sessionId, out var state)
            ? state.Entries.OrderBy(e => e.Timestamp).ToList()
            : [];
    }

    private Session BuildSession(SessionState state)
    {
        var entries = state.Entries.OrderBy(e => e.Timestamp).ToList();
        var changes = _changeGrouper.GroupChanges(entries);

        var userMessages = entries.OfType<UserEntry>().ToList();
        var assistantMessages = entries.OfType<AssistantEntry>().ToList();
        var toolCalls = assistantMessages.SelectMany(a => a.ToolUses).Count();

        var totalInput = assistantMessages
            .Where(a => a.Usage != null)
            .Sum(a => a.Usage!.InputTokens);
        var totalOutput = assistantMessages
            .Where(a => a.Usage != null)
            .Sum(a => a.Usage!.OutputTokens);

        // Extract .NET CLI commands from Bash tool calls
        var dotnetCommands = assistantMessages
            .SelectMany(a => a.ToolUses
                .Where(t => t.Name.Equals("Bash", StringComparison.OrdinalIgnoreCase))
                .SelectMany(t => DotNetCommandExtractor.ExtractAll(t.Command, a.Timestamp)))
            .OrderBy(c => c.Timestamp)
            .ToList();

        var dotnetCliStats = dotnetCommands.Count > 0
            ? DotNetCommandExtractor.Aggregate(dotnetCommands)
            : null;

        var projectPath = entries.FirstOrDefault()?.ProjectPath;
        var projectName = projectPath != null
            ? Path.GetFileName(projectPath)
            : null;

        var isActive = state.LastActivity > DateTime.UtcNow - _activeThreshold;

        return new Session(
            Id: state.SessionId,
            ProjectPath: projectPath,
            ProjectName: projectName,
            StartTime: state.StartTime,
            LastActivityTime: state.LastActivity,
            IsActive: isActive,
            UserMessageCount: userMessages.Count,
            AssistantMessageCount: assistantMessages.Count,
            ToolCallCount: toolCalls,
            Changes: changes
        )
        {
            TotalInputTokens = totalInput,
            TotalOutputTokens = totalOutput,
            DotNetCommands = dotnetCommands,
            DotNetCliStats = dotnetCliStats
        };
    }

    private class SessionState
    {
        public string SessionId { get; }
        public List<Entry> Entries { get; } = [];
        public DateTime StartTime { get; private set; } = DateTime.MaxValue;
        public DateTime LastActivity { get; private set; } = DateTime.MinValue;
        private readonly object _lock = new();

        public SessionState(string sessionId)
        {
            SessionId = sessionId;
        }

        public void AddEntry(Entry entry)
        {
            lock (_lock)
            {
                Entries.Add(entry);
                if (entry.Timestamp < StartTime)
                    StartTime = entry.Timestamp;
                if (entry.Timestamp > LastActivity)
                    LastActivity = entry.Timestamp;
            }
        }
    }
}
