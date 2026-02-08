namespace AgentTrace.UI;

/// <summary>
/// Current input mode for the vim key processor.
/// </summary>
public enum VimMode { Normal, Search, Help }

/// <summary>
/// Maps keystrokes to PagerAction values with mode handling and multi-key sequence support.
/// Inspired by VsVim's BindResult pattern.
/// </summary>
public sealed class VimKeyMap
{
    private readonly Dictionary<(ConsoleKey Key, ConsoleModifiers Modifiers), PagerAction> _keyBindings = new();
    private readonly Dictionary<char, PagerAction> _charBindings = new();
    private readonly Dictionary<(ConsoleKey First, ConsoleKey Second), PagerAction> _sequences = new();

    private ConsoleKey? _pendingKey;
    private string _searchTerm = "";

    public VimMode Mode { get; private set; } = VimMode.Normal;
    public string SearchTerm => _searchTerm;

    /// <summary>Bind a ConsoleKey (with optional modifiers) to an action.</summary>
    public void Bind(ConsoleKey key, PagerAction action, ConsoleModifiers modifiers = 0)
    {
        _keyBindings[(key, modifiers)] = action;
    }

    /// <summary>Bind a character to an action (for keys like '[', ']', '/', '?').</summary>
    public void BindChar(char ch, PagerAction action)
    {
        _charBindings[ch] = action;
    }

    /// <summary>Bind a two-key sequence to an action (e.g. g,g → GoToTop).</summary>
    public void BindSequence(ConsoleKey first, ConsoleKey second, PagerAction action)
    {
        _sequences[(first, second)] = action;
    }

    /// <summary>
    /// Process a keystroke and return the resulting action, or null if unhandled/pending.
    /// </summary>
    public PagerAction? Process(ConsoleKeyInfo keyInfo)
    {
        return Mode switch
        {
            VimMode.Help => ProcessHelp(),
            VimMode.Search => ProcessSearch(keyInfo),
            VimMode.Normal => ProcessNormal(keyInfo),
            _ => null
        };
    }

    private PagerAction ProcessHelp()
    {
        Mode = VimMode.Normal;
        return new PagerAction.DismissHelp();
    }

    private PagerAction? ProcessSearch(ConsoleKeyInfo keyInfo)
    {
        switch (keyInfo.Key)
        {
            case ConsoleKey.Escape:
                Mode = VimMode.Normal;
                _searchTerm = "";
                return new PagerAction.SearchCancel();

            case ConsoleKey.Enter:
                Mode = VimMode.Normal;
                return new PagerAction.SearchConfirm();

            case ConsoleKey.Backspace:
                if (_searchTerm.Length > 0)
                    _searchTerm = _searchTerm[..^1];
                return new PagerAction.SearchUpdate(_searchTerm);

            default:
                if (keyInfo.KeyChar != '\0' && !char.IsControl(keyInfo.KeyChar))
                {
                    _searchTerm += keyInfo.KeyChar;
                    return new PagerAction.SearchUpdate(_searchTerm);
                }
                return null;
        }
    }

    private PagerAction? ProcessNormal(ConsoleKeyInfo keyInfo)
    {
        // Handle multi-key sequences
        if (_pendingKey.HasValue)
        {
            var first = _pendingKey.Value;
            _pendingKey = null;

            if (_sequences.TryGetValue((first, keyInfo.Key), out var seqAction)
                && keyInfo.Modifiers == 0)
            {
                return seqAction;
            }
            // Not a valid sequence — fall through to process this key normally
        }

        // Check if this key starts a sequence
        foreach (var ((first, _), _) in _sequences)
        {
            if (keyInfo.Key == first && keyInfo.Modifiers == 0
                && !_keyBindings.ContainsKey((keyInfo.Key, keyInfo.Modifiers)))
            {
                _pendingKey = keyInfo.Key;
                return null;
            }
        }

        // Exact key+modifier binding
        if (_keyBindings.TryGetValue((keyInfo.Key, keyInfo.Modifiers), out var action))
        {
            return MaybeEnterMode(action);
        }

        // Character binding
        if (keyInfo.KeyChar != '\0' && _charBindings.TryGetValue(keyInfo.KeyChar, out var charAction))
        {
            return MaybeEnterMode(charAction);
        }

        return null;
    }

