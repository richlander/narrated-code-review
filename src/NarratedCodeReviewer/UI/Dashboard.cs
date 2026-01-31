using Spectre.Console;
using NarratedCodeReviewer.Domain;
using NarratedCodeReviewer.Services;

namespace NarratedCodeReviewer.UI;

/// <summary>
/// Main terminal dashboard for the code reviewer.
/// </summary>
public class Dashboard
{
    private readonly SessionManager _sessionManager;
    private readonly StatsAggregator _statsAggregator;
    private readonly LogWatcher _watcher;

    private ViewState _currentView = ViewState.SessionList;
    private int _selectedIndex;
    private string? _selectedSessionId;
    private int _selectedChangeIndex;
    private bool _dataChanged = true;
    private bool _running = true;

    private IReadOnlyList<Session> _sessions = [];

    private enum ViewState
    {
        SessionList,
        SessionDetail,
        ChangeDetail
    }

    public Dashboard(SessionManager sessionManager, StatsAggregator statsAggregator, LogWatcher watcher)
    {
        _sessionManager = sessionManager;
        _statsAggregator = statsAggregator;
        _watcher = watcher;

        _watcher.OnDataChanged += () => _dataChanged = true;
    }

    /// <summary>
    /// Runs the dashboard loop.
    /// </summary>
    public async Task RunAsync()
    {
        Console.CursorVisible = false;
        Console.Clear();

        _watcher.Start();

        var refreshTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(200));

