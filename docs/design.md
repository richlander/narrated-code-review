# Narrated Code Reviewer - Design Document

This document captures the terminology, paradigm, and design decisions for the dashboard.

## Terminology

### Top-Level Views

| Term | Description |
|------|-------------|
| **Session List** | The default view showing all Claude Code sessions, sorted by recency. Entry point to the application. |
| **Live View** | (Planned) Real-time view for watching 1-2 active sessions as operations occur. |

### Session-Level Views

When viewing a session, users can switch between tabs using Left/Right arrow keys:

| Term | Description |
|------|-------------|
| **Summary** | Aggregated bird's-eye view of the session. Shows operation counts grouped by category. |
| **Actions** | Chronological list of logical changes (grouped tool operations). Supports drill-down into individual actions. |

### Core Concepts

| Term | Description |
|------|-------------|
| **Session** | A Claude Code conversation session, identified by session ID. Contains messages, tool calls, and metadata. |
| **Action** | A logical grouping of consecutive tool operations that represent a coherent unit of work. Previously called "Change" internally. |
| **Tool** | A single Claude Code tool invocation (Read, Write, Edit, Bash, Grep, Glob, WebFetch, etc.). |
| **Operation** | Generic term for any tracked activity. Used in the Summary view to categorize work. |

### Operation Categories

Operations are grouped into two categories in the Summary view:

**Claude Operations** - Tool invocations by Claude:

- WebFetch - Web content retrieval
- Read - File reading
- Write - File creation
- Edit - File modification
- Bash - Command execution
- Grep - Content search
- Glob - File pattern matching
- Task - Subagent delegation
- Other tools as they appear

**DotNet Operations** - .NET CLI commands extracted from Bash calls:

- `dotnet build`
- `dotnet run`
- `dotnet test`
- `dotnet publish`
- `dotnet watch`
- `dotnet format`
- Other dotnet subcommands

## Navigation Model

```text
┌─────────────────────────────────────────────────────────┐
│                     Session List                         │
│  ↑↓ Navigate    Enter → Session View    Q Quit          │
└─────────────────────────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────┐
│                     Session View                         │
│  ← → Switch Tab    ↑↓ Navigate    Enter → Detail        │
│                                                          │
│  ┌──────────┐  ┌──────────┐                             │
│  │ Summary  │  │ Actions  │                             │
│  └──────────┘  └──────────┘                             │
│                                                          │
│  Esc → Back to Session List                             │
└─────────────────────────────────────────────────────────┘
                           │ (from Actions tab)
                           ▼
┌─────────────────────────────────────────────────────────┐
│                    Action Detail                         │
│  Shows tool breakdown for a single action               │
│  Esc → Back to Session View                             │
└─────────────────────────────────────────────────────────┘
```

## View States

```csharp
enum ViewState
{
    SessionList,    // Top-level session list
    SessionDetail,  // Viewing a session (with tabs)
    ChangeDetail    // Viewing a single action's details
}

enum SessionTab
{
    Summary,   // Aggregated operation counts
    Actions    // Chronological action list (current detail view)
}
```

## Summary View Layout

```text
┌─────────────────────────────────────────────────────────┐
│ Project: my-project                                      │
│ Duration: 45m 23s  |  Messages: 12↔24  |  Tokens: 50K   │
├─────────────────────────────────────────────────────────┤
│              [Summary]  Actions                          │
├─────────────────────────────────────────────────────────┤
│                                                          │
│  Claude Operations          │  DotNet Operations         │
│  ─────────────────          │  ─────────────────         │
│  Read           42          │  build          5          │
│  Edit           18          │  test           3          │
│  Bash           12          │  run            2          │
│  Grep            8          │  watch          1          │
│  Glob            6          │                            │
│  Write           4          │                            │
│  WebFetch        3          │                            │
│  Task            2          │                            │
│                             │                            │
│  Total: 95 tool calls       │  Total: 11 commands        │
│                                                          │
└─────────────────────────────────────────────────────────┘
```

## Future Considerations

### Live View (Top-Level)

A separate top-level view accessible from Session List that shows:

- Real-time feed of operations from 1-2 active sessions
- Operations appear as they occur
- Quick insight into what Claude is currently doing

### Additional Session Tabs

Potential future tabs within the session view:

- **Timeline** - Visual timeline of activity
- **Files** - Files affected during the session
- **Stats** - Detailed statistics and metrics

## Design Principles

1. **Clarity over density** - Show meaningful summaries, allow drill-down for details
2. **Keyboard-first** - All navigation via keyboard shortcuts
3. **Consistent navigation** - Up/Down for lists, Left/Right for tabs, Enter to drill in, Esc to go back
4. **Real-time awareness** - Dashboard updates as new log data arrives
5. **.NET focus** - Special attention to .NET ecosystem tooling and patterns
