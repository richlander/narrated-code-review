using AgentTrace.Commands;

namespace AgentTrace.Tests;

public class TimelineParseTests
{
    [Fact]
    public void ParseRelativeTime_Hours()
    {
        var result = TimelineCommand.ParseRelativeTime("2h ago");
        Assert.NotNull(result);
        var expected = DateTime.UtcNow - TimeSpan.FromHours(2);
        Assert.InRange((expected - result!.Value).TotalSeconds, -5, 5);
    }

    [Fact]
    public void ParseRelativeTime_Days()
    {
        var result = TimelineCommand.ParseRelativeTime("1d ago");
        Assert.NotNull(result);
        var expected = DateTime.UtcNow - TimeSpan.FromDays(1);
        Assert.InRange((expected - result!.Value).TotalSeconds, -5, 5);
    }

    [Fact]
    public void ParseRelativeTime_Minutes()
    {
        var result = TimelineCommand.ParseRelativeTime("30m ago");
        Assert.NotNull(result);
        var expected = DateTime.UtcNow - TimeSpan.FromMinutes(30);
        Assert.InRange((expected - result!.Value).TotalSeconds, -5, 5);
    }

    [Fact]
    public void ParseRelativeTime_Weeks()
    {
        var result = TimelineCommand.ParseRelativeTime("2w ago");
        Assert.NotNull(result);
        var expected = DateTime.UtcNow - TimeSpan.FromDays(14);
        Assert.InRange((expected - result!.Value).TotalSeconds, -5, 5);
    }

    [Fact]
    public void ParseRelativeTime_AbsoluteDate()
    {
        var result = TimelineCommand.ParseRelativeTime("2026-01-15");
        Assert.NotNull(result);
        Assert.Equal(new DateTime(2026, 1, 15), result!.Value.Date);
    }

    [Fact]
    public void ParseRelativeTime_Invalid()
    {
        var result = TimelineCommand.ParseRelativeTime("not a time");
        Assert.Null(result);
    }
}
