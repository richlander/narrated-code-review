using System.CommandLine;
using Microsoft.Extensions.Terminal;
using AgentLogs.Domain;
using AgentLogs.Providers;
using AgentLogs.Services;
using AgentTrace.Commands;
using AgentTrace.Services;
using AgentTrace.UI;

// Global options (recursive — available on all subcommands)
var pathOption = new Option<string?>("--path") { Description = "Custom Claude logs directory" };
pathOption.Aliases.Add("-p");
pathOption.Recursive = true;

var projectOption = new Option<string?>("--project") { Description = "Filter by project name" };
projectOption.Recursive = true;

var allOption = new Option<bool>("--all") { Description = "Show all sessions" };
allOption.Aliases.Add("-a");
allOption.Recursive = true;

var dirOption = new Option<string?>("--dir") { Description = "Show sessions for a specific directory" };
dirOption.Aliases.Add("-C");
dirOption.Recursive = true;

var claudeOption = new Option<bool>("--claude") { Description = "Show only Claude Code logs" };
claudeOption.Recursive = true;

var copilotOption = new Option<bool>("--copilot") { Description = "Show only GitHub Copilot logs" };
copilotOption.Recursive = true;

// Root command
var rootCommand = new RootCommand("AgentTrace - AI Conversation Reader");
rootCommand.Options.Add(pathOption);
rootCommand.Options.Add(projectOption);
rootCommand.Options.Add(allOption);
rootCommand.Options.Add(dirOption);
rootCommand.Options.Add(claudeOption);
rootCommand.Options.Add(copilotOption);

var sessionArg = new Argument<string?>("session-id") { Arity = ArgumentArity.ZeroOrOne };
rootCommand.Arguments.Add(sessionArg);

rootCommand.SetAction(async (parseResult, ct) =>
{
    var sessionId = parseResult.GetValue(sessionArg);
    var ctx = CreateContext(parseResult);
    if (ctx == null) return 1;

    var console = new SystemConsole();
    var terminal = new AnsiTerminal(console);

    // Follow mode is no longer here — it's the 'follow' subcommand

    var sessionManager = new SessionManager();
    terminal.Append("Loading sessions...");
    await sessionManager.LoadFromProviderAsync(ctx.ScopedProvider);
    var sessions = sessionManager.GetAllSessions();
    var scopeLabel = ctx.DetectedProjectDir != null
        ? $" {sessions.Count} sessions for this project"
        : $" {sessions.Count} sessions loaded";
    terminal.AppendLine(scopeLabel);

    if (sessionId != null)
    {
        await ViewSession(sessionManager, terminal, sessionId, ctx);
        return 0;
    }

    // Non-interactive context (piped, no TTY) — list sessions instead of launching TUI
    if (Console.IsInputRedirected)
    {
        DumpCommand.ListSessionsMarkdown(sessionManager, parseResult.GetValue(projectOption), ctx.DetectedProjectDir, null);
        return 0;
    }

    // Interactive session picker mode
    var pickerIndex = 0;
    while (true)
    {
        var picker = new SessionPicker(sessionManager, terminal, pickerIndex, ctx.BookmarkStore);
        var selectedSession = await picker.RunAsync();
        if (selectedSession == null) break;

        var currentIndex = ((IList<Session>)sessions).IndexOf(selectedSession);
        if (currentIndex < 0) currentIndex = 0;

        while (true)
        {
            var result = await ViewSession(sessionManager, terminal, sessions[currentIndex].Id, ctx, currentIndex, sessions.Count);
            if (result == PagerResult.NextSession && currentIndex < sessions.Count - 1) currentIndex++;
            else if (result == PagerResult.PreviousSession && currentIndex > 0) currentIndex--;
            else break;
        }
        pickerIndex = currentIndex;
    }
    return 0;
});

