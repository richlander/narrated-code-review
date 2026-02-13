using Microsoft.Extensions.Terminal;
using AgentLogs.Domain;
using AgentLogs.Parsing;
using AgentLogs.Providers;
using AgentLogs.Services;
using AgentTrace.Commands;
using AgentTrace.Services;
using AgentTrace.UI;

if (args.Contains("--help") || args.Contains("-h"))
{
    ShowHelp();
    return 0;
}

if (args.Contains("--version") || args.Contains("-v"))
{
    var version = "AgentTrace v0.2.0";
    var infoVersion = System.Reflection.CustomAttributeExtensions
        .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(Program).Assembly)?.InformationalVersion;
    // InformationalVersion format: "1.0.0+commithash"
    if (infoVersion != null)
    {
        var plusIdx = infoVersion.IndexOf('+');
        if (plusIdx >= 0)
            version += $" ({infoVersion[(plusIdx + 1)..].AsSpan(0, Math.Min(7, infoVersion.Length - plusIdx - 1))})";
    }
    Console.WriteLine(version);
    return 0;
}

if (args.Contains("--skill"))
{
    SkillCommand.Execute();
    return 0;
}

// Parse arguments
string? customPath = null;
string? projectFilter = null;
string? searchTerm = null;
string? sessionId = null;
string? watchPattern = null;
string? targetDir = null;
var showAll = false;
var followMode = false;
var plainMode = false;
var tsvMode = false;
string? dumpSessionId = null;
string? infoSessionId = null;
string? summarySessionId = null;
string? tocSessionId = null;
int? headLines = null;
int? tailLines = null;
TurnSlice turnSlice = default;
string? speakerFilter = null;
var compactMode = false;
string? bookmarkSessionId = null;
var bookmarksFilter = false;
string? grepTerm = null;
var briefMode = false;
var timelineMode = false;
string? afterFilter = null;
string? tagSessionId = null;
string? tagLabel = null;
string? untagSessionId = null;
string? untagLabel = null;
string? tagFilter = null;
var autoSearchMode = false;
var orientMode = false;
var stampMode = false;
string? stampMessage = null;
int? lastTurns = null;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--path" or "-p" when i + 1 < args.Length:
            customPath = args[++i];
            break;
        case "--project" when i + 1 < args.Length:
            projectFilter = args[++i];
            break;
        case "--search" or "-s" when i + 1 < args.Length:
            searchTerm = args[++i];
            break;
        case "--list" or "-l":
            break; // Handled below
        case "--all" or "-a":
            showAll = true;
            break;
        case "--follow" or "-f":
            followMode = true;
            break;
        case "--watch" or "-w" when i + 1 < args.Length:
            watchPattern = args[++i];
            followMode = true; // Watch implies follow
            break;
        case "--dir" or "-C" when i + 1 < args.Length:
            targetDir = Path.GetFullPath(args[++i]);
            break;
        case "--plain":
            plainMode = true;
            break;
        case "--tsv":
            tsvMode = true;
            break;
        case "--dump" when i + 1 < args.Length:
            dumpSessionId = args[++i];
            break;
        case "--info" when i + 1 < args.Length:
            infoSessionId = args[++i];
            break;
        case "--summary" when i + 1 < args.Length:
            summarySessionId = args[++i];
            break;
        case "--toc" when i + 1 < args.Length:
            tocSessionId = args[++i];
            break;
        case "--head" when i + 1 < args.Length:
            if (int.TryParse(args[++i], out var h)) headLines = h;
            break;
        case "--tail" when i + 1 < args.Length:
            if (int.TryParse(args[++i], out var t)) tailLines = t;
            break;
        case "--turns" when i + 1 < args.Length:
            turnSlice = TurnSlice.Parse(args[++i]);
            break;
        case "--speaker" when i + 1 < args.Length:
            speakerFilter = args[++i];
            break;
        case "--compact":
            compactMode = true;
            break;
        case "--bookmark" when i + 1 < args.Length:
            bookmarkSessionId = args[++i];
            break;
        case "--bookmarks":
            bookmarksFilter = true;
            break;
        case "--grep" when i + 1 < args.Length:
            grepTerm = args[++i];
            break;
        case "--brief":
            briefMode = true;
            break;
        case "--timeline":
            timelineMode = true;
            break;
        case "--after" when i + 1 < args.Length:
            afterFilter = args[++i];
            break;
        case "--tag" when i + 2 < args.Length:
            tagSessionId = args[++i];
            tagLabel = args[++i];
            break;
        case "--untag" when i + 2 < args.Length:
            untagSessionId = args[++i];
            untagLabel = args[++i];
            break;
        case "--autosearch":
            autoSearchMode = true;
            break;
        case "--orient":
            orientMode = true;
            break;
        case "--last" when i + 1 < args.Length:
            if (int.TryParse(args[++i], out var lastN)) lastTurns = lastN;
            break;
        case "--stamp":
            stampMode = true;
            // Optional message: consume next arg if it doesn't start with --
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                stampMessage = args[++i];
            break;
        case "--tags":
            // Optional label filter: --tags [label]
            if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                tagFilter = args[++i];
            else
                tagFilter = ""; // Empty string means "show tags column but don't filter"
            break;
        default:
            // Positional argument: treat as session ID
            if (!args[i].StartsWith('-') && sessionId == null)
            {
                sessionId = args[i];
            }
            break;
    }
}

