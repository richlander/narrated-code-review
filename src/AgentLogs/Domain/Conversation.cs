namespace AgentLogs.Domain;

/// <summary>
/// A conversation threaded from entries, organized into turns.
/// </summary>
public class Conversation
{
    /// <summary>Session ID this conversation belongs to.</summary>
    public string SessionId { get; }

    /// <summary>All entries in chronological order.</summary>
    public IReadOnlyList<Entry> Entries { get; }

    /// <summary>Entries organized into conversational turns.</summary>
    public IReadOnlyList<Turn> Turns { get; }

    public Conversation(string sessionId, IReadOnlyList<Entry> entries)
    {
        SessionId = sessionId;
        Entries = entries;
        Turns = BuildTurns(entries);
    }

    private static IReadOnlyList<Turn> BuildTurns(IReadOnlyList<Entry> entries)
    {
        var turns = new List<Turn>();
        var currentTurnEntries = new List<Entry>();
        var turnNumber = 0;

        foreach (var entry in entries)
        {
            if (entry is UserEntry user && currentTurnEntries.Count > 0 && IsRealUserMessage(user))
            {
                // A new user message with actual text starts a new turn.
                // Tool-result-only user entries are continuations of the previous turn.
                turns.Add(CreateTurn(turnNumber++, currentTurnEntries));
                currentTurnEntries = [];
            }

            currentTurnEntries.Add(entry);
        }

        // Don't forget the last turn
        if (currentTurnEntries.Count > 0)
        {
            turns.Add(CreateTurn(turnNumber, currentTurnEntries));
        }

        return turns;
    }

    /// <summary>
    /// A user entry with empty/whitespace text content that only carries tool results
    /// is protocol plumbing, not a real user message.
    /// </summary>
    private static bool IsRealUserMessage(UserEntry user)
    {
        return !string.IsNullOrWhiteSpace(user.Content);
    }

    private static Turn CreateTurn(int number, List<Entry> entries)
    {
        var userEntry = entries.OfType<UserEntry>().FirstOrDefault();
        var assistantEntries = entries.OfType<AssistantEntry>().ToList();
        var toolUses = assistantEntries.SelectMany(a => a.ToolUses).ToList();

        return new Turn(
            Number: number,
            Entries: entries,
            UserMessage: userEntry?.Content,
            AssistantMessages: assistantEntries,
            ToolUses: toolUses
        );
    }
}

/// <summary>
/// A single turn in a conversation (user message + assistant responses + tool calls).
/// </summary>
public record Turn(
    int Number,
    IReadOnlyList<Entry> Entries,
    string? UserMessage,
    IReadOnlyList<AssistantEntry> AssistantMessages,
    IReadOnlyList<ToolUse> ToolUses
)
{
    /// <summary>Start time of this turn.</summary>
    public DateTime StartTime => Entries.First().Timestamp;

    /// <summary>End time of this turn.</summary>
    public DateTime EndTime => Entries.Last().Timestamp;

    /// <summary>Duration of this turn.</summary>
    public TimeSpan Duration => EndTime - StartTime;
}
