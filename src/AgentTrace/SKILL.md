# AgentTrace — LLM Skill Guide

You have access to `agent-trace`, a CLI tool that reads Claude Code conversation logs.
Use it to recover context after compaction or to understand what happened in a session.

## Quick Start: "I've been compacted!"

```bash
# 1. List recent sessions for this project (markdown format)
#    Run from the project directory, or use -C to specify it
agent-trace --list --plain
agent-trace --list --plain -C /path/to/project

# 2. Get session metadata (line count, turns, duration) without dumping content
agent-trace --info <session-id>

# 3. Dump the full conversation for a session
agent-trace --dump <session-id>

# 4. Dump only the last N turns (most useful for recent context)
agent-trace --dump <session-id> --turns 5

# 5. Dump a specific range of turns (1-indexed, inclusive)
agent-trace --dump <session-id> --turns 9..13

# 6. Get a table of contents (one line per turn — find the turn you need)
agent-trace --toc <session-id>

# 7. Get only assistant prose (skip tools, thinking, system entries)
agent-trace --dump <session-id> --speaker assistant

# 8. Compact mode — collapse large tool results to one-line summaries
agent-trace --dump <session-id> --compact

# 9. Combine flags for surgical extraction
agent-trace --dump <session-id> --turns 9..13 --speaker assistant --compact

# 10. Get first/last N lines of a dump
agent-trace --dump <session-id> --head 200
agent-trace --dump <session-id> --tail 100

# 11. Summarize a session via claude CLI (requires claude on PATH)
agent-trace --summary <session-id>
agent-trace --summary <session-id> --turns 5
```

## Commands

### List sessions

```bash
# Markdown format — ideal for LLM reading
agent-trace --list --plain
agent-trace --list --plain -C /path/to/project

# Tab-separated — ideal for parsing/scripting
agent-trace --list --tsv

# Output columns: ID (7-char), Status, Project, Messages, Tools, Duration, Date
```

### Session info (metadata only)

```bash
# Quick overview without dumping content — shows line count estimate
agent-trace --info <session-id>

# Output: ID, Project, Status, Started, Duration, Turns, Messages, Tool calls, Lines
```

### Dump a conversation

```bash
# Print full conversation as plain text to stdout
agent-trace --dump <session-id>

# Session ID prefix matching works
agent-trace --dump abc123

# Last N turns only (most common for context recovery)
agent-trace --dump <session-id> --turns 3

# Specific turn range (1-indexed, inclusive)
agent-trace --dump <session-id> --turns 9..13

# First/last N lines
agent-trace --dump <session-id> --head 200
agent-trace --dump <session-id> --tail 100

# Scope to a specific project directory
agent-trace --dump <session-id> -C /path/to/project
```

### Table of contents

```bash
# One-line summary per turn — find the turn you need before dumping
agent-trace --toc <session-id>

# Output: Turn number, Messages, Tools, Duration, Content preview
```

### Speaker filter

```bash
# Only assistant prose (skip tool calls, thinking blocks, tool results)
agent-trace --dump <session-id> --speaker assistant

# Only user messages (skip assistant responses)
agent-trace --dump <session-id> --speaker user

# Combine with turn range
agent-trace --dump <session-id> --turns 1..5 --speaker user
```

### Compact mode

```bash
# Collapse tool results > 200 chars to one-line summaries
agent-trace --dump <session-id> --compact

# Great for sessions heavy with sub-agent Task results
agent-trace --dump <session-id> --turns 9..13 --speaker assistant --compact
```

### Summarize a session

```bash
# Pipes conversation to claude --print for a concise summary
agent-trace --summary <session-id>

# Summarize only recent turns
agent-trace --summary <session-id> --turns 5
```

### Search across sessions

```bash
# Find sessions mentioning a term (supports regex)
agent-trace --search "migration"
agent-trace --search "error|fail" -C /path/to/project
```

## Output Format

The `--dump` output is plain text with this structure:

```
# Session abc1234

**Project:** my-project
**Started:** 2026-02-11 14:30:00
**Turns:** 12

--- Turn 1 ---
[user]
<user message>

[assistant]
<assistant response>
  > ToolName (target)
    $ command
  < tool [OK]
    output

--- Turn 2 ---
...
```

## Tips

- All `--plain`, `--dump`, `--info`, and `--summary` output goes to stdout with zero ANSI codes
- `--list --plain` outputs markdown (readable by humans and LLMs); use `--tsv` for tab-separated parsing
- **Important**: `--list --plain` scopes to the current directory by default.
  Use `-C <dir>` to target a specific project, or `--all` for all projects.
  A warning is printed to stderr if no sessions match the current directory.
- Session IDs support prefix matching — you don't need the full UUID
- Use `--info` to gauge dump size before committing to a full `--dump`
- Use `--toc <id>` to scan the session and find which turns matter before dumping
- Use `--turns N` for last N turns, or `--turns M..N` for a specific range (1-indexed, inclusive)
- Use `--speaker assistant` to get only the prose — skip tool calls and results
- Use `--compact` to collapse large tool results (e.g. sub-agent Task output) to one line
- Combine flags: `--dump <id> --turns 9..13 --speaker assistant --compact`
- Combine with standard Unix tools: `wc -l`, `grep`, `head`, `tail`, `less`

## Bookmarks

Bookmarks let the human mark important sessions and the agent discover them.

```bash
# Toggle bookmark on a session
agent-trace --bookmark <session-id>

# List only bookmarked sessions
agent-trace --list --plain --bookmarks

# Workflow: recover context from bookmarked sessions
agent-trace --list --plain --bookmarks
agent-trace --dump <bookmarked-id> --turns 5
```

In the interactive pager, press `b` to toggle a bookmark. The status line shows `★` when a session is bookmarked. In the session picker, bookmarked sessions display `★` in the list.

Storage: `.bookmarks` file in the project log directory (one session ID per line).
