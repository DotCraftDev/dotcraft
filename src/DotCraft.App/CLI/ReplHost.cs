using System.Text;
using DotCraft.Agents;
using DotCraft.CLI.Rendering;
using DotCraft.Commands;
using DotCraft.Commands.Custom;
using DotCraft.Diagnostics;
using DotCraft.Cron;
using DotCraft.Heartbeat;
using DotCraft.Hooks;
using DotCraft.Localization;
using DotCraft.Mcp;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.Skills;
using Spectre.Console;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotCraft.CLI;

public sealed class ReplHost(
    SkillsLoader skillsLoader,
    ICliSession session,
    string workspacePath = "", string dotCraftPath = "",
    HeartbeatService? heartbeatService = null, CronService? cronService = null,
    McpClientManager? mcpClientManager = null,
    string? dashBoardUrl = null,
    CustomCommandLoader? customCommandLoader = null,
    HookRunner? hookRunner = null,
    CliBackendInfo? backendInfo = null,
    AppServerWireClient? wireClient = null)
{
    private readonly AgentModeManager _modeManager = new();

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

    /// <summary>
    /// The <see cref="LineEditor"/> currently blocking on user input, or null when no prompt is active.
    /// Used by <see cref="WriteExternalOutput"/> to suspend and restore the prompt around out-of-band output.
    /// </summary>
    private LineEditor? _activeEditor;

    private readonly Lock _outputLock = new();
    private readonly bool _modelCatalogSupported = backendInfo?.ModelCatalogManagement ?? false;
    private readonly bool _workspaceConfigSupported = backendInfo?.WorkspaceConfigManagement ?? false;
    private Task<ModelCatalogSnapshot>? _modelCatalogLoadTask;
    private ModelCatalogSnapshot? _modelCatalogCache;
    private string _workspaceModel = "Default";
    private string? _threadModelOverride;

    private sealed record ModelCatalogSnapshot(
        bool Success,
        IReadOnlyList<string> Models,
        string? ErrorMessage);

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        // Defer thread creation until the user sends a first message.
        // _currentThreadId stays null until RunSessionInputAsync materializes the thread.
        _currentThreadId = null;
        _currentSessionId = string.Empty;
        _threadModelOverride = null;
        _workspaceModel = ReadWorkspaceModelFromConfig();

        StartModelCatalogPreload();

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

        AnsiConsole.MarkupLine($"\n[blue]👋 {Strings.Goodbye}[/]");
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
        lock (_outputLock) _activeEditor = editor;

        // Restore buffer that was saved during a previous Shift+Tab mode-switch
        if (_pendingBuffer is { Count: > 0 })
        {
            editor.SetInitialBuffer(_pendingBuffer);
            _pendingBuffer = null;
        }

        var (result, text) = await editor.ReadLineAsync(cancellationToken);
        lock (_outputLock) _activeEditor = null;

        if (result == LineEditResult.ModeSwitch)
        {
            // Save current buffer so it survives the Shift+Tab mode switch
            _pendingBuffer = new List<string>(editor.Buffer);
            var next = _modeManager.CurrentMode == AgentMode.Plan
                ? AgentMode.Agent
                : AgentMode.Plan;
            await SwitchToModeAsync(next);
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

    /// <summary>
    /// Suspends the active prompt and input area, executes <paramref name="writeAction"/>
    /// to print out-of-band output (e.g. a server notification), then re-prints the prompt
    /// and restores the user's in-progress input buffer.
    ///
    /// Safe to call from any thread while the REPL is waiting for user input.
    /// When no prompt is currently active, the output is printed directly.
    /// </summary>
    public void WriteExternalOutput(Action writeAction)
    {
        lock (_outputLock)
        {
            var editor = _activeEditor;
            if (editor != null)
            {
                editor.SuspendDisplay();
                writeAction();
                AnsiConsole.WriteLine();
                PrintPrompt();
                editor.ResumeDisplay();
            }
            else
            {
                writeAction();
                AnsiConsole.WriteLine();
            }
        }
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
        StatusPanel.ShowWelcome(currentSessionId, dashBoardUrl, backendInfo, GetEffectiveModelDisplay());
    }

    private async Task SwitchToModeAsync(AgentMode mode)
    {
        if (_modeManager.CurrentMode == mode)
        {
            AnsiConsole.MarkupLine($"[grey]Already in {mode.ToString().ToLower()} mode.[/]\n");
            return;
        }

        _modeManager.SwitchMode(mode);

        if (_currentThreadId != null)
        {
            // SessionService.SetThreadModeAsync rebuilds the agent with a per-thread
            // ModeManager, so no local rebuild is needed when a thread exists.
            await session.SetThreadModeAsync(_currentThreadId, mode.ToString().ToLowerInvariant());
        }
        var (emoji, color) = mode == AgentMode.Plan ? ("📋", "yellow") : ("⚡", "green");
        var rule = new Rule($"[{color}]{emoji} {mode.ToString().ToLower()}[/]");
        rule.RuleStyle($"{color} dim");
        AnsiConsole.Write(rule);
    }

    private async Task LoadSessionAsync(string newSessionId, CancellationToken cancellationToken)
    {
        try
        {
            var thread = await session.ResumeThreadAsync(newSessionId, cancellationToken);
            _currentThreadId = newSessionId;
            _currentSessionId = newSessionId;
            _threadModelOverride = thread.Configuration?.Model;

            // Run SessionStart hooks for loaded session
            await RunSessionStartHooksAsync(_currentSessionId, cancellationToken);

            // Refresh display
            AnsiConsole.Clear();
            ShowWelcomeScreen(_currentSessionId);

            AnsiConsole.MarkupLine($"[green]✓[/] {Strings.SessionLoaded}：[cyan]{newSessionId.EscapeMarkup()}[/]");
            AnsiConsole.WriteLine();

            // Print conversation history
            if (thread.Turns is { Count: > 0 })
                SessionHistoryPrinter.Print(thread);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] {Strings.SessionLoadFailed}：{ex.Message.EscapeMarkup()}");
            AnsiConsole.WriteLine();
        }
    }

    /// <summary>
    /// Create a new session by resetting the current identity to a fresh thread.
    /// </summary>
    private async Task NewSession(CancellationToken cancellationToken)
    {
        try
        {
            _currentThreadId = await session.ResetConversationAsync(_cliIdentity, _currentThreadId, cancellationToken);
            _currentSessionId = _currentThreadId;
            _threadModelOverride = null;

            await RunSessionStartHooksAsync(_currentSessionId, cancellationToken);

            AnsiConsole.Clear();
            ShowWelcomeScreen(_currentSessionId);
            AnsiConsole.MarkupLine($"[green]✓[/] {Strings.SessionCreated}");
            AnsiConsole.WriteLine();
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] {Strings.SessionCreateFailed}：{ex.Message.EscapeMarkup()}");
            AnsiConsole.WriteLine();
        }
    }

    private async Task DeleteSession(string sessionId)
    {
        try
        {
            var wasCurrent = sessionId == _currentSessionId;

            await session.ArchiveThreadAsync(sessionId);
            AnsiConsole.MarkupLine($"[green]✓[/] {Strings.SessionDeleted}：[cyan]{sessionId.EscapeMarkup()}[/]");

            if (wasCurrent)
            {
                // Lazy: reset to pending state; thread is created on next input.
                _currentThreadId = null;
                _currentSessionId = string.Empty;
                _threadModelOverride = null;
                AnsiConsole.MarkupLine($"[grey]→ {Strings.SessionCreated}[/]");
            }

            AnsiConsole.WriteLine();
        }
        catch (KeyNotFoundException)
        {
            AnsiConsole.MarkupLine($"[yellow]{Strings.SessionNotFound}：{sessionId.EscapeMarkup()}[/]");
            AnsiConsole.WriteLine();
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] {Strings.SessionDeleteFailed}：{ex.Message.EscapeMarkup()}");
            AnsiConsole.WriteLine();
        }
    }

    private static readonly string[] KnownCommands =
    [
        "/exit", "/help", "/clear", "/new", "/load", "/delete",
        "/init", "/skills", "/mcp", "/sessions", "/memory",
        "/debug", "/heartbeat", "/cron", "/lang", "/commands", "/model",
        "/plan", "/agent"
    ];

    private async Task<(bool Handled, bool ShouldExit, string? ExpandedPrompt)> HandleCommand(string input)
    {
        switch (input.ToLowerInvariant())
        {
            case "/exit":
                return (true, true, null);

            case "/help":
                StatusPanel.ShowHelp();
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
                var threads = await session.FindThreadsAsync(_cliIdentity);
                var selectedThread = SessionPrompt.SelectThreadToLoad(threads, _currentThreadId);
                if (selectedThread != null)
                    await LoadSessionAsync(selectedThread, CancellationToken.None);
                return (true, false, null);
            }

            case "/delete":
            {
                var threadsToDelete = await session.FindThreadsAsync(_cliIdentity);
                var threadToDelete = SessionPrompt.SelectThreadToDelete(threadsToDelete, _currentThreadId);
                if (threadToDelete != null)
                {
                    if (SessionPrompt.ConfirmDelete(threadToDelete, threadToDelete == _currentThreadId))
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
                StatusPanel.ShowSkillsTable(allSkills, skillsLoader.WorkspaceSkillsPath, skillsLoader.UserSkillsPath);
                return (true, false, null);

            case "/mcp":
                StatusPanel.ShowMcpServersTable(mcpClientManager);
                return (true, false, null);

            case "/sessions":
            {
                var threadList = await session.FindThreadsAsync(_cliIdentity);
                StatusPanel.ShowThreadsTable(threadList);
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

            case "/model":
                await HandleModelCommandAsync(input);
                return (true, false, null);

            case "/plan":
                await SwitchToModeAsync(AgentMode.Plan);
                return (true, false, null);

            case "/agent":
                await SwitchToModeAsync(AgentMode.Agent);
                return (true, false, null);
        }

        if (input.StartsWith("/heartbeat", StringComparison.OrdinalIgnoreCase))
        {
            await HandleHeartbeatCommandAsync(input);
            return (true, false, null);
        }

        if (input.StartsWith("/model ", StringComparison.OrdinalIgnoreCase))
        {
            await HandleModelCommandAsync(input);
            return (true, false, null);
        }

        if (input.StartsWith("/cron", StringComparison.OrdinalIgnoreCase))
        {
            await HandleCronCommandAsync(input);
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
            var msg = CommandHelper.FormatUnknownCommandMessage(input, KnownCommands);
            AnsiConsole.MarkupLine($"[red]{msg.EscapeMarkup()}[/]");
            AnsiConsole.WriteLine();
            return (true, false, null);
        }

        return (false, false, null);
    }

    private void HandleInitCommand()
    {
        AnsiConsole.MarkupLine($"\n[blue]🚀 {Strings.InitWorkspace}[/]");
        AnsiConsole.MarkupLine($"[grey]{Strings.CurrentWorkspace}: {workspacePath.EscapeMarkup()}[/]");

        if (Directory.Exists(workspacePath))
        {
            var confirm = AnsiConsole.Prompt(
                new ConfirmationPrompt(Strings.WorkspaceExists)
                {
                    DefaultValue = false
                });

            if (!confirm)
            {
                AnsiConsole.MarkupLine($"[grey]{Strings.InitCancelled}[/]\n");
                return;
            }
        }

        var result = InitHelper.InitializeWorkspace(workspacePath);

        if (result == 0)
        {
            AnsiConsole.MarkupLine($"[green]✓[/] {Strings.InitComplete}\n");
        }
        else
        {
            AnsiConsole.MarkupLine($"[red]✗[/] {Strings.InitFailed}: {result}\n");
        }
    }

    private void HandleMemoryCommand()
    {
        AnsiConsole.MarkupLine($"\n[purple]🧠 {Strings.LongTermMemory}[/]");
        var memoryDir = Path.Combine(dotCraftPath, "memory");
        var memoryPath = Path.Combine(memoryDir, "MEMORY.md");

        if (!File.Exists(memoryPath))
        {
            AnsiConsole.MarkupLine($"[grey]{Strings.MemoryNotExists}[/]");
            AnsiConsole.MarkupLine($"[grey]{Strings.ExpectedPath}: {memoryPath.EscapeMarkup()}[/]");
        }
        else
        {
            var content = File.ReadAllText(memoryPath, Encoding.UTF8);

            if (string.IsNullOrWhiteSpace(content))
            {
                AnsiConsole.MarkupLine($"[grey]{Strings.MemoryEmpty}[/]");
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
            ? $"[green]✓[/] {Strings.DebugEnabled}"
            : $"[green]✓[/] {Strings.DebugDisabled}";

        AnsiConsole.MarkupLine($"\n{statusMsg}\n");
    }

    private void HandleLanguageCommand()
    {
        var lang = LanguageService.Current;
        var newLang = lang.CurrentLanguage == Language.Chinese ? Language.English : Language.Chinese;
        var configPath = Path.Combine(dotCraftPath, "config.json");
        lang.SetLanguageAndPersist(newLang, configPath);
        var langName = newLang == Language.Chinese
            ? Strings.LanguageChinese
            : Strings.LanguageEnglish;
        AnsiConsole.MarkupLine($"\n[green]✓[/] {Strings.LanguageSwitched}: [cyan]{langName}[/]\n");
    }

    private async Task HandleModelCommandAsync(string input)
    {
        if (wireClient != null && !_workspaceConfigSupported)
        {
            AnsiConsole.MarkupLine($"[yellow]{Strings.ModelFeatureUnavailable}[/]");
            AnsiConsole.WriteLine();
            return;
        }
        var parts = input.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var hasArg = parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]);
        if (hasArg)
        {
            await ApplyModelSelectionAsync(parts[1].Trim());
            AnsiConsole.WriteLine();
            return;
        }

        var snapshot = await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync(Strings.ModelLoading, async _ => await EnsureModelCatalogLoadedAsync());

        if (!snapshot.Success)
        {
            if (!string.IsNullOrWhiteSpace(snapshot.ErrorMessage))
            {
                AnsiConsole.MarkupLine($"[yellow]{Strings.ModelFetchFailed}: {snapshot.ErrorMessage!.EscapeMarkup()}[/]");
            }
            else
            {
                AnsiConsole.MarkupLine($"[yellow]{Strings.ModelFeatureUnavailable}[/]");
            }

            var manual = AnsiConsole.Prompt(
                new TextPrompt<string>(Strings.ModelManualPrompt)
                    .AllowEmpty());
            if (!string.IsNullOrWhiteSpace(manual))
                await ApplyModelSelectionAsync(manual.Trim());
            AnsiConsole.WriteLine();
            return;
        }

        var options = new List<string> { "Default" };
        options.AddRange(snapshot.Models.Where(m => !string.Equals(m, "Default", StringComparison.OrdinalIgnoreCase)));
        if (options.Count == 1)
        {
            AnsiConsole.MarkupLine($"[yellow]{Strings.ModelNoOptions}[/]");
            var manual = AnsiConsole.Prompt(
                new TextPrompt<string>(Strings.ModelManualPrompt)
                    .AllowEmpty());
            if (!string.IsNullOrWhiteSpace(manual))
                await ApplyModelSelectionAsync(manual.Trim());
            AnsiConsole.WriteLine();
            return;
        }

        var selected = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title(Strings.ModelSelectTitle)
                .PageSize(10)
                .AddChoices(options));

        await ApplyModelSelectionAsync(selected);
        AnsiConsole.WriteLine();
    }

    private void StartModelCatalogPreload()
    {
        if (!_modelCatalogSupported || wireClient == null || _modelCatalogCache != null || _modelCatalogLoadTask != null)
            return;
        _modelCatalogLoadTask = LoadModelCatalogAsync();
    }

    private async Task<ModelCatalogSnapshot> EnsureModelCatalogLoadedAsync()
    {
        if (_modelCatalogCache != null)
            return _modelCatalogCache;
        if (!_modelCatalogSupported || wireClient == null)
        {
            _modelCatalogCache = new ModelCatalogSnapshot(false, [], Strings.ModelFeatureUnavailable);
            return _modelCatalogCache;
        }

        _modelCatalogLoadTask ??= LoadModelCatalogAsync();
        var loaded = await _modelCatalogLoadTask;
        _modelCatalogCache ??= loaded;
        return _modelCatalogCache;
    }

    private async Task<ModelCatalogSnapshot> LoadModelCatalogAsync()
    {
        try
        {
            var result = await wireClient!.ModelListAsync();
            if (!result.Success)
            {
                return new ModelCatalogSnapshot(
                    false,
                    [],
                    string.IsNullOrWhiteSpace(result.ErrorMessage) ? result.ErrorCode : result.ErrorMessage);
            }

            var options = result.Models
                .Select(m => m.Id?.Trim())
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(m => m, StringComparer.OrdinalIgnoreCase)
                .Cast<string>()
                .ToList();

            return new ModelCatalogSnapshot(true, options, null);
        }
        catch (Exception ex)
        {
            return new ModelCatalogSnapshot(false, [], ex.Message);
        }
    }

    private async Task ApplyModelSelectionAsync(string modelInput)
    {
        var trimmed = modelInput.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return;

        var model = string.Equals(trimmed, "default", StringComparison.OrdinalIgnoreCase)
            ? null
            : trimmed;

        try
        {
            await PersistWorkspaceModelAsync(model);

            if (!string.IsNullOrEmpty(_currentThreadId) && wireClient != null)
            {
                await UpdateThreadModelAsync(_currentThreadId, model);
            }
            else
            {
                _threadModelOverride = null;
            }

            AnsiConsole.MarkupLine(
                model == null
                    ? $"[green]✓[/] {Strings.ModelUpdatedDefault}"
                    : $"[green]✓[/] {Strings.ModelUpdatedTo(model).EscapeMarkup()}");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] {ex.Message.EscapeMarkup()}");
        }
    }

    private void SaveWorkspaceModelConfig(string? model)
    {
        var configPath = Path.Combine(dotCraftPath, "config.json");
        Directory.CreateDirectory(dotCraftPath);
        var root = LoadWorkspaceConfigObject(configPath);
        var modelKey = FindCaseInsensitiveKey(root, "Model");

        if (string.IsNullOrWhiteSpace(model))
        {
            if (modelKey != null)
                root.Remove(modelKey);
            _workspaceModel = "Default";
        }
        else
        {
            root[modelKey ?? "Model"] = model;
            _workspaceModel = model;
        }

        var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(configPath, json, new UTF8Encoding(false));
    }

    private async Task PersistWorkspaceModelAsync(string? model)
    {
        if (wireClient != null)
        {
            if (!_workspaceConfigSupported)
                throw new InvalidOperationException(Strings.ModelFeatureUnavailable);
            var response = await wireClient.WorkspaceConfigUpdateAsync(model);
            _workspaceModel = string.IsNullOrWhiteSpace(response.Model) ? "Default" : response.Model!;
            return;
        }

        SaveWorkspaceModelConfig(model);
    }

    private async Task UpdateThreadModelAsync(string threadId, string? model)
    {
        var readDoc = await wireClient!.SendRequestAsync(
            AppServerMethods.ThreadRead,
            new ThreadReadParams
            {
                ThreadId = threadId,
                IncludeTurns = false
            });

        var config = new ThreadConfiguration();
        if (readDoc.RootElement.TryGetProperty("result", out var resultEl) &&
            resultEl.TryGetProperty("thread", out var threadEl) &&
            threadEl.TryGetProperty("configuration", out var cfgEl) &&
            cfgEl.ValueKind == JsonValueKind.Object)
        {
            config = JsonSerializer.Deserialize<ThreadConfiguration>(
                         cfgEl.GetRawText(),
                         new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                     ?? new ThreadConfiguration();
        }

        config.Model = model;
        await wireClient.SendRequestAsync(
            AppServerMethods.ThreadConfigUpdate,
            new ThreadConfigUpdateParams
            {
                ThreadId = threadId,
                Config = config
            });

        _threadModelOverride = model;
    }

    private string ReadWorkspaceModelFromConfig()
    {
        var configPath = Path.Combine(dotCraftPath, "config.json");
        var root = LoadWorkspaceConfigObject(configPath);
        var modelKey = FindCaseInsensitiveKey(root, "Model");
        if (modelKey == null)
            return "Default";
        var model = root[modelKey]?.GetValue<string>()?.Trim();
        return string.IsNullOrWhiteSpace(model) ? "Default" : model;
    }

    private static JsonObject LoadWorkspaceConfigObject(string configPath)
    {
        if (!File.Exists(configPath))
            return new JsonObject();
        try
        {
            var node = JsonNode.Parse(File.ReadAllText(configPath));
            return node as JsonObject ?? new JsonObject();
        }
        catch
        {
            return new JsonObject();
        }
    }

    private static string? FindCaseInsensitiveKey(JsonObject obj, string expectedKey)
    {
        foreach (var kv in obj)
        {
            if (string.Equals(kv.Key, expectedKey, StringComparison.OrdinalIgnoreCase))
                return kv.Key;
        }
        return null;
    }

    private string GetEffectiveModelDisplay()
    {
        return !string.IsNullOrWhiteSpace(_threadModelOverride) ? _threadModelOverride! : _workspaceModel;
    }

    private async Task HandleHeartbeatCommandAsync(string input)
    {
        if (heartbeatService == null && wireClient == null)
        {
            AnsiConsole.MarkupLine($"[yellow]{Strings.HeartbeatUnavailable}[/]\n");
            return;
        }

        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var subCmd = parts.Length > 1 ? parts[1].ToLowerInvariant() : "trigger";

        switch (subCmd)
        {
            case "trigger":
                await HandleHeartbeatTriggerAsync();
                break;
            default:
                AnsiConsole.MarkupLine($"[yellow]{Strings.HeartbeatUsage}[/]");
                break;
        }
        AnsiConsole.WriteLine();
    }

    private async Task HandleHeartbeatTriggerAsync()
    {
        AnsiConsole.MarkupLine($"[blue]{Strings.TriggeringHeartbeat}[/]");

        if (wireClient != null)
        {
            try
            {
                var response = await wireClient.HeartbeatTriggerAsync();
                if (response.Error != null)
                    AnsiConsole.MarkupLine($"[red]{Markup.Escape(response.Error)}[/]");
                else if (response.Result != null)
                    AnsiConsole.MarkupLine($"[green]{Strings.HeartbeatResult}:[/] {Markup.Escape(response.Result)}");
                else
                    AnsiConsole.MarkupLine($"[grey]{Strings.HeartbeatNoResponse}[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
            }
            return;
        }

        // Fallback: local HeartbeatService (standalone mode without AppServer)
        var result = await heartbeatService!.TriggerNowAsync();
        if (result != null)
            AnsiConsole.MarkupLine($"[green]{Strings.HeartbeatResult}:[/] {Markup.Escape(result)}");
        else
            AnsiConsole.MarkupLine($"[grey]{Strings.HeartbeatNoResponse}[/]");
    }

    private async Task HandleCronCommandAsync(string input)
    {
        if (cronService == null && wireClient == null)
        {
            AnsiConsole.MarkupLine($"[yellow]{Strings.CronUnavailable}[/]\n");
            return;
        }

        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var subCmd = parts.Length > 1 ? parts[1].ToLowerInvariant() : "list";

        switch (subCmd)
        {
            case "list":
                await HandleCronListAsync();
                break;

            case "remove":
            {
                if (parts.Length < 3)
                {
                    AnsiConsole.MarkupLine($"[yellow]{Strings.CronRemoveUsage}[/]");
                    break;
                }
                await HandleCronRemoveAsync(parts[2]);
                break;
            }

            case "enable":
            case "disable":
            {
                if (parts.Length < 3)
                {
                    AnsiConsole.MarkupLine($"[yellow]{Strings.CronToggleUsage}：/cron {subCmd} <jobId>[/]");
                    break;
                }
                await HandleCronEnableAsync(parts[2], enabled: subCmd == "enable");
                break;
            }

            default:
                AnsiConsole.MarkupLine($"[yellow]{Strings.CronUsage}[/]");
                break;
        }
        AnsiConsole.WriteLine();
    }

    private async Task HandleCronListAsync()
    {
        List<CronJobWireInfo>? wireJobs = null;

        // Prefer wire path so the CLI reads authoritative in-memory state from the AppServer.
        if (wireClient != null)
        {
            try
            {
                wireJobs = await wireClient.CronListAsync(includeDisabled: true);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
                return;
            }
        }

        if (wireJobs != null)
        {
            RenderCronTable(wireJobs);
            return;
        }

        // Fallback: use local CronService (e.g. standalone mode without a subprocess).
        cronService!.ReloadStore();
        var localJobs = cronService.ListJobs(includeDisabled: true);
        RenderCronTable(localJobs.Select(j => new CronJobWireInfo
        {
            Id = j.Id,
            Name = j.Name,
            Schedule = new CronScheduleWireInfo { Kind = j.Schedule.Kind, EveryMs = j.Schedule.EveryMs, AtMs = j.Schedule.AtMs },
            Enabled = j.Enabled,
            CreatedAtMs = j.CreatedAtMs,
            DeleteAfterRun = j.DeleteAfterRun,
            State = new CronJobStateWireInfo
            {
                NextRunAtMs = j.State.NextRunAtMs,
                LastRunAtMs = j.State.LastRunAtMs,
                LastStatus = j.State.LastStatus,
                LastError = j.State.LastError
            }
        }).ToList());
    }

    private static void RenderCronTable(IReadOnlyList<CronJobWireInfo> jobs)
    {
        if (jobs.Count == 0)
        {
            AnsiConsole.MarkupLine($"[grey]{Strings.NoCronJobs}[/]");
            return;
        }

        var table = new Table();
        table.Border(TableBorder.Rounded);
        table.AddColumn(Strings.CronColId);
        table.AddColumn(Strings.CronColName);
        table.AddColumn(Strings.CronColSchedule);
        table.AddColumn(Strings.CronColStatus);
        table.AddColumn(Strings.CronColNextRun);

        foreach (var job in jobs)
        {
            var schedDesc = job.Schedule.Kind switch
            {
                "at" when job.Schedule.AtMs.HasValue =>
                    $"{Strings.CronExecuteOnce} {DateTimeOffset.FromUnixTimeMilliseconds(job.Schedule.AtMs.Value):u} {Strings.CronExecuteOnceSuffix}",
                "every" when job.Schedule.EveryMs.HasValue =>
                    $"{Strings.CronEvery} {TimeSpan.FromMilliseconds(job.Schedule.EveryMs.Value)}",
                _ => job.Schedule.Kind
            };
            var next = job.State.NextRunAtMs.HasValue
                ? DateTimeOffset.FromUnixTimeMilliseconds(job.State.NextRunAtMs.Value).ToString("u")
                : "-";
            var status = job.Enabled ? $"[green]{Strings.CronEnabled}[/]" : $"[grey]{Strings.CronDisabled}[/]";
            table.AddRow(
                Markup.Escape(job.Id),
                Markup.Escape(job.Name),
                Markup.Escape(schedDesc),
                status,
                Markup.Escape(next));
        }
        AnsiConsole.Write(table);
    }

    private async Task HandleCronRemoveAsync(string jobId)
    {
        if (wireClient != null)
        {
            try
            {
                await wireClient.CronRemoveAsync(jobId);
                AnsiConsole.MarkupLine($"[green]{Strings.CronJobDeleted} '{Markup.Escape(jobId)}' {Strings.CronJobDeletedSuffix}[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(ex.Message)}[/]");
            }
            return;
        }

        // Fallback: direct local CronService mutation.
        if (cronService!.RemoveJob(jobId))
            AnsiConsole.MarkupLine($"[green]{Strings.CronJobDeleted} '{Markup.Escape(jobId)}' {Strings.CronJobDeletedSuffix}[/]");
        else
            AnsiConsole.MarkupLine($"[yellow]{Strings.CronJobNotFound} '{Markup.Escape(jobId)}'。[/]");
    }

    private async Task HandleCronEnableAsync(string jobId, bool enabled)
    {
        if (wireClient != null)
        {
            try
            {
                await wireClient.CronEnableAsync(jobId, enabled);
                AnsiConsole.MarkupLine($"[green]{Strings.CronJobDeleted} '{Markup.Escape(jobId)}' {(enabled ? Strings.CronJobEnabled : Strings.CronJobDisabled)}[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(ex.Message)}[/]");
            }
            return;
        }

        // Fallback: direct local CronService mutation.
        var job = cronService!.EnableJob(jobId, enabled);
        if (job != null)
            AnsiConsole.MarkupLine($"[green]{Strings.CronJobDeleted} '{Markup.Escape(jobId)}' {(enabled ? Strings.CronJobEnabled : Strings.CronJobDisabled)}[/]");
        else
            AnsiConsole.MarkupLine($"[yellow]{Strings.CronJobNotFound} '{Markup.Escape(jobId)}'。[/]");
    }

    /// <summary>
    /// Submits user input to the session and renders the turn to the console.
    /// Delegates entirely to <see cref="ICliSession.RunTurnAsync"/>, which manages
    /// the renderer lifecycle, event streaming, and approval prompts.
    /// </summary>
    private async Task<bool> RunSessionInputAsync(string userInput, CancellationToken cancellationToken)
    {
        // Materialize the thread on first user input (lazy creation).
        if (_currentThreadId == null)
        {
            _currentThreadId = await session.CreateThreadAsync(_cliIdentity, cancellationToken);
            _currentSessionId = _currentThreadId;

            // In Wire mode, if the user switched to a non-default mode (e.g. Plan)
            // before sending the first message, the server doesn't know about it yet
            // because SwitchToModeAsync couldn't notify the server without a threadId.
            // Sync the mode now so the server-side agent gets the correct tool set.
            if (_modeManager.CurrentMode != AgentMode.Agent)
            {
                await session.SetThreadModeAsync(
                    _currentThreadId,
                    _modeManager.CurrentMode.ToString().ToLowerInvariant(),
                    cancellationToken);
            }
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var interrupt = new InterruptHandler(cts);
        var token = cts.Token;

        try
        {
            await session.RunTurnAsync(_currentThreadId!, userInput, token);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            AnsiConsole.MarkupLine($"\n[yellow]{Strings.AgentInterrupted}[/]");
            return false;
        }
        catch (Exception ex)
        {
            MessageFormatter.Error(ex.Message);
            return false;
        }
    }
}
