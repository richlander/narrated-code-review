namespace AgentTrace.UI;

/// <summary>
/// Discriminated union representing every action a pager can perform.
/// VimKeyMap produces these; pagers consume them via pattern matching.
/// </summary>
public abstract record PagerAction
{
    internal PagerAction() { }

    // Navigation
    public sealed record Scroll(ScrollDirection Direction, ScrollAmount Amount) : PagerAction;
    public sealed record GoToTop : PagerAction;
    public sealed record GoToBottom : PagerAction;
    public sealed record NextTurn : PagerAction;
    public sealed record PreviousTurn : PagerAction;

    // Session
    public sealed record PreviousSession : PagerAction;
    public sealed record NextSession : PagerAction;

    // Display
    public sealed record ToggleToolDetails : PagerAction;
    public sealed record ToggleThinking : PagerAction;
    public sealed record ShowHelp : PagerAction;
    public sealed record DismissHelp : PagerAction;

    // Search
    public sealed record SearchUpdate(string Term) : PagerAction;
    public sealed record SearchConfirm : PagerAction;
    public sealed record SearchCancel : PagerAction;
    public sealed record NextMatch : PagerAction;
    public sealed record PreviousMatch : PagerAction;
    public sealed record ClearSearch : PagerAction;

    // Lifecycle
    public sealed record Quit : PagerAction;

    // Live-only
    public sealed record TogglePause : PagerAction;
}

public enum ScrollDirection { Down, Up }

public enum ScrollAmount { Line, HalfPage, FullPage }
