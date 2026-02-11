using Microsoft.Extensions.Terminal;
using AgentLogs.Domain;
using AgentTrace.Services;

namespace AgentTrace.UI;

/// <summary>
/// Scrollable pager for conversation display with vim-like keybindings.
/// </summary>
public class ConversationPager
{
    private readonly Conversation _conversation;
    private readonly ConversationRenderer _renderer;
    private readonly ITerminal _terminal;
    private readonly SessionContext? _sessionContext;
    private readonly BookmarkStore? _bookmarkStore;

    private readonly VimKeyMap _keyMap;
    private bool _isBookmarked;

    private List<StyledLine> _lines = [];
    private int _scrollOffset;
    private int _viewportHeight;
    private bool _quit;
    private PagerResult _result = PagerResult.Quit;

    // Search state — each match tracks (line, column, length) for word-level highlighting
    private readonly record struct SearchMatch(int Line, int Column, int Length);
    private List<SearchMatch> _searchMatches = [];
    private int _currentMatchIndex = -1;

    public ConversationPager(Conversation conversation, ITerminal terminal, SessionContext? sessionContext = null, BookmarkStore? bookmarkStore = null)
    {
        _conversation = conversation;
        _renderer = new ConversationRenderer();
        _terminal = terminal;
        _sessionContext = sessionContext;
        _bookmarkStore = bookmarkStore;
        _isBookmarked = bookmarkStore?.IsBookmarked(conversation.SessionId) ?? false;
        _keyMap = CreateKeyMap();
    }

    public async Task<PagerResult> RunAsync()
    {
        _lines = _renderer.Render(_conversation);
        _scrollOffset = 0;

        _terminal.HideCursor();
        _terminal.Append(AnsiCodes.ClearScreenAndHome);

        try
        {
            using var ticker = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
            var needsRedraw = true;

            while (!_quit)
            {
                _viewportHeight = _terminal.Height - 2; // Reserve for status line and search

                while (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(intercept: true);
                    HandleKey(key);
                    needsRedraw = true;
                }

                if (needsRedraw)
                {
                    Render();
                    needsRedraw = false;
                }

                await ticker.WaitForNextTickAsync();
            }
        }
        finally
        {
            _terminal.ShowCursor();
            _terminal.Append(AnsiCodes.ClearScreenAndHome);
        }

        return _result;
    }

    private void HandleKey(ConsoleKeyInfo key)
    {
        var action = _keyMap.Process(key);
        if (action != null)
            ApplyAction(action);
    }

    private void ApplyAction(PagerAction action)
    {
        switch (action)
        {
            case PagerAction.Scroll s:
                var lines = s.Amount switch
                {
                    ScrollAmount.Line => 1,
                    ScrollAmount.HalfPage => _viewportHeight / 2,
                    ScrollAmount.FullPage => _viewportHeight,
                    _ => 1
                };
                if (s.Direction == ScrollDirection.Down) ScrollDown(lines); else ScrollUp(lines);
                break;

            case PagerAction.GoToTop:
                _scrollOffset = 0;
                break;

            case PagerAction.GoToBottom:
                _scrollOffset = Math.Max(0, _lines.Count - _viewportHeight);
                break;

            case PagerAction.NextTurn:
                JumpToNextTurn();
                break;

            case PagerAction.PreviousTurn:
                JumpToPreviousTurn();
                break;

            case PagerAction.PreviousSession:
                _result = PagerResult.PreviousSession;
                _quit = true;
                break;

            case PagerAction.NextSession:
                _result = PagerResult.NextSession;
                _quit = true;
                break;

            case PagerAction.ToggleToolDetails:
                _renderer.ShowToolDetails = !_renderer.ShowToolDetails;
                ReRender();
                break;

            case PagerAction.ToggleThinking:
                _renderer.ShowThinking = !_renderer.ShowThinking;
                ReRender();
                break;

            case PagerAction.ToggleBookmark when _bookmarkStore != null:
                _isBookmarked = _bookmarkStore.Toggle(_conversation.SessionId);
                break;

            case PagerAction.ShowHelp:
            case PagerAction.DismissHelp:
                break; // Mode tracked by VimKeyMap

            case PagerAction.SearchUpdate:
                ExecuteSearch();
                break;

            case PagerAction.SearchConfirm:
                break; // Matches already computed

            case PagerAction.SearchCancel:
                _searchMatches.Clear();
                _currentMatchIndex = -1;
                break;

            case PagerAction.NextMatch:
                if (_searchMatches.Count > 0)
                {
                    _currentMatchIndex = (_currentMatchIndex + 1) % _searchMatches.Count;
                    _scrollOffset = Math.Max(0, _searchMatches[_currentMatchIndex].Line - _viewportHeight / 2);
                    ClampScroll();
                }
                break;

            case PagerAction.PreviousMatch:
                if (_searchMatches.Count > 0)
                {
                    _currentMatchIndex = (_currentMatchIndex - 1 + _searchMatches.Count) % _searchMatches.Count;
                    _scrollOffset = Math.Max(0, _searchMatches[_currentMatchIndex].Line - _viewportHeight / 2);
                    ClampScroll();
                }
                break;

            case PagerAction.ClearSearch:
                _searchMatches.Clear();
                _currentMatchIndex = -1;
                break;

            case PagerAction.Quit:
                _result = PagerResult.Quit;
                _quit = true;
                break;
        }
    }

