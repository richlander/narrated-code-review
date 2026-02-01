using System.Diagnostics;
using System.Text;
using NarratedCodeReviewer.Domain;

namespace NarratedCodeReviewer.Services;

/// <summary>
/// Provides AI summarization using a dedicated Claude Code session.
/// Manages a persistent Claude Code process with idle timeout.
/// </summary>
public sealed class ClaudeSummarizationService : IDisposable
{
    private readonly string _claudePath;
    private readonly TimeSpan _idleTimeout;
    private readonly object _lock = new();
    private readonly Timer _idleTimer;

    private Process? _process;
    private StreamWriter? _stdin;
    private StringBuilder _outputBuffer = new();
    private TaskCompletionSource<string>? _pendingResponse;
    private DateTime _lastActivity = DateTime.UtcNow;
    private bool _disposed;

    /// <summary>
    /// Gets whether Claude Code is installed and available.
    /// </summary>
    public static bool IsAvailable => FindClaudePath() != null;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClaudeSummarizationService"/> class.
    /// </summary>
    /// <param name="idleTimeout">Time after which to terminate the session if idle. Default is 10 minutes.</param>
    public ClaudeSummarizationService(TimeSpan? idleTimeout = null)
    {
        _idleTimeout = idleTimeout ?? TimeSpan.FromMinutes(10);
        _claudePath = FindClaudePath() ?? throw new InvalidOperationException("Claude Code is not installed");

        // Start idle check timer (checks every minute)
        _idleTimer = new Timer(CheckIdleTimeout, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// Summarizes a session.
    /// </summary>
    public async Task<string> SummarizeSessionAsync(Session session, CancellationToken cancellationToken = default)
    {
        var prompt = BuildSessionSummaryPrompt(session);
        return await SendPromptAsync(prompt, cancellationToken);
    }

    /// <summary>
    /// Summarizes an individual activity/change.
    /// </summary>
    public async Task<string> SummarizeActivityAsync(LogicalChange change, CancellationToken cancellationToken = default)
    {
        var prompt = BuildActivitySummaryPrompt(change);
        return await SendPromptAsync(prompt, cancellationToken);
    }

    /// <summary>
    /// Sends a raw prompt and gets a response.
    /// </summary>
    public async Task<string> SendPromptAsync(string prompt, CancellationToken cancellationToken = default)
    {
        EnsureProcessStarted();
        UpdateLastActivity();

        var tcs = new TaskCompletionSource<string>();

        lock (_lock)
        {
            _pendingResponse = tcs;
            _outputBuffer.Clear();
        }

        // Send the prompt
        await _stdin!.WriteLineAsync(prompt.AsMemory(), cancellationToken);
        await _stdin.FlushAsync(cancellationToken);

        // Wait for response with cancellation
        using var registration = cancellationToken.Register(() => tcs.TrySetCanceled());

        try
        {
            return await tcs.Task;
        }
        finally
        {
            lock (_lock)
            {
                _pendingResponse = null;
            }
        }
    }

    private void EnsureProcessStarted()
    {
        lock (_lock)
        {
            if (_process != null && !_process.HasExited)
            {
                return;
            }

            var psi = new ProcessStartInfo
            {
                FileName = _claudePath,
                Arguments = "--verbose", // Conversational mode
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                Environment =
                {
                    ["CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC"] = "1"
                }
            };

            _process = Process.Start(psi);
            if (_process == null)
            {
                throw new InvalidOperationException("Failed to start Claude Code process");
            }

            _stdin = _process.StandardInput;
            _stdin.AutoFlush = true;

            // Start reading output
            _ = ReadOutputAsync(_process.StandardOutput);
            _ = ReadOutputAsync(_process.StandardError);
        }
    }

    private async Task ReadOutputAsync(StreamReader reader)
    {
        try
        {
            var buffer = new char[4096];
            while (true)
            {
                var bytesRead = await reader.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead == 0)
                {
                    break;
                }

                var text = new string(buffer, 0, bytesRead);

                lock (_lock)
                {
                    _outputBuffer.Append(text);

                    // Check for response completion (Claude's prompt marker)
                    var content = _outputBuffer.ToString();
                    if (IsResponseComplete(content))
                    {
                        var response = ExtractResponse(content);
                        _pendingResponse?.TrySetResult(response);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            lock (_lock)
            {
                _pendingResponse?.TrySetException(ex);
            }
        }
    }

    private static bool IsResponseComplete(string content)
    {
        // Claude Code shows a prompt when ready for input
        // Look for the prompt pattern or a reasonable completion signal
        return content.TrimEnd().EndsWith(">") ||
               content.Contains("\n\n> ") ||
               content.Contains("╭") && content.Contains("╰"); // Box drawing chars in output
    }

    private static string ExtractResponse(string content)
    {
        // Remove the prompt marker and clean up
        var lines = content.Split('\n');
        var result = new StringBuilder();

        foreach (var line in lines)
        {
            // Skip prompt lines and empty formatting
            if (line.TrimStart().StartsWith(">") && line.Length < 5)
            {
                continue;
            }

            result.AppendLine(line);
        }

        return result.ToString().Trim();
    }

    private static string BuildSessionSummaryPrompt(Session session)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Summarize this Claude Code session in 2-3 concise sentences. Focus on what was accomplished, not the process. Be specific about files and features.");
        sb.AppendLine();
        sb.AppendLine($"Project: {session.ProjectName}");
        sb.AppendLine($"Duration: {session.Duration}");
        sb.AppendLine($"Messages: {session.UserMessageCount} user, {session.AssistantMessageCount} assistant");
        sb.AppendLine($"Tool calls: {session.ToolCallCount}");
        sb.AppendLine();
        sb.AppendLine("Activities:");

        foreach (var change in session.Changes.Take(20)) // Limit to recent changes
        {
            sb.AppendLine($"- [{change.Type}] {change.Description}");
        }

        return sb.ToString();
    }

    private static string BuildActivitySummaryPrompt(LogicalChange change)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Describe this code change in one sentence. Be specific and concise.");
        sb.AppendLine();
        sb.AppendLine($"Type: {change.Type}");
        sb.AppendLine($"Description: {change.Description}");
        sb.AppendLine($"Tools used: {change.Tools.Count}");

        if (change.AffectedFiles.Count > 0)
        {
            sb.AppendLine($"Files: {string.Join(", ", change.AffectedFiles)}");
        }

        foreach (var tool in change.Tools.Take(5))
        {
            sb.AppendLine($"- {tool.Name}: {tool.FilePath ?? tool.Command ?? "-"}");
        }

        return sb.ToString();
    }

    private void UpdateLastActivity()
    {
        _lastActivity = DateTime.UtcNow;
    }

    private void CheckIdleTimeout(object? state)
    {
        if (_disposed)
        {
            return;
        }

        var idleTime = DateTime.UtcNow - _lastActivity;
        if (idleTime > _idleTimeout)
        {
            TerminateProcess();
        }
    }

    private void TerminateProcess()
    {
        lock (_lock)
        {
            if (_process != null && !_process.HasExited)
            {
                try
                {
                    _process.Kill();
                }
                catch
                {
                    // Ignore errors during termination
                }
            }

            _process?.Dispose();
            _process = null;
            _stdin = null;
            _pendingResponse?.TrySetCanceled();
            _pendingResponse = null;
        }
    }

    private static string? FindClaudePath()
    {
        var candidates = new[]
        {
            "claude", // In PATH
            "/usr/local/bin/claude",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "bin", "claude"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Claude", "claude")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }

            // Check if it's in PATH
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = candidate,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process != null && process.WaitForExit(2000) && process.ExitCode == 0)
                {
                    return candidate;
                }
            }
            catch
            {
                // Not found, try next
            }
        }

        return null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _idleTimer.Dispose();
        TerminateProcess();
    }
}
