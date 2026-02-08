using Microsoft.Extensions.Terminal;
using AgentLogs.Domain;
using AgentLogs.Parsing;
using AgentLogs.Providers;
using AgentLogs.Services;
using AgentTrace.Commands;
using AgentTrace.UI;

if (args.Contains("--help") || args.Contains("-h"))
{
    ShowHelp();
    return 0;
}

if (args.Contains("--version") || args.Contains("-v"))
{
    Console.WriteLine("AgentTrace v0.1.0");
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
        default:
            // Positional argument: treat as session ID
            if (!args[i].StartsWith('-') && sessionId == null)
            {
                sessionId = args[i];
            }
            break;
    }
}

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
terminal.Append("Loading sessions...");
await sessionManager.LoadFromProviderAsync(provider);
var sessions = sessionManager.GetAllSessions();

var scopeLabel = detectedProjectDir != null
    ? $" {sessions.Count} sessions for this project"
    : $" {sessions.Count} sessions loaded";
terminal.AppendLine(scopeLabel);

// Route to appropriate command
if (args.Contains("--list") || args.Contains("-l"))
{
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

// Interactive session picker mode — loop between picker and session view
while (true)
{
    var picker = new SessionPicker(sessionManager, terminal);
    var selectedSession = await picker.RunAsync();

    if (selectedSession == null)
        break;

    // Find index in session list for left/right navigation
    var currentIndex = ((IList<Session>)sessions).IndexOf(selectedSession);
    if (currentIndex < 0) currentIndex = 0;

    while (true)
    {
        var result = await ViewSession(sessionManager, terminal, sessions[currentIndex].Id);

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
}

return 0;

// View a specific session in the conversation pager
async Task<PagerResult> ViewSession(SessionManager sm, ITerminal term, string sid)
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

    // Use live pager for active sessions
    if (session?.IsActive == true)
    {
        var filePath = provider.DiscoverLogFiles()
            .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f) == sid);

        if (filePath != null)
        {
            var livePager = new LiveConversationPager(conversation, term, filePath);
            return await livePager.RunAsync();
        }
    }

    var pager = new ConversationPager(conversation, term);
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
    Console.WriteLine("  agent-trace --follow     Follow the active session (live tail)");
    Console.WriteLine("  agent-trace --watch \"pattern\"  Follow + exit on match (tripwire)");
    Console.WriteLine("  agent-trace --search \"term\"  Search across sessions");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  -h, --help               Show this help");
    Console.WriteLine("  -v, --version            Show version");
    Console.WriteLine("  -p, --path <path>        Custom Claude logs directory");
    Console.WriteLine("  -l, --list               List sessions non-interactively");
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
    Console.WriteLine("  /                        Search");
    Console.WriteLine("  n/N                      Next/previous match");
    Console.WriteLine("  ?                        Help");
    Console.WriteLine("  q/Esc                    Back to session list");
    Console.WriteLine();
}
