using AgentLogs.Domain;

namespace AgentTrace.Services;

/// <summary>
/// Selects a slice of turns: a single turn by index, a 1-indexed range M..N, or the last N.
/// </summary>
public readonly record struct TurnSlice(int? Last, int? From, int? To)
{
    /// <summary>
    /// Parses --turns value: bare "5" -> turn 5 (1-indexed), "3..7" -> range 3-7.
    /// </summary>
    public static TurnSlice Parse(string value)
    {
        var dotIdx = value.IndexOf("..", StringComparison.Ordinal);
        if (dotIdx >= 0)
        {
            var fromStr = value[..dotIdx];
            var toStr = value[(dotIdx + 2)..];
            if (int.TryParse(fromStr, out var from) && int.TryParse(toStr, out var to))
                return new TurnSlice(null, from, to);
        }

        // Bare number = single turn by 1-indexed position
        if (int.TryParse(value, out var index) && index > 0)
            return new TurnSlice(null, index, index);

        return default;
    }

    /// <summary>
    /// Creates a TurnSlice for the last N turns (used by --last flag).
    /// </summary>
    public static TurnSlice LastN(int count) => new(count, null, null);

    public IReadOnlyList<Turn> Apply(IReadOnlyList<Turn> turns)
    {
        if (From.HasValue && To.HasValue)
        {
            // 1-indexed inclusive range -> 0-indexed
            var from = Math.Max(0, From.Value - 1);
            var to = Math.Min(turns.Count, To.Value);
            if (from >= to) return [];
            return turns.Skip(from).Take(to - from).ToList();
        }

        if (Last.HasValue && Last.Value > 0 && Last.Value < turns.Count)
            return turns.Skip(turns.Count - Last.Value).ToList();

        return turns;
    }

    public string Describe()
    {
        if (From.HasValue && To.HasValue)
        {
            if (From.Value == To.Value)
                return $"turn {From.Value}";
            return $"turns {From.Value}..{To.Value}";
        }
        if (Last.HasValue)
            return $"last {Last.Value} turns";
        return "all turns";
    }

    public bool IsSet => Last.HasValue || (From.HasValue && To.HasValue);
}
