# Termalive Integration

The `TermaliveProvider` enables real-time monitoring of terminal sessions managed by [termalive](https://github.com/Microsoft/Microsoft.Extensions.Terminal).

## Overview

```
┌─────────────────────────────────────────────────────────────────┐
│  narrated-code-reviewer                                         │
│                                                                 │
│  ┌─────────────────────┐     ┌─────────────────────┐           │
│  │ ClaudeCodeProvider  │     │ TermaliveProvider   │           │
│  │ (reads ~/.claude)   │     │ (live streams)      │           │
│  └──────────┬──────────┘     └──────────┬──────────┘           │
│             │                           │                       │
│             │    ILogProvider           │  ILiveLogProvider     │
│             │                           │                       │
│             └───────────┬───────────────┘                       │
│                         ▼                                       │
│                  SessionManager                                 │
└─────────────────────────────────────────────────────────────────┘
                          │
                          ▼
              ┌───────────────────────┐
              │    termalive host     │
              │  ┌─────────────────┐  │
              │  │ Claude session  │  │
              │  │ (live PTY)      │  │
              │  └─────────────────┘  │
              └───────────────────────┘
```

## Setup

### 1. Install termalive

```bash
dotnet tool install -g termalive
```

### 2. Start the termalive host

```bash
termalive start
```

### 3. Create a Claude session

```bash
termalive new claude-dev --command "claude"
```

## Usage

### Basic Usage

```csharp
var provider = new TermaliveProvider();

// Check if termalive is available
if (provider.IsAvailable)
{
    // List active sessions
    var sessions = await provider.ListSessionsAsync();
    foreach (var session in sessions)
    {
        Console.WriteLine($"{session.Id}: {session.Command} ({session.State})");
    }
}
```

### Watch a Session in Real-Time

```csharp
var provider = new TermaliveProvider();

await foreach (var entry in provider.WatchSessionAsync("claude-dev"))
{
    switch (entry)
    {
        case UserEntry user:
            Console.WriteLine($"User: {user.Content}");
            break;
            
        case AssistantEntry assistant:
            Console.WriteLine($"Assistant: {assistant.TextContent}");
            foreach (var tool in assistant.ToolUses)
            {
                Console.WriteLine($"  Tool: {tool.Name}");
            }
            break;
    }
}
```

### Connect to Remote Host

```csharp
// Connect via WebSocket to a remote termalive host
var provider = new TermaliveProvider(uri: "ws://server:7777");

var sessions = await provider.ListSessionsAsync();
```

### Connect via Named Pipe (Local)

```csharp
// Default uses named pipe for local connections
var provider = new TermaliveProvider(uri: "pipe://termalive");
```

## Integration with Dashboard

The `TermaliveProvider` implements `ILiveLogProvider`, which extends the standard log reading with real-time streaming capabilities:

```csharp
public interface ILiveLogProvider
{
    string Name { get; }
    bool IsAvailable { get; }
    
    Task<IReadOnlyList<LiveSessionInfo>> ListSessionsAsync(CancellationToken ct = default);
    IAsyncEnumerable<Entry> WatchSessionAsync(string sessionId, CancellationToken ct = default);
    Task<IReadOnlyList<Entry>> GetBufferedEntriesAsync(string sessionId, CancellationToken ct = default);
}
```

### Adding Live View to Dashboard

```csharp
// In your dashboard code
var liveProvider = new TermaliveProvider();
var fileProvider = new ClaudeCodeProvider();

// Combine historical and live data
var historicalEntries = await fileProvider.GetAllEntriesAsync().ToListAsync();
var liveEntries = liveProvider.WatchSessionAsync("active-session");

// Stream to UI
await foreach (var entry in liveEntries)
{
    UpdateUI(entry);
}
```

## Exit Codes

When using `termalive logs`, exit codes indicate why streaming stopped:

| Code | Meaning |
|------|---------|
| 0 | Pattern matched or complete |
| 1 | Error |
| 2 | Timeout reached |
| 3 | Idle timeout (response complete) |
| 4 | Session exited |

This is useful for detecting when an LLM has finished responding (exit code 3 = idle timeout).

## Architecture Benefits

1. **Real-time**: No polling delay - events stream as they happen
2. **Unified**: Same `Entry` types as file-based provider
3. **Remote**: Connect to termalive hosts on other machines
4. **Multi-session**: Monitor multiple Claude sessions simultaneously
5. **NativeAOT**: Fully AOT-compatible (no reflection)
