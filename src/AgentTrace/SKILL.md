# AgentTrace — LLM Skill Guide

You have access to `agent-trace`, a CLI tool that reads Claude Code conversation logs.
Use it to recover context after compaction or to understand what happened in a session.

**Key insight:** You are both **producer** and **consumer** of log data. Your conversation text
becomes searchable history. Write clues into your responses that future sessions can find —
like how sourcelink embeds metadata in PDBs for later discovery.

## Quick Start: "I've been compacted!"

```bash
# 1. Quick digest of 5 most recent sessions (goal + last message)
agent-trace --brief

# 2. List recent sessions for this project (markdown format)
agent-trace --list --plain

# 3. Get session metadata (line count, turns, duration, git commits)
agent-trace --info <session-id>

# 4. Table of contents — find the turn you need before dumping
agent-trace --toc <session-id>

# 5. Dump relevant turns
agent-trace --dump <session-id> --turns 9..13 --compact

# 6. Search for a specific topic across all sessions
agent-trace --list --plain --grep "migration"

# 7. Cross-session timeline of recent activity
agent-trace --timeline --after "2h ago"
```

## Writing Breadcrumbs (Producer)

Everything you write becomes searchable history. Leave clues that your future self (or a
colleague agent in a different session) can find via `--grep`, `--search`, or `--dump`.

### What to write into conversations

**Commit hashes** — After committing, echo the short hash in your response text:
```
Committed as abc1234: "Add user authentication middleware"
```
Future agents can `--grep "abc1234"` to find when and why that commit was made.

**Decision records** — When making architectural choices, state them clearly:
```
Decision: Using JWT tokens (not sessions) because the API is stateless.
Alternatives considered: session cookies, OAuth tokens.
```

**Milestone markers** — At significant points, write a clear summary:
```
Milestone: Authentication system complete. All 12 tests passing.
Files changed: src/Auth/, src/Middleware/AuthMiddleware.cs, tests/Auth/
```

**Error resolution notes** — When you fix a tricky bug, document the root cause:
```
Root cause: The DbContext was being disposed before the async query completed.
Fix: Changed to scoped lifetime in DI container (AddScoped instead of AddTransient).
```

**Cross-references** — Link related work:
```
This continues the work from session 1f8fc04 (database migration).
Related PR: #42 (schema changes)
```

### What makes good breadcrumbs

- **Searchable terms**: Use specific names, hashes, error messages — not just "fixed the bug"
- **Self-contained context**: Include enough that `--grep` hit + surrounding lines tells the story
- **Structured patterns**: Consistent prefixes like `Decision:`, `Milestone:`, `Root cause:` make searching reliable
- **File paths**: Mention full paths so `--grep "AuthMiddleware"` finds relevant sessions

### Tagging sessions

Tag sessions to categorize completed work for future discovery:

```bash
agent-trace --tag <session-id> feature
agent-trace --tag <session-id> migration
agent-trace --tag <session-id> bugfix
```

Future agents can filter: `agent-trace --list --plain --tags migration`

## Recovering Context (Consumer)

### Orientation: what happened recently?

```bash
# Compact digest — goal + last message for 5 recent sessions
agent-trace --brief

# Timeline view — all turns across sessions, chronologically
agent-trace --timeline --after "1d ago"

# Full session list with details
agent-trace --list --plain
```

### Finding specific information

```bash
# Search for a topic across all sessions (regex supported)
agent-trace --list --plain --grep "database migration"
agent-trace --list --plain --grep "error|fail"

# Search with ANSI output (shows match context)
agent-trace --search "AuthMiddleware"

# Find sessions tagged with a label
agent-trace --list --plain --tags bugfix

# Find bookmarked sessions (human-curated important ones)
agent-trace --list --plain --bookmarks
```

### Drilling into a session

