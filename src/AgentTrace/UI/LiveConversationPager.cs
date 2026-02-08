using Microsoft.Extensions.Terminal;
using AgentLogs.Domain;
using AgentLogs.Parsing;

namespace AgentTrace.UI;

/// <summary>
/// Live-tailing conversation pager that watches a JSONL file for new entries.
/// </summary>
public class LiveConversationPager
{
    private readonly ConversationRenderer _renderer;
    private readonly ITerminal _terminal;
    private readonly string _filePath;
    private readonly string _sessionId;

    private static readonly TimeSpan HighlightDuration = TimeSpan.FromSeconds(2);

    // Highlight shades computed at startup from the terminal's actual background
    private string[] _highlightShades = [];

    private List<Entry> _entries;
    private Conversation _conversation;
    private List<StyledLine> _rawLines = [];  // Pre-wrap
    private List<StyledLine> _lines = [];     // Post-wrap (1 per terminal row)
    private int _scrollOffset;
    private int _viewportHeight;
    private int _lastTerminalWidth;
    private bool _quit;
    private PagerResult _result = PagerResult.Quit;
    private bool _autoFollow = true;
    private long _lastFilePosition;
    private int _lastEntryCount;

    // Highlight state
    private int _highlightFromLine = int.MaxValue;
    private DateTime _highlightExpiry = DateTime.MinValue;

    private readonly VimKeyMap _keyMap;

    // Search state — each match tracks (line, column, length) for word-level highlighting
    private readonly record struct SearchMatch(int Line, int Column, int Length);
    private List<SearchMatch> _searchMatches = [];
    private int _currentMatchIndex = -1;

    // CLI watch (--watch flag, exits on match)
    private string? _cliWatchPattern;
    private string? _watchMatchContext;

    // Interactive watch (\ key, pauses on match)
    private string _watchTerm = "";

