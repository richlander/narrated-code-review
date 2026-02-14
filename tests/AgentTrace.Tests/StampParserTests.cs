using AgentTrace.Services;

namespace AgentTrace.Tests;

public class StampParserTests
{
    [Fact]
    public void ParseStampField_ExtractsValue()
    {
        var text = """
            «stamp:2026-02-13T08:15:00Z»
            session: abc123
            branch: main
            message: Test stamp
            «/stamp»
            """;
        Assert.Equal("abc123", StampParser.ParseStampField(text, "session"));
        Assert.Equal("main", StampParser.ParseStampField(text, "branch"));
        Assert.Equal("Test stamp", StampParser.ParseStampField(text, "message"));
    }

    [Fact]
    public void ParseStampField_ReturnsNull_WhenNotFound()
    {
        var text = "session: abc123";
        Assert.Null(StampParser.ParseStampField(text, "branch"));
    }

    [Fact]
    public void ParseStampTimestamp_ExtractsTimestamp()
    {
        var text = "«stamp:2026-02-13T08:15:00Z»\nsession: abc\n«/stamp»";
        Assert.Equal("2026-02-13T08:15:00Z", StampParser.ParseStampTimestamp(text));
    }

    [Fact]
    public void ParseStampTimestamp_ReturnsNull_WhenNoStampTag()
    {
        var text = "no stamp here";
        Assert.Null(StampParser.ParseStampTimestamp(text));
    }

    [Fact]
    public void ParseDecisionTimestamp_ExtractsTimestamp()
    {
        var text = "«decision:2026-02-13T14:30:00Z»\n  chose: Use X\n«/decision»";
        Assert.Equal("2026-02-13T14:30:00Z", StampParser.ParseDecisionTimestamp(text));
    }

    [Fact]
    public void ParseDecisionTimestamp_ReturnsNull_WhenNoDecisionTag()
    {
        var text = "no decision here";
        Assert.Null(StampParser.ParseDecisionTimestamp(text));
    }

    [Fact]
    public void ParseStampField_WorksForDecisionFields()
    {
        var text = """
            «decision:2026-02-13T14:30:00Z»
              chose: Use System.CommandLine
              over: hand-rolled, Spectre.Console
              because: better help, subcommands
              session: 3cb8313
            «/decision»
            """;
        Assert.Equal("Use System.CommandLine", StampParser.ParseStampField(text, "chose"));
        Assert.Equal("hand-rolled, Spectre.Console", StampParser.ParseStampField(text, "over"));
        Assert.Equal("better help, subcommands", StampParser.ParseStampField(text, "because"));
        Assert.Equal("3cb8313", StampParser.ParseStampField(text, "session"));
    }

    [Fact]
    public void ParseStampField_DecisionWithoutOptionalFields()
    {
        var text = """
            «decision:2026-02-13T14:30:00Z»
              chose: Simple choice
              session: abc1234
            «/decision»
            """;
        Assert.Equal("Simple choice", StampParser.ParseStampField(text, "chose"));
        Assert.Null(StampParser.ParseStampField(text, "over"));
        Assert.Null(StampParser.ParseStampField(text, "because"));
    }
}