// Apply --last flag to turn slice (takes precedence if --turns not set)
if (lastTurns.HasValue && !turnSlice.IsSet)
    turnSlice = TurnSlice.LastN(lastTurns.Value);

// Create provider — auto-detect project from cwd unless --all or --project given
var baseProvider = customPath != null
    ? new ClaudeCodeProvider(customPath)
    : new ClaudeCodeProvider();

string? detectedProjectDir = null;
if (targetDir != null)
{
    detectedProjectDir = baseProvider.FindProjectDir(targetDir);
    if (detectedProjectDir == null)
    {
        Console.Error.WriteLine($"No Claude Code sessions found for: {targetDir}");
        Console.Error.WriteLine($"  expected: {baseProvider.BasePath}/{ClaudeCodeProvider.EncodeProjectPath(targetDir)}");
        return 1;
    }
}
else if (!showAll && projectFilter == null && customPath == null)
{
    var cwd = Environment.CurrentDirectory;
    detectedProjectDir = baseProvider.FindProjectDir(cwd);
}

var provider = detectedProjectDir != null
    ? new ClaudeCodeProvider(baseProvider.BasePath) { ProjectDirFilter = detectedProjectDir }
    : baseProvider;

// Create bookmark store when project is known
BookmarkStore? bookmarkStore = detectedProjectDir != null
    ? new BookmarkStore(baseProvider.GetProjectLogPath(detectedProjectDir))
    : null;

// Create tag store when project is known
TagStore? tagStore = detectedProjectDir != null
    ? new TagStore(baseProvider.GetProjectLogPath(detectedProjectDir))
    : null;

// Handle --bookmark toggle (needs provider + project dir only, no session loading)
if (bookmarkSessionId != null)
{
    if (bookmarkStore == null)
    {
        Console.Error.WriteLine("Cannot bookmark: no project directory detected. Use -C <dir> to specify one.");
        return 1;
    }

    var added = bookmarkStore.Toggle(bookmarkSessionId);
    Console.WriteLine(added ? $"★ Bookmarked {bookmarkSessionId}" : $"  Unbookmarked {bookmarkSessionId}");
    return 0;
}

// Handle --tag / --untag (needs project dir only, no session loading)
if (tagSessionId != null && tagLabel != null)
{
    if (tagStore == null)
    {
        Console.Error.WriteLine("Cannot tag: no project directory detected. Use -C <dir> to specify one.");
        return 1;
    }

    var added = tagStore.AddTag(tagSessionId, tagLabel);
    Console.WriteLine(added ? $"Tagged {tagSessionId} with '{tagLabel}'" : $"Already tagged {tagSessionId} with '{tagLabel}'");
    return 0;
}