// --- Subcommands ---
rootCommand.Subcommands.Add(BuildListCommand());
rootCommand.Subcommands.Add(BuildDumpCommand());
rootCommand.Subcommands.Add(BuildInfoCommand());
rootCommand.Subcommands.Add(BuildTocCommand());
rootCommand.Subcommands.Add(BuildSummaryCommand());
rootCommand.Subcommands.Add(BuildFollowCommand());
rootCommand.Subcommands.Add(BuildSearchCommand());
rootCommand.Subcommands.Add(BuildBriefCommand());
rootCommand.Subcommands.Add(BuildOrientCommand());
rootCommand.Subcommands.Add(BuildAutoSearchCommand());
rootCommand.Subcommands.Add(BuildTimelineCommand());
rootCommand.Subcommands.Add(BuildBookmarkCommand());
rootCommand.Subcommands.Add(BuildTagCommand());
rootCommand.Subcommands.Add(BuildUntagCommand());
rootCommand.Subcommands.Add(BuildStampCommand());
rootCommand.Subcommands.Add(BuildDecisionCommand());
rootCommand.Subcommands.Add(BuildPacketCommand());
rootCommand.Subcommands.Add(BuildSkillCommand());

return await rootCommand.Parse(args).InvokeAsync();

// --- Helpers ---

TraceContext? CreateContext(System.CommandLine.ParseResult pr)
{
    var targetDir = pr.GetValue(dirOption);
    if (targetDir != null) targetDir = Path.GetFullPath(targetDir);
    return TraceContextFactory.Create(
        pr.GetValue(pathOption),
        targetDir,
        pr.GetValue(allOption),
        pr.GetValue(projectOption),
        pr.GetValue(claudeOption),
        pr.GetValue(copilotOption));
}

async Task<PagerResult> ViewSession(SessionManager sm, ITerminal term, string sid, TraceContext ctx, int index = -1, int total = -1)
{
    var entries = sm.GetSessionEntries(sid);
    if (entries.Count == 0)
    {
        var match = sm.GetAllSessions().FirstOrDefault(s =>
            s.Id.StartsWith(sid, StringComparison.OrdinalIgnoreCase));
        if (match != null) { entries = sm.GetSessionEntries(match.Id); sid = match.Id; }
        if (entries.Count == 0) { term.SetColor(TerminalColor.Red); term.AppendLine($"Session not found: {sid}"); term.ResetColor(); return PagerResult.Quit; }
    }

    var session = sm.GetSession(sid);
    var conversation = new Conversation(sid, entries);
    SessionContext? sessionCtx = session != null ? new SessionContext(session.Id, session.ProjectName, session.StartTime, Math.Max(0, index), Math.Max(1, total)) : null;

    if (session?.IsActive == true)
    {
        var filePath = ctx.ScopedProvider.DiscoverLogFiles().FirstOrDefault(f => ctx.ScopedProvider.ExtractSessionId(f) == sid);
        if (filePath != null)
        {
            var livePager = new LiveConversationPager(conversation, term, filePath, ctx.ScopedProvider.CreateLineParserForFile(filePath), sessionCtx, ctx.BookmarkStore);
            return await livePager.RunAsync();
        }
    }

    var pager = new ConversationPager(conversation, term, sessionCtx, ctx.BookmarkStore);
    return await pager.RunAsync();
}

// --- Subcommand builders ---

