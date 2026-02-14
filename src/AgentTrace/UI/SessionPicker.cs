using Microsoft.Extensions.Terminal;
using Microsoft.Extensions.Terminal.Components;
using AgentLogs.Domain;
using AgentLogs.Services;
using AgentTrace.Services;

namespace AgentTrace.UI;

/// <summary>
/// Interactive session picker using M.E.Terminal.
/// </summary>
public class SessionPicker
{
    private readonly SessionManager _sessionManager;
    private readonly BookmarkStore? _bookmarkStore;
    private readonly TerminalApp _app;
    private readonly Table _table;
    private readonly Text _headerText;
    private readonly Text _footerText;
    private readonly Text _filterText;
    private readonly Panel _headerPanel;
    private readonly Panel _footerPanel;

    private readonly int _initialSelectedIndex;

    private IReadOnlyList<Session> _sessions = [];
    private IReadOnlyList<Session> _filteredSessions = [];
    private HashSet<string> _bookmarks = [];
    private int _selectedIndex;
    private string _filter = "";
    private bool _isFiltering;
    private Session? _selectedSession;
    private bool _quit;

    public SessionPicker(SessionManager sessionManager, ITerminal terminal, int initialSelectedIndex = 0, BookmarkStore? bookmarkStore = null)
    {
        _sessionManager = sessionManager;
        _bookmarkStore = bookmarkStore;
        _initialSelectedIndex = initialSelectedIndex;
        _app = new TerminalApp(terminal);

        _headerText = new Text();
        _footerText = new Text();
        _filterText = new Text();

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

        _table = new Table
        {
            Border = TableBorderStyle.None,
            IsSelectable = true,
            ShowHeader = true
        };
        _table.AddColumn("", 2);
        _table.AddColumn("", 2);
        _table.AddColumn("", 2);
        _table.AddColumn("Project", 25);
        _table.AddColumn("Messages", 12);
        _table.AddColumn("Tools", 8);
        _table.AddColumn("Duration", 12);
        _table.AddColumn("Date", 20);

        _app.Layout.Direction = LayoutDirection.Vertical;
        _app.Layout.Add(_headerPanel, LayoutSize.Fixed(3));
        _app.Layout.Add(_table, LayoutSize.Fill);
        _app.Layout.Add(_footerPanel, LayoutSize.Fixed(3));
    }

    /// <summary>
    /// Runs the picker and returns the selected session, or null if quit.
    /// </summary>
    public async Task<Session?> RunAsync()
    {
        _sessions = _sessionManager.GetAllSessions();
        _filteredSessions = _sessions;
        _bookmarks = _bookmarkStore?.Load() ?? [];
        _selectedIndex = Math.Min(_initialSelectedIndex, Math.Max(0, _sessions.Count - 1));

        _app.Terminal.HideCursor();
        _app.Buffer.Invalidate();

        try
        {
            using var ticker = new PeriodicTimer(TimeSpan.FromMilliseconds(100));

            while (!_quit && _selectedSession == null)
            {
                if (_app.Buffer.Width != _app.Terminal.Width || _app.Buffer.Height != _app.Terminal.Height)
                {
                    _app.Invalidate();
                }

                while (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(intercept: true);
                    HandleKey(key);
                }

                Render();
                _app.Layout.Render(_app.Buffer, Region.FromTerminal(_app.Terminal));
                _app.Buffer.Flush(_app.Terminal);

                await ticker.WaitForNextTickAsync();
            }
        }
        finally
        {
            _app.Terminal.ShowCursor();
            _app.Terminal.Append(AnsiCodes.ClearScreenAndHome);
        }

        return _selectedSession;
    }

