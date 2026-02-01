using Microsoft.Extensions.Terminal;
using NarratedCodeReviewer.Domain;
using NarratedCodeReviewer.Providers;
using NarratedCodeReviewer.Services;
using NarratedCodeReviewer.UI;

if (args.Contains("--help") || args.Contains("-h"))
{
    ShowHelp();
    return 0;
}

if (args.Contains("--version") || args.Contains("-v"))
{
    Console.WriteLine("Narrated Code Reviewer v0.1.0");
    return 0;
}

// Get custom path if specified
string? customPath = null;
for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--path" || args[i] == "-p")
    {
        customPath = args[i + 1];
        break;
    }
}

// Create provider
ILogProvider provider = customPath != null
    ? new ClaudeCodeProvider(customPath)
    : new ClaudeCodeProvider();

// Create terminal
var console = new SystemConsole();
var terminal = new AnsiTerminal(console);

// Check if base path exists
if (!Directory.Exists(provider.BasePath))
{
    terminal.SetColor(TerminalColor.Yellow);
    terminal.Append("Warning: ");
    terminal.ResetColor();
    terminal.Append($"Claude Code logs directory not found at ");
    terminal.SetColor(TerminalColor.Gray);
    terminal.AppendLine(provider.BasePath);
    terminal.ResetColor();
    terminal.AppendLine("The dashboard will start but no sessions will be displayed until Claude Code creates logs.");
    terminal.AppendLine();
}

// Create services
var sessionManager = new SessionManager();
var statsAggregator = new StatsAggregator();
var watcher = new LogWatcher(provider, sessionManager);

// Load existing data
terminal.Append("Loading sessions...");
await sessionManager.LoadFromProviderAsync(provider);
terminal.AppendLine(" done");

var sessions = sessionManager.GetAllSessions();
terminal.SetColor(TerminalColor.Green);
terminal.AppendLine($"Loaded {sessions.Count} sessions");
terminal.ResetColor();

if (args.Contains("--list") || args.Contains("-l"))
{
    // List mode - just show sessions and exit
    ShowSessionList(sessions, terminal);
    return 0;
}

if (args.Contains("--stats") || args.Contains("-s"))
{
    // Stats mode - show statistics and exit
    ShowStats(statsAggregator.ComputeStats(sessions), statsAggregator.ComputeDailyStats(sessions), terminal);
    return 0;
}

// Interactive dashboard mode
var dashboard = new Dashboard(sessionManager, statsAggregator, watcher, terminal);
await dashboard.RunAsync();

return 0;

// Helper methods
void ShowHelp()
{
    Console.WriteLine();
    Console.WriteLine("  ╔═╗╔═╗╦═╗");
    Console.WriteLine("  ║  ║  ╠╦╝  Narrated Code Reviewer");
    Console.WriteLine("  ╚═╝╚═╝╩╚═");
    Console.WriteLine();
    Console.WriteLine("Terminal dashboard for Claude Code sessions");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  -h, --help           Show this help message");
    Console.WriteLine("  -v, --version        Show version information");
    Console.WriteLine("  -p, --path <path>    Custom path to Claude logs directory");
    Console.WriteLine("  -l, --list           List sessions and exit (non-interactive)");
    Console.WriteLine("  -s, --stats          Show statistics and exit (non-interactive)");
    Console.WriteLine();
    Console.WriteLine("Keyboard Controls (Interactive Mode):");
    Console.WriteLine("  ↑/↓ or j/k           Navigate up/down");
    Console.WriteLine("  ←/→                  Switch tabs");
    Console.WriteLine("  Enter                View selected item");
    Console.WriteLine("  Esc/Backspace        Go back");
    Console.WriteLine("  r                    Refresh data");
    Console.WriteLine("  s                    Toggle sort order");
    Console.WriteLine("  q                    Quit");
    Console.WriteLine();
}