if (untagSessionId != null && untagLabel != null)
{
    if (tagStore == null)
    {
        Console.Error.WriteLine("Cannot untag: no project directory detected. Use -C <dir> to specify one.");
        return 1;
    }

    var removed = tagStore.RemoveTag(untagSessionId, untagLabel);
    Console.WriteLine(removed ? $"Removed tag '{untagLabel}' from {untagSessionId}" : $"Tag '{untagLabel}' not found on {untagSessionId}");
    return 0;
}

// Handle --stamp (needs provider + project dir only, no session loading)
if (stampMode)
{
    var projectLogPath = detectedProjectDir != null
        ? baseProvider.GetProjectLogPath(detectedProjectDir)
        : null;
    var projectPath = targetDir ?? Environment.CurrentDirectory;
    StampCommand.Execute(projectLogPath, projectPath, stampMessage);
    return 0;
}

// Create terminal
var console = new SystemConsole();
var terminal = new AnsiTerminal(console);

// Check if base path exists
if (!Directory.Exists(provider.BasePath))
{
    terminal.SetColor(TerminalColor.Yellow);
    terminal.Append("Warning: ");
    terminal.ResetColor();
    terminal.AppendLine($"Claude Code logs directory not found at {provider.BasePath}");
    terminal.AppendLine();
}

// Follow mode — find and tail the active session
if (followMode)
{
    if (detectedProjectDir == null)
    {
        var dir = targetDir ?? Environment.CurrentDirectory;
        terminal.SetColor(TerminalColor.Red);
        terminal.AppendLine("No Claude Code project found for current directory.");
        terminal.ResetColor();
        terminal.AppendLine($"  dir: {dir}");
        terminal.AppendLine($"  expected: {provider.BasePath}/{ClaudeCodeProvider.EncodeProjectPath(dir)}");
        terminal.AppendLine();
        terminal.AppendLine("  Use -C <dir> to specify a different project directory.");
        return 1;
    }

    var exitCode = await FollowCommand.RunAsync(baseProvider, detectedProjectDir, terminal, watchPattern);
    return exitCode;
}

// Create services
var sessionManager = new SessionManager();

// Load sessions
var quietMode = plainMode || tsvMode || briefMode || timelineMode || autoSearchMode || orientMode
    || dumpSessionId != null || infoSessionId != null || summarySessionId != null || tocSessionId != null;
if (!quietMode)
    terminal.Append("Loading sessions...");
await sessionManager.LoadFromProviderAsync(provider);
var sessions = sessionManager.GetAllSessions();

if (!quietMode)
{
    var scopeLabel = detectedProjectDir != null
        ? $" {sessions.Count} sessions for this project"
        : $" {sessions.Count} sessions loaded";
    terminal.AppendLine(scopeLabel);
}

// Plain-text commands — no ANSI, suitable for LLM consumption / piping
if (orientMode)
{
    var projectPath = sessions.FirstOrDefault()?.ProjectPath ?? targetDir ?? Environment.CurrentDirectory;
    OrientCommand.Execute(sessionManager, projectPath, projectFilter, bookmarkStore, tagStore);
    return 0;
}

if (autoSearchMode)
{
    // Resolve project path from first session's ProjectPath, or targetDir/cwd
    var projectPath = sessions.FirstOrDefault()?.ProjectPath ?? targetDir ?? Environment.CurrentDirectory;
    AutoSearchCommand.Execute(sessionManager, projectPath, projectFilter, bookmarkStore, tagStore);
    return 0;
}

if (briefMode)
{
    DumpCommand.PrintBrief(sessionManager, projectFilter, bookmarksFilter ? bookmarkStore : null, tagFilter != null ? tagStore : null);
    return 0;
}

if (timelineMode)
{
    TimelineCommand.Execute(sessionManager, projectFilter, afterFilter);
    return 0;
}

if (infoSessionId != null)
{
    DumpCommand.PrintInfo(sessionManager, infoSessionId, turnSlice, bookmarkStore, tagStore);
    return 0;
}

if (tocSessionId != null)
{
    DumpCommand.PrintToc(sessionManager, tocSessionId);
    return 0;
}

