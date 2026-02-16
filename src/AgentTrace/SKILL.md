# AgentTrace — LLM Skill Guide

You have access to `agent-trace`, a CLI tool that reads Claude Code conversation logs.
Use it to recover context after compaction or to understand what happened in a session.

## Quick Orient

```bash
# Single-call context recovery — recent sessions, previous session detail, uncommitted state, breadcrumbs
agent-trace orient
```

For deeper investigation: `agent-trace toc <id>` then `agent-trace dump <id> --turns 3..5`.
For breadcrumb search: `agent-trace autosearch`.

**Key insight:** You are both **producer** and **consumer** of log data. Your conversation text
becomes searchable history. Write clues into your responses that future sessions can find —
like how sourcelink embeds metadata in PDBs for later discovery.

## Quick Start: Recovery after compaction

```bash
# 1. Single-call orientation digest (recommended first command)
agent-trace orient

# 2. If you need more detail, drill into specific sessions:
agent-trace toc <session-id>
agent-trace dump <session-id> --turns 3..5 --compact

# 3. Deep investigation — breadcrumbs, commits, bookmarks
agent-trace autosearch

# 4. Quick digest of 5 most recent sessions (goal + last message)
agent-trace brief

# 5. Search for a specific topic across all sessions
agent-trace list --plain --grep "migration"

# 6. Cross-session timeline of recent activity
agent-trace timeline --after "2h ago"

# 7. Dense structured packet (alternative to orient for agents)
agent-trace packet
agent-trace packet --depth 10
```

## Structured Stamps (Primary Breadcrumb)

Use `stamp` to emit a structured telemetry block with session ID, git state, and an optional
message. Stamps use guillemet delimiters (`«stamp:...»`) that never appear naturally in code,
giving zero false positives when searching.

```bash
# Emit a stamp with a message
agent-trace stamp "Completed authentication middleware"

# Emit a stamp without a message (auto-only: session + git state)
agent-trace stamp
```

Output format:
```
«stamp:2026-02-13T08:15:00Z»
  session: 3cb8313
  commit: 173b29f Add continuation detection...
  branch: main
  staged: src/Foo.cs, src/Bar.cs
  modified: src/Baz.cs
  untracked: tests/new_test.cs
  message: Completed authentication middleware
«/stamp»
```

### When to stamp

- **After commits** — capture the commit hash + what was done
- **At milestones** — summarize progress at significant points
- **Before ending a session** — leave a final breadcrumb for the next agent
- **Before context compaction** — if you sense compaction is imminent, stamp first

### Backtrace: linking plans to sessions

When writing a plan (especially before session continuation or ending), record the current
session ID so future agents can backtrace to the full discussion:

```
Plan written in session 3cb8313. Use `agent-trace dump 3cb8313` to review the full discussion.
```

You can discover your session ID from the `session:` field in `stamp` output, or from
`agent-trace brief` (the most recent active session).

### Finding stamps

Stamps are searchable via `autosearch` (shows a dedicated Stamps section), and via grep:

```bash
agent-trace autosearch                      # Stamps section with parsed fields
agent-trace list --plain --grep "«stamp:"   # Find sessions containing stamps
```

## Decision Stamps

Use `agent-trace decision` to record architectural and design decisions in a structured
`«decision:...»` block. Lighter than stamps — captures the choice, alternatives, and rationale
without working tree state.

```bash
# Record a decision with alternatives and rationale
agent-trace decision "Use System.CommandLine" --over "hand-rolled, Spectre.Console" --because "better help, subcommands"

# Record a simple choice (--over and --because are optional)
agent-trace decision "Store decisions as guillemet blocks"
```

Output format:
```
«decision:2026-02-13T14:30:00Z»
  chose: Use System.CommandLine
  over: hand-rolled, Spectre.Console
  because: better help, subcommands
  session: 3cb8313
  branch: main
  commit: e4a249d Add --orient command...
«/decision»
```

### When to record decisions

- **Architectural choices** — framework, library, or pattern selection
- **Design tradeoffs** — when you pick one approach over another
- **Convention adoption** — naming, file layout, API style decisions
- **Technology changes** — switching from one tool/library to another

### Finding decisions

Decisions appear in `orient` (Breadcrumbs section), `autosearch` (dedicated Decisions section),
and `packet` (decisions section):