void ShowSessionList(IReadOnlyList<Session> sessionList, ITerminal term)
{
    term.AppendLine();
    term.SetColor(TerminalColor.White);
    term.AppendLine("Recent Sessions");
    term.SetColor(TerminalColor.Gray);
    term.AppendLine(new string('─', 80));
    term.ResetColor();

    term.AppendLine($"{"Project",-25} {"Status",-10} {"Messages",-12} {"Tools",-8} {"Changes",-8} {"Last Activity",-15}");
    term.SetColor(TerminalColor.Gray);
    term.AppendLine(new string('─', 80));
    term.ResetColor();

    foreach (var session in sessionList.Take(20))
    {
        var status = session.IsActive ? "Active" : "Idle";
        var statusColor = session.IsActive ? TerminalColor.Green : TerminalColor.Gray;
        var timeAgo = FormatTimeAgo(session.LastActivityTime);

        term.Append($"{Truncate(session.ProjectName ?? "Unknown", 25),-25} ");
        term.SetColor(statusColor);
        term.Append($"{status,-10} ");
        term.ResetColor();
        term.AppendLine($"{session.UserMessageCount}↔{session.AssistantMessageCount,-7} {session.ToolCallCount,-8} {session.Changes.Count,-8} {timeAgo,-15}");
    }

    term.AppendLine();
}

void ShowStats(Stats stats, IReadOnlyList<DailyStats> daily, ITerminal term)
{
    term.AppendLine();
    term.SetColor(TerminalColor.Blue);
    term.AppendLine("─── Overall Statistics ───");
    term.ResetColor();
    term.AppendLine();

    term.AppendLine($"  Total Sessions:      {stats.TotalSessions:N0}");
    term.AppendLine($"  Active Sessions:     {stats.ActiveSessions:N0}");
    term.AppendLine($"  User Messages:       {stats.TotalUserMessages:N0}");
    term.AppendLine($"  Assistant Messages:  {stats.TotalAssistantMessages:N0}");
    term.AppendLine($"  Tool Calls:          {stats.TotalToolCalls:N0}");
    term.AppendLine($"  Input Tokens:        {stats.TotalInputTokens:N0}");
    term.AppendLine($"  Output Tokens:       {stats.TotalOutputTokens:N0}");

    if (stats.ToolUsageCounts.Count > 0)
    {
        term.AppendLine();
        term.SetColor(TerminalColor.Blue);
        term.AppendLine("─── Tool Usage ───");
        term.ResetColor();
        term.AppendLine();

        var maxCount = stats.ToolUsageCounts.Values.Max();
        foreach (var kvp in stats.ToolUsageCounts.OrderByDescending(x => x.Value).Take(10))
        {
            var barLength = (int)(40.0 * kvp.Value / maxCount);
            var bar = new string('█', barLength);
            term.Append($"  {kvp.Key,-15} ");
            term.SetColor(TerminalColor.Cyan);
            term.Append(bar);
            term.ResetColor();
            term.AppendLine($" {kvp.Value}");
        }
    }

    term.AppendLine();
    term.SetColor(TerminalColor.Blue);
    term.AppendLine("─── Daily Activity (Last 7 Days) ───");
    term.ResetColor();
    term.AppendLine();

    term.AppendLine($"  {"Date",-12} {"Sessions",-10} {"Messages",-12} {"Tools",-10} {"Tokens",-20}");
    term.SetColor(TerminalColor.Gray);
    term.AppendLine($"  {new string('─', 70)}");
    term.ResetColor();

    foreach (var day in daily)
    {
        var dateStr = day.Date.ToString("ddd MM/dd");
        term.AppendLine($"  {dateStr,-12} {day.SessionCount,-10} {day.UserMessages}↔{day.AssistantMessages,-7} {day.ToolCalls,-10:N0} {day.InputTokens:N0}/{day.OutputTokens:N0}");
    }

    term.AppendLine();
}

string FormatTimeAgo(DateTime time)
{
    var diff = DateTime.UtcNow - time;
    return diff.TotalMinutes switch
    {
        < 1 => "just now",
        < 60 => $"{(int)diff.TotalMinutes}m ago",
        < 1440 => $"{(int)diff.TotalHours}h ago",
        _ => $"{(int)diff.TotalDays}d ago"
    };
}

string Truncate(string text, int maxLength)
{
    if (string.IsNullOrEmpty(text)) return "";
    return text.Length <= maxLength ? text : text[..(maxLength - 3)] + "...";
}
