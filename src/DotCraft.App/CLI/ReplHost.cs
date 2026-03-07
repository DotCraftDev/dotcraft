using System.Text;
using DotCraft.Agents;
using DotCraft.CLI.Rendering;
using DotCraft.Commands;
using DotCraft.Commands.Custom;
using DotCraft.Diagnostics;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Cron;
using DotCraft.DashBoard;
using DotCraft.Heartbeat;
using DotCraft.Hooks;
using DotCraft.Localization;
using DotCraft.Mcp;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Skills;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Spectre.Console;

namespace DotCraft.CLI;

public sealed class ReplHost(AIAgent agent, SessionStore sessionStore, SkillsLoader skillsLoader, 
    string workspacePath = "", string dotCraftPath = "", AppConfig? config = null, 
    HeartbeatService? heartbeatService = null, CronService? cronService = null,
    AgentFactory? agentFactory = null, McpClientManager? mcpClientManager = null,
    string? dashBoardUrl = null,
    LanguageService? languageService = null, TokenUsageStore? tokenUsageStore = null,
    CustomCommandLoader? customCommandLoader = null,
    AgentModeManager? modeManager = null,
    PlanStore? planStore = null,
    HookRunner? hookRunner = null)
{
    private readonly AppConfig _config = config ?? new AppConfig();
    private readonly LanguageService _lang = languageService ?? new LanguageService();
    private readonly AgentModeManager _modeManager = modeManager ?? new AgentModeManager();

    private string _currentSessionId = string.Empty;
    
    private AgentSession _agentSession = null!;
    private AIAgent _currentAgent = agent;

    /// <summary>Command history shared across all line-editor instances.</summary>
    private readonly List<string> _inputHistory = new();

    /// <summary>
    /// Buffer snapshot saved when the user presses Tab to switch mode,
    /// so it can be restored on the next prompt.
    /// </summary>
    private List<string>? _pendingBuffer;

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        // Generate a new session ID on startup
        _currentSessionId = SessionStore.GenerateSessionId();
        ShowWelcomeScreen(_currentSessionId);

        // Load or create session
        _agentSession = await sessionStore.LoadOrCreateAsync(_currentAgent, _currentSessionId, cancellationToken);

        // Run SessionStart hooks
        await RunSessionStartHooksAsync(_currentSessionId, cancellationToken);

        while (true)
        {
            var (result, input) = await ReadInputAsync(cancellationToken);
            if (result == LineEditResult.ModeSwitch)
            {
                // Tab was pressed -- mode already toggled, just re-loop for new prompt
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

            // Run PrePrompt hooks (can block the prompt)
            if (hookRunner != null)
            {
                var prePromptInput = new HookInput { SessionId = _currentSessionId, Prompt = agentInput };
                var prePromptResult = await hookRunner.RunAsync(HookEvent.PrePrompt, prePromptInput, cancellationToken);
                if (prePromptResult.Blocked)
                {
                    AnsiConsole.MarkupLine($"[yellow]Prompt blocked by hook: {Markup.Escape(prePromptResult.BlockReason ?? "no reason")}[/]");
                    continue;
                }
            }

            if (await RunStreamingAsync(agentInput, _agentSession, cancellationToken))
            {
                // Run Stop hooks after agent finishes responding
                await RunStopHooksAsync(_currentSessionId, null, cancellationToken);

                await TryCompactContextAsync(_currentSessionId, _agentSession, cancellationToken);
                var consolidationTask = agentFactory?.TryConsolidateMemory(_agentSession, _currentSessionId);
                var saveTask = sessionStore.SaveAsync(_currentAgent, _agentSession, _currentSessionId, cancellationToken);
                if (consolidationTask != null)
                {
                    await AnsiConsole.Status()
                        .Spinner(Spinner.Known.Dots)
                        .SpinnerStyle(new Style(Color.Grey))
                        .StartAsync(Strings.MemoryConsolidating(_lang), async _ =>
                        {
                            await Task.WhenAll(consolidationTask, saveTask);
                        });
                }
                else
                    await saveTask;
            }
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
    /// Runs Stop hooks after agent finishes responding.
    /// </summary>
    private async Task RunStopHooksAsync(string sessionId, string? response, CancellationToken ct)
    {
        if (hookRunner == null) return;
        var input = new HookInput { SessionId = sessionId, Response = response };
        await hookRunner.RunAsync(HookEvent.Stop, input, ct);
    }

    /// <summary>
    /// Reads a line of input using the <see cref="LineEditor"/>.
    /// Supports cursor movement, command history, multi-line input (Ctrl+Enter),
    /// word-wrap across terminal width, command hints, and Tab mode-switch.
    /// </summary>
    private async Task<(LineEditResult Result, string Text)> ReadInputAsync(CancellationToken cancellationToken)
    {
        var promptWidth = PrintPrompt();

        var editor = new LineEditor(_inputHistory, promptWidth, GetCommandHints);

        // Restore buffer that was saved during a previous Tab mode-switch
        if (_pendingBuffer is { Count: > 0 })
        {
            editor.SetInitialBuffer(_pendingBuffer);
            _pendingBuffer = null;
        }

        var (result, text) = await editor.ReadLineAsync(cancellationToken);

        if (result == LineEditResult.ModeSwitch)
        {
            // Save current buffer so it survives the mode switch
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
            var shortId = _currentSessionId.Length > 8
                ? _currentSessionId[^8..]
                : _currentSessionId;
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
            var shortId = _currentSessionId.Length > 8
                ? _currentSessionId[^8..]
                : _currentSessionId;
            displayWidth += LineEditor.GetDisplayWidth($"({shortId}) ");
        }
        displayWidth += LineEditor.GetDisplayWidth($"{emoji} ❯ ");
        return displayWidth;
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
                    result.Add((fullName, cmd.Description ?? string.Empty));
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

        _currentAgent = agentFactory.CreateAgentForMode(_modeManager.CurrentMode, _modeManager);
    }

    private async Task LoadSessionAsync(string newSessionId, CancellationToken cancellationToken)
    {
        try
        {
            // Load new session
            _agentSession = await sessionStore.LoadOrCreateAsync(_currentAgent, newSessionId, cancellationToken);
            _currentSessionId = newSessionId;

            // Run SessionStart hooks for loaded session
            await RunSessionStartHooksAsync(_currentSessionId, cancellationToken);

            // Refresh display
            AnsiConsole.Clear();
            ShowWelcomeScreen(_currentSessionId);
            
            AnsiConsole.MarkupLine($"[green]✓[/] {Strings.SessionLoaded(_lang)}：[cyan]{newSessionId.EscapeMarkup()}[/]");
            AnsiConsole.WriteLine();
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] {Strings.SessionLoadFailed(_lang)}：{ex.Message.EscapeMarkup()}");
            AnsiConsole.WriteLine();
        }
    }

    /// <summary>
    /// Create a new session.
    /// </summary>
    private async Task NewSession(CancellationToken cancellationToken)
    {
        try
        {
            // Generate new session ID and create session
            var newSessionId = SessionStore.GenerateSessionId();
            _agentSession = await sessionStore.LoadOrCreateAsync(_currentAgent, newSessionId, cancellationToken);
            _currentSessionId = newSessionId;

            // Run SessionStart hooks for new session
            await RunSessionStartHooksAsync(_currentSessionId, cancellationToken);

            // Refresh display
            AnsiConsole.Clear();
            ShowWelcomeScreen(_currentSessionId);

            AnsiConsole.MarkupLine($"[green]✓[/] {Strings.SessionCreated(_lang)}：[cyan]{newSessionId.EscapeMarkup()}[/]");
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

            // Delete session and associated plan file
            var sessionDeleted = sessionStore.Delete(sessionId);
            planStore?.DeletePlan(sessionId);

            if (sessionDeleted)
            {
                AnsiConsole.MarkupLine($"[green]✓[/] {Strings.SessionDeleted(_lang)}：[cyan]{sessionId.EscapeMarkup()}[/]");
            }
            else
            {
                AnsiConsole.MarkupLine($"[yellow]{Strings.SessionNotFound(_lang)}：{sessionId.EscapeMarkup()}[/]");
            }

            // If deleted current session, create a new session
            if (wasCurrent)
            {
                _currentSessionId = SessionStore.GenerateSessionId();
                _agentSession = await sessionStore.LoadOrCreateAsync(_currentAgent, _currentSessionId, CancellationToken.None);
                AnsiConsole.MarkupLine($"[grey]→ {Strings.SessionNewCreated(_lang)}：{_currentSessionId.EscapeMarkup()}[/]");
            }

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
        "/config", "/debug", "/heartbeat", "/cron", "/lang", "/commands",
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
                var sessions = sessionStore.ListSessions();
                var selectedSession = SessionPrompt.SelectSessionToLoad(sessions, _currentSessionId);
                if (selectedSession != null)
                {
                    await LoadSessionAsync(selectedSession, CancellationToken.None);
                }
                return (true, false, null);

            case "/delete":
                var sessionsToDelete = sessionStore.ListSessions();
                var sessionToDelete = SessionPrompt.SelectSessionToDelete(sessionsToDelete, _currentSessionId);
                if (sessionToDelete != null)
                {
                    if (SessionPrompt.ConfirmDelete(sessionToDelete, sessionToDelete == _currentSessionId))
                    {
                        await DeleteSession(sessionToDelete);
                        AnsiConsole.Clear();
                        ShowWelcomeScreen(_currentSessionId);
                    }
                }
                return (true, false, null);

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
                var sessionList = sessionStore.ListSessions();
                StatusPanel.ShowSessionsTable(sessionList, _lang);
                return (true, false, null);

            case "/memory":
                HandleMemoryCommand();
                return (true, false, null);

            case "/config":
                HandleConfigCommand();
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

    private void HandleConfigCommand()
    {
        AnsiConsole.MarkupLine($"\n[blue]📋 {Strings.CurrentConfig(_lang)}[/]");
        var configPath = Path.Combine(dotCraftPath, "config.json");

        // Show current config
        var table = new Table();
        table.Border(TableBorder.Rounded);
        table.AddColumn($"[grey]{Strings.ConfigItem(_lang)}[/]");
        table.AddColumn("[grey]值[/]");

        table.AddRow("API Key", string.IsNullOrWhiteSpace(_config.ApiKey) ? $"[red]{Strings.NotConfigured(_lang)}[/]" : $"[green]{Strings.Configured(_lang)}[/]");
        table.AddRow("Model", _config.Model.EscapeMarkup());
        table.AddRow("Endpoint", _config.EndPoint.EscapeMarkup());
        table.AddRow("System Instructions", 
            _config.SystemInstructions.Length > 50 
                ? _config.SystemInstructions.Substring(0, 50) + "..." 
                : _config.SystemInstructions.EscapeMarkup());

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"\n[grey]{Strings.ConfigFilePath(_lang)}: {configPath.EscapeMarkup()}[/]");

        // Offer to edit
        AnsiConsole.MarkupLine($"\n[yellow]{Strings.ConfigTip(_lang)}[/]: {Strings.ConfigEditTip(_lang)}");
        AnsiConsole.WriteLine();
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

    private async Task TryCompactContextAsync(string sessionId, AgentSession session, CancellationToken cancellationToken)
    {
        if (agentFactory?.Compactor == null || agentFactory.MaxContextTokens <= 0)
            return;

        var tracker = agentFactory.GetOrCreateTokenTracker(sessionId);
        if (tracker.LastInputTokens < agentFactory.MaxContextTokens)
            return;

        AnsiConsole.MarkupLine($"[yellow]{Strings.ContextLimitReached(_lang)}[/]");
        var compacted = await agentFactory.Compactor.TryCompactAsync(session, cancellationToken);
        if (compacted)
        {
            tracker.Reset();
            AnsiConsole.MarkupLine($"[green]{Strings.ContextCompacted(_lang)}[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[grey]{Strings.ContextCompactSkipped(_lang)}[/]");
        }
    }

    private async Task<bool> RunStreamingAsync(
        string userInput,
        AgentSession session,
        CancellationToken cancellationToken)
    {
        try
        {
            // Create renderer and start it
            using var renderer = new AgentRenderer();
            await renderer.StartAsync(cancellationToken);

            // Set render control for approval service (thread-local)
            ConsoleApprovalService.SetRenderControl(renderer);

            // Route hook debug output through the renderer so [Hooks] lines are
            // buffered during Status spinner sessions and printed cleanly afterward.
            if (hookRunner != null)
                hookRunner.DebugLogger = renderer.TryEnqueueDebug;

            try
            {
                var tokenTracker = agentFactory?.GetOrCreateTokenTracker(_currentSessionId);

                TracingChatClient.CurrentSessionKey = _currentSessionId;
                TracingChatClient.ResetCallState(_currentSessionId);
                long inputTokens = 0, outputTokens = 0;

                // Get streaming updates from agent
                var stream = _currentAgent.RunStreamingAsync(RuntimeContextBuilder.AppendTo(userInput), session, cancellationToken: cancellationToken);

                // Adapt stream to render events
                var events = StreamAdapter.AdaptAsync(WrapStream(stream), cancellationToken, tokenTracker);
                
                // Consume events through renderer
                await renderer.ConsumeEventsAsync(events, cancellationToken);
                
                // Wait for renderer to finish
                await renderer.StopAsync();

                if (inputTokens > 0 || outputTokens > 0)
                {
                    tokenUsageStore?.Record(new TokenUsageRecord
                    {
                        Source = TokenUsageSource.Cli,
                        UserId = "local",
                        DisplayName = "CLI",
                        InputTokens = inputTokens,
                        OutputTokens = outputTokens
                    });
                }

                return true;

                async IAsyncEnumerable<AgentResponseUpdate> WrapStream(IAsyncEnumerable<AgentResponseUpdate> source)
                {
                    await foreach (var update in source.WithCancellation(cancellationToken))
                    {
                        foreach (var content in update.Contents)
                        {
                            if (content is UsageContent usage)
                            {
                                if (usage.Details.InputTokenCount.HasValue)
                                    inputTokens = usage.Details.InputTokenCount.Value;
                                if (usage.Details.OutputTokenCount.HasValue)
                                    outputTokens = usage.Details.OutputTokenCount.Value;
                            }
                        }
                        yield return update;
                    }
                }
            }
            finally
            {
                TracingChatClient.ResetCallState(_currentSessionId);
                TracingChatClient.CurrentSessionKey = null;
                // Clear render control
                ConsoleApprovalService.SetRenderControl(null);
                // Restore hook debug logger to default (Console.Error.WriteLine fallback)
                if (hookRunner != null)
                    hookRunner.DebugLogger = null;
            }
        }
        catch (Exception ex)
        {
            MessageFormatter.Error(ex.Message);
            return false;
        }
    }
}
