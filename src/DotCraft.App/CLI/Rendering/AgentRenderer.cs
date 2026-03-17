using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using DotCraft.Abstractions;
using DotCraft.Agents;
using DotCraft.Diagnostics;
using DotCraft.Tools;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace DotCraft.CLI.Rendering;

/// <summary>
/// Core renderer for agent display.
/// Owns all Spectre.Console output and runs a single render loop.
///
/// Goal:
/// - When a tool starts: show a live <see cref="AnsiConsole.Status"/> spinner.
/// - When the tool completes: stop the spinner and emit a history.
/// - Stream model response chunks as normal text (kept as history).
/// - Handle approval prompts by pausing/resuming rendering.
/// </summary>
public sealed class AgentRenderer : IRenderControl, IDisposable
{
    private readonly Context.TokenTracker? _tokenTracker;

    private readonly Channel<RenderEvent> _eventQueue = Channel.CreateUnbounded<RenderEvent>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });
    
    private Task? _renderTask;
    
    private CancellationTokenSource? _cancellationTokenSource;

    private RenderState _currentState = RenderState.Idle;

    private string? _currentToolIcon;
    
    private string? _currentToolTitle;
    
    private string? _currentToolContent;
    
    private string? _currentToolAdditional;

    private string? _currentFormattedDisplay;

    private readonly StringBuilder _responseBuffer = new();
    private MarkdownConsoleRenderer.StreamSession? _markdownStreamSession;
    private bool _hasPendingUsage;
    private long _pendingInputTokens;
    private long _pendingOutputTokens;
    
    private bool _disposed;

    // True while an agent stream is active (between StreamStarted and Complete events)
    private bool _streamActive;

    // For approval handling - action to execute on the render thread while paused
    private volatile Func<object?>? _pausedAction;
    private volatile TaskCompletionSource<object?>? _pausedActionResultTcs;

    private const string SubAgentToolName = "SpawnSubagent";
    private const string SubAgentDisplayPrefix = "Spawned subagent: ";

    private static bool IsSubAgentTool(RenderEvent evt) =>
        string.Equals(evt.Title, SubAgentToolName, StringComparison.Ordinal);

    private sealed class SubAgentEntry
    {
        public required string CallId { get; init; }
        public required string Label { get; init; }
        public bool Completed { get; set; }
        public bool Failed { get; set; }

        // Event-driven progress fields (populated by SubAgentProgress RenderEvents in Wire mode)
        public string? CurrentTool { get; set; }
        public string? CurrentToolDisplay { get; set; }
        public long InputTokens { get; set; }
        public long OutputTokens { get; set; }
        /// <summary>True when at least one SubAgentProgress event has been received for this entry.</summary>
        public bool HasProgressData { get; set; }
        /// <summary>True when a SubAgentProgress event with IsCompleted=true has been received for this entry.
        /// This signals that the final snapshot (with complete token stats) has arrived.</summary>
        public bool ProgressCompleted { get; set; }
    }

    private enum RenderState
    {
        Idle,
        ToolExecuting,
        Responding,
        ApprovalPaused
    }

    public AgentRenderer(Context.TokenTracker? tokenTracker = null)
    {
        _tokenTracker = tokenTracker;
    }

    private string GetTokenSuffix()
    {
        if (_tokenTracker == null) return string.Empty;
        var input = _tokenTracker.TotalInputTokens + _tokenTracker.SubAgentInputTokens;
        var output = _tokenTracker.TotalOutputTokens + _tokenTracker.SubAgentOutputTokens;
        if (input == 0 && output == 0) return string.Empty;
        return $" [dim grey]· ↑{Context.TokenTracker.FormatCompact(input)} ↓{Context.TokenTracker.FormatCompact(output)}[/]";
    }

    /// <summary>
    /// Start the rendering loop.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_renderTask != null)
        {
            throw new InvalidOperationException("Renderer already started");
        }

        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _renderTask = Task.Run(() => RenderLoopAsync(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Synchronously enqueue a debug message. Safe to call from any thread.
    /// During an active Status spinner session the event lands in the buffered list
    /// and is printed after the spinner closes, preventing console corruption.
    /// </summary>
    public void TryEnqueueDebug(string message) =>
        _eventQueue.Writer.TryWrite(RenderEvent.DebugMessage(message));

    /// <summary>
    /// Send a rendering event.
    /// </summary>
    public async ValueTask SendEventAsync(RenderEvent evt, CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(AgentRenderer));
        }

        await _eventQueue.Writer.WriteAsync(evt, cancellationToken);
    }

    /// <summary>
    /// Consume event stream and send to renderer.
    /// </summary>
    public async Task ConsumeEventsAsync(IAsyncEnumerable<RenderEvent> events, CancellationToken cancellationToken = default)
    {
        await foreach (var evt in events.WithCancellation(cancellationToken))
        {
            await SendEventAsync(evt, cancellationToken);
        }
    }

    /// <summary>
    /// Stop the rendering loop gracefully (drain queued events).
    /// </summary>
    public async Task StopAsync()
    {
        if (_renderTask == null)
        {
            return;
        }

        // Signal completion; do NOT cancel here, otherwise we may drop queued events.
        _eventQueue.Writer.TryComplete();

        try
        {
            await _renderTask;
        }
        catch (OperationCanceledException)
        {
            // Expected if outer cancellation token was canceled.
        }
    }

    /// <summary>
    /// Pause rendering, execute an action on the render thread, then resume.
    /// This ensures the action has exclusive console access without cross-thread
    /// live rendering conflicts with Spectre.Console (IRenderControl implementation).
    /// </summary>
    public async Task<T> ExecuteWhilePausedAsync<T>(Func<T> action, CancellationToken cancellationToken = default)
    {
        var resultTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Store action and TCS BEFORE sending the event to avoid a race where
        // the render loop processes the event before fields are set.
        // The Channel write in SendEventAsync provides the memory barrier.
        _pausedAction = () => action();
        _pausedActionResultTcs = resultTcs;

        // Send approval required event to pause the Status spinner
        await SendEventAsync(RenderEvent.ApprovalRequest(), cancellationToken);

        // Wait for the render loop to execute the action and return the result
        var result = await resultTcs.Task;
        return (T)result!;
    }

    private async Task RenderLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            var reader = _eventQueue.Reader;

            while (true)
            {
                bool hasData;
                if (_streamActive && _currentState == RenderState.Idle)
                    hasData = await WaitWithThinkingSpinnerAsync(reader, cancellationToken);
                else
                    hasData = await reader.WaitToReadAsync(cancellationToken);

                if (!hasData) break;

                while (reader.TryRead(out var evt))
                {
                    switch (evt.Type)
                    {
                        case RenderEventType.StreamStarted:
                            _streamActive = true;
                            break;

                        case RenderEventType.ToolCallStarted:
                            await RunToolStatusSessionAsync(evt, cancellationToken);
                            break;

                        default:
                            ProcessNonToolStartEvent(evt);
                            break;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Clean shutdown
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Renderer error: {Markup.Escape(ex.Message)}[/]");
#if DEBUG
            AnsiConsole.WriteException(ex);
#endif
        }
    }

    /// <summary>
    /// Waits for events from the channel while showing an animated "Thinking..." spinner.
    /// Updates elapsed seconds every second until the first event arrives.
    /// Follows the same pattern as <see cref="RunToolStatusSessionAsync"/>.
    /// </summary>
    private async Task<bool> WaitWithThinkingSpinnerAsync(ChannelReader<RenderEvent> reader, CancellationToken ct)
    {
        bool result = false;

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(new Style(Color.Cyan))
            .StartAsync("[cyan]💭 Thinking...[/]", async ctx =>
            {
                var sw = Stopwatch.StartNew();

                while (!ct.IsCancellationRequested)
                {
                    // Wait up to 1 second, then update elapsed display
                    using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    delayCts.CancelAfter(TimeSpan.FromSeconds(1));
                    try
                    {
                        result = await reader.WaitToReadAsync(delayCts.Token);
                        return; // Event available (or channel completed)
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        // 1-second tick expired; update elapsed label
                    }

                    var elapsed = (long)sw.Elapsed.TotalSeconds;
                    var tokenSuffix = GetTokenSuffix();
                    ctx.Status = elapsed > 0
                        ? $"[cyan]💭 Thinking... ({elapsed}s)[/]{tokenSuffix}"
                        : $"[cyan]💭 Thinking...[/]{tokenSuffix}";
                }
            });

        return result;
    }

    /// <summary>
    /// Enters a live Status spinner session until a ToolCallCompleted arrives.
    /// The session keeps consuming the shared event queue, and buffers non-tool events
    /// to be rendered after the status is closed (to keep the console stable).
    /// </summary>
    private async Task RunToolStatusSessionAsync(RenderEvent toolStarted, CancellationToken cancellationToken)
    {
        if (IsSubAgentTool(toolStarted))
        {
            await RunSubAgentGroupSessionAsync(toolStarted, cancellationToken);
            return;
        }

        CleanupCurrentState();

        _currentState = RenderState.ToolExecuting;
        _currentToolIcon = toolStarted.Icon;
        _currentToolTitle = toolStarted.Title;
        _currentToolContent = toolStarted.Content;
        _currentToolAdditional = toolStarted.AdditionalInfo;
        _currentFormattedDisplay = toolStarted.FormattedDisplay;

        // Fallback: if neither tool name nor icon is present, use defaults
        // so the spinner still works for unregistered or parallel tool calls.
        if (string.IsNullOrWhiteSpace(_currentToolTitle) && string.IsNullOrWhiteSpace(_currentToolIcon))
        {
            _currentToolIcon = "🔧";
            _currentToolTitle = "Tool";
        }

        var reader = _eventQueue.Reader;
        RenderEvent? toolCompleted = null;
        var buffered = new List<RenderEvent>(capacity: 8);

        var initialStatus = BuildToolStatusMarkup();

        try
        {
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(new Style(Color.Yellow))
                .StartAsync(initialStatus, async ctx =>
                {
                    ctx.Status = initialStatus;

                    while (!cancellationToken.IsCancellationRequested)
                    {
                        // Wait for more events
                        if (!await reader.WaitToReadAsync(cancellationToken))
                        {
                            break;
                        }

                        while (reader.TryRead(out var evt))
                        {
                            switch (evt.Type)
                            {
                                case RenderEventType.ThinkingStep:
                                    // Treat thinking steps during tool execution as status updates
                                    _currentToolIcon = evt.Icon ?? _currentToolIcon;
                                    _currentToolTitle = evt.Title ?? _currentToolTitle;
                                    _currentToolContent = evt.Content;
                                    ctx.Status = BuildToolStatusMarkup();
                                    break;

                                case RenderEventType.ToolCallStarted:
                                    // Nested/second tool start: treat as a new status target
                                    _currentToolIcon = evt.Icon ?? _currentToolIcon;
                                    _currentToolTitle = evt.Title ?? _currentToolTitle;
                                    _currentToolContent = evt.Content;
                                    _currentToolAdditional = evt.AdditionalInfo ?? _currentToolAdditional;
                                    _currentFormattedDisplay = evt.FormattedDisplay ?? _currentFormattedDisplay;
                                    ctx.Status = BuildToolStatusMarkup();
                                    break;

                                case RenderEventType.ToolCallCompleted:
                                    toolCompleted = evt;
                                    return;

                                case RenderEventType.ApprovalRequired:
                                    // Pause for approval prompt.
                                    // The paused action and result TCS are already set by
                                    // ExecuteWhilePausedAsync before it sent this event,
                                    // and the Channel write provides the memory barrier.
                                    _currentState = RenderState.ApprovalPaused;
                                    
                                    // Exit Status context temporarily
                                    return;

                                default:
                                    buffered.Add(evt);
                                    break;
                            }
                        }
                    }
                });
        }
        catch (OperationCanceledException)
        {
            // If canceled during a tool call, just exit.
        }

        // If we paused for approval, execute the paused action on the render thread
        if (_currentState == RenderState.ApprovalPaused)
        {
            // Execute the paused action on the render thread (same thread as Status)
            // to avoid cross-thread Spectre.Console live rendering issues
            var action = _pausedAction;
            var resultTcs = _pausedActionResultTcs;
            _pausedAction = null;
            _pausedActionResultTcs = null;

            if (action != null && resultTcs != null)
            {
                try
                {
                    var result = action();
                    resultTcs.TrySetResult(result);
                }
                catch (Exception ex)
                {
                    resultTcs.TrySetException(ex);
                }
            }

            // Resume tool status session
            _currentState = RenderState.ToolExecuting;

            // Re-enter status spinner and wait for completion
            await RunToolStatusSessionAsync(
                RenderEvent.ToolStarted(_currentToolIcon, _currentToolTitle, _currentToolContent ?? string.Empty, _currentToolAdditional, _currentFormattedDisplay),
                cancellationToken);
            return;
        }

        // Emit the tool history line after Status closes.
        if (toolCompleted != null)
        {
            WriteToolHistoryLine(toolCompleted);
        }

        // Reset tool state
        _currentState = RenderState.Idle;
        _currentToolIcon = null;
        _currentToolTitle = null;
        _currentToolContent = null;
        _currentToolAdditional = null;
        _currentFormattedDisplay = null;

        // Replay buffered events
        foreach (var evt in buffered)
        {
            ProcessNonToolStartEvent(evt);
        }
    }

    #region SubAgent Group Rendering

    /// <summary>
    /// Renders a dynamic Live table for one or more parallel SubAgent tool calls.
    /// Each SubAgent gets its own row with an animated spinner that turns into a
    /// checkmark on completion. The session stays open until every tracked SubAgent
    /// has finished (or failed), then leaves the final table visible in scrollback.
    /// </summary>
    private async Task RunSubAgentGroupSessionAsync(RenderEvent firstTool, CancellationToken ct)
    {
        CleanupCurrentState();
        _currentState = RenderState.ToolExecuting;

        var entries = new List<SubAgentEntry>();
        var buffered = new List<RenderEvent>(capacity: 8);
        var reader = _eventQueue.Reader;

        AddSubAgentEntry(entries, firstTool);

        var spinner = Spinner.Known.Dots;
        var frames = spinner.Frames;
        int frameIndex = 0;
        var interval = TimeSpan.FromMilliseconds(80);

        // Loop allows re-entry after an approval pause
        while (true)
        {
            bool approvalPaused = false;

            try
            {
                await AnsiConsole.Live(BuildSubAgentTable(entries, frames, frameIndex))
                    .AutoClear(false)
                    .StartAsync(async ctx =>
                    {
                        while (!ct.IsCancellationRequested)
                        {
                            while (reader.TryRead(out var evt))
                            {
                                switch (evt.Type)
                                {
                                    case RenderEventType.ToolCallStarted when IsSubAgentTool(evt):
                                        AddSubAgentEntry(entries, evt);
                                        break;

                                    case RenderEventType.ToolCallCompleted:
                                        if (!string.IsNullOrEmpty(evt.CallId))
                                        {
                                            var matched = entries.Find(e => e.CallId == evt.CallId);
                                            if (matched != null)
                                            {
                                                matched.Completed = true;
                                                break;
                                            }
                                        }
                                        buffered.Add(evt);
                                        break;

                                    case RenderEventType.SubAgentProgress when evt.SubAgentEntries is { } progressEntries:
                                        // Event-driven update path: apply snapshot data to tracked entries.
                                        // This is the primary data source in Wire mode where SubAgentProgressBridge
                                        // is unavailable (cross-process). In InProcess mode this is a no-op redundancy.
                                        foreach (var pe in progressEntries)
                                        {
                                            var target = entries.Find(e =>
                                                string.Equals(e.Label, pe.Label, StringComparison.Ordinal));
                                            if (target == null) continue;
                                            target.CurrentTool = pe.CurrentTool;
                                            target.CurrentToolDisplay = pe.CurrentToolDisplay;
                                            target.InputTokens = pe.InputTokens;
                                            target.OutputTokens = pe.OutputTokens;
                                            target.HasProgressData = true;
                                            if (pe.IsCompleted)
                                            {
                                                target.Completed = true;
                                                target.ProgressCompleted = true;
                                            }
                                        }
                                        break;

                                    case RenderEventType.ApprovalRequired:
                                        approvalPaused = true;
                                        return;

                                    default:
                                        buffered.Add(evt);
                                        break;
                                }
                            }

                            // Check bridge for early completion signals (before FIC yields ToolCallCompleted).
                            // Skip bridge polling for entries that have event-driven data (Wire mode).
                            foreach (var entry in entries)
                            {
                                if (!entry.Completed && !entry.Failed && !entry.HasProgressData)
                                {
                                    var progress = SubAgentProgressBridge.TryGet(entry.Label);
                                    if (progress is { IsCompleted: true })
                                        entry.Completed = true;
                                }
                            }

                            ctx.UpdateTarget(BuildSubAgentTable(entries, frames, frameIndex++));

                            if (entries.TrueForAll(e => e.Completed || e.Failed))
                            {
                                // In Wire mode, the final SubAgentProgress snapshot (with complete
                                // token stats) is emitted by the server's DisposeAsync() *after*
                                // the ToolResult ItemCompleted that triggers Completed=true above.
                                // The DisposeAsync involves an async await (_runTask), so the final
                                // snapshot may not yet be in the channel when TryRead runs.
                                //
                                // Strategy: wait (with timeout) for the final SubAgentProgress that
                                // has IsCompleted=true for all progress-tracked entries. The server
                                // guarantees this event arrives before TurnCompleted, so a bounded
                                // wait is safe.
                                await DrainFinalSubAgentProgressAsync(
                                    reader, entries, buffered, ct);

                                ctx.UpdateTarget(BuildSubAgentTable(entries, frames, frameIndex));
                                return;
                            }

                            using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                            delayCts.CancelAfter(interval);
                            try
                            {
                                await reader.WaitToReadAsync(delayCts.Token);
                            }
                            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                            {
                                // Tick expired; loop to advance spinner frame
                            }
                        }
                    });
            }
            catch (OperationCanceledException)
            {
                // Clean exit on cancellation
            }

            if (!approvalPaused)
                break;

            // Handle approval pause then re-enter Live context
            _currentState = RenderState.ApprovalPaused;
            var action = _pausedAction;
            var resultTcs = _pausedActionResultTcs;
            _pausedAction = null;
            _pausedActionResultTcs = null;

            if (action != null && resultTcs != null)
            {
                try { resultTcs.TrySetResult(action()); }
                catch (Exception ex) { resultTcs.TrySetException(ex); }
            }

            _currentState = RenderState.ToolExecuting;
        }

        // Clean up bridge entries now that the live table session is done.
        // Only needed for InProcess mode where entries were polled from SubAgentProgressBridge.
        foreach (var entry in entries)
        {
            if (!entry.HasProgressData)
                SubAgentProgressBridge.Remove(entry.Label);
        }

        // Reset state
        _currentState = RenderState.Idle;
        _currentToolIcon = null;
        _currentToolTitle = null;
        _currentToolContent = null;
        _currentToolAdditional = null;
        _currentFormattedDisplay = null;

        // Replay buffered events
        foreach (var evt in buffered)
        {
            ProcessNonToolStartEvent(evt);
        }
    }

    /// <summary>
    /// After all SubAgent entries are marked Completed, wait (with timeout) for the
    /// final <see cref="RenderEventType.SubAgentProgress"/> event that carries the
    /// complete token statistics. In Wire mode, this event is emitted by the server's
    /// <c>SubAgentProgressAggregator.DisposeAsync()</c> which runs after the last
    /// <c>EmitItemCompleted(ToolResult)</c> and involves an async <c>await _runTask</c>.
    /// A non-blocking <c>TryRead</c> is therefore insufficient — the final snapshot
    /// may still be in transit. We wait up to a bounded timeout for it to arrive.
    /// </summary>
    private static async Task DrainFinalSubAgentProgressAsync(
        ChannelReader<RenderEvent> reader,
        List<SubAgentEntry> entries,
        List<RenderEvent> buffered,
        CancellationToken ct)
    {
        // Determine whether we need to wait: only if at least one entry has received
        // event-driven progress data (Wire mode). In InProcess mode entries use
        // SubAgentProgressBridge directly and HasProgressData is false.
        bool anyProgressTracked = entries.Exists(e => e.HasProgressData);

        // Phase 1: non-blocking sweep of anything already in the channel.
        DrainAvailableProgress(reader, entries, buffered);

        if (!anyProgressTracked || AllProgressComplete(entries))
            return;

        // Phase 2: bounded wait. The server guarantees the final SubAgentProgress
        // snapshot arrives before TurnCompleted, so this should resolve quickly.
        // Timeout is generous to cover slow networks / thread-pool saturation.
        const int maxWaitMs = 2000;
        using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        waitCts.CancelAfter(maxWaitMs);

        try
        {
            while (!AllProgressComplete(entries))
            {
                // Block until at least one event is available, then sweep.
                await reader.WaitToReadAsync(waitCts.Token);
                DrainAvailableProgress(reader, entries, buffered);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timeout expired — accept whatever data we have. This is a safety
            // net; under normal conditions the final snapshot arrives well within
            // the timeout.
        }
    }

    /// <summary>
    /// Non-blocking sweep: read all available events from the channel, apply any
    /// <see cref="RenderEventType.SubAgentProgress"/> data, and buffer the rest.
    /// </summary>
    private static void DrainAvailableProgress(
        ChannelReader<RenderEvent> reader,
        List<SubAgentEntry> entries,
        List<RenderEvent> buffered)
    {
        while (reader.TryRead(out var evt))
        {
            if (evt.Type == RenderEventType.SubAgentProgress
                && evt.SubAgentEntries is { } progressEntries)
            {
                foreach (var pe in progressEntries)
                {
                    var target = entries.Find(e =>
                        string.Equals(e.Label, pe.Label, StringComparison.Ordinal));
                    if (target == null) continue;
                    target.CurrentTool = pe.CurrentTool;
                    target.CurrentToolDisplay = pe.CurrentToolDisplay;
                    target.InputTokens = pe.InputTokens;
                    target.OutputTokens = pe.OutputTokens;
                    target.HasProgressData = true;
                    if (pe.IsCompleted)
                        target.ProgressCompleted = true;
                }
            }
            else
            {
                buffered.Add(evt);
            }
        }
    }

    /// <summary>
    /// Returns true when every entry that has received event-driven progress data
    /// also received a SubAgentProgress event with IsCompleted=true (the final snapshot).
    /// Entries without progress data (InProcess mode) are excluded from the check.
    /// </summary>
    private static bool AllProgressComplete(List<SubAgentEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (!entry.HasProgressData)
                continue;
            if (!entry.ProgressCompleted)
                return false;
        }
        return true;
    }

    private static void AddSubAgentEntry(List<SubAgentEntry> entries, RenderEvent evt)
    {
        var display = evt.FormattedDisplay ?? evt.Title ?? "SubAgent";

        // Strip the "Spawned subagent: " prefix to keep table rows concise
        if (display.StartsWith(SubAgentDisplayPrefix, StringComparison.Ordinal))
            display = display[SubAgentDisplayPrefix.Length..];

        entries.Add(new SubAgentEntry
        {
            CallId = evt.CallId ?? Guid.NewGuid().ToString("N"),
            Label = display
        });
    }

    private IRenderable BuildSubAgentTable(
        List<SubAgentEntry> entries,
        IReadOnlyList<string> spinnerFrames,
        int frameIndex)
    {
        var completed = entries.Count(e => e.Completed);
        var total = entries.Count;
        var frame = spinnerFrames[frameIndex % spinnerFrames.Count];

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .AddColumn(new TableColumn("[grey]Task[/]"))
            .AddColumn(new TableColumn("[grey]Activity[/]").Width(28))
            .AddColumn(new TableColumn("[grey]Status[/]").Centered().Width(8));

        foreach (var entry in entries)
        {
            var status = entry.Completed
                ? "[green]✓[/]"
                : entry.Failed
                    ? "[red]✗[/]"
                    : $"[yellow]{Markup.Escape(frame)}[/]";

            var label = entry.Label.Length > 60
                ? entry.Label[..57] + "..."
                : entry.Label;

            var activity = string.Empty;
            const int maxActivityLen = 24;

            // Dual data source: prefer event-driven data (HasProgressData = Wire mode),
            // fall back to SubAgentProgressBridge polling (InProcess mode).
            if (entry.HasProgressData)
            {
                // Wire mode: use event-driven progress fields from SubAgentProgress RenderEvents
                if (entry.Completed)
                {
                    if (entry.InputTokens > 0 || entry.OutputTokens > 0)
                        activity = $"[blue]↑ {entry.InputTokens}[/] [green]↓ {entry.OutputTokens}[/]";
                }
                else
                {
                    var display = entry.CurrentToolDisplay ?? entry.CurrentTool;
                    activity = !string.IsNullOrEmpty(display)
                        ? $"[dim]{Markup.Escape(ToolDisplayHelpers.Truncate(display, maxActivityLen))}[/]"
                        : "[dim]Thinking...[/]";

                    if (entry.InputTokens > 0 || entry.OutputTokens > 0)
                        activity = $"{activity} [dim grey]· ↑{entry.InputTokens} ↓{entry.OutputTokens}[/]";
                }
            }
            else
            {
                // InProcess mode: poll SubAgentProgressBridge directly
                var progress = SubAgentProgressBridge.TryGet(entry.Label);
                if (entry.Completed)
                {
                    if (progress != null && (progress.InputTokens > 0 || progress.OutputTokens > 0))
                        activity = $"[blue]↑ {progress.InputTokens}[/] [green]↓ {progress.OutputTokens}[/]";
                }
                else if (progress != null)
                {
                    var display = progress.CurrentToolDisplay ?? progress.LastToolDisplay
                                  ?? progress.CurrentTool ?? progress.LastTool;
                    activity = !string.IsNullOrEmpty(display)
                        ? $"[dim]{Markup.Escape(ToolDisplayHelpers.Truncate(display, maxActivityLen))}[/]"
                        : "[dim]Thinking...[/]";

                    if (progress.InputTokens > 0 || progress.OutputTokens > 0)
                        activity = $"{activity} [dim grey]· ↑{progress.InputTokens} ↓{progress.OutputTokens}[/]";
                }
            }

            table.AddRow(
                new Markup(Markup.Escape(label)),
                new Markup(activity),
                new Markup(status));
        }

        var tokenSuffix = GetTokenSuffix();
        return new Rows(
            new Markup($"[purple]🐧 SubAgents ({completed}/{total})[/]{tokenSuffix}"),
            table);
    }

    #endregion

    private void ProcessNonToolStartEvent(RenderEvent evt)
    {
        switch (evt.Type)
        {
            case RenderEventType.StreamStarted:
                // StreamStarted may be buffered during tool spinner; apply state now.
                _streamActive = true;
                break;

            case RenderEventType.ToolCallCompleted:
                // In case a completed event arrives without an active status session.
                WriteToolHistoryLine(evt);
                break;

            case RenderEventType.ResponseChunk:
                HandleResponseChunk(evt);
                break;

            case RenderEventType.ThinkingStep:
                HandleThinkingStep(evt);
                break;

            case RenderEventType.Warning:
                HandleWarning(evt);
                break;

            case RenderEventType.Error:
                HandleError(evt);
                break;

            case RenderEventType.Complete:
                HandleComplete(evt);
                break;

            case RenderEventType.Usage:
                HandleUsage(evt);
                break;

            case RenderEventType.Debug:
                AnsiConsole.MarkupLine($"[dim]{Markup.Escape(evt.Content)}[/]");
                break;

            case RenderEventType.ApprovalRequired:
            case RenderEventType.ApprovalCompleted:
            case RenderEventType.SubAgentProgress:
                break;
        }
    }

    private string BuildToolStatusMarkup()
    {
        if (string.IsNullOrWhiteSpace(_currentToolTitle) && string.IsNullOrWhiteSpace(_currentToolIcon))
            return "[red]Error: Invalid tool call (missing tool name/icon).[/]";

        var icon = string.IsNullOrWhiteSpace(_currentToolIcon) ? string.Empty : _currentToolIcon;
        var segmentMaxLength = GetStatusSegmentMaxLength();

        // Use human-readable formatted display when available; fall back to raw title
        var displayText = !string.IsNullOrWhiteSpace(_currentFormattedDisplay)
            ? NormalizeInlineText(TruncateText(_currentFormattedDisplay!, segmentMaxLength * 2))
            : NormalizeInlineText(_currentToolTitle ?? string.Empty);

        var sb = new StringBuilder();
        sb.Append($"[yellow]{Markup.Escape((icon + " " + displayText).Trim())}[/]");

        // Show thinking-step content in grey when it arrives mid-execution
        if (!string.IsNullOrWhiteSpace(_currentToolContent))
        {
            var displayContent = DebugModeService.IsEnabled()
                ? _currentToolContent
                : TruncateText(_currentToolContent!, segmentMaxLength);
            sb.Append($" [grey]{Markup.Escape(NormalizeInlineText(displayContent))}[/]");
        }

        // In debug mode, also show raw args
        if (DebugModeService.IsEnabled() && !string.IsNullOrWhiteSpace(_currentToolAdditional))
        {
            var debugArgs = NormalizeInlineText(TruncateText(_currentToolAdditional!, segmentMaxLength));
            sb.Append($" [dim]({Markup.Escape(debugArgs)})[/]");
        }

        sb.Append(GetTokenSuffix());

        return sb.ToString();
    }

    /// <summary>
    /// Convert a tool completion into a single-line history record followed by an
    /// indented result line. Uses the last known tool state as fallback.
    /// </summary>
    private void WriteToolHistoryLine(RenderEvent evt)
    {
        var icon = evt.Icon ?? _currentToolIcon;
        var title = evt.Title ?? _currentToolTitle;

        // Fallback when parallel tool calls complete after state has been reset
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(icon))
        {
            icon = "🔧";
            title = "Tool";
        }

        var formattedDisplay = evt.FormattedDisplay ?? _currentFormattedDisplay;
        var rawArgs = evt.Content; // argsJson stored in Content
        var result = evt.AdditionalInfo;

        // Prefer human-readable formatted display; fall back to tool name
        var displayText = !string.IsNullOrWhiteSpace(formattedDisplay)
            ? formattedDisplay
            : title ?? "Tool";

        var line = new StringBuilder();
        line.Append($"[yellow]{Markup.Escape($"{icon} {displayText}")}[/]");

        // In debug mode, append raw args for diagnostics
        if (DebugModeService.IsEnabled() && !string.IsNullOrWhiteSpace(rawArgs))
        {
            var debugArgs = NormalizeInlineText(TruncateText(rawArgs, 120));
            line.Append($" [dim]{Markup.Escape(debugArgs)}[/]");
        }

        AnsiConsole.MarkupLine(line.ToString());

        // Result on an indented sub-line — use structured formatter for known JSON tools,
        // fall back to generic truncation for all others.
        if (!string.IsNullOrWhiteSpace(result))
        {
            var formattedResult = ToolRegistry.FormatToolResult(title, result);
            if (formattedResult != null)
            {
                foreach (var resultValue in formattedResult)
                    AnsiConsole.MarkupLine($"  [grey]{Markup.Escape(resultValue)}[/]");
            }
            else
            {
                var resultLine = NormalizeInlineText(TruncateText(result, 200));
                AnsiConsole.MarkupLine($"  [grey]{Markup.Escape(resultLine)}[/]");
            }
        }
    }

    private void HandleResponseChunk(RenderEvent evt)
    {
        // Use IsNullOrEmpty (not IsNullOrWhiteSpace) so that whitespace-only chunks
        // containing newlines are passed through to the markdown stream session.
        // These newlines are structurally significant for markdown block parsing.
        if (string.IsNullOrEmpty(evt.Content))
        {
            return;
        }

        if (_currentState != RenderState.Responding)
        {
            CleanupCurrentState();
            _currentState = RenderState.Responding;
            _responseBuffer.Clear();
            _hasPendingUsage = false;
            _markdownStreamSession = MarkdownConsoleRenderer.CreateStreamSession();
            AnsiConsole.WriteLine();
        }

        _responseBuffer.Append(evt.Content);
        _markdownStreamSession ??= MarkdownConsoleRenderer.CreateStreamSession();
        _markdownStreamSession.Append(evt.Content);
    }

    private void HandleThinkingStep(RenderEvent evt)
    {
        // If tool session is active, thinking steps will be consumed inside the status session.
        if (_currentState == RenderState.ToolExecuting)
        {
            return;
        }

        var icon = evt.Icon ?? "💭";
        var title = evt.Title ?? "Thinking";
        var color = evt.Color ?? "cyan";

        AnsiConsole.MarkupLine($"[{color}]{Markup.Escape($"{icon} {title}")}[/] [dim]{Markup.Escape(evt.Content)}[/]");
    }

    private void HandleWarning(RenderEvent evt)
    {
        CleanupCurrentState();
        var color = evt.Color ?? "yellow";
        AnsiConsole.MarkupLine($"[{color}]⚠ {Markup.Escape(evt.Content)}[/]");
        _currentState = RenderState.Idle;
    }

    private void HandleError(RenderEvent evt)
    {
        CleanupCurrentState();
        var color = evt.Color ?? "red";
        AnsiConsole.MarkupLine($"[{color}]✗ {Markup.Escape(evt.Content)}[/]");
        _currentState = RenderState.Idle;
    }

    private void HandleUsage(RenderEvent evt)
    {
        var parts = evt.Content.Split(',');
        if (!(parts.Length >= 2 &&
              long.TryParse(parts[0], out var input) &&
              long.TryParse(parts[1], out var output)))
        {
            return;
        }

        if (_currentState == RenderState.Responding)
        {
            // Delay usage output until markdown stream is fully flushed.
            _pendingInputTokens = input;
            _pendingOutputTokens = output;
            _hasPendingUsage = true;
            return;
        }

        PrintUsage(input, output);
    }

    private void HandleComplete(RenderEvent _)
    {
        _streamActive = false;

        if (_currentState == RenderState.Responding && _responseBuffer.Length > 0)
        {
            _markdownStreamSession?.Complete();
            _markdownStreamSession = null;
            _responseBuffer.Clear();
            FlushPendingUsageIfAny();
            AnsiConsole.WriteLine();
            _currentState = RenderState.Idle;
            return;
        }

        CleanupCurrentState();
        FlushPendingUsageIfAny();
        AnsiConsole.WriteLine();
        _currentState = RenderState.Idle;
    }

    private void CleanupCurrentState()
    {
        if (_currentState == RenderState.Responding && _responseBuffer.Length > 0)
        {
            _markdownStreamSession?.Complete();
            _markdownStreamSession = null;
            AnsiConsole.WriteLine();
            _responseBuffer.Clear();
        }
    }

    private static string TruncateText(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
        {
            return text;
        }

        return text[..maxLength] + "...";
    }

    private static string NormalizeInlineText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        // Status spinner is a single-line live render; embedded newlines
        // break cursor overwrite and cause line-by-line growth.
        return text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();
    }

    private static int GetStatusSegmentMaxLength()
    {
        try
        {
            // Keep each segment short enough to reduce terminal auto-wrap.
            // Reserve width for icon/title/spinner and markup overhead.
            var width = Console.WindowWidth;
            if (width <= 0)
            {
                return 60;
            }

            return Math.Clamp(width / 3, 24, 60);
        }
        catch
        {
            // Console width may be unavailable in some hosts.
            return 60;
        }
    }

    private static void PrintUsage(long inputTokens, long outputTokens)
    {
        AnsiConsole.MarkupLine($"[blue]↑ {inputTokens} input[/] [green]↓ {outputTokens} output[/]");
    }

    private void FlushPendingUsageIfAny()
    {
        if (!_hasPendingUsage)
        {
            return;
        }

        PrintUsage(_pendingInputTokens, _pendingOutputTokens);
        _hasPendingUsage = false;
        _pendingInputTokens = 0;
        _pendingOutputTokens = 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _eventQueue.Writer.TryComplete();
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();

        try
        {
            _renderTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
            // Ignore errors during disposal
        }
    }
}