    private void HandleKey(ConsoleKeyInfo key)
    {
        if (_isFiltering)
        {
            HandleFilterKey(key);
            return;
        }

        switch (key.Key)
        {
            case ConsoleKey.Q:
            case ConsoleKey.Escape:
                _quit = true;
                break;

            case ConsoleKey.UpArrow:
            case ConsoleKey.K:
                _selectedIndex = Math.Max(0, _selectedIndex - 1);
                break;

            case ConsoleKey.DownArrow:
            case ConsoleKey.J:
                _selectedIndex = Math.Min(_filteredSessions.Count - 1, _selectedIndex + 1);
                break;

            case ConsoleKey.Home:
            case ConsoleKey.G when !key.Modifiers.HasFlag(ConsoleModifiers.Shift):
                _selectedIndex = 0;
                break;

            case ConsoleKey.End:
            case ConsoleKey.G when key.Modifiers.HasFlag(ConsoleModifiers.Shift):
                _selectedIndex = Math.Max(0, _filteredSessions.Count - 1);
                break;

            case ConsoleKey.PageDown:
                _selectedIndex = Math.Min(_filteredSessions.Count - 1, _selectedIndex + 15);
                break;

            case ConsoleKey.PageUp:
                _selectedIndex = Math.Max(0, _selectedIndex - 15);
                break;

            case ConsoleKey.Enter:
                if (_selectedIndex >= 0 && _selectedIndex < _filteredSessions.Count)
                {
                    _selectedSession = _filteredSessions[_selectedIndex];
                }
                break;

            case ConsoleKey.B when _bookmarkStore != null:
                if (_selectedIndex >= 0 && _selectedIndex < _filteredSessions.Count)
                {
                    var sid = _filteredSessions[_selectedIndex].Id;
                    _bookmarkStore.Toggle(sid);
                    _bookmarks = _bookmarkStore.Load();
                }
                break;

            case ConsoleKey.Oem2 when key.KeyChar == '/': // / key
                _isFiltering = true;
                _filter = "";
                break;
        }
    }

    private void HandleFilterKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Escape:
                _isFiltering = false;
                _filter = "";
                _filteredSessions = _sessions;
                _selectedIndex = 0;
                break;

            case ConsoleKey.Enter:
                _isFiltering = false;
                break;

            case ConsoleKey.Backspace:
                if (_filter.Length > 0)
                {
                    _filter = _filter[..^1];
                    ApplyFilter();
                }
                break;

            default:
                if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar))
                {
                    _filter += key.KeyChar;
                    ApplyFilter();
                }
                break;
        }
    }

    private void ApplyFilter()
    {
        if (string.IsNullOrEmpty(_filter))
        {
            _filteredSessions = _sessions;
        }
        else
        {
            _filteredSessions = _sessions
                .Where(s => (s.ProjectName ?? "").Contains(_filter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        _selectedIndex = 0;
    }

    private void Render()
    {
        // Header
        _headerText.Clear();
        _headerText
            .Append("AgentTrace", TerminalColor.Blue)
            .Append(" | ")
            .Append($"{_filteredSessions.Count}", TerminalColor.White)
            .Append(" sessions");

        if (!string.IsNullOrEmpty(_filter))
        {
            _headerText.Append(" | filter: ", TerminalColor.Gray)
                .Append(_filter, TerminalColor.Yellow);
        }

        // Table
        _table.Bind(_filteredSessions, (session, index) =>
        {
            var duration = Formatting.FormatDuration(session.Duration);
            var date = session.StartTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            var bookmarked = _bookmarks.Contains(session.Id);

            return new TableRow(new[]
            {
                new TableCell(index == _selectedIndex ? ">" : ""),
                new TableCell(session.IsActive ? "●" : "○",
                    session.IsActive ? TerminalColor.Green : TerminalColor.Gray),
                new TableCell(bookmarked ? "★" : "", TerminalColor.Yellow),
                new TableCell(session.ProjectName ?? "Unknown"),
                new TableCell($"{session.UserMessageCount}↔{session.AssistantMessageCount}"),
                new TableCell($"{session.ToolCallCount}"),
                new TableCell(duration),
                new TableCell(date)
            });
        });
        _table.SelectedIndex = _selectedIndex;

        // Footer
        _footerText.Clear();
        if (_isFiltering)
        {
            _footerText
                .Append("/", TerminalColor.Yellow)
                .Append(_filter)
                .Append("█", TerminalColor.Gray)
                .Append("  ")
                .Append("Enter", TerminalColor.Gray).Append(" confirm  ")
                .Append("Esc", TerminalColor.Gray).Append(" cancel");
        }
        else
        {
            _footerText
                .Append("↑↓/jk", TerminalColor.Gray).Append(" Navigate  ")
                .Append("Enter", TerminalColor.Gray).Append(" Open  ")
                .Append("/", TerminalColor.Gray).Append(" Filter  ");
            if (_bookmarkStore != null)
                _footerText.Append("b", TerminalColor.Gray).Append(" Bookmark  ");
            _footerText
                .Append("q", TerminalColor.Gray).Append(" Quit");
        }
    }
}