if (summarySessionId != null)
{
    return await SummaryCommand.RunAsync(sessionManager, summarySessionId, turnSlice);
}

if (dumpSessionId != null)
{
    DumpCommand.PrintConversation(sessionManager, dumpSessionId, headLines, tailLines, turnSlice, speakerFilter, compactMode);
    return 0;
}

// Route to appropriate command
if (args.Contains("--list") || args.Contains("-l"))
{
    // Determine effective tag filter: --tags without a label means show column, --tags <label> means filter
    var effectiveTagFilter = tagFilter == "" ? null : tagFilter;
    var showTagStore = tagFilter != null ? tagStore : null;

    if (tsvMode)
    {
        if (detectedProjectDir == null && !showAll && targetDir == null && projectFilter == null && customPath == null)
        {
            var cwd = Environment.CurrentDirectory;
            Console.Error.WriteLine($"Warning: No sessions found for current directory: {cwd}");
            Console.Error.WriteLine($"  Use -C <dir> to specify a project directory, or --all for all projects.");
        }

        DumpCommand.ListSessionsTsv(sessionManager, projectFilter, bookmarksFilter ? bookmarkStore : null, grepTerm, showTagStore, effectiveTagFilter);
        return 0;
    }

    if (plainMode)
    {
        if (detectedProjectDir == null && !showAll && targetDir == null && projectFilter == null && customPath == null)
        {
            var cwd = Environment.CurrentDirectory;
            Console.Error.WriteLine($"Warning: No sessions found for current directory: {cwd}");
            Console.Error.WriteLine($"  Use -C <dir> to specify a project directory, or --all for all projects.");
        }

        DumpCommand.ListSessionsMarkdown(sessionManager, projectFilter, detectedProjectDir, bookmarksFilter ? bookmarkStore : null, grepTerm, showTagStore, effectiveTagFilter);
        return 0;
    }

    ListCommand.Execute(sessionManager, terminal, projectFilter);
    return 0;
}

if (searchTerm != null)
{
    SearchCommand.Execute(sessionManager, terminal, searchTerm, projectFilter);
    return 0;
}

if (sessionId != null)
{
    // Direct session view
    await ViewSession(sessionManager, terminal, sessionId);
    return 0;
}

// Non-interactive context (piped, no TTY) — list sessions instead of launching TUI
if (Console.IsInputRedirected)
{
    DumpCommand.ListSessionsMarkdown(sessionManager, projectFilter, detectedProjectDir, bookmarksFilter ? bookmarkStore : null);
    return 0;
}

// Interactive session picker mode — loop between picker and session view
var pickerIndex = 0;
while (true)
{
    var picker = new SessionPicker(sessionManager, terminal, pickerIndex, bookmarkStore);
    var selectedSession = await picker.RunAsync();

    if (selectedSession == null)
        break;

    // Find index in session list for left/right navigation
    var currentIndex = ((IList<Session>)sessions).IndexOf(selectedSession);
    if (currentIndex < 0) currentIndex = 0;

    while (true)
    {
        var result = await ViewSession(sessionManager, terminal, sessions[currentIndex].Id, currentIndex, sessions.Count);

        if (result == PagerResult.NextSession && currentIndex < sessions.Count - 1)
        {
            currentIndex++;
        }
        else if (result == PagerResult.PreviousSession && currentIndex > 0)
        {
            currentIndex--;
        }
        else
        {
            break; // Back to picker
        }
    }

    // Restore picker position to the session we were just viewing
    pickerIndex = currentIndex;
}

return 0;

