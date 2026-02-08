namespace AgentLogs.Domain;

/// <summary>
/// Represents a .NET CLI command invocation.
/// </summary>
public record DotNetCommand(
    string Command,         // The subcommand (build, run, test, publish, etc.)
    string FullCommand,     // The complete command line
    string? Arguments,      // Arguments after the subcommand
    DateTime Timestamp
);

/// <summary>
/// Aggregated .NET CLI usage statistics.
/// </summary>
public record DotNetCliStats(
    IReadOnlyDictionary<string, int> CommandCounts,  // Command -> count
    int TotalCommands
);