    // Thinking indicator state
    private bool _isThinking;
    private DateTime _thinkingStartTime;
    private DateTime _lastFileChange = DateTime.MinValue;
    private static readonly string[] SpinnerFrames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];


    /// <summary>
    /// Optional watch pattern. When set, the pager exits with a match
    /// when new content matches this pattern.
    /// </summary>
    public string? WatchPattern
    {
        get => _cliWatchPattern;
        init => _cliWatchPattern = value;
    }

    /// <summary>
    /// After RunAsync completes, contains the matched line if watch triggered, null otherwise.
    /// </summary>
    public string? WatchMatchContext => _watchMatchContext;

    public LiveConversationPager(Conversation conversation, ITerminal terminal, string filePath)
    {
        _conversation = conversation;
        _entries = new List<Entry>(conversation.Turns.SelectMany(t => t.Entries));
        _renderer = new ConversationRenderer();
        _terminal = terminal;
        _filePath = filePath;
        _sessionId = conversation.SessionId;
        _lastEntryCount = _entries.Count;
        _keyMap = CreateKeyMap();
    }

    public async Task<PagerResult> RunAsync()
    {
        _rawLines = _renderer.Render(_conversation);
        _lastTerminalWidth = _terminal.Width;
        _lines = WrapLines(_rawLines, _terminal.Width);

        // Start scrolled to bottom in follow mode
        _viewportHeight = _terminal.Height - 2;
        _scrollOffset = Math.Max(0, _lines.Count - _viewportHeight);

        // Get initial file position (end of file)
        _lastFilePosition = new FileInfo(_filePath).Length;

        // Detect terminal background and compute highlight shades
        _highlightShades = ComputeHighlightShades(QueryTerminalBackground());

        _terminal.HideCursor();
        _terminal.Append(AnsiCodes.ClearScreenAndHome);

        try
        {
            using var ticker = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
            var needsRedraw = true;
            var tickCount = 0;

            while (!_quit)
            {
                _viewportHeight = _terminal.Height - 2;

                // Re-wrap on terminal resize
                if (_terminal.Width != _lastTerminalWidth)
                {
                    _lastTerminalWidth = _terminal.Width;
                    var oldCount = _lines.Count;
                    _lines = WrapLines(_rawLines, _terminal.Width);
                    if (_autoFollow)
                        _scrollOffset = Math.Max(0, _lines.Count - _viewportHeight);
                    ClampScroll();
                    needsRedraw = true;
                }

                while (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(intercept: true);
                    HandleKey(key);
                    needsRedraw = true;
                }

                // Check for file changes every ~500ms
                tickCount++;
                if (tickCount % 10 == 0)
                {
                    if (await CheckForNewEntriesAsync())
                    {
                        needsRedraw = true;
                    }

                    // Re-evaluate thinking state (auto-expires after 5s of inactivity)
                    var wasThinking = _isThinking;
                    UpdateThinkingState();
                    if (wasThinking && !_isThinking)
                        needsRedraw = true;
                }

                // Keep redrawing while highlight is active (to animate the fade)
                // but only when following — don't repaint while paused (allows text selection)
                if (_autoFollow && DateTime.UtcNow < _highlightExpiry)
                    needsRedraw = true;

                // Animate thinking spinner
                if (_isThinking && _autoFollow)
                    needsRedraw = true;

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

    private async Task<bool> CheckForNewEntriesAsync()
    {
        try
        {
            var fileInfo = new FileInfo(_filePath);
            if (!fileInfo.Exists)
                return false;

            // Track file activity even if no complete lines are ready yet
            if (fileInfo.Length > _lastFilePosition)
                _lastFileChange = DateTime.UtcNow;
            else
                return false;

            var newEntries = new List<Entry>();

            await using var stream = new FileStream(
                _filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 4096,
                useAsync: true);

            stream.Seek(_lastFilePosition, SeekOrigin.Begin);
            using var reader = new StreamReader(stream);

            while (await reader.ReadLineAsync() is { } line)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    var entry = EntryParser.ParseLineFull(line);
                    if (entry != null)
                        newEntries.Add(entry);
                }
                catch
                {
                    // Skip malformed lines
                }
            }

            _lastFilePosition = fileInfo.Length;

            if (newEntries.Count == 0)
                return false;

            _lastFileChange = DateTime.UtcNow;

            // Check CLI watch pattern against new entries (exits on match)
            if (_cliWatchPattern != null)
            {
                foreach (var entry in newEntries)
                {
                    var text = entry switch
                    {
                        AssistantEntry a => a.TextContent,
                        UserEntry u => u.Content,
                        _ => null
                    };

                    if (text != null && text.Contains(_cliWatchPattern, StringComparison.OrdinalIgnoreCase))
                    {
                        // Find the matching line for context
                        foreach (var line in text.Split('\n'))
                        {
                            if (line.Contains(_cliWatchPattern, StringComparison.OrdinalIgnoreCase))
                            {
                                _watchMatchContext = line.Trim();
                                break;
                            }
                        }
                        _watchMatchContext ??= text.Split('\n').FirstOrDefault()?.Trim();
                        _quit = true;
                        return true;
                    }

                    // Also check tool calls (commands, file paths)
                    if (entry is AssistantEntry asst)
                    {
                        foreach (var tool in asst.ToolUses)
                        {
                            var cmd = tool.Command ?? tool.Content ?? "";
                            if (cmd.Contains(_cliWatchPattern, StringComparison.OrdinalIgnoreCase))
                            {
                                _watchMatchContext = $"{tool.Name}: {cmd.Trim().Split('\n').FirstOrDefault()}";
                                _quit = true;
                                return true;
                            }
                        }
                    }
                }
            }

            // Record where new content starts (before re-render)
            var oldLineCount = _lines.Count;

            // Rebuild conversation with new entries
            _entries.AddRange(newEntries);
            _conversation = new Conversation(_sessionId, _entries);
            _lastEntryCount = _entries.Count;

            var wasAtBottom = _scrollOffset >= Math.Max(0, _lines.Count - _viewportHeight - 2);
            _rawLines = _renderer.Render(_conversation);
            _lines = WrapLines(_rawLines, _terminal.Width);

            // Set highlight on new lines
            _highlightFromLine = oldLineCount;
            _highlightExpiry = DateTime.UtcNow + HighlightDuration;

            // Interactive watch: pause when new content matches
            if (_autoFollow && _watchTerm.Length > 0)
            {
                for (var i = oldLineCount; i < _lines.Count; i++)
                {
                    if (_lines[i].Text.Contains(_watchTerm, StringComparison.OrdinalIgnoreCase))
                    {
                        _autoFollow = false;
                        break;
                    }
                }
            }

            // Auto-follow: scroll to bottom when new content arrives
            if (_autoFollow || wasAtBottom)
            {
                _scrollOffset = Math.Max(0, _lines.Count - _viewportHeight);
            }

            ClampScroll();
            UpdateThinkingState();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Detects if the assistant is likely thinking (processing before responding).
    /// True when the file is being actively written but the last non-metadata entry
    /// is a user message (meaning the assistant hasn't started streaming a response yet).
    /// </summary>
    private void UpdateThinkingState()
    {
        // Only show thinking if file was recently active
        if ((DateTime.UtcNow - _lastFileChange).TotalSeconds > 5)
        {
            _isThinking = false;
            return;
        }

        // Find the last non-metadata entry
        Entry? lastDisplayable = null;
        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            if (_entries[i] is not MetadataEntry)
            {
                lastDisplayable = _entries[i];
                break;
            }
        }

        var shouldThink = lastDisplayable switch
        {
            // User spoke, assistant hasn't responded yet
            UserEntry => true,
            // Assistant entry with only thinking blocks — real response hasn't arrived
            AssistantEntry asst
                when string.IsNullOrWhiteSpace(asst.TextContent) && asst.ToolUses.Count == 0 => true,
            _ => false
        };

        if (shouldThink)
        {
            if (!_isThinking)
            {
                _isThinking = true;
                _thinkingStartTime = DateTime.UtcNow;
            }
        }
        else
        {
            _isThinking = false;
        }
    }

    /// <summary>
    /// Wraps long lines to fit terminal width. Each StyledLine becomes one or more display lines.
    /// </summary>
    private static List<StyledLine> WrapLines(List<StyledLine> lines, int terminalWidth)
    {
        var maxWidth = Math.Max(10, terminalWidth - 1);
        var wrapped = new List<StyledLine>(lines.Count);

        foreach (var line in lines)
        {
            if (line.Text.Length <= maxWidth || line.Style == LineStyle.Empty)
            {
                wrapped.Add(line);
                continue;
            }

            // Wrap at maxWidth boundaries
            var text = line.Text;
            var offset = 0;
            while (offset < text.Length)
            {
                var remaining = text.Length - offset;
                var chunkLen = Math.Min(remaining, maxWidth);
                var chunk = text.Substring(offset, chunkLen);
                wrapped.Add(new StyledLine(chunk, line.Style, line.TurnNumber));
                offset += chunkLen;
            }
        }

        return wrapped;
    }

    /// <summary>
    /// Queries the terminal's background color via OSC 11.
    /// Returns (r, g, b) in 0-255 range, or null if detection fails.
    /// Works on Ghostty, Alacritty, iTerm2, kitty, Terminal.app, etc.
    /// </summary>
    private static (int R, int G, int B)? QueryTerminalBackground()
    {
        try
        {
            // Drain any pending input
            while (Console.KeyAvailable)
                Console.ReadKey(intercept: true);

            // Send OSC 11 query: "what is your background color?"
            Console.Write("\x1b]11;?\x1b\\");

            // Read response with timeout — format: \e]11;rgb:RRRR/GGGG/BBBB\e\\
            var response = new System.Text.StringBuilder();
            var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(200);

            while (DateTime.UtcNow < deadline)
            {
                if (Console.KeyAvailable)
                {
                    var ch = Console.ReadKey(intercept: true).KeyChar;
                    response.Append(ch);

                    // Look for the end of the OSC response
                    var s = response.ToString();
                    if (s.Contains('\\') || s.Contains('\x07'))
                        break;
                }
                else
                {
                    Thread.Sleep(5);
                }
            }

            return ParseOsc11Response(response.ToString());
        }
        catch
        {
            return null;
        }
    }

    private static (int R, int G, int B)? ParseOsc11Response(string response)
    {
        // Response format: ...11;rgb:RRRR/GGGG/BBBB... (4-digit hex per channel)
        // Some terminals use 2-digit: rgb:RR/GG/BB
        var idx = response.IndexOf("rgb:", StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return null;

        var colorPart = response[(idx + 4)..];
        // Trim any trailing escape sequences
        var endIdx = colorPart.IndexOfAny(['\x1b', '\x07', '\\']);
        if (endIdx >= 0)
            colorPart = colorPart[..endIdx];

        var parts = colorPart.Split('/');
        if (parts.Length != 3)
            return null;

        try
        {
            // Convert to 0-255: if 4-digit hex, take top 2 digits; if 2-digit, use as-is
            static int ParseChannel(string hex)
            {
                var value = Convert.ToInt32(hex, 16);
                return hex.Length == 4 ? value >> 8 : value;
            }

            return (ParseChannel(parts[0]), ParseChannel(parts[1]), ParseChannel(parts[2]));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Computes 3 fading highlight shades slightly brighter than the given background.
    /// Falls back to reasonable defaults if background detection failed.
    /// </summary>
    private static string[] ComputeHighlightShades((int R, int G, int B)? bg)
    {
        // Default to a dark charcoal if detection failed
        var (r, g, b) = bg ?? (30, 30, 30);

        // Brighten by decreasing amounts for a 3-step fade
        static string Shade(int r, int g, int b, int boost) =>
            $"48;2;{Math.Min(255, r + boost)};{Math.Min(255, g + boost)};{Math.Min(255, b + boost)}";

        return
        [
            Shade(r, g, b, 12),  // subtle glow
            Shade(r, g, b, 6),   // barely there
            Shade(r, g, b, 3),   // almost gone
        ];
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
                if (s.Direction == ScrollDirection.Down)
                {
                    ScrollDown(lines);
                    _autoFollow = _scrollOffset >= Math.Max(0, _lines.Count - _viewportHeight);
                }
                else
                {
                    ScrollUp(lines);
                    _autoFollow = false;
                }
                break;

            case PagerAction.GoToTop:
                _scrollOffset = 0;
                _autoFollow = false;
                break;

            case PagerAction.GoToBottom:
                _scrollOffset = Math.Max(0, _lines.Count - _viewportHeight);
                _autoFollow = true;
                break;

            case PagerAction.NextTurn:
                JumpToNextTurn();
                _autoFollow = false;
                break;

            case PagerAction.PreviousTurn:
                JumpToPreviousTurn();
                _autoFollow = false;
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
                    CenterOnMatch();
                }
                break;

            case PagerAction.PreviousMatch:
                if (_searchMatches.Count > 0)
                {
                    _currentMatchIndex = (_currentMatchIndex - 1 + _searchMatches.Count) % _searchMatches.Count;
                    CenterOnMatch();
                }
                break;

            case PagerAction.ClearSearch:
                if (_searchMatches.Count > 0)
                {
                    _searchMatches.Clear();
                    _currentMatchIndex = -1;
                }
                else
                {
                    _result = PagerResult.Quit;
                    _quit = true;
                }
                break;

            case PagerAction.WatchUpdate:
                break; // Preview handled by rendering via _keyMap.WatchTerm

            case PagerAction.WatchConfirm:
                _watchTerm = _keyMap.WatchTerm;
                break;

            case PagerAction.WatchCancel:
                break; // Keep existing _watchTerm unchanged

            case PagerAction.TogglePause:
                _autoFollow = !_autoFollow;
                if (_autoFollow)
                {
                    _scrollOffset = Math.Max(0, _lines.Count - _viewportHeight);
                    ClampScroll();
                }
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
            _currentMatchIndex = 0;
            for (var i = 0; i < _searchMatches.Count; i++)
            {
                if (_searchMatches[i].Line >= _scrollOffset)
                {
                    _currentMatchIndex = i;
                    break;
                }
            }

            CenterOnMatch();
        }
    }

    private void CenterOnMatch()
    {
        if (_currentMatchIndex < 0 || _currentMatchIndex >= _searchMatches.Count)
            return;
        _scrollOffset = Math.Max(0, _searchMatches[_currentMatchIndex].Line - _viewportHeight / 2);
        ClampScroll();
        _autoFollow = false;
    }

    private void ScrollDown(int lines) { _scrollOffset += lines; ClampScroll(); }
    private void ScrollUp(int lines) { _scrollOffset -= lines; ClampScroll(); }

    private void ClampScroll()
    {
        _scrollOffset = Math.Max(0, Math.Min(_scrollOffset, Math.Max(0, _lines.Count - _viewportHeight)));
    }

    private void JumpToNextTurn()
    {
        for (var i = _scrollOffset + 1; i < _lines.Count; i++)
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
        for (var i = _scrollOffset - 1; i >= 0; i--)
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
        var currentTurn = _autoFollow ? -1 : GetCurrentTurn();
        _rawLines = _renderer.Render(_conversation);
        _lines = WrapLines(_rawLines, _terminal.Width);

        if (_autoFollow)
        {
            _scrollOffset = Math.Max(0, _lines.Count - _viewportHeight);
        }
        else if (currentTurn >= 0)
        {
            for (var i = 0; i < _lines.Count; i++)
            {
                if (_lines[i].Style == LineStyle.Separator && _lines[i].TurnNumber == currentTurn)
                {
                    _scrollOffset = i;
                    break;
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

        // Compute which highlight shade to use (or -1 for none)
        var highlightShade = -1;
        if (DateTime.UtcNow < _highlightExpiry)
        {
            var elapsed = DateTime.UtcNow - (_highlightExpiry - HighlightDuration);
            var progress = Math.Clamp(elapsed / HighlightDuration, 0.0, 1.0);
            highlightShade = (int)(progress * _highlightShades.Length);
            if (highlightShade >= _highlightShades.Length)
                highlightShade = -1; // expired
        }

        for (var i = _scrollOffset; i < _scrollOffset + _viewportHeight; i++)
        {
            _terminal.Append($"{AnsiCodes.CSI}{AnsiCodes.EraseInLine}");

            if (i < _lines.Count)
            {
                var shade = (i >= _highlightFromLine) ? highlightShade : -1;
                RenderStyledLine(_lines[i], i, shade);
            }
            else if (i == _lines.Count && _isThinking && _autoFollow)
            {
                RenderThinkingIndicator();
            }

            _terminal.AppendLine("");
        }

        _terminal.Append($"{AnsiCodes.CSI}{AnsiCodes.EraseInLine}");
        RenderStatusLine();
    }

    private void RenderStyledLine(StyledLine line, int lineIndex, int highlightShade)
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

        var text = line.Text;
        var hasShade = highlightShade >= 0 && highlightShade < _highlightShades.Length;

        // Emit a non-highlighted segment with optional shade background
        void EmitNormal(string segment)
        {
            if (hasShade)
                _terminal.Append($"{AnsiCodes.CSI}{_highlightShades[highlightShade]}m");
            _terminal.SetColor(color);
            _terminal.Append(segment);
        }

        // Emit padding to fill the row for shade background
        void EmitPadding()
        {
            if (!hasShade) return;
            var pad = Math.Max(0, _terminal.Width - 1 - text.Length);
            if (pad > 0)
            {
                _terminal.Append($"{AnsiCodes.CSI}{_highlightShades[highlightShade]}m");
                _terminal.Append(new string(' ', pad));
            }
        }

        // Try rendering with search match spans (word-level)
        var hasSearchSpans = false;
        var pos = 0;

        for (var m = 0; m < _searchMatches.Count; m++)
        {
            var match = _searchMatches[m];
            if (match.Line != lineIndex) continue;

            hasSearchSpans = true;

            // Text before this match
            if (pos < match.Column)
                EmitNormal(text[pos..match.Column]);

            // Highlighted match span
            var end = Math.Min(match.Column + match.Length, text.Length);
            if (m == _currentMatchIndex)
                _terminal.Append($"{AnsiCodes.CSI}43;30m"); // IncSearch: yellow bg, black fg
            else
            {
                _terminal.Append($"{AnsiCodes.CSI}48;5;58m"); // Search: dim yellow bg
                _terminal.SetColor(color);
            }
            _terminal.Append(text[match.Column..end]);
            _terminal.Append($"{AnsiCodes.CSI}49m");
            _terminal.ResetColor();

            pos = end;
        }

        if (hasSearchSpans)
        {
            // Remaining text after last search match
            if (pos < text.Length)
                EmitNormal(text[pos..]);
            EmitPadding();
            _terminal.Append($"{AnsiCodes.CSI}49m");
            _terminal.ResetColor();
            return;
        }

        // No search matches — check watch pattern (word-level)
        // Interactive watch (\ key) takes priority, then CLI --watch, then preview during input
        var watchPat = _keyMap.Mode == VimMode.Watch && _keyMap.WatchTerm.Length > 0
            ? _keyMap.WatchTerm
            : _watchTerm.Length > 0 ? _watchTerm
            : _cliWatchPattern;

        if (watchPat != null)
        {
            var hasWatch = false;
            pos = 0;
            var wp = 0;
            while ((wp = text.IndexOf(watchPat, pos, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                hasWatch = true;
                if (pos < wp)
                    EmitNormal(text[pos..wp]);

                _terminal.Append($"{AnsiCodes.CSI}41;37m"); // Watch: red bg, white fg
                _terminal.Append(text[wp..(wp + watchPat.Length)]);
                _terminal.Append($"{AnsiCodes.CSI}49m");
                _terminal.ResetColor();

                pos = wp + watchPat.Length;
            }

            if (hasWatch)
            {
                if (pos < text.Length)
                    EmitNormal(text[pos..]);
                _terminal.Append($"{AnsiCodes.CSI}49m");
                _terminal.ResetColor();
                return;
            }
        }

        // No matches at all — plain rendering with optional shade
        if (hasShade)
        {
            _terminal.Append($"{AnsiCodes.CSI}{_highlightShades[highlightShade]}m");
            _terminal.SetColor(color);
            _terminal.Append(text);
            EmitPadding();
            _terminal.Append($"{AnsiCodes.CSI}49m");
        }
        else
        {
            _terminal.SetColor(color);
            _terminal.Append(text);
        }

        _terminal.ResetColor();
    }

    private void RenderThinkingIndicator()
    {
        var elapsed = DateTime.UtcNow - _thinkingStartTime;
        var frameIndex = (int)(elapsed.TotalMilliseconds / 100) % SpinnerFrames.Length;
        var spinner = SpinnerFrames[frameIndex];

        var seconds = (int)elapsed.TotalSeconds;
        var duration = seconds > 0 ? $" {seconds}s" : "";

        _terminal.SetColor(TerminalColor.Green);
        _terminal.Append("[assistant] ");
        _terminal.SetColor(TerminalColor.Gray);
        _terminal.Append($"{spinner} waiting...{duration}");
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
            ("p", "Toggle pause / follow"),
            ("t", "Toggle tool details"),
            ("e", "Toggle thinking blocks"),
            ("", ""),
            ("/", "Search"),
            ("\\", "Watch (pause on match)"),
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

        string mode;
        if (_autoFollow)
        {
            mode = "LIVE";
        }
        else
        {
            var unseenBelow = Math.Max(0, _lines.Count - (_scrollOffset + _viewportHeight));
            mode = unseenBelow > 0 ? $"PAUSED +{unseenBelow}" : "PAUSED";
        }

        var left = $" {mode} | {turnCount} turns | {position}";

        if (_watchTerm.Length > 0)
        {
            left += $" | \\:{_watchTerm}";
        }
        else if (_cliWatchPattern != null)
        {
            left += $" | watch:{_cliWatchPattern}";
        }

        if (_keyMap.Mode == VimMode.Search)
        {
            left = $" /{_keyMap.SearchTerm}\u2588";
        }
        else if (_keyMap.Mode == VimMode.Watch)
        {
            left = $" \\{_keyMap.WatchTerm}\u2588";
        }
        else if (_searchMatches.Count > 0)
        {
            left += $" | [{_currentMatchIndex + 1}/{_searchMatches.Count}]";
        }

        var tools = _renderer.ShowToolDetails ? "t:on" : "t:off";
        var thinking = _renderer.ShowThinking ? "e:on" : "e:off";
        var right = $" {tools} {thinking} ?:help ";

        var maxWidth = _terminal.Width - 1;
        var gap = Math.Max(1, maxWidth - left.Length - right.Length);
        var status = left + new string(' ', gap) + right;
        if (status.Length > maxWidth)
            status = status[..maxWidth];
        else if (status.Length < maxWidth)
            status += new string(' ', maxWidth - status.Length);

        if (_autoFollow)
        {
            _terminal.Append($"{AnsiCodes.CSI}42;30m"); // Green background
        }
        else
        {
            _terminal.Append($"{AnsiCodes.CSI}43;30m"); // Yellow background
        }
        _terminal.Append(status);
        _terminal.Append(AnsiCodes.SetDefaultColor);
        _terminal.ResetColor();
    }

    private static VimKeyMap CreateKeyMap()
    {
        var map = VimKeyMap.CreateStandard();

        // Live pager: Q quits, Esc clears search (falls back to quit in ApplyAction)
        map.Bind(ConsoleKey.Q, new PagerAction.Quit());
        map.Bind(ConsoleKey.Escape, new PagerAction.ClearSearch());

        // Live-only: pause/follow toggle and interactive watch
        map.Bind(ConsoleKey.P, new PagerAction.TogglePause());
        map.BindChar('\\', new VimKeyMap.EnterWatch());

        return map;
    }
}
