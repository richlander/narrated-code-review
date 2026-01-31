using NarratedCodeReviewer.Domain;
using NarratedCodeReviewer.Providers;
using NarratedCodeReviewer.Services;
using NarratedCodeReviewer.UI;
using Spectre.Console;

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

// Check if base path exists
if (!Directory.Exists(provider.BasePath))
{
    AnsiConsole.MarkupLine($"[yellow]Warning:[/] Claude Code logs directory not found at [dim]{provider.BasePath}[/]");
    AnsiConsole.MarkupLine("[dim]The dashboard will start but no sessions will be displayed until Claude Code creates logs.[/]");
    AnsiConsole.WriteLine();
}

// Create services
var sessionManager = new SessionManager();
var statsAggregator = new StatsAggregator();
var watcher = new LogWatcher(provider, sessionManager);

// Load existing data
AnsiConsole.Status()
    .Spinner(Spinner.Known.Dots)
    .Start("Loading sessions...", ctx =>
    {
        sessionManager.LoadFromProviderAsync(provider).GetAwaiter().GetResult();
    });

var sessions = sessionManager.GetAllSessions();
AnsiConsole.MarkupLine($"[green]Loaded {sessions.Count} sessions[/]");

if (args.Contains("--list") || args.Contains("-l"))
{
    // List mode - just show sessions and exit
    ShowSessionList(sessions);
    return 0;
}

if (args.Contains("--stats") || args.Contains("-s"))
{
    // Stats mode - show statistics and exit
    ShowStats(statsAggregator.ComputeStats(sessions), statsAggregator.ComputeDailyStats(sessions));
    return 0;
}

// Interactive dashboard mode
var dashboard = new Dashboard(sessionManager, statsAggregator, watcher);
await dashboard.RunAsync();

return 0;

// Helper methods
void ShowHelp()
{
    AnsiConsole.Write(new FigletText("NCR").Color(Color.Blue));
    AnsiConsole.MarkupLine("[bold]Narrated Code Reviewer[/] - Terminal dashboard for Claude Code sessions\n");

    var table = new Table()
        .Border(TableBorder.Rounded)
        .AddColumn("Option")
        .AddColumn("Description");

    table.AddRow("[cyan]-h, --help[/]", "Show this help message");
    table.AddRow("[cyan]-v, --version[/]", "Show version information");
    table.AddRow("[cyan]-p, --path <path>[/]", "Custom path to Claude logs directory");
    table.AddRow("[cyan]-l, --list[/]", "List sessions and exit (non-interactive)");
    table.AddRow("[cyan]-s, --stats[/]", "Show statistics and exit (non-interactive)");

    AnsiConsole.Write(table);

    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[bold]Keyboard Controls (Interactive Mode):[/]");

    var controls = new Table()
        .Border(TableBorder.Simple)
        .AddColumn("Key")
        .AddColumn("Action");

    controls.AddRow("[cyan]↑/↓ or j/k[/]", "Navigate up/down");
    controls.AddRow("[cyan]Enter[/]", "View selected item");
    controls.AddRow("[cyan]Esc/Backspace[/]", "Go back");
    controls.AddRow("[cyan]r[/]", "Refresh data");
    controls.AddRow("[cyan]q[/]", "Quit");

    AnsiConsole.Write(controls);
}

void ShowSessionList(IReadOnlyList<Session> sessionList)
{
    var table = new Table()
        .Border(TableBorder.Rounded)
        .Title("[bold]Recent Sessions[/]")
        .AddColumn("Project")
        .AddColumn("Status")
        .AddColumn("Messages")
        .AddColumn("Tools")
        .AddColumn("Changes")
        .AddColumn("Last Activity");

    foreach (var session in sessionList.Take(20))
    {
        var status = session.IsActive ? "[green]Active[/]" : "[grey]Idle[/]";
        var timeAgo = FormatTimeAgo(session.LastActivityTime);

        table.AddRow(
            $"[bold]{Markup.Escape(session.ProjectName ?? "Unknown")}[/]",
            status,
            $"{session.UserMessageCount}↔{session.AssistantMessageCount}",
            session.ToolCallCount.ToString(),
            session.Changes.Count.ToString(),
            timeAgo
        );
    }

    AnsiConsole.Write(table);
}

void ShowStats(Stats stats, IReadOnlyList<DailyStats> daily)
{
    AnsiConsole.Write(new Rule("[bold]Overall Statistics[/]").RuleStyle("blue"));

    var overview = new Table()
        .Border(TableBorder.Rounded)
        .AddColumn("Metric")
        .AddColumn("Value");

    overview.AddRow("Total Sessions", stats.TotalSessions.ToString("N0"));
    overview.AddRow("Active Sessions", stats.ActiveSessions.ToString("N0"));
    overview.AddRow("User Messages", stats.TotalUserMessages.ToString("N0"));
    overview.AddRow("Assistant Messages", stats.TotalAssistantMessages.ToString("N0"));
    overview.AddRow("Tool Calls", stats.TotalToolCalls.ToString("N0"));
    overview.AddRow("Input Tokens", stats.TotalInputTokens.ToString("N0"));
    overview.AddRow("Output Tokens", stats.TotalOutputTokens.ToString("N0"));

    AnsiConsole.Write(overview);

    if (stats.ToolUsageCounts.Count > 0)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[bold]Tool Usage[/]").RuleStyle("blue"));

        var toolChart = new BarChart()
            .Width(60)
            .Label("[green bold]Top Tools[/]");

        foreach (var kvp in stats.ToolUsageCounts.OrderByDescending(x => x.Value).Take(10))
        {
            toolChart.AddItem(kvp.Key, kvp.Value, Color.Cyan1);
        }

        AnsiConsole.Write(toolChart);
    }

    AnsiConsole.WriteLine();
    AnsiConsole.Write(new Rule("[bold]Daily Activity (Last 7 Days)[/]").RuleStyle("blue"));

    var dailyTable = new Table()
        .Border(TableBorder.Rounded)
        .AddColumn("Date")
        .AddColumn("Sessions")
        .AddColumn("Messages")
        .AddColumn("Tools")
        .AddColumn("Tokens");

    foreach (var day in daily)
    {
        dailyTable.AddRow(
            day.Date.ToString("ddd MM/dd"),
            day.SessionCount.ToString(),
            $"{day.UserMessages}↔{day.AssistantMessages}",
            day.ToolCalls.ToString("N0"),
            $"{day.InputTokens:N0}/{day.OutputTokens:N0}"
        );
    }

    AnsiConsole.Write(dailyTable);
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
