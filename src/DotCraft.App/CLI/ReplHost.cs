using System.Text;
using DotCraft.Agents;
using DotCraft.CLI.Rendering;
using DotCraft.Commands;
using DotCraft.Commands.Custom;
using DotCraft.Diagnostics;
using DotCraft.Cron;
using DotCraft.Tracing;
using DotCraft.Heartbeat;
using DotCraft.Hooks;
using DotCraft.Localization;
using DotCraft.Mcp;
using DotCraft.Protocol;
using DotCraft.Security;
using DotCraft.Skills;
using Spectre.Console;

namespace DotCraft.CLI;

public sealed class ReplHost(
    SkillsLoader skillsLoader,
    ISessionService sessionService,
    string workspacePath = "", string dotCraftPath = "",
    HeartbeatService? heartbeatService = null, CronService? cronService = null,
    AgentFactory? agentFactory = null, McpClientManager? mcpClientManager = null,
    string? dashBoardUrl = null,
    LanguageService? languageService = null, TokenUsageStore? tokenUsageStore = null,
    CustomCommandLoader? customCommandLoader = null,
    AgentModeManager? modeManager = null,
    HookRunner? hookRunner = null)
{
    private readonly LanguageService _lang = languageService ?? new LanguageService();
    private readonly AgentModeManager _modeManager = modeManager ?? new AgentModeManager();

    private string _currentSessionId = string.Empty;

    // Thread ID for deferred (lazy) thread creation in Session Protocol path
    private string? _currentThreadId;

    // Identity for the CLI channel (used for thread discovery/creation)
    private readonly SessionIdentity _cliIdentity = new()
    {
        ChannelName = "cli",
        UserId = "local",
        WorkspacePath = workspacePath
    };

    /// <summary>Command history shared across all line-editor instances.</summary>
    private readonly List<string> _inputHistory = [];

    /// <summary>
    /// Buffer snapshot saved when the user presses Shift+Tab to switch mode,
    /// so it can be restored on the next prompt.
    /// </summary>
    private List<string>? _pendingBuffer;

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        // Defer thread creation until the user sends a first message.
        // _currentThreadId stays null until RunSessionInputAsync materializes the thread.
        _currentThreadId = null;
        _currentSessionId = string.Empty;

        ShowWelcomeScreen(_currentSessionId);

        // Run SessionStart hooks
        await RunSessionStartHooksAsync(_currentSessionId, cancellationToken);

        while (true)
        {
            var (result, input) = await ReadInputAsync(cancellationToken);
            if (result == LineEditResult.ModeSwitch)
            {
                // Shift+Tab was pressed -- mode already toggled, just re-loop for new prompt
                continue;
            }
            if (string.IsNullOrWhiteSpace(input))
            {
                continue;
            }

            // Record in history (avoid consecutive duplicates)
            var trimmed = input.Trim();
            if (_inputHistory.Count == 0 || _inputHistory[^1] != trimmed)
                _inputHistory.Add(trimmed);
            var (handled, shouldExit, expandedPrompt) = await HandleCommand(trimmed);
            if (handled && expandedPrompt == null)
            {
                if (shouldExit)
                {
                    break;
                }
                continue;
            }

            var agentInput = expandedPrompt ?? trimmed;

            await RunSessionInputAsync(agentInput, cancellationToken);
        }

        AnsiConsole.MarkupLine($"\n[blue]👋 {Strings.Goodbye(_lang)}[/]");
    }

    /// <summary>
    /// Runs SessionStart hooks for the given session.
    /// </summary>
    private async Task RunSessionStartHooksAsync(string sessionId, CancellationToken ct)
    {
        if (hookRunner == null) return;
        var input = new HookInput { SessionId = sessionId };
        await hookRunner.RunAsync(HookEvent.SessionStart, input, ct);
    }

    /// <summary>
    /// Reads a line of input using the <see cref="LineEditor"/>.
    /// Supports cursor movement, command history, multi-line input (Ctrl+Enter),
    /// word-wrap across terminal width, command hints, Tab auto-complete, and Shift+Tab mode-switch.
    /// </summary>
    private async Task<(LineEditResult Result, string Text)> ReadInputAsync(CancellationToken cancellationToken)
    {
        var promptWidth = PrintPrompt();

        var editor = new LineEditor(_inputHistory, promptWidth, GetCommandHints);

        // Restore buffer that was saved during a previous Shift+Tab mode-switch
        if (_pendingBuffer is { Count: > 0 })
        {
            editor.SetInitialBuffer(_pendingBuffer);
            _pendingBuffer = null;
        }

        var (result, text) = await editor.ReadLineAsync(cancellationToken);

        if (result == LineEditResult.ModeSwitch)
        {
            // Save current buffer so it survives the Shift+Tab mode switch
            _pendingBuffer = new List<string>(editor.Buffer);
            var next = _modeManager.CurrentMode == AgentMode.Plan
                ? AgentMode.Agent
                : AgentMode.Plan;
            SwitchToMode(next);
        }

        return (result, text);
    }

    /// <summary>
    /// Render the prompt and return its display-column width so the
    /// <see cref="LineEditor"/> can correctly track multi-row cursor positions.
    /// <para>
    /// Uses <see cref="Console.CursorLeft"/> to read the actual terminal cursor
    /// column after rendering, which correctly handles emojis and symbols whose
    /// rendered width differs from <see cref="LineEditor.GetDisplayWidth"/>'s
    /// prediction (e.g. ⚡ U+26A1 renders as 2 columns on most terminals but
    /// is classified as narrow by Unicode East Asian Width). An inaccurate
    /// prompt width causes <see cref="LineEditor.GetVisualPosition"/> to compute
    /// the wrong line-wrap boundary, producing row-level cursor misalignment.
    /// </para>
    /// </summary>
    private int PrintPrompt()
    {
        var (emoji, color) = _modeManager.CurrentMode == AgentMode.Plan
            ? ("📋", "yellow")
            : ("⚡", "green");

        if (!string.IsNullOrEmpty(_currentSessionId))
        {
            var shortId = GetShortSessionId(_currentSessionId);
            AnsiConsole.Markup($"[grey]({shortId.EscapeMarkup()})[/] ");
        }

        AnsiConsole.Markup($"[{color}]{emoji} ❯[/] ");

        // Read actual terminal cursor column for accurate prompt width.
        // This avoids the row-level misalignment that occurs when GetDisplayWidth
        // under-counts emoji width (e.g. ⚡) and the wrap point shifts by one character.
        try
        {
            var pos = Console.CursorLeft;
            if (pos > 0) return pos;
        }
        catch
        {
            // Console.CursorLeft may throw in redirected/non-interactive environments.
        }

        // Fallback to computed width using the shortened ID.
        int displayWidth = 0;
        if (!string.IsNullOrEmpty(_currentSessionId))
        {
            var shortId = GetShortSessionId(_currentSessionId);
            displayWidth += LineEditor.GetDisplayWidth($"({shortId}) ");
        }
        displayWidth += LineEditor.GetDisplayWidth($"{emoji} ❯ ");
        return displayWidth;
    }

    /// <summary>
    /// Returns a short display label for a session/thread ID.
    /// For Session Protocol thread IDs (format: thread_{date}_{suffix}), shows only the suffix.
    /// For legacy session IDs, falls back to the last 8 characters.
    /// </summary>
    private static string GetShortSessionId(string sessionId)
    {
        // Session Protocol thread IDs: "thread_20260315_lbdp2i" → "lbdp2i"
        var lastUnderscore = sessionId.LastIndexOf('_');
        if (lastUnderscore >= 0 && lastUnderscore < sessionId.Length - 1)
            return sessionId[(lastUnderscore + 1)..];

        // Legacy fallback: last 8 chars
        return sessionId.Length > 8 ? sessionId[^8..] : sessionId;
    }

    public void ReprintPrompt()
    {
        AnsiConsole.WriteLine();
        PrintPrompt();
    }

    /// <summary>
    /// Returns up to 5 command hint entries for the current buffer text.
    /// Hints are shown when the buffer starts with '/'.
    /// </summary>
    private IReadOnlyList<(string Command, string Description)> GetCommandHints(string bufferText)
    {
        if (!bufferText.StartsWith('/'))
            return Array.Empty<(string, string)>();

        var result = new List<(string Command, string Description)>();

        foreach (var cmd in KnownCommands)
        {
            if (cmd.StartsWith(bufferText, StringComparison.OrdinalIgnoreCase))
                result.Add((cmd, string.Empty));
        }

        if (customCommandLoader != null)
        {
            foreach (var cmd in customCommandLoader.ListCommands())
            {
                var fullName = "/" + cmd.Name;
                if (fullName.StartsWith(bufferText, StringComparison.OrdinalIgnoreCase))
                    result.Add((fullName, cmd.Description));
            }
        }

        return result.Take(5).ToList();
    }

    private void ShowWelcomeScreen(string currentSessionId)
    {
        StatusPanel.ShowWelcome(currentSessionId, dashBoardUrl, _lang);
    }

    private void SwitchToMode(AgentMode mode)
    {
        if (_modeManager.CurrentMode == mode)
        {
            AnsiConsole.MarkupLine($"[grey]Already in {mode.ToString().ToLower()} mode.[/]\n");
            return;
        }

        _modeManager.SwitchMode(mode);
        RebuildAgentForCurrentMode();
        var (emoji, color) = mode == AgentMode.Plan ? ("📋", "yellow") : ("⚡", "green");
        var rule = new Rule($"[{color}]{emoji} {mode.ToString().ToLower()}[/]");
        rule.RuleStyle($"{color} dim");
        AnsiConsole.Write(rule);
    }

    private void RebuildAgentForCurrentMode()
    {
        if (agentFactory == null)
            return;

        agentFactory.CreateAgentForMode(_modeManager.CurrentMode, _modeManager);
    }

    private async Task LoadSessionAsync(string newSessionId, CancellationToken cancellationToken)
    {
        try
        {
            var thread = await sessionService.ResumeThreadAsync(newSessionId, cancellationToken);
            _currentThreadId = newSessionId;
            _currentSessionId = newSessionId;

            // Run SessionStart hooks for loaded session
            await RunSessionStartHooksAsync(_currentSessionId, cancellationToken);

            // Refresh display
            AnsiConsole.Clear();
            ShowWelcomeScreen(_currentSessionId);

            AnsiConsole.MarkupLine($"[green]✓[/] {Strings.SessionLoaded(_lang)}：[cyan]{newSessionId.EscapeMarkup()}[/]");
            AnsiConsole.WriteLine();

            // Print conversation history from Session Protocol turns
            if (thread.Turns.Count > 0)
                SessionHistoryPrinter.Print(thread);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] {Strings.SessionLoadFailed(_lang)}：{ex.Message.EscapeMarkup()}");
            AnsiConsole.WriteLine();
        }
    }

    /// <summary>
    /// Create a new session (lazy: in Session Protocol mode, the thread is deferred until first input).
    /// </summary>
    private async Task NewSession(CancellationToken cancellationToken)
    {
        try
        {
            // Lazy: reset to pending state; thread is created on first input.
            _currentThreadId = null;
            _currentSessionId = string.Empty;

            await RunSessionStartHooksAsync(_currentSessionId, cancellationToken);

            AnsiConsole.Clear();
            ShowWelcomeScreen(_currentSessionId);
            AnsiConsole.MarkupLine($"[green]✓[/] {Strings.SessionCreated(_lang)}");
            AnsiConsole.WriteLine();
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] {Strings.SessionCreateFailed(_lang)}：{ex.Message.EscapeMarkup()}");
            AnsiConsole.WriteLine();
        }
    }

    private async Task DeleteSession(string sessionId)
    {
        try
        {
            var wasCurrent = sessionId == _currentSessionId;

            await sessionService.ArchiveThreadAsync(sessionId);
            AnsiConsole.MarkupLine($"[green]✓[/] {Strings.SessionDeleted(_lang)}：[cyan]{sessionId.EscapeMarkup()}[/]");

            if (wasCurrent)
            {
                // Lazy: reset to pending state; thread is created on next input.
                _currentThreadId = null;
                _currentSessionId = string.Empty;
                AnsiConsole.MarkupLine($"[grey]→ {Strings.SessionNewCreated(_lang)}[/]");
            }

            AnsiConsole.WriteLine();
        }
        catch (KeyNotFoundException)
        {
            AnsiConsole.MarkupLine($"[yellow]{Strings.SessionNotFound(_lang)}：{sessionId.EscapeMarkup()}[/]");
            AnsiConsole.WriteLine();
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] {Strings.SessionDeleteFailed(_lang)}：{ex.Message.EscapeMarkup()}");
            AnsiConsole.WriteLine();
        }
    }

    private static readonly string[] KnownCommands =
    [
        "/exit", "/help", "/clear", "/new", "/load", "/delete",
        "/init", "/skills", "/mcp", "/sessions", "/memory",
        "/debug", "/heartbeat", "/cron", "/lang", "/commands",
        "/plan", "/agent"
    ];

    private async Task<(bool Handled, bool ShouldExit, string? ExpandedPrompt)> HandleCommand(string input)
    {
        switch (input.ToLowerInvariant())
        {
            case "/exit":
                return (true, true, null);

            case "/help":
                StatusPanel.ShowHelp(_lang);
                return (true, false, null);

            case "/clear":
                AnsiConsole.Clear();
                ShowWelcomeScreen(_currentSessionId);
                return (true, false, null);

            case "/new":
                await NewSession(CancellationToken.None);
                return (true, false, null);

            case "/load":
            {
                var threads = await sessionService.FindThreadsAsync(_cliIdentity);
                var selectedThread = SessionPrompt.SelectThreadToLoad(threads, _currentThreadId, _lang);
                if (selectedThread != null)
                    await LoadSessionAsync(selectedThread, CancellationToken.None);
                return (true, false, null);
            }

            case "/delete":
            {
                var threadsToDelete = await sessionService.FindThreadsAsync(_cliIdentity);
                var threadToDelete = SessionPrompt.SelectThreadToDelete(threadsToDelete, _currentThreadId, _lang);
                if (threadToDelete != null)
                {
                    if (SessionPrompt.ConfirmDelete(threadToDelete, threadToDelete == _currentThreadId, _lang))
                    {
                        await DeleteSession(threadToDelete);
                        AnsiConsole.Clear();
                        ShowWelcomeScreen(_currentSessionId);
                    }
                }
                return (true, false, null);
            }

            case "/init":
                HandleInitCommand();
                return (true, false, null);

            case "/skills":
                var allSkills = skillsLoader.ListSkills(filterUnavailable: false);
                StatusPanel.ShowSkillsTable(allSkills, skillsLoader.WorkspaceSkillsPath, skillsLoader.UserSkillsPath, _lang);
                return (true, false, null);

            case "/mcp":
                StatusPanel.ShowMcpServersTable(mcpClientManager, _lang);
                return (true, false, null);

            case "/sessions":
            {
                var threadList = await sessionService.FindThreadsAsync(_cliIdentity);
                StatusPanel.ShowThreadsTable(threadList, _lang);
                return (true, false, null);
            }

            case "/memory":
                HandleMemoryCommand();
                return (true, false, null);

            case "/debug":
                HandleDebugCommand();
                return (true, false, null);

            case "/lang":
                HandleLanguageCommand();
                return (true, false, null);

            case "/commands":
                HandleCommandsCommand();
                return (true, false, null);

            case "/plan":
                SwitchToMode(AgentMode.Plan);
                return (true, false, null);

            case "/agent":
                SwitchToMode(AgentMode.Agent);
                return (true, false, null);
        }

        if (input.StartsWith("/heartbeat", StringComparison.OrdinalIgnoreCase))
        {
            await HandleHeartbeatCommandAsync(input);
            return (true, false, null);
        }

        if (input.StartsWith("/cron", StringComparison.OrdinalIgnoreCase))
        {
            HandleCronCommand(input);
            return (true, false, null);
        }

        // Try custom commands before "unknown command" fallback
        if (input.StartsWith('/') && customCommandLoader != null)
        {
            var resolved = customCommandLoader.TryResolve(input);
            if (resolved != null)
                return (true, false, resolved.ExpandedPrompt);
        }

        if (input.StartsWith('/'))
        {
            var msg = CommandHelper.FormatUnknownCommandMessage(input, KnownCommands, _lang);
            AnsiConsole.MarkupLine($"[red]{msg.EscapeMarkup()}[/]");
            AnsiConsole.WriteLine();
            return (true, false, null);
        }

        return (false, false, null);
    }

    private void HandleInitCommand()
    {
        AnsiConsole.MarkupLine($"\n[blue]🚀 {Strings.InitWorkspace(_lang)}[/]");
        AnsiConsole.MarkupLine($"[grey]{Strings.CurrentWorkspace(_lang)}: {workspacePath.EscapeMarkup()}[/]");

        if (Directory.Exists(workspacePath))
        {
            var confirm = AnsiConsole.Prompt(
                new ConfirmationPrompt(Strings.WorkspaceExists(_lang))
                {
                    DefaultValue = false
                });

            if (!confirm)
            {
                AnsiConsole.MarkupLine($"[grey]{Strings.InitCancelled(_lang)}[/]\n");
                return;
            }
        }

        var result = InitHelper.InitializeWorkspace(workspacePath);

        if (result == 0)
        {
            AnsiConsole.MarkupLine($"[green]✓[/] {Strings.InitComplete(_lang)}\n");
        }
        else
        {
            AnsiConsole.MarkupLine($"[red]✗[/] {Strings.InitFailed(_lang)}: {result}\n");
        }
    }

    private void HandleMemoryCommand()
    {
        AnsiConsole.MarkupLine($"\n[purple]🧠 {Strings.LongTermMemory(_lang)}[/]");
        var memoryDir = Path.Combine(dotCraftPath, "memory");
        var memoryPath = Path.Combine(memoryDir, "MEMORY.md");

        if (!File.Exists(memoryPath))
        {
            AnsiConsole.MarkupLine($"[grey]{Strings.MemoryNotExists(_lang)}[/]");
            AnsiConsole.MarkupLine($"[grey]{Strings.ExpectedPath(_lang)}: {memoryPath.EscapeMarkup()}[/]");
        }
        else
        {
            var content = File.ReadAllText(memoryPath, Encoding.UTF8);

            if (string.IsNullOrWhiteSpace(content))
            {
                AnsiConsole.MarkupLine($"[grey]{Strings.MemoryEmpty(_lang)}[/]");
            }
            else
            {
                var panel = new Panel(Markup.Escape(content))
                {
                    Header = new PanelHeader($"[purple]🗃️ {memoryPath.EscapeMarkup()}[/]"),
                    Border = BoxBorder.Rounded,
                    BorderStyle = new Style(Color.Purple),
                    Expand = true
                };
                AnsiConsole.Write(panel);
            }
        }

        // Show HISTORY.md summary
        var historyPath = Path.Combine(memoryDir, "HISTORY.md");
        if (File.Exists(historyPath))
        {
            var historyInfo = new FileInfo(historyPath);
            var historyContent = File.ReadAllText(historyPath, Encoding.UTF8);
            var entryCount = historyContent.Split("\n\n", StringSplitOptions.RemoveEmptyEntries).Length;

            // Show last entry as preview
            var entries = historyContent.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
            var lastEntry = entries.Length > 0 ? entries[^1].Trim() : string.Empty;
            var preview = lastEntry.Length > 200 ? lastEntry[..200] + "..." : lastEntry;

            var historyPanel = new Panel(
                string.IsNullOrWhiteSpace(preview) ? "[grey](no entries)[/]" : Markup.Escape(preview))
            {
                Header = new PanelHeader($"[blue]📜 HISTORY.md — {entryCount} entries, {historyInfo.Length / 1024.0:F1} KB[/]"),
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.Blue),
                Expand = true
            };
            AnsiConsole.Write(historyPanel);
            AnsiConsole.MarkupLine($"[grey]  Search: grep -i \"keyword\" \"{historyPath.EscapeMarkup()}\"[/]");
        }

        AnsiConsole.WriteLine();
    }

    private void HandleCommandsCommand()
    {
        if (customCommandLoader == null)
        {
            AnsiConsole.MarkupLine("[grey]Custom commands are not available.[/]\n");
            return;
        }

        var commands = customCommandLoader.ListCommands();
        if (commands.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No custom commands found.[/]");
            AnsiConsole.MarkupLine($"[grey]Place .md files in: {customCommandLoader.WorkspaceCommandsPath.EscapeMarkup()}[/]\n");
            return;
        }

        var table = new Table();
        table.Border(TableBorder.Rounded);
        table.AddColumn("[grey]Command[/]");
        table.AddColumn("[grey]Description[/]");
        table.AddColumn("[grey]Source[/]");

        foreach (var cmd in commands)
        {
            var desc = string.IsNullOrWhiteSpace(cmd.Description) ? "[grey]-[/]" : cmd.Description.EscapeMarkup();
            var source = cmd.Source == "workspace" ? "[green]workspace[/]" : "[blue]user[/]";
            table.AddRow($"[cyan]/{cmd.Name.EscapeMarkup()}[/]", desc, source);
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"[grey]Workspace: {customCommandLoader.WorkspaceCommandsPath.EscapeMarkup()}[/]");
        AnsiConsole.MarkupLine($"[grey]User: {customCommandLoader.UserCommandsPath.EscapeMarkup()}[/]");
        AnsiConsole.WriteLine();
    }

    private void HandleDebugCommand()
    {
        var newState = DebugModeService.Toggle();
        var statusMsg = newState 
            ? $"[green]✓[/] {Strings.DebugEnabled(_lang)}" 
            : $"[green]✓[/] {Strings.DebugDisabled(_lang)}";
        
        AnsiConsole.MarkupLine($"\n{statusMsg}\n");
    }

    private void HandleLanguageCommand()
    {
        var newLang = _lang.ToggleLanguage();
        var langName = newLang == Language.Chinese 
            ? Strings.LanguageChinese(_lang) 
            : Strings.LanguageEnglish(_lang);
        AnsiConsole.MarkupLine($"\n[green]✓[/] {Strings.LanguageSwitched(_lang)}: [cyan]{langName}[/]\n");
    }

    private async Task HandleHeartbeatCommandAsync(string input)
    {
        if (heartbeatService == null)
        {
            AnsiConsole.MarkupLine($"[yellow]{Strings.HeartbeatUnavailable(_lang)}[/]\n");
            return;
        }

        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var subCmd = parts.Length > 1 ? parts[1].ToLowerInvariant() : "trigger";

        switch (subCmd)
        {
            case "trigger":
                AnsiConsole.MarkupLine($"[blue]{Strings.TriggeringHeartbeat(_lang)}[/]");
                var result = await heartbeatService.TriggerNowAsync();
                if (result != null)
                    AnsiConsole.MarkupLine($"[green]{Strings.HeartbeatResult(_lang)}：[/] {Markup.Escape(result)}");
                else
                    AnsiConsole.MarkupLine($"[grey]{Strings.HeartbeatNoResponse(_lang)}[/]");
                break;
            default:
                AnsiConsole.MarkupLine($"[yellow]{Strings.HeartbeatUsage(_lang)}[/]");
                break;
        }
        AnsiConsole.WriteLine();
    }

    private void HandleCronCommand(string input)
    {
        if (cronService == null)
        {
            AnsiConsole.MarkupLine($"[yellow]{Strings.CronUnavailable(_lang)}[/]\n");
            return;
        }

        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var subCmd = parts.Length > 1 ? parts[1].ToLowerInvariant() : "list";

        switch (subCmd)
        {
            case "list":
            {
                var jobs = cronService.ListJobs(includeDisabled: true);
                if (jobs.Count == 0)
                {
                    AnsiConsole.MarkupLine($"[grey]{Strings.NoCronJobs(_lang)}[/]");
                }
                else
                {
                    var table = new Table();
                    table.Border(TableBorder.Rounded);
                    table.AddColumn(Strings.CronColId(_lang));
                    table.AddColumn(Strings.CronColName(_lang));
                    table.AddColumn(Strings.CronColSchedule(_lang));
                    table.AddColumn(Strings.CronColStatus(_lang));
                    table.AddColumn(Strings.CronColNextRun(_lang));

                    foreach (var job in jobs)
                    {
                        var schedDesc = job.Schedule.Kind switch
                        {
                            "at" when job.Schedule.AtMs.HasValue =>
                                $"{Strings.CronExecuteOnce(_lang)} {DateTimeOffset.FromUnixTimeMilliseconds(job.Schedule.AtMs.Value):u} {Strings.CronExecuteOnceSuffix(_lang)}",
                            "every" when job.Schedule.EveryMs.HasValue =>
                                $"{Strings.CronEvery(_lang)} {TimeSpan.FromMilliseconds(job.Schedule.EveryMs.Value)}",
                            _ => job.Schedule.Kind
                        };
                        var next = job.State.NextRunAtMs.HasValue
                            ? DateTimeOffset.FromUnixTimeMilliseconds(job.State.NextRunAtMs.Value).ToString("u")
                            : "-";
                        var status = job.Enabled ? $"[green]{Strings.CronEnabled(_lang)}[/]" : $"[grey]{Strings.CronDisabled(_lang)}[/]";
                        table.AddRow(
                            Markup.Escape(job.Id),
                            Markup.Escape(job.Name),
                            Markup.Escape(schedDesc),
                            status,
                            Markup.Escape(next));
                    }
                    AnsiConsole.Write(table);
                }
                break;
            }
            case "remove":
            {
                if (parts.Length < 3)
                {
                    AnsiConsole.MarkupLine($"[yellow]{Strings.CronRemoveUsage(_lang)}[/]");
                    break;
                }
                var jobId = parts[2];
                if (cronService.RemoveJob(jobId))
                    AnsiConsole.MarkupLine($"[green]{Strings.CronJobDeleted(_lang)} '{Markup.Escape(jobId)}' {Strings.CronJobDeletedSuffix(_lang)}[/]");
                else
                    AnsiConsole.MarkupLine($"[yellow]{Strings.CronJobNotFound(_lang)} '{Markup.Escape(jobId)}'。[/]");
                break;
            }
            case "enable":
            case "disable":
            {
                if (parts.Length < 3)
                {
                    AnsiConsole.MarkupLine($"[yellow]{Strings.CronToggleUsage(_lang)}：/cron {subCmd} <jobId>[/]");
                    break;
                }
                var jobId = parts[2];
                var enabled = subCmd == "enable";
                var job = cronService.EnableJob(jobId, enabled);
                if (job != null)
                    AnsiConsole.MarkupLine($"[green]{Strings.CronJobDeleted(_lang)} '{Markup.Escape(jobId)}' {(enabled ? Strings.CronJobEnabled(_lang) : Strings.CronJobDisabled(_lang))}[/]");
                else
                    AnsiConsole.MarkupLine($"[yellow]{Strings.CronJobNotFound(_lang)} '{Markup.Escape(jobId)}'。[/]");
                break;
            }
            default:
                AnsiConsole.MarkupLine($"[yellow]{Strings.CronUsage(_lang)}[/]");
                break;
        }
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Runs an interactive input turn via <see cref="ISessionService.SubmitInputAsync"/>.
    /// If <see cref="_currentThreadId"/> is null (lazy-init pending), creates the thread first.
    /// Handles approval events inline by pausing the renderer and prompting the user.
    /// </summary>
    private async Task<bool> RunSessionInputAsync(string userInput, CancellationToken cancellationToken)
    {
        // Materialize the thread on first user input (lazy creation).
        if (_currentThreadId == null)
        {
            var newThread = await sessionService.CreateThreadAsync(_cliIdentity, ct: cancellationToken);
            _currentThreadId = newThread.Id;
            _currentSessionId = newThread.Id;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var interrupt = new InterruptHandler(cts);
        var token = cts.Token;

        try
        {
            var tokenTracker = agentFactory?.GetOrCreateTokenTracker(_currentSessionId);
            using var renderer = new AgentRenderer(tokenTracker);
            await renderer.StartAsync(token);
            await renderer.SendEventAsync(RenderEvent.StreamStart(), token);
            ConsoleApprovalService.SetRenderControl(renderer);
            if (hookRunner != null)
                hookRunner.DebugLogger = renderer.TryEnqueueDebug;

            try
            {
                var handler = new SessionEventHandler
                {
                    OnTextDelta = text => renderer.SendEventAsync(RenderEvent.Response(text), token).AsTask(),
                    OnReasoningDelta = reasoning =>
                        renderer.SendEventAsync(RenderEvent.Thinking("💭", "Thinking", reasoning), token).AsTask(),
                    OnToolStarted = async (name, icon, formatted, callId) =>
                    {
                        string? argsJson = null;
                        await renderer.SendEventAsync(
                            RenderEvent.ToolStarted(icon, name, string.Empty, argsJson, formatted, callId: callId),
                            token);
                    },
                    OnToolCompleted = (callId, result) =>
                        renderer.SendEventAsync(
                            RenderEvent.ToolCompleted(null, null, string.Empty, result, callId: callId),
                            token).AsTask(),
                    OnApprovalRequested = async req =>
                    {
                        ApprovalOption choice;
                        if (req.ApprovalType == "shell")
                        {
                            choice = await renderer.ExecuteWhilePausedAsync(
                                () => ApprovalPrompt.RequestShellApproval(req.Operation, req.Target));
                        }
                        else
                        {
                            choice = await renderer.ExecuteWhilePausedAsync(
                                () => ApprovalPrompt.RequestFileApproval(req.Operation, req.Target));
                        }
                        return choice switch
                        {
                            ApprovalOption.Once => SessionApprovalDecision.AcceptOnce,
                            ApprovalOption.Session => SessionApprovalDecision.AcceptForSession,
                            ApprovalOption.Always => SessionApprovalDecision.AcceptAlways,
                            _ => SessionApprovalDecision.Reject
                        };
                    },
                    OnTurnCompleted = async usage =>
                    {
                        if (usage != null)
                        {
                            await renderer.SendEventAsync(
                                RenderEvent.TokenUsage(usage.InputTokens, usage.OutputTokens, usage.TotalTokens),
                                token);
                            tokenUsageStore?.Record(new TokenUsageRecord
                            {
                                Channel = "cli",
                                UserId = "local",
                                DisplayName = "CLI",
                                InputTokens = usage.InputTokens,
                                OutputTokens = usage.OutputTokens
                            });
                        }
                        await renderer.SendEventAsync(RenderEvent.Completed(string.Empty), token);
                    },
                    OnTurnFailed = async errMsg =>
                    {
                        await renderer.SendEventAsync(RenderEvent.ErrorEvent(errMsg), token);
                        await renderer.SendEventAsync(RenderEvent.Completed(string.Empty), token);
                    }
                };

                await handler.ProcessAsync(
                    sessionService.SubmitInputAsync(_currentThreadId!, userInput, ct: token),
                    (thId, tid, rid, ok) => sessionService.ResolveApprovalAsync(thId, tid, rid, ok, token),
                    token);
            }
            finally
            {
                ConsoleApprovalService.SetRenderControl(null);
                if (hookRunner != null)
                    hookRunner.DebugLogger = null;
            }

            await renderer.StopAsync();
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            AnsiConsole.MarkupLine($"\n[yellow]{Strings.AgentInterrupted(_lang)}[/]");
            return false;
        }
        catch (Exception ex)
        {
            MessageFormatter.Error(ex.Message);
            return false;
        }
    }

}
