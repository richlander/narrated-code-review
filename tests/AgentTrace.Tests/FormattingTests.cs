using AgentTrace.Services;

namespace AgentTrace.Tests;

public class FormattingTests
{
    [Fact]
    public void FormatDuration_Seconds()
    {
        Assert.Equal("45s", Formatting.FormatDuration(TimeSpan.FromSeconds(45)));
    }

    [Fact]
    public void FormatDuration_ZeroSeconds()
    {
        Assert.Equal("0s", Formatting.FormatDuration(TimeSpan.Zero));
    }

    [Fact]
    public void FormatDuration_Minutes()
    {
        Assert.Equal("5m 30s", Formatting.FormatDuration(TimeSpan.FromSeconds(330)));
    }

    [Fact]
    public void FormatDuration_Hours()
    {
        Assert.Equal("2h 15m", Formatting.FormatDuration(TimeSpan.FromMinutes(135)));
    }

    [Fact]
    public void FormatAge_Minutes_WithSuffix()
    {
        Assert.Equal("5m ago", Formatting.FormatAge(TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void FormatAge_Hours_WithSuffix()
    {
        Assert.Equal("3h ago", Formatting.FormatAge(TimeSpan.FromHours(3.5)));
    }

    [Fact]
    public void FormatAge_Days_WithSuffix()
    {
        Assert.Equal("2d ago", Formatting.FormatAge(TimeSpan.FromDays(2)));
    }

    [Fact]
    public void FormatAge_Hours_WithoutSuffix()
    {
        Assert.Equal("3h 30m", Formatting.FormatAge(TimeSpan.FromHours(3.5), withSuffix: false));
    }

    [Fact]
    public void FormatAge_Minutes_WithoutSuffix()
    {
        Assert.Equal("5m", Formatting.FormatAge(TimeSpan.FromMinutes(5), withSuffix: false));
    }

    [Fact]
    public void Truncate_Short()
    {
        Assert.Equal("hello", Formatting.Truncate("hello", 20));
    }

    [Fact]
    public void Truncate_Long()
    {
        Assert.Equal("hello...", Formatting.Truncate("hello world", 5));
    }

    [Fact]
    public void Truncate_Multiline()
    {
        Assert.Equal("first second...", Formatting.Truncate("first\nsecond\nthird", 12));
    }

    [Fact]
    public void Truncate_Null()
    {
        Assert.Equal("", Formatting.Truncate(null, 10));
    }

    [Fact]
    public void Truncate_Empty()
    {
        Assert.Equal("", Formatting.Truncate("", 10));
    }

    [Fact]
    public void Truncate_ExactLength()
    {
        Assert.Equal("hello", Formatting.Truncate("hello", 5));
    }
}