```bash
agent-trace orient                                # decisions in Breadcrumbs
agent-trace autosearch                            # dedicated Decisions section
agent-trace search "«decision:"                   # find sessions with decisions
```

## Writing Breadcrumbs (Producer)

Everything you write becomes searchable history. Leave clues that your future self (or a
colleague agent in a different session) can find via `list --grep`, `search`, or `dump`.

### Structured stamps vs. ad-hoc breadcrumbs

**Prefer `stamp`** for structured telemetry — it captures session ID, git state, and timestamp
automatically. Use ad-hoc breadcrumbs (below) as lightweight supplements when a full stamp
would be overkill.

### What to write into conversations

**Commit hashes** — After committing, echo the short hash in your response text:
```
Committed as abc1234: "Add user authentication middleware"
```
Future agents can `list --grep "abc1234"` to find when and why that commit was made.

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
- **Self-contained context**: Include enough that a grep hit + surrounding lines tells the story
- **Structured patterns**: Consistent prefixes like `Decision:`, `Milestone:`, `Root cause:` make searching reliable
- **File paths**: Mention full paths so `search "AuthMiddleware"` finds relevant sessions

### Tagging sessions

Tag sessions to categorize completed work for future discovery:

```bash
agent-trace tag <session-id> feature
agent-trace tag <session-id> migration
agent-trace tag <session-id> bugfix
```

Future agents can filter: `agent-trace list --plain --tags migration`

## Recovering Context (Consumer)

### Orientation: what happened recently?

```bash
# Single-call digest — the best first command after compaction
agent-trace orient

# For deeper investigation:
agent-trace autosearch                      # breadcrumbs, commits, milestones, decisions
agent-trace brief                           # goal + last message for 5 recent sessions
agent-trace timeline --after "1d ago"       # cross-session chronological timeline
agent-trace list --plain                    # full session list with details
```

### Finding specific information

```bash
# Search for a topic across all sessions (regex supported)
agent-trace list --plain --grep "database migration"
agent-trace list --plain --grep "error|fail"

# Search with ANSI output (shows match context)
agent-trace search "AuthMiddleware"

# Find sessions tagged with a label
agent-trace list --plain --tags bugfix

# Find bookmarked sessions (human-curated important ones)
agent-trace list --plain --bookmarks
```

### Drilling into a session

```bash
# Metadata first — gauge size before dumping
agent-trace info <session-id>

# Table of contents — one line per turn, find what matters
agent-trace toc <session-id>

# Dump a specific turn (1-indexed)
agent-trace dump <session-id> --turns 5

# Dump a range of turns (1-indexed, inclusive)
agent-trace dump <session-id> --turns 9..13

# Last N turns (most recent context)
agent-trace dump <session-id> --last 3

# Assistant prose only (skip tool calls, thinking, results)
agent-trace dump <session-id> --speaker assistant

# Compact mode — collapse tool results + continuation preambles
agent-trace dump <session-id> --compact

# Combine flags for surgical extraction
agent-trace dump <session-id> --turns 9..13 --speaker assistant --compact

# First/last N lines of a dump
agent-trace dump <session-id> --head 200
agent-trace dump <session-id> --tail 100
```

### Workflow: reconstruct multi-day project history

```bash
# 1. See all sessions for a project
agent-trace list --plain -C /path/to/project

# 2. Get the timeline of recent work
agent-trace timeline --after "2d ago"

# 3. Check bookmarked/tagged sessions first
agent-trace list --plain --bookmarks
agent-trace list --plain --tags feature

# 4. Grep for specific topics
agent-trace list --plain --grep "schema change"

# 5. Drill into relevant sessions
agent-trace info <id>          # metadata + git commits
agent-trace toc <id>           # find the right turns
agent-trace dump <id> --turns 3..7 --compact
```

## Commands Reference

### Orient

```bash
agent-trace orient
```

Single-call orientation digest. Shows: recent sessions (ID, age, status, goal), previous session
detail (turns, duration, tools, commits, turn-by-turn summary), uncommitted git state (staged,
modified, new files), and breadcrumbs (stamps, commit mentions). Target: under 1000 tokens.

### Auto-search

```bash
agent-trace autosearch
```

Automatically gathers context and searches for breadcrumbs. Shows: git branch and recent
commits, which sessions mention which commit hashes, bookmarked/tagged sessions, and
structured breadcrumbs (Milestone, Decision, Root cause). Best for deep investigation after `orient`.