Command BuildListCommand()
{
    var cmd = new Command("list", "List sessions");
    var plainOpt = new Option<bool>("--plain") { Description = "Markdown output (no ANSI)" };
    var tsvOpt = new Option<bool>("--tsv") { Description = "Tab-separated output" };
    var bookmarksOpt = new Option<bool>("--bookmarks") { Description = "Show only bookmarked sessions" };
    var grepOpt = new Option<string?>("--grep") { Description = "Filter by content match" };
    var tagsOpt = new Option<string?>("--tags") { Description = "Show tags column; optionally filter by label" };
    tagsOpt.Arity = ArgumentArity.ZeroOrOne;
    cmd.Options.Add(plainOpt); cmd.Options.Add(tsvOpt); cmd.Options.Add(bookmarksOpt);
    cmd.Options.Add(grepOpt); cmd.Options.Add(tagsOpt);

    cmd.SetAction(async (pr, ct) =>
    {
        var ctx = CreateContext(pr);
        if (ctx == null) return 1;
        var sm = new SessionManager();
        await sm.LoadFromProviderAsync(ctx.ScopedProvider);
        var projFilter = pr.GetValue(projectOption);
        var tagsSpecified = pr.GetResult(tagsOpt) != null;
        string? tagFilter = tagsSpecified ? (pr.GetValue(tagsOpt) ?? "") : null;
        var effectiveTagFilter = tagFilter == "" ? null : tagFilter;
        var showTagStore = tagFilter != null ? ctx.TagStore : null;
        var bkStore = pr.GetValue(bookmarksOpt) ? ctx.BookmarkStore : null;

        if (pr.GetValue(tsvOpt))
        {
            DumpCommand.ListSessionsTsv(sm, projFilter, bkStore, pr.GetValue(grepOpt), showTagStore, effectiveTagFilter);
            return 0;
        }
        if (pr.GetValue(plainOpt))
        {
            DumpCommand.ListSessionsMarkdown(sm, projFilter, ctx.DetectedProjectDir, bkStore, pr.GetValue(grepOpt), showTagStore, effectiveTagFilter);
            return 0;
        }
        // Interactive table
        var console = new SystemConsole();
        var terminal = new AnsiTerminal(console);
        ListCommand.Execute(sm, terminal, projFilter);
        return 0;
    });
    return cmd;
}

Command BuildDumpCommand()
{
    var cmd = new Command("dump", "Print full conversation as plain text");
    var idArg = new Argument<string>("id") { Description = "Session ID" };
    var headOpt = new Option<int?>("--head") { Description = "First N lines" };
    var tailOpt = new Option<int?>("--tail") { Description = "Last N lines" };
    var turnsOpt = new Option<string?>("--turns") { Description = "Turn N (1-indexed) or range M..N" };
    var lastOpt = new Option<int?>("--last") { Description = "Last N turns" };
    var speakerOpt = new Option<string?>("--speaker") { Description = "Filter by role (user|assistant)" };
    var compactOpt = new Option<bool>("--compact") { Description = "Collapse large tool results" };
    cmd.Arguments.Add(idArg);
    cmd.Options.Add(headOpt); cmd.Options.Add(tailOpt); cmd.Options.Add(turnsOpt);
    cmd.Options.Add(lastOpt); cmd.Options.Add(speakerOpt); cmd.Options.Add(compactOpt);

    cmd.SetAction(async (pr, ct) =>
    {
        var ctx = CreateContext(pr);
        if (ctx == null) return 1;
        var sm = new SessionManager();
        await sm.LoadFromProviderAsync(ctx.ScopedProvider);
        var ts = pr.GetValue(turnsOpt) != null ? TurnSlice.Parse(pr.GetValue(turnsOpt)!) : default;
        var last = pr.GetValue(lastOpt);
        if (last.HasValue && !ts.IsSet) ts = TurnSlice.LastN(last.Value);
        DumpCommand.PrintConversation(sm, pr.GetValue(idArg)!, pr.GetValue(headOpt), pr.GetValue(tailOpt), ts, pr.GetValue(speakerOpt), pr.GetValue(compactOpt));
        return 0;
    });
    return cmd;
}

Command BuildInfoCommand()
{
    var cmd = new Command("info", "Print session metadata");
    var idArg = new Argument<string>("id") { Description = "Session ID" };
    cmd.Arguments.Add(idArg);
    cmd.SetAction(async (pr, ct) =>
    {
        var ctx = CreateContext(pr);
        if (ctx == null) return 1;
        var sm = new SessionManager();
        await sm.LoadFromProviderAsync(ctx.ScopedProvider);
        DumpCommand.PrintInfo(sm, pr.GetValue(idArg)!, default, ctx.BookmarkStore, ctx.TagStore);
        return 0;
    });
    return cmd;
}

