using Microsoft.Extensions.Terminal;
using Microsoft.Extensions.Terminal.Components;
using AgentLogs.Domain;
using AgentLogs.Services;

namespace NarratedCodeReviewer.UI;

/// <summary>
/// Main terminal dashboard for the code reviewer.
/// </summary>
public class Dashboard
{
    private readonly SessionManager _sessionManager;
    private readonly StatsAggregator _statsAggregator;
    private readonly LogWatcher _watcher;
    private readonly TerminalApp _app;

    private string? _selectedSessionId;
    private int _selectedSessionIndex;
    private int _selectedChangeIndex;
    private bool _newestFirst = true;
    private bool _dataChanged = true;

    private IReadOnlyList<Session> _sessions = [];

    // Navigation
    private readonly ViewStack _viewStack;

    // UI Components
    private readonly Panel _headerPanel;
    private readonly Panel _footerPanel;
    private readonly Panel _mainPanel;
    private readonly Table _sessionTable;
    private readonly Table _actionsTable;
    private readonly TabView _sessionTabs;
    private readonly Text _headerText;
    private readonly Text _footerText;
    private readonly Text _summaryText;
    private readonly Text _detailText;
    private readonly Text _detailHeader;
    private readonly Panel _detailPanel;
    private readonly Text _diffText;
    private readonly Panel _diffPanel;
    private readonly Layout _actionsLayout;

    public Dashboard(SessionManager sessionManager, StatsAggregator statsAggregator, LogWatcher watcher, ITerminal terminal)
    {
        _sessionManager = sessionManager;
        _statsAggregator = statsAggregator;
        _watcher = watcher;

        _watcher.OnDataChanged += () => _dataChanged = true;

        // Create the app
        _app = new TerminalApp(terminal);

        // Initialize text components
        _headerText = new Text();
        _footerText = new Text();
        _summaryText = new Text();
        _detailText = new Text();
        _detailHeader = new Text();

        // Header and footer panels
        _headerPanel = new Panel
        {
            Content = _headerText,
            Border = BoxBorderStyle.Rounded,
            BorderColor = TerminalColor.Blue
        };

        _footerPanel = new Panel
        {
            Content = _footerText,
            Border = BoxBorderStyle.Rounded,
            BorderColor = TerminalColor.Gray
        };

        // Session list table
        _sessionTable = new Table
        {
            Border = TableBorderStyle.None,
            IsSelectable = true,
            ShowHeader = true
        };
        _sessionTable.AddColumn("", 2);
        _sessionTable.AddColumn("Project", 25);
        _sessionTable.AddColumn("Status", 10);
        _sessionTable.AddColumn("Messages", 10);
        _sessionTable.AddColumn("Tools", 8);
        _sessionTable.AddColumn("Last Activity", 20);

        // Actions table for session detail
        _actionsTable = new Table
        {
            Border = TableBorderStyle.None,
            IsSelectable = true,
            ShowHeader = true
        };
        _actionsTable.AddColumn("", 2);
        _actionsTable.AddColumn("Type", 10);
        _actionsTable.AddColumn("Description", 45);
        _actionsTable.AddColumn("Tools", 6);
        _actionsTable.AddColumn("Time", 10);

        // Actions layout (header + table)
        _actionsLayout = new Layout { Direction = LayoutDirection.Vertical };
        _actionsLayout.Add(_detailHeader, LayoutSize.Fixed(4));
        _actionsLayout.Add(_actionsTable, LayoutSize.Fill);

        // Session tabs (Summary / Actions)
        _sessionTabs = new TabView
        {
            ActiveTabColor = TerminalColor.Cyan,
            InactiveTabColor = TerminalColor.Gray
        };
        _sessionTabs.Add("Summary", _summaryText);
        _sessionTabs.Add("Actions", _actionsLayout);
        _sessionTabs.SelectedIndex = 1; // Start on Actions tab
        _sessionTabs.OnKey = HandleSessionDetailKey;

        // Detail panel for action details
        _detailPanel = new Panel
        {
            Content = _detailText,
            Border = BoxBorderStyle.None
        };

        // Diff panel for code view
        _diffText = new Text();
        _diffPanel = new Panel
        {
            Content = _diffText,
            Border = BoxBorderStyle.None
        };

        // Main content panel
        _mainPanel = new Panel
        {
            Border = BoxBorderStyle.Rounded,
            BorderColor = TerminalColor.Gray
        };

        // View stack for navigation
        _viewStack = new ViewStack();
        _viewStack.OnKey = HandleGlobalKey;

        // Set up the app layout
        _app.Layout.Direction = LayoutDirection.Vertical;
        _app.Layout.Add(_headerPanel, LayoutSize.Fixed(3));
        _app.Layout.Add(_mainPanel, LayoutSize.Fill);
        _app.Layout.Add(_footerPanel, LayoutSize.Fixed(3));
    }