// View a specific session in the conversation pager
async Task<PagerResult> ViewSession(SessionManager sm, ITerminal term, string sid, int index = -1, int total = -1)
{
    var entries = sm.GetSessionEntries(sid);
    if (entries.Count == 0)
    {
        // Try matching session ID by prefix
        var allSessions = sm.GetAllSessions();
        var match = allSessions.FirstOrDefault(s =>
            s.Id.StartsWith(sid, StringComparison.OrdinalIgnoreCase));

        if (match != null)
        {
            entries = sm.GetSessionEntries(match.Id);
            sid = match.Id;
        }

        if (entries.Count == 0)
        {
            term.SetColor(TerminalColor.Red);
            term.AppendLine($"Session not found: {sid}");
            term.ResetColor();
            return PagerResult.Quit;
        }
    }

    var session = sm.GetSession(sid);
    var conversation = new Conversation(sid, entries);

    // Build session context for status bar display
    SessionContext? ctx = null;
    if (session != null)
    {
        ctx = new SessionContext(
            session.Id,
            session.ProjectName,
            session.StartTime,
            Math.Max(0, index),
            Math.Max(1, total));
    }

    // Use live pager for active sessions
    if (session?.IsActive == true)
    {
        var filePath = provider.DiscoverLogFiles()
            .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f) == sid);

        if (filePath != null)
        {
            var livePager = new LiveConversationPager(conversation, term, filePath, ctx, bookmarkStore);
            return await livePager.RunAsync();
        }
    }

    var pager = new ConversationPager(conversation, term, ctx, bookmarkStore);
    return await pager.RunAsync();
}