Command BuildTocCommand()
{
    var cmd = new Command("toc", "Table of contents (one line per turn)");
    var idArg = new Argument<string>("id") { Description = "Session ID" };
    cmd.Arguments.Add(idArg);
    cmd.SetAction(async (pr, ct) =>
    {
        var ctx = CreateContext(pr);
        if (ctx == null) return 1;
        var sm = new SessionManager();
        await sm.LoadFromProviderAsync(ctx.ScopedProvider);
        DumpCommand.PrintToc(sm, pr.GetValue(idArg)!);
        return 0;
    });
    return cmd;
}

Command BuildSummaryCommand()
{
    var cmd = new Command("summary", "Summarize conversation via claude CLI");
    var idArg = new Argument<string>("id") { Description = "Session ID" };
    var turnsOpt = new Option<string?>("--turns") { Description = "Turn N or range M..N" };
    var lastOpt = new Option<int?>("--last") { Description = "Last N turns" };
    cmd.Arguments.Add(idArg); cmd.Options.Add(turnsOpt); cmd.Options.Add(lastOpt);
    cmd.SetAction(async (pr, ct) =>
    {
        var ctx = CreateContext(pr);
        if (ctx == null) return 1;
        var sm = new SessionManager();
        await sm.LoadFromProviderAsync(ctx.ScopedProvider);
        var ts = pr.GetValue(turnsOpt) != null ? TurnSlice.Parse(pr.GetValue(turnsOpt)!) : default;
        var last = pr.GetValue(lastOpt);
        if (last.HasValue && !ts.IsSet) ts = TurnSlice.LastN(last.Value);
        return await SummaryCommand.RunAsync(sm, pr.GetValue(idArg)!, ts);
    });
    return cmd;
}

Command BuildFollowCommand()
{
    var cmd = new Command("follow", "Follow the active session (live tail)");
    var watchOpt = new Option<string?>("--watch") { Description = "Exit on pattern match (tripwire)" };
    watchOpt.Aliases.Add("-w");
    cmd.Options.Add(watchOpt);
    cmd.SetAction(async (pr, ct) =>
    {
        var ctx = CreateContext(pr);
        if (ctx == null) return 1;
        if (ctx.DetectedProjectDir == null && pr.GetValue(claudeOption))
        {
            var dir = pr.GetValue(dirOption) ?? Environment.CurrentDirectory;
            Console.Error.WriteLine("No Claude Code project found for current directory.");
            Console.Error.WriteLine($"  dir: {dir}");
            Console.Error.WriteLine($"  Use -C <dir> to specify a different project directory.");
            return 1;
        }
        var console = new SystemConsole();
        var terminal = new AnsiTerminal(console);
        return await FollowCommand.RunAsync(ctx.BaseProvider, ctx.DetectedProjectDir, terminal, pr.GetValue(watchOpt));
    });
    return cmd;
}

Command BuildSearchCommand()
{
    var cmd = new Command("search", "Cross-session search (supports regex)");
    var termArg = new Argument<string>("term") { Description = "Search term" };
    cmd.Arguments.Add(termArg);
    cmd.SetAction(async (pr, ct) =>
    {
        var ctx = CreateContext(pr);
        if (ctx == null) return 1;
        var sm = new SessionManager();
        await sm.LoadFromProviderAsync(ctx.ScopedProvider);
        var console = new SystemConsole();
        var terminal = new AnsiTerminal(console);
        SearchCommand.Execute(sm, terminal, pr.GetValue(termArg)!, pr.GetValue(projectOption));
        return 0;
    });
    return cmd;
}

Command BuildBriefCommand()
{
    var cmd = new Command("brief", "Compact digest of 5 most recent sessions");
    cmd.SetAction(async (pr, ct) =>
    {
        var ctx = CreateContext(pr);
        if (ctx == null) return 1;
        var sm = new SessionManager();
        await sm.LoadFromProviderAsync(ctx.ScopedProvider);
        DumpCommand.PrintBrief(sm, pr.GetValue(projectOption));
        return 0;
    });
    return cmd;
}