    /// <summary>
    /// Runs the dashboard loop.
    /// </summary>
    public async Task RunAsync()
    {
        _watcher.Start();

        // Initialize with session list view
        _viewStack.Reset(_sessionTable);
        _mainPanel.Header = "Sessions";
        _mainPanel.Content = _viewStack;

        // Register view stack as focusable
        _app.RegisterFocusable(_viewStack);

        _app.Terminal.HideCursor();
        _app.Buffer.Invalidate();

        try
        {
            using var ticker = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
            bool running = true;

            while (running)
            {
                // Check for terminal resize
                if (_app.Buffer.Width != _app.Terminal.Width || _app.Buffer.Height != _app.Terminal.Height)
                {
                    _app.Invalidate();
                    _dataChanged = true;
                }

                // Process keys
                while (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(intercept: true);

                    // Q quits from anywhere
                    if (key.Key == ConsoleKey.Q)
                    {
                        running = false;
                        break;
                    }

                    // Escape at root quits
                    if (key.Key == ConsoleKey.Escape && _viewStack.Count == 1)
                    {
                        running = false;
                        break;
                    }

                    // Invalidate buffer on back navigation
                    if ((key.Key == ConsoleKey.Escape || key.Key == ConsoleKey.Backspace) && _viewStack.Count > 1)
                    {
                        _app.Buffer.Invalidate();
                    }

                    // Let the view stack handle the key
                    _viewStack.HandleKey(key);
                    _dataChanged = true;
                }

                // Render if needed
                if (_dataChanged)
                {
                    _sessions = _sessionManager.GetRecentSessions(50);
                    UpdateUI();
                    _app.Layout.Render(_app.Buffer, Region.FromTerminal(_app.Terminal));
                    _app.Buffer.Flush(_app.Terminal);
                    _dataChanged = false;
                }

                await ticker.WaitForNextTickAsync();
            }
        }
        finally
        {
            _app.Terminal.ShowCursor();
            _app.Terminal.Append(AnsiCodes.ClearScreenAndHome);
        }
    }

    private bool HandleGlobalKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.R:
                _dataChanged = true;
                return true;

            case ConsoleKey.UpArrow:
            case ConsoleKey.K:
                if (_viewStack.Current == _sessionTable && _sessions.Count > 0)
                {
                    _selectedSessionIndex = Math.Max(0, _selectedSessionIndex - 1);
                    return true;
                }
                if (_viewStack.Current == _detailPanel && _selectedChangeIndex > 0)
                {
                    _selectedChangeIndex--;
                    _app.Buffer.Invalidate();
                    return true;
                }
                break;

            case ConsoleKey.DownArrow:
            case ConsoleKey.J:
                if (_viewStack.Current == _sessionTable && _sessions.Count > 0)
                {
                    _selectedSessionIndex = Math.Min(_sessions.Count - 1, _selectedSessionIndex + 1);
                    return true;
                }
                if (_viewStack.Current == _detailPanel)
                {
                    var detailSession = _sessionManager.GetSession(_selectedSessionId!);
                    if (detailSession != null && _selectedChangeIndex < detailSession.Changes.Count - 1)
                    {
                        _selectedChangeIndex++;
                        _app.Buffer.Invalidate();
                        return true;
                    }
                }
                break;

