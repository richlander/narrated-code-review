# .NET CLI Wish List for AI Observability

This document describes .NET CLI features that would enable better observability tooling for AI-assisted development workflows (Claude Code, GitHub Copilot, etc.).

## Context

When AI coding assistants invoke .NET CLI commands (`dotnet build`, `dotnet test`, etc.), there's currently no structured way to:

- Correlate CLI invocations with the AI session that triggered them
- Capture detailed timing and outcome data
- Analyze patterns across multiple sessions
- Debug failures after-the-fact

## Proposed Features

### 1. Session Logging Mode

A mode where the CLI records all operations to a session logs directory.

```bash
# Enable session logging
export DOTNET_SESSION_LOG_PATH=~/.dotnet/sessions

# Or via CLI flag
dotnet build --session-log ~/.dotnet/sessions
```

**Log format considerations:**

- JSONL format (similar to Copilot's approach) for easy parsing
- One file per session or per invocation
- Include timestamps, duration, exit codes, and output summary

**Example log entry:**

```json
{
  "timestamp": "2025-01-31T10:30:00Z",
  "command": "build",
  "args": ["--configuration", "Release"],
  "workingDirectory": "/Users/rich/git/myproject",
  "duration_ms": 2340,
  "exitCode": 0,
  "correlationId": "abc123",
  "warnings": 2,
  "errors": 0
}
```

### 2. Correlation ID Support

Allow AI assistants to pass a correlation ID that the CLI records in its logs.

```bash
# Via environment variable (preferred for AI assistants)
export DOTNET_CORRELATION_ID="claude-session-abc123"
dotnet build

# Or via CLI flag (for manual use)
dotnet build --correlation-id "claude-session-abc123"
```

**Benefits:**

- Easy to match CLI logs with AI session logs
- Enables end-to-end tracing across systems
- Supports debugging and post-hoc analysis

**Implementation options:**

- Environment variable: `DOTNET_CORRELATION_ID` (preferred)
- CLI flag: `--correlation-id <id>` (override)

**Why environment variable is preferred:**

The ENV approach works well with AI assistants:

1. **Claude Code**: Supports `CLAUDE_ENV_FILE` via SessionStart hooks - a hook can generate a correlation ID and write it to this file, making it available to all subsequent Bash commands in the session
2. **No command modification**: Every `dotnet` command automatically picks up the correlation ID without needing to modify each invocation
3. **Works with existing scripts**: Build scripts and CI pipelines that call `dotnet` commands will automatically include the correlation ID

**Claude Code integration example:**

```bash
#!/bin/bash
# .claude/hooks/setup-dotnet-telemetry.sh

if [ -n "$CLAUDE_ENV_FILE" ]; then
  # Generate correlation ID from Claude's session
  CORRELATION_ID="claude-$(date +%Y%m%d)-$(openssl rand -hex 4)"

  echo "export DOTNET_CORRELATION_ID=\"$CORRELATION_ID\"" >> "$CLAUDE_ENV_FILE"
  echo "export DOTNET_SESSION_LOG_PATH=\"$HOME/.dotnet/sessions/$CORRELATION_ID\"" >> "$CLAUDE_ENV_FILE"

  # Create the log directory
  mkdir -p "$HOME/.dotnet/sessions/$CORRELATION_ID"
fi

exit 0
```

### 3. Structured Output Mode

JSON output for commands that currently only produce text.

```bash
dotnet build --output-format json
```

**Example output:**

```json
{
  "success": true,
  "duration_ms": 2340,
  "projects": [
    {
      "path": "src/MyApp/MyApp.csproj",
      "targetFramework": "net9.0",
      "warnings": 2,
      "errors": 0
    }
  ],
  "artifacts": [
    "bin/Release/net9.0/MyApp.dll"
  ]
}
```

### 4. Claude Code Integration Options

There are two approaches for integrating with Claude Code:

**Option A: SessionStart Hook (works today)**

Use Claude Code's hook system to set up telemetry at session start:

```json
// .claude/settings.json
{
  "hooks": {
    "SessionStart": [
      {
        "matcher": "startup",
        "hooks": [
          {
            "type": "command",
            "command": ".claude/hooks/setup-dotnet-telemetry.sh"
          }
        ]
      }
    ]
  }
}
```

This approach:
- Works with current Claude Code capabilities
- Sets ENVs that persist across all Bash commands in the session
- Requires no changes to Claude Code itself

**Option B: Native Claude Code Support (future)**

Claude Code could natively support correlation IDs:

1. Generate a unique session ID (it may already have one internally)
2. Expose it via `CLAUDE_SESSION_ID` environment variable
3. Record it in the JSONL logs alongside tool invocations

This would enable perfect correlation without any user configuration.

**Option C: Skill-based approach (future)**

A Claude Code skill that provides explicit control:

```text
/dotnet-telemetry enable

> Telemetry enabled for this session
> Correlation ID: ncr-20250131-abc123
> Logs will be written to: ~/.dotnet/sessions/ncr-20250131-abc123/
```

The skill would:
- Generate a unique correlation ID
- Write it to `CLAUDE_ENV_FILE` for the session
- Optionally record it in Claude Code's session metadata

### 5. Build Server Telemetry

The MSBuild build server (`dotnet build-server`) could expose metrics:

- Active builds
- Queue depth
- Cache hit rates
- Memory usage

**Access via:**

```bash
dotnet build-server status --format json
```

## Use Cases

### Post-Hoc Analysis

After a coding session, analyze what happened:

```bash
# Find all builds in today's sessions
ncr --analyze ~/.dotnet/sessions/2025-01-31/

# Show failed operations
ncr --failures --since "2 hours ago"
```

### Real-Time Dashboard

The Narrated Code Reviewer dashboard could show:

- Live build status
- Test results as they complete
- Actual execution durations (not just invocation times)
- Success/failure patterns

### Correlation with AI Logs

Match CLI operations to AI session context:

```text
Session: claude-abc123
├── 10:30:00 User: "Fix the build errors"
├── 10:30:05 Claude: Read errors from previous build
├── 10:30:10 Claude: Edit src/Program.cs
├── 10:30:15 Claude: dotnet build (2.3s, success)  ← correlated
└── 10:30:20 Claude: "Build succeeded"
```

## Compatibility Notes

### Copilot Comparison

GitHub Copilot logs to VS Code's workspace storage in JSONL format. A similar approach for the .NET CLI would enable:

- Consistent tooling across AI assistants
- Shared analysis infrastructure
- Easier migration between tools

### Existing Telemetry

The .NET CLI already has opt-in telemetry (`DOTNET_CLI_TELEMETRY_OPTOUT`). Session logging would be:

- Local-only (no data sent anywhere)
- Opt-in per session or globally
- User-controlled log location and retention

## Priority

| Feature | Impact | Complexity | Priority |
|---------|--------|------------|----------|
| Session Logging Mode | High | Medium | P1 |
| Correlation ID Support | High | Low | P1 |
| Structured Output Mode | Medium | Medium | P2 |
| Claude Code Skill | Medium | Low | P2 |
| Build Server Telemetry | Low | Medium | P3 |

## Next Steps

1. Prototype session logging with a simple wrapper script
2. Propose correlation ID support to .NET CLI team
3. Explore structured output for `dotnet build` and `dotnet test`
4. Design Claude Code skill for telemetry coordination