```bash
# Metadata first — gauge size before dumping
agent-trace --info <session-id>

# Table of contents — one line per turn, find what matters
agent-trace --toc <session-id>

# Dump specific turns (1-indexed, inclusive range)
agent-trace --dump <session-id> --turns 9..13

# Last N turns (most recent context)
agent-trace --dump <session-id> --turns 5

# Assistant prose only (skip tool calls, thinking, results)
agent-trace --dump <session-id> --speaker assistant

# Compact mode — collapse tool results + continuation preambles
agent-trace --dump <session-id> --compact

# Combine flags for surgical extraction
agent-trace --dump <session-id> --turns 9..13 --speaker assistant --compact

# First/last N lines of a dump
agent-trace --dump <session-id> --head 200
agent-trace --dump <session-id> --tail 100
```

### Workflow: reconstruct multi-day project history

```bash
# 1. See all sessions for a project
agent-trace --list --plain -C /path/to/project

# 2. Get the timeline of recent work
agent-trace --timeline --after "2d ago"

# 3. Check bookmarked/tagged sessions first
agent-trace --list --plain --bookmarks
agent-trace --list --plain --tags feature

# 4. Grep for specific topics
agent-trace --list --plain --grep "schema change"

# 5. Drill into relevant sessions
agent-trace --info <id>          # metadata + git commits
agent-trace --toc <id>           # find the right turns
agent-trace --dump <id> --turns 3..7 --compact
```

## Commands Reference

### Brief digest

```bash
agent-trace --brief
```

Compact digest of 5 most recent sessions. Shows ID, age, status, turn count, goal (first
user message, skipping continuation preambles), and last assistant message. Target: ~2K tokens.

### List sessions

```bash
agent-trace --list --plain                    # markdown format
agent-trace --list --plain -C /path/to/project
agent-trace --list --tsv                      # tab-separated
agent-trace --list --plain --grep "term"      # filter by content match
agent-trace --list --plain --tags             # show tags column
agent-trace --list --plain --tags feature     # filter by tag
agent-trace --list --plain --bookmarks        # bookmarked only
```

### Session info

```bash
agent-trace --info <session-id>
```

Shows: ID, Project, Status, Started, Duration, Turns, Messages, Tool calls, Lines,
Bookmarked, Tags, Type (continuation), Branch, Commits during session.

### Table of contents

```bash
agent-trace --toc <session-id>
```

One-line summary per turn. Continuation sessions show `[continued]` prefix.

### Dump conversation

```bash
agent-trace --dump <session-id>
agent-trace --dump <id> --turns 5             # last 5 turns
agent-trace --dump <id> --turns 9..13         # range (1-indexed)
agent-trace --dump <id> --speaker assistant   # prose only
agent-trace --dump <id> --compact             # collapse tool results + continuations
agent-trace --dump <id> --head 200            # first 200 lines
agent-trace --dump <id> --tail 100            # last 100 lines
```

### Timeline

```bash
agent-trace --timeline                        # all turns, all sessions
agent-trace --timeline --after "2h ago"       # relative time filter
agent-trace --timeline --after "1d ago"
agent-trace --timeline --after "2026-02-12"   # absolute date
agent-trace --timeline --project my-project   # scope to project
```

### Search

```bash
agent-trace --search "term"                   # ANSI output with context
agent-trace --search "error|fail"             # regex supported
```

### Summarize

```bash
agent-trace --summary <session-id>            # pipes to claude --print
agent-trace --summary <id> --turns 5
```

### Bookmarks and Tags

```bash
agent-trace --bookmark <session-id>           # toggle bookmark
agent-trace --tag <id> <label>                # add tag
agent-trace --untag <id> <label>              # remove tag
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

In compact mode, continuation preambles are collapsed:
```
--- Turn 1 ---
[user]
[continuation summary: 3,456 chars]
Implement the feature described above
```

## Tips

- All text output (`--plain`, `--dump`, `--info`, `--brief`, `--timeline`, `--summary`) has zero ANSI codes — safe for piping
- Session IDs support prefix matching — you don't need the full UUID
- `--list --plain` scopes to the current directory by default; use `-C <dir>` or `--all` to widen
- Use `--brief` first for quick orientation, then drill into specific sessions
- Use `--toc` to find turns before dumping — saves tokens
- Use `--compact` to collapse continuation preambles and large tool results
- Combine with Unix tools: `wc -l`, `grep`, `head`, `tail`, `less`
- Write searchable breadcrumbs (commit hashes, decisions, milestones) — your future self will thank you