            case ConsoleKey.Enter when _viewStack.Current == _sessionTable:
                // Navigate into session detail
                if (_selectedSessionIndex >= 0 && _selectedSessionIndex < _sessions.Count)
                {
                    _selectedSessionId = _sessions[_selectedSessionIndex].Id;
                    _selectedChangeIndex = 0;
                    _sessionTabs.SelectedIndex = 1; // Actions tab
                    _viewStack.Push(_sessionTabs);
                    _app.Buffer.Invalidate();
                    return true;
                }
                break;

            case ConsoleKey.Enter when _viewStack.Current == _sessionTabs && _sessionTabs.SelectedIndex == 1:
                // Navigate into change detail from Actions tab
                var session = _sessionManager.GetSession(_selectedSessionId!);
                if (session != null && _selectedChangeIndex < session.Changes.Count)
                {
                    _viewStack.Push(_detailPanel);
                    _app.Buffer.Invalidate();
                    return true;
                }
                break;

            case ConsoleKey.F when _viewStack.Current == _detailPanel:
                // Show full content view if there's expandable content
                if (_hasMoreContent)
                {
                    _viewStack.Push(_diffPanel);
                    _app.Buffer.Invalidate();
                    return true;
                }
                break;
        }

        return false;
    }

    private bool HandleSessionDetailKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.S:
                _newestFirst = !_newestFirst;
                _selectedChangeIndex = 0;
                return true;

            case ConsoleKey.UpArrow:
            case ConsoleKey.K:
                if (_sessionTabs.SelectedIndex == 1) // Actions tab
                {
                    _selectedChangeIndex = Math.Max(0, _selectedChangeIndex - 1);
                    _actionsTable.SelectedIndex = _selectedChangeIndex;
                    return true;
                }
                break;

            case ConsoleKey.DownArrow:
            case ConsoleKey.J:
                if (_sessionTabs.SelectedIndex == 1) // Actions tab
                {
                    var session = _sessionManager.GetSession(_selectedSessionId!);
                    if (session != null)
                    {
                        _selectedChangeIndex = Math.Min(session.Changes.Count - 1, _selectedChangeIndex + 1);
                        _actionsTable.SelectedIndex = _selectedChangeIndex;
                        return true;
                    }
                }
                break;

            case ConsoleKey.Home:
                _selectedChangeIndex = 0;
                _actionsTable.SelectedIndex = _selectedChangeIndex;
                return true;

            case ConsoleKey.End:
                var endSession = _sessionManager.GetSession(_selectedSessionId!);
                if (endSession != null && endSession.Changes.Count > 0)
                {
                    _selectedChangeIndex = endSession.Changes.Count - 1;
                    _actionsTable.SelectedIndex = _selectedChangeIndex;
                }
                return true;

            case ConsoleKey.PageDown:
                var pgDnSession = _sessionManager.GetSession(_selectedSessionId!);
                if (pgDnSession != null)
                {
                    _selectedChangeIndex = Math.Min(pgDnSession.Changes.Count - 1, _selectedChangeIndex + 15);
                    _actionsTable.SelectedIndex = _selectedChangeIndex;
                }
                return true;

            case ConsoleKey.PageUp:
                _selectedChangeIndex = Math.Max(0, _selectedChangeIndex - 15);
                _actionsTable.SelectedIndex = _selectedChangeIndex;
                return true;
        }

        return false;
    }

    private IReadOnlyList<LogicalChange> GetSortedChanges(Session session)
    {
        return _newestFirst
            ? session.Changes.OrderByDescending(c => c.StartTime).ToList()
            : session.Changes.OrderBy(c => c.StartTime).ToList();
    }

    private void UpdateUI()
    {
        UpdateHeader();

        // Update current view content and panel content (before footer, as content may set state used by footer)
        if (_viewStack.Current == _sessionTable)
        {
            UpdateSessionList();
            _mainPanel.Header = "Sessions";
            _mainPanel.Content = _viewStack;
        }
        else if (_viewStack.Current == _sessionTabs)
        {
            if (_sessionTabs.SelectedIndex == 0)
            {
                UpdateSessionSummary();
            }
            else
            {
                UpdateSessionActions();
            }
            _mainPanel.Header = "Session";
            _mainPanel.Content = _viewStack;
        }
        else if (_viewStack.Current == _detailPanel)
        {
            UpdateChangeDetail();
            _mainPanel.Header = "Action Detail";
            _mainPanel.Content = _detailPanel;
        }
        else if (_viewStack.Current == _diffPanel)
        {
            UpdateFullContentView();
            _mainPanel.Header = "Full Content";
            _mainPanel.Content = _diffPanel;
        }

        UpdateFooter();
    }

    private void UpdateHeader()
    {
        var stats = _statsAggregator.ComputeStats(_sessions);
        _headerText.Clear();
        _headerText
            .Append("Narrated Code Reviewer", TerminalColor.Blue)
            .Append(" | ")
            .Append($"{stats.ActiveSessions}", TerminalColor.Green)
            .Append(" active | ")
            .Append($"{stats.TotalSessions}", TerminalColor.Gray)
            .Append(" total | ")
            .Append($"{stats.TotalToolCalls:N0}", TerminalColor.Gray)
            .Append(" tool calls");
    }

    private void UpdateFooter()
    {
        _footerText.Clear();

        if (_viewStack.Current == _sessionTable)
        {
            _footerText
                .Append("↑↓", TerminalColor.Gray).Append(" Navigate  ")
                .Append("Enter", TerminalColor.Gray).Append(" View  ")
                .Append("R", TerminalColor.Gray).Append(" Refresh  ")
                .Append("Q", TerminalColor.Gray).Append(" Quit");
        }
        else if (_viewStack.Current == _sessionTabs)
        {
            var sortLabel = _newestFirst ? "Newest" : "Oldest";
            if (_sessionTabs.SelectedIndex == 0) // Summary
            {
                _footerText
                    .Append("←→", TerminalColor.Gray).Append(" Tab  ")
                    .Append("Esc", TerminalColor.Gray).Append(" Back  ")
                    .Append("Q", TerminalColor.Gray).Append(" Quit");
            }
            else // Actions
            {
                _footerText
                    .Append("←→", TerminalColor.Gray).Append(" Tab  ")
                    .Append("↑↓", TerminalColor.Gray).Append(" Navigate  ")
                    .Append("S", TerminalColor.Gray).Append($" Sort:{sortLabel}  ")
                    .Append("Enter", TerminalColor.Gray).Append(" View  ")
                    .Append("Esc", TerminalColor.Gray).Append(" Back");
            }
        }
        else if (_viewStack.Current == _detailPanel)
        {
            _footerText
                .Append("↑↓", TerminalColor.Gray).Append(" Prev/Next  ");
            if (_hasMoreContent)
            {
                _footerText
                    .Append("F", TerminalColor.Gray).Append(" Full View  ");
            }
            _footerText
                .Append("Esc", TerminalColor.Gray).Append(" Back  ")
                .Append("Q", TerminalColor.Gray).Append(" Quit");
        }
        else if (_viewStack.Current == _diffPanel)
        {
            _footerText
                .Append("Esc", TerminalColor.Gray).Append(" Back  ")
                .Append("Q", TerminalColor.Gray).Append(" Quit");
        }
    }

    private void UpdateSessionList()
    {
        _sessionTable.Bind(_sessions, (session, index) => new TableRow(new[]
        {
            new TableCell(index == _selectedSessionIndex ? ">" : ""),
            new TableCell(session.ProjectName ?? "Unknown"),
            new TableCell(session.IsActive ? "● Active" : "○ Idle",
                session.IsActive ? TerminalColor.Green : TerminalColor.Gray),
            new TableCell($"{session.UserMessageCount}↔{session.AssistantMessageCount}"),
            new TableCell($"{session.ToolCallCount}"),
            new TableCell(FormatTimeAgo(session.LastActivityTime))
        }));

        _sessionTable.SelectedIndex = _selectedSessionIndex;

        if (_sessions.Count == 0)
        {
            _sessionTable.AddRow(
                new TableCell(""),
                new TableCell("No sessions found", TerminalColor.Gray),
                new TableCell(""),
                new TableCell(""),
                new TableCell(""),
                new TableCell("")
            );
        }
    }

    private void UpdateSessionActions()
    {
        var session = _sessionManager.GetSession(_selectedSessionId!);
        if (session == null)
        {
            return;
        }

        var sortedChanges = _newestFirst
            ? session.Changes.OrderByDescending(c => c.StartTime).ToList()
            : session.Changes.OrderBy(c => c.StartTime).ToList();

        // Build detail header
        _detailHeader.Clear();
        _detailHeader
            .AppendLine(session.ProjectName ?? "Unknown", TerminalColor.White)
            .Append("Path: ", TerminalColor.Gray).AppendLine(session.ProjectPath ?? "N/A")
            .Append("Duration: ", TerminalColor.Gray).Append(FormatDuration(session.Duration))
            .Append("  |  Messages: ", TerminalColor.Gray).Append($"{session.UserMessageCount}↔{session.AssistantMessageCount}")
            .Append("  |  Tokens: ", TerminalColor.Gray).AppendLine($"{session.TotalInputTokens:N0}/{session.TotalOutputTokens:N0}");

        // Bind actions table
        _actionsTable.Bind(sortedChanges, (change, index) =>
        {
            var typeColor = change.Type switch
            {
                ChangeType.Write => TerminalColor.Green,
                ChangeType.Edit => TerminalColor.Yellow,
                ChangeType.Execute => TerminalColor.Cyan,
                ChangeType.Explore => TerminalColor.Blue,
                _ => TerminalColor.White
            };

            return new TableRow(new[]
            {
                new TableCell(index == _selectedChangeIndex ? ">" : ""),
                new TableCell(change.Type.ToString(), typeColor),
                new TableCell(Truncate(change.Description, 45)),
                new TableCell($"{change.Tools.Count}"),
                new TableCell(change.StartTime.ToLocalTime().ToString("HH:mm:ss"))
            });
        });

        _actionsTable.SelectedIndex = _selectedChangeIndex;
    }

    private void UpdateSessionSummary()
    {
        var session = _sessionManager.GetSession(_selectedSessionId!);
        if (session == null)
        {
            return;
        }

        var toolCounts = session.Changes
            .SelectMany(c => c.Tools)
            .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        _summaryText.Clear();
        _summaryText
            .AppendLine(session.ProjectName ?? "Unknown", TerminalColor.White)
            .Append("Path: ", TerminalColor.Gray).AppendLine(session.ProjectPath ?? "N/A")
            .Append("Duration: ", TerminalColor.Gray).Append(FormatDuration(session.Duration))
            .Append("  |  Messages: ", TerminalColor.Gray).Append($"{session.UserMessageCount}↔{session.AssistantMessageCount}")
            .Append("  |  Tokens: ", TerminalColor.Gray).AppendLine($"{session.TotalInputTokens:N0}/{session.TotalOutputTokens:N0}")
            .AppendLine()
            .AppendLine("─── Claude Operations ───", TerminalColor.Gray);

        foreach (var kvp in toolCounts.OrderByDescending(x => x.Value))
        {
            var color = kvp.Key.ToLowerInvariant() switch
            {
                "read" => TerminalColor.Blue,
                "edit" => TerminalColor.Yellow,
                "write" => TerminalColor.Green,
                "bash" => TerminalColor.Cyan,
                "webfetch" => TerminalColor.Magenta,
                "grep" or "glob" => TerminalColor.White,
                "task" => TerminalColor.Green,
                _ => TerminalColor.Gray
            };
            _summaryText.Append($"  {kvp.Key,-15}", color).AppendLine($" {kvp.Value}");
        }

        if (session.DotNetCliStats != null && session.DotNetCliStats.TotalCommands > 0)
        {
            _summaryText.AppendLine()
                .AppendLine("─── DotNet Operations ───", TerminalColor.Gray);

            foreach (var kvp in session.DotNetCliStats.CommandCounts.OrderByDescending(x => x.Value))
            {
                var displayName = kvp.Key.StartsWith("dotnet-", StringComparison.OrdinalIgnoreCase)
                    ? kvp.Key
                    : $"dotnet {kvp.Key}";
                _summaryText.Append($"  {displayName,-15}", TerminalColor.Cyan).AppendLine($" {kvp.Value}");
            }
        }
    }

    private void UpdateChangeDetail()
    {
        var session = _sessionManager.GetSession(_selectedSessionId!);
        if (session == null || _selectedChangeIndex >= session.Changes.Count)
        {
            return;
        }

        var change = GetSortedChanges(session)[_selectedChangeIndex];
        var typeColor = change.Type switch
        {
            ChangeType.Write => TerminalColor.Green,
            ChangeType.Edit => TerminalColor.Yellow,
            ChangeType.Execute => TerminalColor.Cyan,
            ChangeType.Explore => TerminalColor.Blue,
            _ => TerminalColor.White
        };

        _detailText.Clear();
        _detailText
            .AppendLine(change.Description, TerminalColor.White)
            .Append(change.Type.ToString(), typeColor)
            .Append(" | Tools: ", TerminalColor.Gray).Append($"{change.Tools.Count}")
            .Append(" | Time: ", TerminalColor.Gray).AppendLine(change.StartTime.ToLocalTime().ToString("HH:mm:ss"));

        // Show inline tool details
        foreach (var tool in change.Tools)
        {
            _detailText.AppendLine()
                .Append($"─── {tool.Name} ", TerminalColor.Cyan);

            var target = tool.FilePath ?? tool.Command ?? "";
            if (!string.IsNullOrEmpty(target))
            {
                var fileName = tool.FilePath != null ? Path.GetFileName(tool.FilePath) : target;
                _detailText.Append($"({fileName})", TerminalColor.Gray);
            }
            _detailText.AppendLine(" ───", TerminalColor.Cyan);

            // Show tool-specific content inline
            RenderToolContent(_detailText, tool, maxLines: 8);
        }

        _hasMoreContent = change.Tools.Any(t => HasExpandableContent(t));
    }

    private bool _hasMoreContent;

    private void RenderToolContent(Text text, ToolUse tool, int maxLines)
    {
        var toolName = tool.Name.ToLowerInvariant();

        switch (toolName)
        {
            case "edit":
                if (!string.IsNullOrEmpty(tool.OldContent) || !string.IsNullOrEmpty(tool.Content))
                {
                    var oldLines = (tool.OldContent ?? "").Split('\n');
                    var newLines = (tool.Content ?? "").Split('\n');
                    var totalLines = oldLines.Length + newLines.Length;
                    var shown = 0;

                    foreach (var line in oldLines.Take(maxLines / 2))
                    {
                        text.Append("- ", TerminalColor.Red)
                            .AppendLine(TruncateLine(line, 70), TerminalColor.Red);
                        shown++;
                    }
                    foreach (var line in newLines.Take(maxLines / 2))
                    {
                        text.Append("+ ", TerminalColor.Green)
                            .AppendLine(TruncateLine(line, 70), TerminalColor.Green);
                        shown++;
                    }
                    if (totalLines > shown)
                    {
                        text.AppendLine($"  ... ({totalLines - shown} more lines, press F for full view)", TerminalColor.Gray);
                    }
                }
                break;

            case "write":
                if (!string.IsNullOrEmpty(tool.Content))
                {
                    var lines = tool.Content.Split('\n');
                    text.AppendLine("[NEW FILE]", TerminalColor.Green);
                    for (int i = 0; i < Math.Min(lines.Length, maxLines); i++)
                    {
                        text.Append($"{i + 1,3} ", TerminalColor.Gray)
                            .AppendLine(TruncateLine(lines[i], 70), TerminalColor.White);
                    }
                    if (lines.Length > maxLines)
                    {
                        text.AppendLine($"    ... ({lines.Length - maxLines} more lines, press F for full view)", TerminalColor.Gray);
                    }
                }
                break;

            case "bash":
                if (!string.IsNullOrEmpty(tool.Command))
                {
                    text.Append("$ ", TerminalColor.Green)
                        .AppendLine(tool.Command, TerminalColor.White);
                }
                break;

            case "grep":
            case "glob":
                if (!string.IsNullOrEmpty(tool.Command)) // Pattern stored in Command
                {
                    text.Append("Pattern: ", TerminalColor.Gray)
                        .AppendLine(tool.Command, TerminalColor.Yellow);
                }
                if (!string.IsNullOrEmpty(tool.FilePath))
                {
                    text.Append("Path: ", TerminalColor.Gray)
                        .AppendLine(tool.FilePath, TerminalColor.White);
                }
                break;

            case "read":
                if (!string.IsNullOrEmpty(tool.FilePath))
                {
                    text.Append("Reading: ", TerminalColor.Gray)
                        .AppendLine(tool.FilePath, TerminalColor.White);
                }
                break;

            default:
                if (!string.IsNullOrEmpty(tool.FilePath))
                {
                    text.Append("Path: ", TerminalColor.Gray)
                        .AppendLine(tool.FilePath, TerminalColor.White);
                }
                if (!string.IsNullOrEmpty(tool.Command))
                {
                    text.Append("Command: ", TerminalColor.Gray)
                        .AppendLine(tool.Command, TerminalColor.White);
                }
                break;
        }
    }

    private static bool HasExpandableContent(ToolUse tool)
    {
        var toolName = tool.Name.ToLowerInvariant();
        return toolName switch
        {
            "edit" => (tool.OldContent?.Split('\n').Length ?? 0) + (tool.Content?.Split('\n').Length ?? 0) > 8,
            "write" => (tool.Content?.Split('\n').Length ?? 0) > 8,
            _ => false
        };
    }

    private void UpdateFullContentView()
    {
        var session = _sessionManager.GetSession(_selectedSessionId!);
        if (session == null || _selectedChangeIndex >= session.Changes.Count)
        {
            return;
        }

        var change = GetSortedChanges(session)[_selectedChangeIndex];

        _diffText.Clear();

        // Show change context
        _diffText
            .AppendLine(change.Description, TerminalColor.White)
            .Append(change.Type.ToString(), TerminalColor.Yellow)
            .Append(" | ", TerminalColor.Gray)
            .AppendLine(change.StartTime.ToLocalTime().ToString("HH:mm:ss"), TerminalColor.Gray);

        // Show full content for each tool (no truncation in full view)
        foreach (var tool in change.Tools)
        {
            _diffText.AppendLine()
                .Append($"─── {tool.Name} ", TerminalColor.Cyan);

            var target = tool.FilePath ?? tool.Command ?? "";
            if (!string.IsNullOrEmpty(target))
            {
                var fileName = tool.FilePath != null ? Path.GetFileName(tool.FilePath) : target;
                _diffText.Append($"({fileName})", TerminalColor.Gray);
            }
            _diffText.AppendLine(" ───", TerminalColor.Cyan);

            // Show tool content with high line limit (effectively unlimited for most cases)
            RenderToolContent(_diffText, tool, maxLines: 500);
        }
    }

    private static string TruncateLine(string line, int maxLength)
    {
        // Remove carriage return if present
        line = line.TrimEnd('\r');
        if (line.Length <= maxLength)
            return line;
        return line[..(maxLength - 3)] + "...";
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