void ShowHelp()
{
    Console.WriteLine();
    Console.WriteLine("  AgentTrace - AI Conversation Reader");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  agent-trace              Interactive picker (scoped to current project)");
    Console.WriteLine("  agent-trace <session-id> View a specific session");
    Console.WriteLine("  agent-trace --list       List sessions");
    Console.WriteLine("  agent-trace --list --plain  List sessions as markdown");
    Console.WriteLine("  agent-trace --list --tsv    List sessions as tab-separated values");
    Console.WriteLine("  agent-trace --info <id>  Print session metadata (no content)");
    Console.WriteLine("  agent-trace --dump <id>  Print full conversation as plain text");
    Console.WriteLine("  agent-trace --dump <id> --head 50   First 50 lines of dump");
    Console.WriteLine("  agent-trace --dump <id> --tail 50   Last 50 lines of dump");
    Console.WriteLine("  agent-trace --dump <id> --turns 5   Show turn 5 (1-indexed)");
    Console.WriteLine("  agent-trace --dump <id> --turns 9..13  Turns 9 through 13");
    Console.WriteLine("  agent-trace --dump <id> --last 3    Last 3 turns");
    Console.WriteLine("  agent-trace --toc <id>              Table of contents (one line per turn)");
    Console.WriteLine("  agent-trace --dump <id> --speaker assistant  Only assistant text");
    Console.WriteLine("  agent-trace --dump <id> --compact   Collapse large tool results");
    Console.WriteLine("  agent-trace --summary <id>          Summarize via claude CLI");
    Console.WriteLine("  agent-trace --brief                 Digest of 5 most recent sessions");
    Console.WriteLine("  agent-trace --orient                Single-call orientation digest");
    Console.WriteLine("  agent-trace --autosearch            Auto-discover breadcrumbs + git context");
    Console.WriteLine("  agent-trace --timeline              Cross-session chronological view");
    Console.WriteLine("  agent-trace --timeline --after \"2h ago\"  Timeline filtered by time");
    Console.WriteLine("  agent-trace --bookmark <id>  Toggle bookmark on a session");
    Console.WriteLine("  agent-trace --list --plain --bookmarks  List only bookmarked sessions");
    Console.WriteLine("  agent-trace --list --plain --grep \"term\"  Filter list by content match");
    Console.WriteLine("  agent-trace --tag <id> <label>   Add a tag to a session");
    Console.WriteLine("  agent-trace --untag <id> <label> Remove a tag from a session");
    Console.WriteLine("  agent-trace --list --plain --tags        List with tags column");
    Console.WriteLine("  agent-trace --list --plain --tags <label>  Filter by tag label");
    Console.WriteLine("  agent-trace --stamp [message]  Emit structured stamp (session + git state)");
    Console.WriteLine("  agent-trace --follow     Follow the active session (live tail)");
    Console.WriteLine("  agent-trace --watch \"pattern\"  Follow + exit on match (tripwire)");
    Console.WriteLine("  agent-trace --search \"term\"  Search across sessions");
    Console.WriteLine("  agent-trace --skill      Print LLM skill guide (how to use this tool)");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  -h, --help               Show this help");
    Console.WriteLine("  -v, --version            Show version");
    Console.WriteLine("  -p, --path <path>        Custom Claude logs directory");
    Console.WriteLine("  -l, --list               List sessions non-interactively");
    Console.WriteLine("  --plain                  Markdown output (no ANSI); use with --list");
    Console.WriteLine("  --tsv                    Tab-separated output; use with --list");
    Console.WriteLine("  --info <session-id>      Print session metadata as markdown");
    Console.WriteLine("  --dump <session-id>      Print full conversation as plain text to stdout");
    Console.WriteLine("  --toc <session-id>       Print table of contents (one line per turn)");
    Console.WriteLine("  --head <N>               Output first N lines (use with --dump)");
    Console.WriteLine("  --tail <N>               Output last N lines (use with --dump)");
    Console.WriteLine("  --turns <N|M..N>         Turn N (1-indexed), or range M..N (inclusive)");
    Console.WriteLine("  --last <N>               Last N turns (use with --dump)");
    Console.WriteLine("  --speaker <user|assistant>  Filter entries by role (use with --dump)");
    Console.WriteLine("  --compact                Collapse large tool results to one line");
    Console.WriteLine("  --brief                  Compact digest of 5 most recent sessions");
    Console.WriteLine("  --orient                 Single-call orientation digest (sessions + breadcrumbs)");
    Console.WriteLine("  --autosearch             Search for breadcrumbs, commits, bookmarks, tags");
    Console.WriteLine("  --timeline               Cross-session chronological timeline");
    Console.WriteLine("  --after <time>           Filter timeline (\"2h ago\", \"1d ago\", date)");
    Console.WriteLine("  --grep <term>            Filter --list to sessions containing term");
    Console.WriteLine("  --bookmark <session-id>  Toggle bookmark on a session");
    Console.WriteLine("  --bookmarks              Show only bookmarked sessions (use with --list)");
    Console.WriteLine("  --tag <id> <label>       Add a tag to a session");
    Console.WriteLine("  --untag <id> <label>     Remove a tag from a session");
    Console.WriteLine("  --tags [label]           Show tags column; optionally filter by label");
    Console.WriteLine("  --stamp [message]        Emit structured «stamp» with session + git state");
    Console.WriteLine("  --summary <session-id>   Summarize conversation via claude --print");
    Console.WriteLine("  --skill                  Print LLM skill guide (SKILL.md)");
    Console.WriteLine("  -f, --follow             Follow the active session for this project");
    Console.WriteLine("  -w, --watch <pattern>    Follow + exit with code 2 when pattern matches");
    Console.WriteLine("  -s, --search <term>      Cross-session search (supports regex)");
    Console.WriteLine("  -a, --all                Show all sessions (not just current project)");
    Console.WriteLine("  -C, --dir <path>         Show sessions for a specific directory");
    Console.WriteLine("  --project <name>         Filter by project name");
    Console.WriteLine();
    Console.WriteLine("  By default, sessions are scoped to the current directory's project.");
    Console.WriteLine("  Use --all to see sessions from all projects.");
    Console.WriteLine();
    Console.WriteLine("Pager Controls:");
    Console.WriteLine("  j/k or ↑/↓               Scroll line");
    Console.WriteLine("  Ctrl-D/U                 Half page down/up");
    Console.WriteLine("  Ctrl-F/B or Space        Full page down/up");
    Console.WriteLine("  gg/G                     Top/bottom");
    Console.WriteLine("  [/]                      Jump between turns");
    Console.WriteLine("  ←/→                      Previous/next session");
    Console.WriteLine("  t                        Toggle tool details");
    Console.WriteLine("  e                        Toggle thinking blocks");
    Console.WriteLine("  b                        Toggle bookmark");
    Console.WriteLine("  /                        Search");
    Console.WriteLine("  n/N                      Next/previous match");
    Console.WriteLine("  ?                        Help");
    Console.WriteLine("  q/Esc                    Back to session list");
    Console.WriteLine();
}
