using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AgentLogs.Domain;

namespace AgentLogs.Providers;

/// <summary>
/// Live log provider that connects to termalive for real-time session streaming.
/// </summary>
public class TermaliveProvider : ILiveLogProvider
{
    private readonly string _uri;
    private readonly string _termalivePath;

    public TermaliveProvider(string uri = "pipe://termalive", string? termalivePath = null)
    {
        _uri = uri;
        _termalivePath = termalivePath ?? FindTermalivePath();
    }

    public string Name => "Termalive";

    public bool IsAvailable => File.Exists(_termalivePath) || CanFindInPath();

    /// <summary>
    /// Lists active sessions from termalive.
    /// </summary>
    public async Task<IReadOnlyList<LiveSessionInfo>> ListSessionsAsync(CancellationToken cancellationToken = default)
    {
        // Run: termalive list --uri <uri> --json (if we add JSON output to list)
        // For now, parse the text output
        var output = await RunTermaliveAsync($"list --uri {_uri}", cancellationToken);
        return ParseSessionList(output);
    }

    /// <summary>
    /// Watches a session for new entries in real-time.
    /// Uses termalive logs command with --follow and --json.
    /// </summary>
    public async IAsyncEnumerable<Entry> WatchSessionAsync(
        string sessionId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _termalivePath,
            Arguments = $"logs {sessionId} --follow --json --uri {_uri}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
            yield break;

        var reader = process.StandardOutput;
        var outputBuffer = new System.Text.StringBuilder();

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line == null)
                break;

            var logEvent = ParseLogEvent(line);
            if (logEvent == null)
                continue;

            switch (logEvent.Event)
            {
                case "data":
                    // Accumulate output data
                    if (logEvent.Content != null)
                    {
                        outputBuffer.Append(logEvent.Content);

                        // Try to parse complete JSONL entries from the buffer
                        foreach (var entry in ExtractEntriesFromBuffer(outputBuffer, sessionId))
                        {
                            yield return entry;
                        }
                    }
                    break;

                case "end":
                    // Session ended or detached
                    yield break;
            }
        }

        if (!process.HasExited)
        {
            process.Kill();
        }
    }

    /// <summary>
    /// Gets buffered entries from a session's history.
    /// </summary>
    public async Task<IReadOnlyList<Entry>> GetBufferedEntriesAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var entries = new List<Entry>();

        // Run termalive logs without --follow to get buffered content
        var output = await RunTermaliveAsync($"logs {sessionId} --uri {_uri}", cancellationToken);

        // Parse as Claude JSONL
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var entry = Parsing.EntryParser.ParseLine(line);
            if (entry != null)
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    private async Task<string> RunTermaliveAsync(string arguments, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _termalivePath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
            return string.Empty;

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return output;
    }

    private static IReadOnlyList<LiveSessionInfo> ParseSessionList(string output)
    {
        var sessions = new List<LiveSessionInfo>();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Skip header lines
        var dataStarted = false;
        foreach (var line in lines)
        {
            if (line.StartsWith("---") || line.StartsWith("==="))
            {
                dataStarted = true;
                continue;
            }

            if (!dataStarted || line.StartsWith("ID") || line.StartsWith("Total:"))
                continue;

            // Parse: ID  STATE  COMMAND  CREATED
            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 4)
            {
                sessions.Add(new LiveSessionInfo(
                    Id: parts[0],
                    Command: parts[2],
                    WorkingDirectory: null,
                    State: parts[1],
                    Created: DateTime.UtcNow, // Would need better parsing
                    ExitCode: null
                ));
            }
        }

        return sessions;
    }

    private static TermaliveLogEvent? ParseLogEvent(string json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, TermaliveJsonContext.Default.TermaliveLogEvent);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IEnumerable<Entry> ExtractEntriesFromBuffer(System.Text.StringBuilder buffer, string sessionId)
    {
        // Look for complete JSONL lines in the buffer
        var content = buffer.ToString();
        var lastNewline = content.LastIndexOf('\n');

        if (lastNewline == -1)
            yield break;

        // Process complete lines
        var completeLines = content[..lastNewline];
        buffer.Clear();
        buffer.Append(content[(lastNewline + 1)..]);

        foreach (var line in completeLines.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var entry = Parsing.EntryParser.ParseLine(line);
            if (entry != null)
            {
                yield return entry;
            }
        }
    }

    private static string FindTermalivePath()
    {
        // Check common locations
        var locations = new[]
        {
            "termalive", // In PATH
            Path.Combine(AppContext.BaseDirectory, "termalive"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "tools", "termalive")
        };

        foreach (var loc in locations)
        {
            if (File.Exists(loc))
                return loc;
        }

        return "termalive"; // Hope it's in PATH
    }

    private bool CanFindInPath()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _termalivePath,
                Arguments = "version",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            return process != null && process.WaitForExit(1000);
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Log event from termalive JSONL output.
/// </summary>
internal sealed class TermaliveLogEvent
{
    public string? Ts { get; set; }
    public string? Session { get; set; }
    public string? Event { get; set; }
    public string? Content { get; set; }
    public string? Reason { get; set; }
}

/// <summary>
/// JSON context for termalive event parsing.
/// </summary>
[System.Text.Json.Serialization.JsonSourceGenerationOptions(PropertyNamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.CamelCase)]
[System.Text.Json.Serialization.JsonSerializable(typeof(TermaliveLogEvent))]
internal partial class TermaliveJsonContext : System.Text.Json.Serialization.JsonSerializerContext
{
}
