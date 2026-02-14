using System.Diagnostics;
using AgentLogs.Services;
using AgentTrace.Services;

namespace AgentTrace.Commands;

/// <summary>
/// Pipes a conversation dump to `claude --print` for summarization.
/// </summary>
public static class SummaryCommand
{
    private const string SummarizePrompt =
        "Summarize this conversation concisely. Cover: " +
        "(1) what the user wanted, " +
        "(2) what was accomplished, " +
        "(3) key decisions made, " +
        "(4) current state / any unfinished work.";

    public static async Task<int> RunAsync(SessionManager sessionManager, string sessionId, TurnSlice turnSlice = default)
    {
        var (session, conversation) = SessionHelper.ResolveSession(sessionManager, sessionId);
        if (session == null || conversation == null)
            return 1;

        // Render conversation to a string
        using var buffer = new StringWriter();
        DumpCommand.RenderConversation(session, conversation, buffer, turnSlice);
        var dump = buffer.ToString();

        // Launch claude --print
        var psi = new ProcessStartInfo
        {
            FileName = "claude",
            Arguments = "--print",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        Process process;
        try
        {
            process = Process.Start(psi)!;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to launch claude: {ex.Message}");
            Console.Error.WriteLine("Make sure the Claude CLI is installed and on your PATH.");
            return 1;
        }

        // Write the prompt + dump to stdin
        await process.StandardInput.WriteLineAsync(SummarizePrompt);
        await process.StandardInput.WriteLineAsync();
        await process.StandardInput.WriteLineAsync("--- CONVERSATION ---");
        await process.StandardInput.WriteAsync(dump);
        process.StandardInput.Close();

        // Stream stdout to our stdout
        var outputTask = Task.Run(async () =>
        {
            var buffer = new char[4096];
            int read;
            while ((read = await process.StandardOutput.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                Console.Out.Write(buffer, 0, read);
            }
        });

        // Capture stderr
        var stderrTask = process.StandardError.ReadToEndAsync();

        await outputTask;
        await process.WaitForExitAsync();

        var stderr = await stderrTask;
        if (process.ExitCode != 0 && !string.IsNullOrWhiteSpace(stderr))
        {
            Console.Error.WriteLine(stderr);
        }

        return process.ExitCode;
    }
}
