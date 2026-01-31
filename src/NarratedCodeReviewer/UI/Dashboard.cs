using Spectre.Console;
using Spectre.Console.Rendering;
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
    private int _changeScrollOffset;
    private bool _newestFirst = true;  // Default to newest first
    private SessionTab _currentTab = SessionTab.Actions;  // Default to Actions (existing behavior)
    private bool _dataChanged = true;
    private bool _running = true;

    private const int MaxVisibleChanges = 15;  // Max changes to show at once

    private IReadOnlyList<Session> _sessions = [];

    private enum ViewState
    {
        SessionList,
        SessionDetail,
        ChangeDetail
    }

    private enum SessionTab
    {
        Summary,
        Actions
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

            case ConsoleKey.S when _currentView == ViewState.SessionDetail:
                _newestFirst = !_newestFirst;
                _selectedChangeIndex = 0;
                _changeScrollOffset = 0;
                break;

            case ConsoleKey.Home when _currentView == ViewState.SessionDetail:
                _selectedChangeIndex = 0;
                _changeScrollOffset = 0;
                break;

            case ConsoleKey.End when _currentView == ViewState.SessionDetail:
                var endSession = _sessionManager.GetSession(_selectedSessionId!);
                if (endSession != null && endSession.Changes.Count > 0)
                {
                    _selectedChangeIndex = endSession.Changes.Count - 1;
                    _changeScrollOffset = Math.Max(0, endSession.Changes.Count - MaxVisibleChanges);
                }
                break;

            case ConsoleKey.PageDown when _currentView == ViewState.SessionDetail:
                var pgDnSession = _sessionManager.GetSession(_selectedSessionId!);
                if (pgDnSession != null)
                {
                    _selectedChangeIndex = Math.Min(pgDnSession.Changes.Count - 1, _selectedChangeIndex + MaxVisibleChanges);
                    _changeScrollOffset = Math.Min(
                        Math.Max(0, pgDnSession.Changes.Count - MaxVisibleChanges),
                        _changeScrollOffset + MaxVisibleChanges);
                }
                break;

            case ConsoleKey.PageUp when _currentView == ViewState.SessionDetail:
                _selectedChangeIndex = Math.Max(0, _selectedChangeIndex - MaxVisibleChanges);
                _changeScrollOffset = Math.Max(0, _changeScrollOffset - MaxVisibleChanges);
                break;

            case ConsoleKey.LeftArrow when _currentView == ViewState.SessionDetail:
            case ConsoleKey.RightArrow when _currentView == ViewState.SessionDetail:
                // Cycle through tabs (wraps around)
                var tabCount = Enum.GetValues<SessionTab>().Length;
                var direction = key.Key == ConsoleKey.RightArrow ? 1 : -1;
                _currentTab = (SessionTab)(((int)_currentTab + direction + tabCount) % tabCount);
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
                // Adjust scroll if selection moves above visible area
                if (_selectedChangeIndex < _changeScrollOffset)
                {
                    _changeScrollOffset = _selectedChangeIndex;
                }
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
                    // Adjust scroll if selection moves below visible area
                    if (_selectedChangeIndex >= _changeScrollOffset + MaxVisibleChanges)
                    {
                        _changeScrollOffset = _selectedChangeIndex - MaxVisibleChanges + 1;
                    }
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
                _changeScrollOffset = 0;
                _currentTab = SessionTab.Actions;  // Default to Actions when entering session
                _currentView = ViewState.SessionDetail;
                break;
            case ViewState.SessionDetail when _currentTab == SessionTab.Actions:
                // Only allow drill-down from Actions tab
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
                layout["Main"].Update(_currentTab == SessionTab.Summary
                    ? RenderSessionSummary()
                    : RenderSessionActions());
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
        var sortLabel = _newestFirst ? "Newest" : "Oldest";
        var help = _currentView switch
        {
            ViewState.SessionList => "[dim]↑↓[/] Navigate  [dim]Enter[/] View Session  [dim]R[/] Refresh  [dim]Q[/] Quit",
            ViewState.SessionDetail when _currentTab == SessionTab.Summary =>
                "[dim]←→[/] Switch Tab  [dim]Esc[/] Back  [dim]Q[/] Quit",
            ViewState.SessionDetail =>
                $"[dim]←→[/] Switch Tab  [dim]↑↓[/] Navigate  [dim]PgUp/PgDn[/] Page  [dim]S[/] Sort: {sortLabel}  [dim]Enter[/] View  [dim]Esc[/] Back  [dim]Q[/] Quit",
            ViewState.ChangeDetail => "[dim]Esc[/] Back to Actions  [dim]Q[/] Quit",
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

    private Panel RenderSessionActions()
    {
        var session = _sessionManager.GetSession(_selectedSessionId!);
        if (session == null)
        {
            return new Panel(new Markup("[red]Session not found[/]"))
                .Border(BoxBorder.Rounded);
        }

        // Sort changes based on sort order
        var sortedChanges = _newestFirst
            ? session.Changes.OrderByDescending(c => c.StartTime).ToList()
            : session.Changes.OrderBy(c => c.StartTime).ToList();

        var totalChanges = sortedChanges.Count;
        var sortLabel = _newestFirst ? "newest first" : "oldest first";

        // Build scroll indicator (escape brackets for Spectre markup)
        var scrollInfo = totalChanges > MaxVisibleChanges
            ? $" [[{_changeScrollOffset + 1}-{Math.Min(_changeScrollOffset + MaxVisibleChanges, totalChanges)} of {totalChanges}]]"
            : "";

        var content = new Rows(
            new Markup($"[bold]{Markup.Escape(session.ProjectName ?? "Unknown")}[/]"),
            new Markup($"[dim]Path:[/] {Markup.Escape(session.ProjectPath ?? "N/A")}"),
            new Markup($"[dim]Duration:[/] {FormatDuration(session.Duration)}  |  " +
                      $"[dim]Messages:[/] {session.UserMessageCount}↔{session.AssistantMessageCount}  |  " +
                      $"[dim]Tokens:[/] {session.TotalInputTokens:N0}/{session.TotalOutputTokens:N0}"),
            new Rule($"[dim]Actions ({sortLabel}){scrollInfo}[/]").RuleStyle("grey")
        );

        var changeTable = new Table()
            .Border(TableBorder.Simple)
            .BorderColor(Color.Grey)
            .AddColumn(new TableColumn("").Width(2))
            .AddColumn(new TableColumn("Type").Width(10))
            .AddColumn(new TableColumn("Description").Width(45))
            .AddColumn(new TableColumn("Tools").Width(6))
            .AddColumn(new TableColumn("Time").Width(10));

        // Show scroll-up indicator
        if (_changeScrollOffset > 0)
        {
            changeTable.AddRow(
                new Markup(""),
                new Markup("[dim]↑↑↑[/]"),
                new Markup($"[dim]{_changeScrollOffset} more above[/]"),
                new Markup(""),
                new Markup("")
            );
        }

        // Show only visible window of changes
        var visibleChanges = sortedChanges
            .Skip(_changeScrollOffset)
            .Take(MaxVisibleChanges)
            .ToList();

        for (int i = 0; i < visibleChanges.Count; i++)
        {
            var actualIndex = _changeScrollOffset + i;
            var change = visibleChanges[i];
            var isSelected = actualIndex == _selectedChangeIndex;
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
                new Markup($"[{highlight}]{change.Tools.Count}[/]"),
                new Markup($"[{highlight}]{change.StartTime.ToLocalTime():HH:mm:ss}[/]")
            );
        }

        // Show scroll-down indicator
        var remainingBelow = totalChanges - _changeScrollOffset - MaxVisibleChanges;
        if (remainingBelow > 0)
        {
            changeTable.AddRow(
                new Markup(""),
                new Markup("[dim]↓↓↓[/]"),
                new Markup($"[dim]{remainingBelow} more below[/]"),
                new Markup(""),
                new Markup("")
            );
        }

        if (totalChanges == 0)
        {
            changeTable.AddRow(
                new Markup(""),
                new Markup("[dim]No actions[/]"),
                new Markup(""),
                new Markup(""),
                new Markup("")
            );
        }

        // Build .NET CLI section if there are any dotnet commands
        IRenderable combined;
        if (session.DotNetCliStats != null && session.DotNetCliStats.TotalCommands > 0)
        {
            var cliTable = new Table()
                .Border(TableBorder.Simple)
                .BorderColor(Color.Grey)
                .AddColumn(new TableColumn("Command").Width(15))
                .AddColumn(new TableColumn("Count").Width(8))
                .AddColumn(new TableColumn("").Width(40));

            foreach (var kvp in session.DotNetCliStats.CommandCounts
                .OrderByDescending(x => x.Value)
                .Take(8))
            {
                var bar = new string('█', Math.Min(kvp.Value, 30));
                // dotnet-* tools show as-is, regular subcommands show as "dotnet <cmd>"
                var displayName = kvp.Key.StartsWith("dotnet-", StringComparison.OrdinalIgnoreCase)
                    ? kvp.Key
                    : $"dotnet {kvp.Key}";
                cliTable.AddRow(
                    new Markup($"[cyan]{Markup.Escape(displayName)}[/]"),
                    new Markup($"[bold]{kvp.Value}[/]"),
                    new Markup($"[blue]{bar}[/]")
                );
            }

            combined = new Rows(
                content,
                changeTable,
                new Rule($"[dim].NET CLI ({session.DotNetCliStats.TotalCommands} commands)[/]").RuleStyle("grey"),
                cliTable
            );
        }
        else
        {
            combined = new Rows(content, changeTable);
        }

        return new Panel(combined)
            .Header($"[bold]Session[/] [dim]Summary[/]  [bold cyan]Actions[/]")
            .Border(BoxBorder.Rounded)
            .Expand();
    }

    private Panel RenderSessionSummary()
    {
        var session = _sessionManager.GetSession(_selectedSessionId!);
        if (session == null)
        {
            return new Panel(new Markup("[red]Session not found[/]"))
                .Border(BoxBorder.Rounded);
        }

        // Aggregate tool operations by name
        var toolCounts = session.Changes
            .SelectMany(c => c.Tools)
            .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var totalTools = toolCounts.Values.Sum();

        // Build Claude Operations table
        var claudeTable = new Table()
            .Border(TableBorder.Simple)
            .BorderColor(Color.Grey)
            .AddColumn(new TableColumn("Operation").Width(18))
            .AddColumn(new TableColumn("Count").RightAligned().Width(8));

        // Sort all tools by count descending
        foreach (var kvp in toolCounts.OrderByDescending(x => x.Value))
        {
            var color = kvp.Key.ToLowerInvariant() switch
            {
                "read" => "blue",
                "edit" => "yellow",
                "write" => "green",
                "bash" => "cyan",
                "webfetch" => "magenta",
                "grep" or "glob" => "white",
                "task" => "green",
                _ => "dim"
            };
            claudeTable.AddRow(
                new Markup($"[{color}]{kvp.Key}[/]"),
                new Markup($"[bold]{kvp.Value}[/]")
            );
        }

        claudeTable.AddEmptyRow();
        claudeTable.AddRow(
            new Markup("[dim]Total[/]"),
            new Markup($"[bold]{totalTools}[/]")
        );

        // Build DotNet Operations table
        var dotnetTable = new Table()
            .Border(TableBorder.Simple)
            .BorderColor(Color.Grey)
            .AddColumn(new TableColumn("Command").Width(18))
            .AddColumn(new TableColumn("Count").RightAligned().Width(8));

        if (session.DotNetCliStats != null && session.DotNetCliStats.TotalCommands > 0)
        {
            foreach (var kvp in session.DotNetCliStats.CommandCounts.OrderByDescending(x => x.Value))
            {
                // dotnet-* tools show as-is, regular subcommands show as "dotnet <cmd>"
                var displayName = kvp.Key.StartsWith("dotnet-", StringComparison.OrdinalIgnoreCase)
                    ? kvp.Key
                    : $"dotnet {kvp.Key}";
                dotnetTable.AddRow(
                    new Markup($"[cyan]{Markup.Escape(displayName)}[/]"),
                    new Markup($"[bold]{kvp.Value}[/]")
                );
            }

            dotnetTable.AddEmptyRow();
            dotnetTable.AddRow(
                new Markup("[dim]Total[/]"),
                new Markup($"[bold]{session.DotNetCliStats.TotalCommands}[/]")
            );
        }
        else
        {
            dotnetTable.AddRow(
                new Markup("[dim]No dotnet commands[/]"),
                new Markup("")
            );
        }

        // Session header info
        var header = new Rows(
            new Markup($"[bold]{Markup.Escape(session.ProjectName ?? "Unknown")}[/]"),
            new Markup($"[dim]Path:[/] {Markup.Escape(session.ProjectPath ?? "N/A")}"),
            new Markup($"[dim]Duration:[/] {FormatDuration(session.Duration)}  |  " +
                      $"[dim]Messages:[/] {session.UserMessageCount}↔{session.AssistantMessageCount}  |  " +
                      $"[dim]Tokens:[/] {session.TotalInputTokens:N0}/{session.TotalOutputTokens:N0}"),
            Text.Empty
        );

        // Two-column layout for operations
        var columnsTable = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn(new TableColumn("").Width(55))
            .AddColumn(new TableColumn("").Width(55));

        columnsTable.AddRow(
            new Rows(
                new Rule("[dim]Claude Operations[/]").RuleStyle("grey").LeftJustified(),
                claudeTable
            ),
            new Rows(
                new Rule("[dim]DotNet Operations[/]").RuleStyle("grey").LeftJustified(),
                dotnetTable
            )
        );

        var combined = new Rows(header, columnsTable);

        return new Panel(combined)
            .Header($"[bold]Session[/] [bold cyan]Summary[/]  [dim]Actions[/]")
            .Border(BoxBorder.Rounded)
            .Expand();
    }

    private Panel RenderChangeDetail()
    {
        var session = _sessionManager.GetSession(_selectedSessionId!);
        if (session == null || _selectedChangeIndex >= session.Changes.Count)
        {
            return new Panel(new Markup("[red]Action not found[/]"))
                .Border(BoxBorder.Rounded);
        }

        // Use same sort order as session detail view
        var sortedChanges = _newestFirst
            ? session.Changes.OrderByDescending(c => c.StartTime).ToList()
            : session.Changes.OrderBy(c => c.StartTime).ToList();

        var change = sortedChanges[_selectedChangeIndex];

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
                      $"[dim]Tools:[/] {change.Tools.Count} | " +
                      $"[dim]Time:[/] {change.StartTime.ToLocalTime():HH:mm:ss}")
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
            .Header("[bold]Action Detail[/]")
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