Command BuildOrientCommand()
{
    var cmd = new Command("orient", "Single-call orientation digest");
    cmd.SetAction(async (pr, ct) =>
    {
        var ctx = CreateContext(pr);
        if (ctx == null) return 1;
        var sm = new SessionManager();
        await sm.LoadFromProviderAsync(ctx.ScopedProvider);
        var sessions = sm.GetAllSessions();
        var projectPath = sessions.FirstOrDefault()?.ProjectPath ?? pr.GetValue(dirOption) ?? Environment.CurrentDirectory;
        OrientCommand.Execute(sm, projectPath, pr.GetValue(projectOption), ctx.BookmarkStore, ctx.TagStore);
        return 0;
    });
    return cmd;
}

Command BuildAutoSearchCommand()
{
    var cmd = new Command("autosearch", "Auto-discover breadcrumbs + git context");
    cmd.SetAction(async (pr, ct) =>
    {
        var ctx = CreateContext(pr);
        if (ctx == null) return 1;
        var sm = new SessionManager();
        await sm.LoadFromProviderAsync(ctx.ScopedProvider);
        var sessions = sm.GetAllSessions();
        var projectPath = sessions.FirstOrDefault()?.ProjectPath ?? pr.GetValue(dirOption) ?? Environment.CurrentDirectory;
        AutoSearchCommand.Execute(sm, projectPath, pr.GetValue(projectOption), ctx.BookmarkStore, ctx.TagStore);
        return 0;
    });
    return cmd;
}

Command BuildTimelineCommand()
{
    var cmd = new Command("timeline", "Cross-session chronological timeline");
    var afterOpt = new Option<string?>("--after") { Description = "Filter by time (\"2h ago\", \"1d ago\", date)" };
    cmd.Options.Add(afterOpt);
    cmd.SetAction(async (pr, ct) =>
    {
        var ctx = CreateContext(pr);
        if (ctx == null) return 1;
        var sm = new SessionManager();
        await sm.LoadFromProviderAsync(ctx.ScopedProvider);
        TimelineCommand.Execute(sm, pr.GetValue(projectOption), pr.GetValue(afterOpt));
        return 0;
    });
    return cmd;
}

Command BuildBookmarkCommand()
{
    var cmd = new Command("bookmark", "Toggle bookmark on a session");
    var idArg = new Argument<string>("id") { Description = "Session ID" };
    cmd.Arguments.Add(idArg);
    cmd.SetAction((pr, ct) =>
    {
        var ctx = CreateContext(pr);
        if (ctx == null) return Task.FromResult(1);
        if (ctx.BookmarkStore == null)
        {
            Console.Error.WriteLine("Cannot bookmark: no project directory detected. Use -C <dir> to specify one.");
            return Task.FromResult(1);
        }
        var sid = pr.GetValue(idArg)!;
        var added = ctx.BookmarkStore.Toggle(sid);
        Console.WriteLine(added ? $"★ Bookmarked {sid}" : $"  Unbookmarked {sid}");
        return Task.FromResult(0);
    });
    return cmd;
}

Command BuildTagCommand()
{
    var cmd = new Command("tag", "Add a tag to a session");
    var idArg = new Argument<string>("id") { Description = "Session ID" };
    var labelArg = new Argument<string>("label") { Description = "Tag label" };
    cmd.Arguments.Add(idArg); cmd.Arguments.Add(labelArg);
    cmd.SetAction((pr, ct) =>
    {
        var ctx = CreateContext(pr);
        if (ctx == null) return Task.FromResult(1);
        if (ctx.TagStore == null)
        {
            Console.Error.WriteLine("Cannot tag: no project directory detected. Use -C <dir> to specify one.");
            return Task.FromResult(1);
        }
        var sid = pr.GetValue(idArg)!;
        var label = pr.GetValue(labelArg)!;
        var added = ctx.TagStore.AddTag(sid, label);
        Console.WriteLine(added ? $"Tagged {sid} with '{label}'" : $"Already tagged {sid} with '{label}'");
        return Task.FromResult(0);
    });
    return cmd;
}

