using AgentLogs.Domain;
using AgentLogs.Services;

namespace AgentTrace.Services;

/// <summary>
/// Shared session utilities — eliminates duplication across commands.
/// </summary>
public static class SessionHelper
{
    /// <summary>
    /// Extracts the goal from a conversation: first substantive user message,
    /// applying continuation detection and truncating.
    /// </summary>
    public static string GetGoal(Conversation conversation, int maxLength = 80)
    {
        var goalMessage = conversation.Turns
            .Select(t => t.UserMessage)
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m =>
            {
                var cont = ContinuationDetector.Parse(m);
                return cont.IsContinuation ? cont.SubstantiveContent : m;
            })
            .FirstOrDefault(m => !string.IsNullOrWhiteSpace(m));

        if (goalMessage == null) return "(no user message)";
        var single = goalMessage.ReplaceLineEndings(" ");
        return single.Length > maxLength ? single[..maxLength] + "..." : single;
    }

    /// <summary>
    /// Gets turn count for a session by building the conversation.
    /// </summary>
    public static int GetTurnCount(SessionManager sessionManager, string sessionId)
    {
        var entries = sessionManager.GetSessionEntries(sessionId);
        if (entries.Count == 0) return 0;
        var conversation = new Conversation(sessionId, entries);
        return conversation.Turns.Count;
    }

    /// <summary>
    /// Filters sessions by project name.
    /// </summary>
    public static IReadOnlyList<Session> FilterSessions(SessionManager sessionManager, string? projectFilter)
    {
        var sessions = sessionManager.GetAllSessions();

        if (projectFilter != null)
        {
            sessions = sessions
                .Where(s => s.ProjectName != null &&
                    s.ProjectName.Contains(projectFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return sessions;
    }

    /// <summary>
    /// Resolves a session ID (with prefix matching) to session + conversation.
    /// </summary>
    public static (Session? Session, Conversation? Conversation) ResolveSession(SessionManager sessionManager, string sessionId)
    {
        var entries = sessionManager.GetSessionEntries(sessionId);

        if (entries.Count == 0)
        {
            var match = sessionManager.GetAllSessions()
                .FirstOrDefault(s => s.Id.StartsWith(sessionId, StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                entries = sessionManager.GetSessionEntries(match.Id);
                sessionId = match.Id;
            }
        }

        if (entries.Count == 0)
        {
            Console.Error.WriteLine($"Session not found: {sessionId}");
            return (null, null);
        }

        var session = sessionManager.GetSession(sessionId);
        var conversation = new Conversation(sessionId, entries);
        return (session, conversation);
    }
}
