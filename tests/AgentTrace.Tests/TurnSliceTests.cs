using AgentLogs.Domain;
using AgentTrace.Services;

namespace AgentTrace.Tests;

public class TurnSliceTests
{
    [Fact]
    public void Parse_SingleTurn()
    {
        var slice = TurnSlice.Parse("5");
        Assert.Equal(5, slice.From);
        Assert.Equal(5, slice.To);
        Assert.Null(slice.Last);
        Assert.True(slice.IsSet);
    }

    [Fact]
    public void Parse_Range()
    {
        var slice = TurnSlice.Parse("3..7");
        Assert.Equal(3, slice.From);
        Assert.Equal(7, slice.To);
        Assert.Null(slice.Last);
        Assert.True(slice.IsSet);
    }

    [Fact]
    public void Parse_Invalid()
    {
        var slice = TurnSlice.Parse("abc");
        Assert.False(slice.IsSet);
    }

    [Fact]
    public void Parse_Zero_ReturnsDefault()
    {
        var slice = TurnSlice.Parse("0");
        Assert.False(slice.IsSet);
    }

    [Fact]
    public void LastN_Creates()
    {
        var slice = TurnSlice.LastN(3);
        Assert.Equal(3, slice.Last);
        Assert.Null(slice.From);
        Assert.Null(slice.To);
        Assert.True(slice.IsSet);
    }

    [Fact]
    public void Apply_Range_SelectsTurns()
    {
        var turns = MakeTurns(5);
        var slice = TurnSlice.Parse("2..4");
        var result = slice.Apply(turns);
        Assert.Equal(3, result.Count); // turns 2, 3, 4 (1-indexed)
    }

    [Fact]
    public void Apply_SingleTurn_SelectsOne()
    {
        var turns = MakeTurns(5);
        var slice = TurnSlice.Parse("3");
        var result = slice.Apply(turns);
        Assert.Single(result);
    }

    [Fact]
    public void Apply_LastN_SelectsFromEnd()
    {
        var turns = MakeTurns(10);
        var slice = TurnSlice.LastN(3);
        var result = slice.Apply(turns);
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void Apply_Default_ReturnsAll()
    {
        var turns = MakeTurns(5);
        var slice = default(TurnSlice);
        var result = slice.Apply(turns);
        Assert.Equal(5, result.Count);
    }

    [Fact]
    public void Describe_SingleTurn()
    {
        Assert.Equal("turn 3", TurnSlice.Parse("3").Describe());
    }

    [Fact]
    public void Describe_Range()
    {
        Assert.Equal("turns 2..5", TurnSlice.Parse("2..5").Describe());
    }

    [Fact]
    public void Describe_LastN()
    {
        Assert.Equal("last 3 turns", TurnSlice.LastN(3).Describe());
    }

    [Fact]
    public void Describe_Default()
    {
        Assert.Equal("all turns", default(TurnSlice).Describe());
    }

    private static IReadOnlyList<Turn> MakeTurns(int count)
    {
        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var turns = new List<Turn>();
        for (int i = 0; i < count; i++)
        {
            var uuid = Guid.NewGuid().ToString();
            var userEntry = new UserEntry(uuid, baseTime.AddMinutes(i * 2), "sess", null, $"User message {i + 1}");
            var assistantEntry = new AssistantEntry(Guid.NewGuid().ToString(), baseTime.AddMinutes(i * 2 + 1), "sess", null, $"Assistant response {i + 1}", [], null);
            var entries = new List<Entry> { userEntry, assistantEntry };
            turns.Add(new Turn(i + 1, entries, userEntry.Content, [assistantEntry], []));
        }
        return turns;
    }
}