Command BuildUntagCommand()
{
    var cmd = new Command("untag", "Remove a tag from a session");
    var idArg = new Argument<string>("id") { Description = "Session ID" };
    var labelArg = new Argument<string>("label") { Description = "Tag label" };
    cmd.Arguments.Add(idArg); cmd.Arguments.Add(labelArg);
    cmd.SetAction((pr, ct) =>
    {
        var ctx = CreateContext(pr);
        if (ctx == null) return Task.FromResult(1);
        if (ctx.TagStore == null)
        {
            Console.Error.WriteLine("Cannot untag: no project directory detected. Use -C <dir> to specify one.");
            return Task.FromResult(1);
        }
        var sid = pr.GetValue(idArg)!;
        var label = pr.GetValue(labelArg)!;
        var removed = ctx.TagStore.RemoveTag(sid, label);
        Console.WriteLine(removed ? $"Removed tag '{label}' from {sid}" : $"Tag '{label}' not found on {sid}");
        return Task.FromResult(0);
    });
    return cmd;
}

Command BuildStampCommand()
{
    var cmd = new Command("stamp", "Emit structured stamp (session + git state)");
    var msgArg = new Argument<string?>("message") { Arity = ArgumentArity.ZeroOrOne };
    cmd.Arguments.Add(msgArg);
    cmd.SetAction((pr, ct) =>
    {
        var ctx = CreateContext(pr);
        if (ctx == null) return Task.FromResult(1);
        var projectLogPath = ctx.DetectedProjectDir != null
            ? ctx.BaseProvider.GetProjectLogPath(ctx.DetectedProjectDir)
            : ctx.BaseProvider.GetProjectLogPath(null);
        var projectPath = pr.GetValue(dirOption) ?? Environment.CurrentDirectory;
        StampCommand.Execute(projectLogPath, projectPath, pr.GetValue(msgArg));
        return Task.FromResult(0);
    });
    return cmd;
}

Command BuildDecisionCommand()
{
    var cmd = new Command("decision", "Record an architectural/design decision");
    var choseArg = new Argument<string>("chose") { Description = "What was chosen" };
    var overOpt = new Option<string?>("--over") { Description = "Alternatives considered" };
    var becauseOpt = new Option<string?>("--because") { Description = "Reason for the choice" };
    cmd.Arguments.Add(choseArg);
    cmd.Options.Add(overOpt); cmd.Options.Add(becauseOpt);
    cmd.SetAction((pr, ct) =>
    {
        var ctx = CreateContext(pr);
        if (ctx == null) return Task.FromResult(1);
        var projectLogPath = ctx.DetectedProjectDir != null
            ? ctx.BaseProvider.GetProjectLogPath(ctx.DetectedProjectDir)
            : ctx.BaseProvider.GetProjectLogPath(null);
        var projectPath = pr.GetValue(dirOption) ?? Environment.CurrentDirectory;
        DecisionCommand.Execute(projectLogPath, projectPath, pr.GetValue(choseArg)!, pr.GetValue(overOpt), pr.GetValue(becauseOpt));
        return Task.FromResult(0);
    });
    return cmd;
}

Command BuildPacketCommand()
{
    var cmd = new Command("packet", "Dense structured context packet for agent consumption");
    var depthOpt = new Option<int?>("--depth") { Description = "Number of recent sessions to include" };
    cmd.Options.Add(depthOpt);
    cmd.SetAction(async (pr, ct) =>
    {
        var ctx = CreateContext(pr);
        if (ctx == null) return 1;
        var sm = new SessionManager();
        await sm.LoadFromProviderAsync(ctx.ScopedProvider);
        var sessions = sm.GetAllSessions();
        var projectPath = sessions.FirstOrDefault()?.ProjectPath ?? pr.GetValue(dirOption) ?? Environment.CurrentDirectory;
        PacketCommand.Execute(sm, projectPath, pr.GetValue(projectOption), pr.GetValue(depthOpt) ?? 5);
        return 0;
    });
    return cmd;
}

Command BuildSkillCommand()
{
    var cmd = new Command("skill", "Print LLM skill guide (SKILL.md)");
    cmd.SetAction((pr, ct) =>
    {
        SkillCommand.Execute();
        return Task.FromResult(0);
    });
    return cmd;
}