### Brief digest

```bash
agent-trace brief
```

Compact digest of 5 most recent sessions. Shows ID, age, status, turn count, goal (first
user message, skipping continuation preambles), and last assistant message. Target: ~2K tokens.

### List sessions

```bash
agent-trace list --plain                    # markdown format
agent-trace list --plain -C /path/to/project
agent-trace list --tsv                      # tab-separated
agent-trace list --plain --grep "term"      # filter by content match
agent-trace list --plain --tags             # show tags column
agent-trace list --plain --tags feature     # filter by tag
agent-trace list --plain --bookmarks        # bookmarked only
```

### Session info

```bash
agent-trace info <session-id>
```

Shows: ID, Project, Status, Started, Duration, Turns, Messages, Tool calls, Lines,
Bookmarked, Tags, Type (continuation), Branch, Commits during session.

### Table of contents

```bash
agent-trace toc <session-id>
```

One-line summary per turn. Continuation sessions show `[continued]` prefix.

### Dump conversation

```bash
agent-trace dump <session-id>
agent-trace dump <id> --turns 5             # turn 5 (1-indexed)
agent-trace dump <id> --turns 9..13         # range (1-indexed, inclusive)
agent-trace dump <id> --last 3              # last 3 turns
agent-trace dump <id> --speaker assistant   # prose only
agent-trace dump <id> --compact             # collapse tool results + continuations
agent-trace dump <id> --head 200            # first 200 lines
agent-trace dump <id> --tail 100            # last 100 lines
```

### Timeline

```bash
agent-trace timeline                        # all turns, all sessions
agent-trace timeline --after "2h ago"       # relative time filter
agent-trace timeline --after "1d ago"
agent-trace timeline --after "2026-02-12"   # absolute date
agent-trace timeline --project my-project   # scope to project
```

### Search

```bash
agent-trace search "term"                   # ANSI output with context
agent-trace search "error|fail"             # regex supported
```

### Follow (live tail)

```bash
agent-trace follow                          # follow active session for current project
agent-trace follow --watch "pattern"        # exit on pattern match (tripwire)
agent-trace follow -w "Done"               # short form
```

### Summarize

```bash
agent-trace summary <session-id>            # pipes to claude --print
agent-trace summary <id> --turns 5
```

### Stamp

```bash
agent-trace stamp "Completed auth middleware"    # with message
agent-trace stamp                                # auto-only (session + git)
```

Emits a `«stamp:...»` block with session ID, git state, and optional message.

### Decision

```bash
agent-trace decision "Use X" --over "Y, Z" --because "reason"
agent-trace decision "Simple choice"
```

Emits a `«decision:...»` block with chose/over/because fields and session/branch/commit context.

### Packet

```bash
agent-trace packet                               # default: 5 recent sessions
agent-trace packet --depth 10                    # include more sessions
```

Dense structured context packet for agent consumption. No markdown — pure `key: value` pairs
in `--- section ---` delimited blocks. Sections: project, git, sessions, decisions, stamps, files.
Alternative to `orient` when you want pure data instead of markdown prose.

### Bookmarks and Tags

```bash
agent-trace bookmark <session-id>           # toggle bookmark
agent-trace tag <id> <label>                # add tag
agent-trace untag <id> <label>              # remove tag
```

### Skill

```bash
agent-trace skill                           # print this skill guide
```

## Output Format

The `dump` output is plain text with this structure:

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

## Global Options

These options work on all subcommands:

```bash
--path, -p <dir>     # custom Claude logs directory
--project <name>     # filter by project name
--all, -a            # show all sessions (ignore project scoping)
--dir, -C <dir>      # scope to a specific directory
--copilot            # use GitHub Copilot logs instead of Claude Code
```

## Tips

- All text output (`list --plain`, `dump`, `info`, `brief`, `timeline`, `summary`, `orient`) has zero ANSI codes — safe for piping
- Session IDs support prefix matching — you don't need the full UUID
- `list --plain` scopes to the current directory by default; use `-C <dir>` or `--all` to widen
- Use `orient` first for quick orientation, then drill into specific sessions
- Use `toc` to find turns before dumping — saves tokens
- Use `--compact` to collapse continuation preambles and large tool results
- Combine with Unix tools: `wc -l`, `grep`, `head`, `tail`, `less`
- Write searchable breadcrumbs (commit hashes, decisions, milestones) — your future self will thank you
