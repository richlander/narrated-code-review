using System.Text.RegularExpressions;
using NarratedCodeReviewer.Domain;

namespace NarratedCodeReviewer.Parsing;

/// <summary>
/// Extracts .NET CLI commands from Bash tool invocations.
/// </summary>
public static partial class DotNetCommandExtractor
{
    // Known dotnet subcommands
    private static readonly HashSet<string> KnownCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        // Build & Run
        "build", "run", "watch", "publish", "pack", "clean", "restore",
        // Testing
        "test", "vstest",
        // Project management
        "new", "add", "remove", "list", "sln",
        // NuGet
        "nuget",
        // Tools
        "tool",
        // Workloads
        "workload",
        // SDK
        "sdk",
        // Other
        "format", "dev-certs", "user-secrets", "ef", "aspnet-codegenerator"
    };

    /// <summary>
    /// Attempts to extract a DotNetCommand from a bash command string.
    /// </summary>
    public static DotNetCommand? Extract(string? command, DateTime timestamp)
    {
        if (string.IsNullOrWhiteSpace(command))
            return null;

        // Look for dotnet commands - handle various patterns:
        // - "dotnet build"
        // - "dotnet run -- args"
        // - "cd /path && dotnet build"
        // - Multiple commands with &&, ||, or ;

        var match = DotNetCommandRegex().Match(command);
        if (!match.Success)
            return null;

        var fullDotnetCommand = match.Value.Trim();
        var parts = fullDotnetCommand.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2)
        {
            // Just "dotnet" with no subcommand
            return new DotNetCommand(
                Command: "dotnet",
                FullCommand: fullDotnetCommand,
                Arguments: null,
                Timestamp: timestamp
            );
        }

        var subcommand = parts[1].ToLowerInvariant();

        // Handle flags before subcommand (e.g., "dotnet --info")
        if (subcommand.StartsWith('-'))
        {
            return new DotNetCommand(
                Command: subcommand,
                FullCommand: fullDotnetCommand,
                Arguments: parts.Length > 2 ? string.Join(' ', parts.Skip(2)) : null,
                Timestamp: timestamp
            );
        }

        var arguments = parts.Length > 2 ? string.Join(' ', parts.Skip(2)) : null;

        return new DotNetCommand(
            Command: subcommand,
            FullCommand: fullDotnetCommand,
            Arguments: arguments,
            Timestamp: timestamp
        );
    }

    /// <summary>
    /// Extracts all .NET commands from a bash command that may contain multiple commands.
    /// </summary>
    public static IEnumerable<DotNetCommand> ExtractAll(string? command, DateTime timestamp)
    {
        if (string.IsNullOrWhiteSpace(command))
            yield break;

        var matches = DotNetCommandRegex().Matches(command);
        foreach (Match match in matches)
        {
            var extracted = Extract(match.Value, timestamp);
            if (extracted != null)
                yield return extracted;
        }
    }

    /// <summary>
    /// Aggregates .NET CLI usage from a collection of commands.
    /// </summary>
    public static DotNetCliStats Aggregate(IEnumerable<DotNetCommand> commands)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var total = 0;

        foreach (var cmd in commands)
        {
            counts.TryGetValue(cmd.Command, out var count);
            counts[cmd.Command] = count + 1;
            total++;
        }

        return new DotNetCliStats(counts, total);
    }

    // Regex to match dotnet commands, handling:
    // - Start of string or after command separator (&&, ||, ;, |)
    // - Optional path prefix
    // - The dotnet command with arguments until end or next separator
    [GeneratedRegex(@"(?:^|&&|\|\||;|\|)\s*(?:[\w/\\.-]*)?dotnet(?:\.exe)?\s+[^&|;]+", RegexOptions.IgnoreCase)]
    private static partial Regex DotNetCommandRegex();
}