        try
        {
            while (_running)
            {
                // Collect all keys from buffer, then process with deduplication
                var keys = new List<ConsoleKeyInfo>();
                while (Console.KeyAvailable)
                {
                    keys.Add(Console.ReadKey(intercept: true));
                }

                if (keys.Count > 0)
                {
                    ProcessKeys(keys);
                    _dataChanged = true;
                }

                if (_dataChanged)
                {
                    _sessions = _sessionManager.GetRecentSessions(50);
                    Render();
                    _dataChanged = false;
                }

                await refreshTimer.WaitForNextTickAsync();
            }
        }
        finally
        {
            Console.CursorVisible = true;
            Console.Clear();
        }
    }

    private void ProcessKeys(List<ConsoleKeyInfo> keys)
    {
        // For navigation keys, collapse repeated presses into a single action per frame.
        // This prevents key repeat from causing multiple movements.
        var processedActions = new HashSet<ConsoleKey>();

        foreach (var key in keys)
        {
            // Navigation keys: only process once per frame
            var isNavigationKey = key.Key is ConsoleKey.UpArrow or ConsoleKey.DownArrow
                or ConsoleKey.K or ConsoleKey.J;

            if (isNavigationKey)
            {
                // Normalize j/k to arrow keys for deduplication
                var normalizedKey = key.Key switch
                {
                    ConsoleKey.K => ConsoleKey.UpArrow,
                    ConsoleKey.J => ConsoleKey.DownArrow,
                    _ => key.Key
                };

                if (processedActions.Contains(normalizedKey))
                    continue;

                processedActions.Add(normalizedKey);
            }

            HandleInput(key);
        }
    }

    private void HandleInput(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Q:
            case ConsoleKey.Escape when _currentView == ViewState.SessionList:
                _running = false;
                break;

            case ConsoleKey.Escape:
            case ConsoleKey.Backspace:
                NavigateBack();
                break;

            case ConsoleKey.UpArrow:
            case ConsoleKey.K:
                NavigateUp();
                break;

            case ConsoleKey.DownArrow:
            case ConsoleKey.J:
                NavigateDown();
                break;

            case ConsoleKey.Enter:
                NavigateInto();
                break;

            case ConsoleKey.R:
                _dataChanged = true;
                break;
        }
    }

    private void NavigateUp()
    {
        switch (_currentView)
        {
            case ViewState.SessionList:
                _selectedIndex = Math.Max(0, _selectedIndex - 1);
                break;
            case ViewState.SessionDetail:
                _selectedChangeIndex = Math.Max(0, _selectedChangeIndex - 1);
                break;
        }
    }

    private void NavigateDown()
    {
        switch (_currentView)
        {
            case ViewState.SessionList:
                _selectedIndex = Math.Min(_sessions.Count - 1, _selectedIndex + 1);
                break;
            case ViewState.SessionDetail:
                var session = _sessionManager.GetSession(_selectedSessionId!);
                if (session != null)
                {
                    _selectedChangeIndex = Math.Min(session.Changes.Count - 1, _selectedChangeIndex + 1);
                }
                break;
        }
    }

    private void NavigateInto()
    {
        switch (_currentView)
        {
            case ViewState.SessionList when _selectedIndex < _sessions.Count:
                _selectedSessionId = _sessions[_selectedIndex].Id;
                _selectedChangeIndex = 0;
                _currentView = ViewState.SessionDetail;
                break;
            case ViewState.SessionDetail:
                var session = _sessionManager.GetSession(_selectedSessionId!);
                if (session != null && _selectedChangeIndex < session.Changes.Count)
                {
                    _currentView = ViewState.ChangeDetail;
                }
                break;
        }
    }

    private void NavigateBack()
    {
        switch (_currentView)
        {
            case ViewState.ChangeDetail:
                _currentView = ViewState.SessionDetail;
                break;
            case ViewState.SessionDetail:
                _currentView = ViewState.SessionList;
                _selectedSessionId = null;
                break;
        }
    }

    private void Render()
    {
        Console.SetCursorPosition(0, 0);

        var layout = new Layout("Root")
            .SplitRows(
                new Layout("Header").Size(3),
                new Layout("Main"),
                new Layout("Footer").Size(3));

        layout["Header"].Update(RenderHeader());
        layout["Footer"].Update(RenderFooter());

        switch (_currentView)
        {
            case ViewState.SessionList:
                layout["Main"].Update(RenderSessionList());
                break;
            case ViewState.SessionDetail:
                layout["Main"].Update(RenderSessionDetail());
                break;
            case ViewState.ChangeDetail:
                layout["Main"].Update(RenderChangeDetail());
                break;
        }

        AnsiConsole.Write(layout);
    }

    private Panel RenderHeader()
    {
        var stats = _statsAggregator.ComputeStats(_sessions);
        var title = new Markup(
            $"[bold blue]Narrated Code Reviewer[/] | " +
            $"[green]{stats.ActiveSessions}[/] active | " +
            $"[dim]{stats.TotalSessions}[/] total sessions | " +
            $"[dim]{stats.TotalToolCalls:N0}[/] tool calls");

        return new Panel(title)
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Blue);
    }

    private Panel RenderFooter()
    {
        var help = _currentView switch
        {
            ViewState.SessionList => "[dim]↑↓[/] Navigate  [dim]Enter[/] View Session  [dim]R[/] Refresh  [dim]Q[/] Quit",
            ViewState.SessionDetail => "[dim]↑↓[/] Navigate  [dim]Enter[/] View Change  [dim]Esc[/] Back  [dim]Q[/] Quit",
            ViewState.ChangeDetail => "[dim]Esc[/] Back  [dim]Q[/] Quit",
            _ => ""
        };

        return new Panel(new Markup(help))
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Grey);
    }

    private Panel RenderSessionList()
    {
        var table = new Table()
            .Border(TableBorder.Simple)
            .BorderColor(Color.Grey)
            .AddColumn(new TableColumn("").Width(2))
            .AddColumn(new TableColumn("Project").Width(25))
            .AddColumn(new TableColumn("Status").Width(10))
            .AddColumn(new TableColumn("Messages").Width(10))
            .AddColumn(new TableColumn("Tools").Width(8))
            .AddColumn(new TableColumn("Last Activity").Width(20));

        for (int i = 0; i < _sessions.Count; i++)
        {
            var session = _sessions[i];
            var isSelected = i == _selectedIndex;
            var prefix = isSelected ? "[bold yellow]>[/]" : " ";
            var highlight = isSelected ? "bold" : "dim";

            var status = session.IsActive
                ? "[green]● Active[/]"
                : "[grey]○ Idle[/]";

            var timeAgo = FormatTimeAgo(session.LastActivityTime);

            table.AddRow(
                new Markup(prefix),
                new Markup($"[{highlight}]{Markup.Escape(session.ProjectName ?? "Unknown")}[/]"),
                new Markup(status),
                new Markup($"[{highlight}]{session.UserMessageCount}↔{session.AssistantMessageCount}[/]"),
                new Markup($"[{highlight}]{session.ToolCallCount}[/]"),
                new Markup($"[{highlight}]{timeAgo}[/]")
            );
        }

        if (_sessions.Count == 0)
        {
            table.AddRow(
                new Markup(""),
                new Markup("[dim]No sessions found[/]"),
                new Markup(""),
                new Markup(""),
                new Markup(""),
                new Markup("")
            );
        }

        return new Panel(table)
            .Header("[bold]Sessions[/]")
            .Border(BoxBorder.Rounded)
            .Expand();
    }

    private Panel RenderSessionDetail()
    {
        var session = _sessionManager.GetSession(_selectedSessionId!);
        if (session == null)
        {
            return new Panel(new Markup("[red]Session not found[/]"))
                .Border(BoxBorder.Rounded);
        }

        var content = new Rows(
            new Markup($"[bold]{Markup.Escape(session.ProjectName ?? "Unknown")}[/]"),
            new Markup($"[dim]Path:[/] {Markup.Escape(session.ProjectPath ?? "N/A")}"),
            new Markup($"[dim]Duration:[/] {FormatDuration(session.Duration)}  |  " +
                      $"[dim]Messages:[/] {session.UserMessageCount}↔{session.AssistantMessageCount}  |  " +
                      $"[dim]Tokens:[/] {session.TotalInputTokens:N0}/{session.TotalOutputTokens:N0}"),
            new Rule("[dim]Changes[/]").RuleStyle("grey")
        );

        var changeTable = new Table()
            .Border(TableBorder.Simple)
            .BorderColor(Color.Grey)
            .AddColumn(new TableColumn("").Width(2))
            .AddColumn(new TableColumn("Type").Width(10))
            .AddColumn(new TableColumn("Description").Width(50))
            .AddColumn(new TableColumn("Files").Width(8))
            .AddColumn(new TableColumn("Time").Width(15));

        for (int i = 0; i < session.Changes.Count; i++)
        {
            var change = session.Changes[i];
            var isSelected = i == _selectedChangeIndex;
            var prefix = isSelected ? "[bold yellow]>[/]" : " ";
            var highlight = isSelected ? "bold" : "dim";

            var typeColor = change.Type switch
            {
                ChangeType.Write => "green",
                ChangeType.Edit => "yellow",
                ChangeType.Execute => "cyan",
                ChangeType.Explore => "blue",
                _ => "white"
            };

            changeTable.AddRow(
                new Markup(prefix),
                new Markup($"[{typeColor}]{change.Type}[/]"),
                new Markup($"[{highlight}]{Markup.Escape(change.Description)}[/]"),
                new Markup($"[{highlight}]{change.AffectedFiles.Count}[/]"),
                new Markup($"[{highlight}]{change.StartTime.ToLocalTime():HH:mm:ss}[/]")
            );
        }

        if (session.Changes.Count == 0)
        {
            changeTable.AddRow(
                new Markup(""),
                new Markup("[dim]No changes[/]"),
                new Markup(""),
                new Markup(""),
                new Markup("")
            );
        }

        var combined = new Rows(content, changeTable);

        return new Panel(combined)
            .Header($"[bold]Session Detail[/]")
            .Border(BoxBorder.Rounded)
            .Expand();
    }

    private Panel RenderChangeDetail()
    {
        var session = _sessionManager.GetSession(_selectedSessionId!);
        if (session == null || _selectedChangeIndex >= session.Changes.Count)
        {
            return new Panel(new Markup("[red]Change not found[/]"))
                .Border(BoxBorder.Rounded);
        }

        var change = session.Changes[_selectedChangeIndex];

        var typeColor = change.Type switch
        {
            ChangeType.Write => "green",
            ChangeType.Edit => "yellow",
            ChangeType.Execute => "cyan",
            ChangeType.Explore => "blue",
            _ => "white"
        };

        var header = new Rows(
            new Markup($"[bold]{Markup.Escape(change.Description)}[/]"),
            new Markup($"[{typeColor}]{change.Type}[/] | " +
                      $"[dim]Duration:[/] {FormatDuration(change.Duration)} | " +
                      $"[dim]Tools:[/] {change.Tools.Count}")
        );

        var toolTable = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .AddColumn("Tool")
            .AddColumn("Target")
            .AddColumn("Details");

        foreach (var tool in change.Tools)
        {
            var target = tool.FilePath ?? tool.Command ?? "-";
            var details = tool.Name.ToLowerInvariant() switch
            {
                "edit" => tool.Content != null ? $"[dim]{Markup.Escape(Truncate(tool.Content, 50))}[/]" : "-",
                "write" => tool.Content != null ? $"[dim]{tool.Content.Length} chars[/]" : "-",
                "bash" => tool.Command != null ? $"[dim]{Markup.Escape(Truncate(tool.Command, 50))}[/]" : "-",
                "grep" or "glob" => tool.Command ?? "-",
                _ => "-"
            };

            toolTable.AddRow(
                new Markup($"[cyan]{Markup.Escape(tool.Name)}[/]"),
                new Markup($"[dim]{Markup.Escape(Truncate(target, 40))}[/]"),
                new Markup(details)
            );
        }

        // Show affected files
        var filesPanel = new Panel(
            new Markup(string.Join("\n",
                change.AffectedFiles.Select(f => $"[dim]•[/] {Markup.Escape(f)}"))))
            .Header("[dim]Affected Files[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Grey);

        var combined = new Rows(header, new Rule().RuleStyle("grey"), toolTable, filesPanel);

        return new Panel(combined)
            .Header("[bold]Change Detail[/]")
            .Border(BoxBorder.Rounded)
            .Expand();
    }

    private static string FormatTimeAgo(DateTime time)
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

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalSeconds < 60)
            return $"{(int)duration.TotalSeconds}s";
        if (duration.TotalMinutes < 60)
            return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
        return $"{(int)duration.TotalHours}h {duration.Minutes}m";
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var firstLine = text.Split('\n')[0];
        return firstLine.Length <= maxLength ? firstLine : firstLine[..(maxLength - 3)] + "...";
    }
}