    private void ExecuteSearch()
    {
        _searchMatches.Clear();
        _currentMatchIndex = -1;

        var term = _keyMap.SearchTerm;
        if (string.IsNullOrEmpty(term))
            return;

        for (var i = 0; i < _lines.Count; i++)
        {
            var text = _lines[i].Text;
            var pos = 0;
            while ((pos = text.IndexOf(term, pos, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                _searchMatches.Add(new SearchMatch(i, pos, term.Length));
                pos += term.Length;
            }
        }

        if (_searchMatches.Count > 0)
        {
            // Jump to first match at or after current scroll position
            _currentMatchIndex = 0;
            for (var i = 0; i < _searchMatches.Count; i++)
            {
                if (_searchMatches[i].Line >= _scrollOffset)
                {
                    _currentMatchIndex = i;
                    break;
                }
            }

            _scrollOffset = Math.Max(0, _searchMatches[_currentMatchIndex].Line - _viewportHeight / 2);
            ClampScroll();
        }
    }

    private void ScrollDown(int lines)
    {
        _scrollOffset += lines;
        ClampScroll();
    }

    private void ScrollUp(int lines)
    {
        _scrollOffset -= lines;
        ClampScroll();
    }

    private void ClampScroll()
    {
        _scrollOffset = Math.Max(0, Math.Min(_scrollOffset, Math.Max(0, _lines.Count - _viewportHeight)));
    }

    private void JumpToNextTurn()
    {
        var currentLine = _scrollOffset;
        for (var i = currentLine + 1; i < _lines.Count; i++)
        {
            if (_lines[i].Style == LineStyle.Separator)
            {
                _scrollOffset = i;
                ClampScroll();
                return;
            }
        }
    }

    private void JumpToPreviousTurn()
    {
        var currentLine = _scrollOffset;
        for (var i = currentLine - 1; i >= 0; i--)
        {
            if (_lines[i].Style == LineStyle.Separator)
            {
                _scrollOffset = i;
                ClampScroll();
                return;
            }
        }
        _scrollOffset = 0;
    }

    private void ReRender()
    {
        var currentTurn = GetCurrentTurn();
        _lines = _renderer.Render(_conversation);

        // Try to maintain position near the same turn
        if (currentTurn >= 0)
        {
            for (var i = 0; i < _lines.Count; i++)
            {
                if (_lines[i].Style == LineStyle.Separator && _lines[i].TurnNumber == currentTurn)
                {
                    _scrollOffset = i;
                    ClampScroll();
                    return;
                }
            }
        }

        ClampScroll();
    }

    private int GetCurrentTurn()
    {
        for (var i = _scrollOffset; i >= 0; i--)
        {
            if (_lines[i].Style == LineStyle.Separator)
                return _lines[i].TurnNumber;
        }
        return -1;
    }

    private void Render()
    {
        if (_keyMap.Mode == VimMode.Help)
        {
            RenderHelp();
            return;
        }

        _terminal.Append(AnsiCodes.MoveCursorHome);

        for (var i = _scrollOffset; i < _scrollOffset + _viewportHeight; i++)
        {
            _terminal.Append($"{AnsiCodes.CSI}{AnsiCodes.EraseInLine}");

            if (i < _lines.Count)
                RenderStyledLine(_lines[i], i);

            _terminal.AppendLine("");
        }

        // Status line
        _terminal.Append($"{AnsiCodes.CSI}{AnsiCodes.EraseInLine}");
        RenderStatusLine();
    }

    private void RenderStyledLine(StyledLine line, int lineIndex)
    {
        var color = line.Style switch
        {
            LineStyle.Separator => TerminalColor.Gray,
            LineStyle.UserPrefix => TerminalColor.Cyan,
            LineStyle.UserText => TerminalColor.White,
            LineStyle.AssistantPrefix => TerminalColor.Green,
            LineStyle.AssistantText => TerminalColor.White,
            LineStyle.ToolUse => TerminalColor.Gray,
            LineStyle.ToolResult => TerminalColor.Gray,
            LineStyle.ToolResultContent => TerminalColor.Gray,
            LineStyle.ToolDetailContent => TerminalColor.White,
            LineStyle.ToolDetailMuted => TerminalColor.Gray,
            LineStyle.ThinkingHeader => TerminalColor.Magenta,
            LineStyle.ThinkingText => TerminalColor.Gray,
            LineStyle.ThinkingCollapsed => TerminalColor.Gray,
            LineStyle.DiffAdded => TerminalColor.Green,
            LineStyle.DiffRemoved => TerminalColor.Red,
            LineStyle.SystemPrefix => TerminalColor.Yellow,
            LineStyle.SystemText => TerminalColor.Gray,
            LineStyle.SummaryPrefix => TerminalColor.Blue,
            LineStyle.SummaryText => TerminalColor.Gray,
            _ => TerminalColor.White
        };

        // Determine visible portion (truncate long lines)
        var originalText = line.Text;
        var maxWidth = _terminal.Width - 1;
        var displayLen = originalText.Length;
        var truncated = false;
        if (originalText.Length > maxWidth && maxWidth > 3)
        {
            displayLen = maxWidth - 3;
            truncated = true;
        }

        // Collect search match spans on this line, clipped to visible portion
        var hasMatches = false;
        var pos = 0;

        for (var m = 0; m < _searchMatches.Count; m++)
        {
            var match = _searchMatches[m];
            if (match.Line != lineIndex) continue;
            var start = match.Column;
            var end = Math.Min(match.Column + match.Length, displayLen);
            if (start >= displayLen) continue;

            hasMatches = true;

            // Text before this match
            if (pos < start)
            {
                _terminal.SetColor(color);
                _terminal.Append(originalText[pos..start]);
            }

            // Highlighted match span
            if (m == _currentMatchIndex)
                _terminal.Append($"{AnsiCodes.CSI}43;30m"); // IncSearch: yellow bg, black fg
            else
            {
                _terminal.Append($"{AnsiCodes.CSI}48;5;58m"); // Search: dim yellow bg
                _terminal.SetColor(color);
            }
            _terminal.Append(originalText[start..end]);
            _terminal.Append($"{AnsiCodes.CSI}49m");
            _terminal.ResetColor();

            pos = end;
        }

        // Remaining text after last match (up to display boundary)
        if (pos < displayLen)
        {
            _terminal.SetColor(color);
            _terminal.Append(originalText[pos..displayLen]);
        }

        // Ellipsis for truncated lines
        if (truncated)
        {
            if (!hasMatches)
                _terminal.SetColor(color);
            _terminal.Append("...");
        }

        _terminal.ResetColor();
    }

    private void RenderHelp()
    {
        _terminal.Append(AnsiCodes.MoveCursorHome);

        var helpLines = new (string key, string desc)[]
        {
            ("j/k ↑/↓", "Scroll line"),
            ("Ctrl-D/U", "Half page down/up"),
            ("Ctrl-F/B", "Full page down/up"),
            ("Space", "Page down"),
            ("gg/G", "Top / bottom"),
            ("[/]", "Previous / next turn"),
            ("←/→", "Previous / next session"),
            ("", ""),
            ("t", "Toggle tool details"),
            ("e", "Toggle thinking blocks"),
            ("b", "Toggle bookmark"),
            ("", ""),
            ("/", "Search"),
            ("n/N", "Next / previous match"),
            ("Esc", "Clear search"),
            ("", ""),
            ("q/Esc", "Back to session list"),
        };

        var boxWidth = 42;
        var boxHeight = helpLines.Length + 4;
        var startRow = Math.Max(0, (_viewportHeight - boxHeight) / 2);
        var startCol = Math.Max(0, (_terminal.Width - boxWidth) / 2);

        for (var row = 0; row < _viewportHeight + 2; row++)
        {
            _terminal.Append($"{AnsiCodes.CSI}{AnsiCodes.EraseInLine}");

            if (row == startRow)
            {
                _terminal.Append(new string(' ', startCol));
                _terminal.SetColor(TerminalColor.Blue);
                _terminal.Append($"┌{"".PadRight(boxWidth - 2, '─')}┐");
                _terminal.ResetColor();
            }
            else if (row == startRow + 1)
            {
                _terminal.Append(new string(' ', startCol));
                _terminal.SetColor(TerminalColor.Blue);
                _terminal.Append("│");
                _terminal.SetColor(TerminalColor.White);
                _terminal.Append("  Key Bindings".PadRight(boxWidth - 2));
                _terminal.SetColor(TerminalColor.Blue);
                _terminal.Append("│");
                _terminal.ResetColor();
            }
            else if (row == startRow + 2)
            {
                _terminal.Append(new string(' ', startCol));
                _terminal.SetColor(TerminalColor.Blue);
                _terminal.Append($"├{"".PadRight(boxWidth - 2, '─')}┤");
                _terminal.ResetColor();
            }
            else if (row > startRow + 2 && row <= startRow + 2 + helpLines.Length)
            {
                var idx = row - startRow - 3;
                var (k, desc) = helpLines[idx];
                _terminal.Append(new string(' ', startCol));
                _terminal.SetColor(TerminalColor.Blue);
                _terminal.Append("│");
                if (k.Length > 0)
                {
                    _terminal.Append("  ");
                    _terminal.SetColor(TerminalColor.Cyan);
                    _terminal.Append(k.PadRight(14));
                    _terminal.SetColor(TerminalColor.White);
                    _terminal.Append(desc.PadRight(boxWidth - 18));
                }
                else
                {
                    _terminal.Append(new string(' ', boxWidth - 2));
                }
                _terminal.SetColor(TerminalColor.Blue);
                _terminal.Append("│");
                _terminal.ResetColor();
            }
            else if (row == startRow + 3 + helpLines.Length)
            {
                _terminal.Append(new string(' ', startCol));
                _terminal.SetColor(TerminalColor.Blue);
                _terminal.Append($"└{"".PadRight(boxWidth - 2, '─')}┘");
                _terminal.ResetColor();
            }

            _terminal.AppendLine("");
        }
    }

    private void RenderStatusLine()
    {
        _terminal.SetColor(TerminalColor.Black);

        var position = _lines.Count > 0
            ? $"{_scrollOffset + 1}-{Math.Min(_scrollOffset + _viewportHeight, _lines.Count)}/{_lines.Count}"
            : "empty";

        var turnCount = _conversation.Turns.Count;
        var tools = _renderer.ShowToolDetails ? "t:on" : "t:off";
        var thinking = _renderer.ShowThinking ? "e:on" : "e:off";

        // Build left side: session identity + turns + position
        var left = " ";
        if (_isBookmarked)
            left += "★ ";
        if (_sessionContext != null)
        {
            var shortId = _sessionContext.Id.Length > 7 ? _sessionContext.Id[..7] : _sessionContext.Id;
            var project = _sessionContext.ProjectName ?? "";
            var date = _sessionContext.StartTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            left += $"{shortId} {project} {date} | ";
        }
        left += $"{turnCount} turns | {tools} {thinking} | {position}";

        // Navigation indicator
        if (_sessionContext is { TotalSessions: > 1 })
        {
            var idx = _sessionContext.Index;
            var total = _sessionContext.TotalSessions;
            var leftArrow = idx > 0 ? "\u2190" : " ";
            var rightArrow = idx < total - 1 ? "\u2192" : " ";
            left += $" | {leftArrow}[{idx + 1}/{total}]{rightArrow}";
        }

        if (_keyMap.Mode == VimMode.Search)
        {
            left = $" /{_keyMap.SearchTerm}\u2588";
        }
        else if (_searchMatches.Count > 0)
        {
            left += $" | [{_currentMatchIndex + 1}/{_searchMatches.Count}]";
        }

        var right = " ?:help ";
        var maxWidth = _terminal.Width - 1;
        var gap = Math.Max(1, maxWidth - left.Length - right.Length);
        var status = left + new string(' ', gap) + right;
        if (status.Length > maxWidth)
            status = status[..maxWidth];
        else if (status.Length < maxWidth)
            status += new string(' ', maxWidth - status.Length);

        _terminal.Append($"{AnsiCodes.CSI}7m");
        _terminal.Append(status);
        _terminal.Append(AnsiCodes.SetDefaultColor);
        _terminal.ResetColor();
    }

    private static VimKeyMap CreateKeyMap()
    {
        var map = VimKeyMap.CreateStandard();

        // Static pager: Q and Esc both quit
        map.Bind(ConsoleKey.Q, new PagerAction.Quit());
        map.Bind(ConsoleKey.Escape, new PagerAction.Quit());

        return map;
    }
}