    /// <summary>
    /// Intercepts sentinel actions that trigger mode switches rather than reaching the pager.
    /// </summary>
    private PagerAction? MaybeEnterMode(PagerAction action)
    {
        if (action is EnterSearch)
        {
            Mode = VimMode.Search;
            _searchTerm = "";
            return new PagerAction.SearchUpdate("");
        }

        if (action is PagerAction.ShowHelp)
        {
            Mode = VimMode.Help;
            return action;
        }

        return action;
    }

    /// <summary>Internal sentinel action — triggers search mode entry, never reaches the pager.</summary>
    internal sealed record EnterSearch : PagerAction;

    /// <summary>
    /// Creates a VimKeyMap with the standard pager bindings (shared between static and live pagers).
    /// </summary>
    public static VimKeyMap CreateStandard()
    {
        var map = new VimKeyMap();

        // Navigation — line
        map.Bind(ConsoleKey.J, new PagerAction.Scroll(ScrollDirection.Down, ScrollAmount.Line));
        map.Bind(ConsoleKey.DownArrow, new PagerAction.Scroll(ScrollDirection.Down, ScrollAmount.Line));
        map.Bind(ConsoleKey.K, new PagerAction.Scroll(ScrollDirection.Up, ScrollAmount.Line));
        map.Bind(ConsoleKey.UpArrow, new PagerAction.Scroll(ScrollDirection.Up, ScrollAmount.Line));

        // Navigation — half page
        map.Bind(ConsoleKey.D, new PagerAction.Scroll(ScrollDirection.Down, ScrollAmount.HalfPage), ConsoleModifiers.Control);
        map.Bind(ConsoleKey.U, new PagerAction.Scroll(ScrollDirection.Up, ScrollAmount.HalfPage), ConsoleModifiers.Control);

        // Navigation — full page
        map.Bind(ConsoleKey.F, new PagerAction.Scroll(ScrollDirection.Down, ScrollAmount.FullPage), ConsoleModifiers.Control);
        map.Bind(ConsoleKey.PageDown, new PagerAction.Scroll(ScrollDirection.Down, ScrollAmount.FullPage));
        map.Bind(ConsoleKey.Spacebar, new PagerAction.Scroll(ScrollDirection.Down, ScrollAmount.FullPage));
        map.Bind(ConsoleKey.B, new PagerAction.Scroll(ScrollDirection.Up, ScrollAmount.FullPage), ConsoleModifiers.Control);
        map.Bind(ConsoleKey.PageUp, new PagerAction.Scroll(ScrollDirection.Up, ScrollAmount.FullPage));

        // Navigation — extremes
        map.Bind(ConsoleKey.G, new PagerAction.GoToBottom(), ConsoleModifiers.Shift);
        map.Bind(ConsoleKey.End, new PagerAction.GoToBottom());
        map.Bind(ConsoleKey.Home, new PagerAction.GoToTop());
        map.BindSequence(ConsoleKey.G, ConsoleKey.G, new PagerAction.GoToTop());

        // Turn jumps
        map.BindChar(']', new PagerAction.NextTurn());
        map.BindChar('[', new PagerAction.PreviousTurn());

        // Session navigation
        map.Bind(ConsoleKey.LeftArrow, new PagerAction.PreviousSession());
        map.Bind(ConsoleKey.RightArrow, new PagerAction.NextSession());

        // Display toggles
        map.Bind(ConsoleKey.T, new PagerAction.ToggleToolDetails());
        map.Bind(ConsoleKey.E, new PagerAction.ToggleThinking());
        map.BindChar('?', new PagerAction.ShowHelp());

        // Search
        map.BindChar('/', new EnterSearch());
        map.Bind(ConsoleKey.N, new PagerAction.NextMatch());
        map.Bind(ConsoleKey.N, new PagerAction.PreviousMatch(), ConsoleModifiers.Shift);

        return map;
    }
}
