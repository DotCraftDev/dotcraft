using DotCraft.Agents;
using DotCraft.CLI.Rendering;
using DotCraft.Hooks;
using DotCraft.Protocol;
using DotCraft.Security;
using DotCraft.Tracing;

namespace DotCraft.CLI;

/// <summary>
/// In-process implementation of <see cref="ICliSession"/> that wraps <see cref="ISessionService"/>.
/// Used when the CLI runs the agent stack in the same process (development mode / fallback).
/// Approval prompts are handled interactively via <see cref="ConsoleApprovalService"/> and
/// <see cref="AgentRenderer.ExecuteWhilePausedAsync"/>.
/// </summary>
public sealed class InProcessCliSession(
    ISessionService sessionService,
    AgentFactory? agentFactory = null,
    TokenUsageStore? tokenUsageStore = null,
    HookRunner? hookRunner = null) : ICliSession
{
    // -------------------------------------------------------------------------
    // ICliSession implementation
    // -------------------------------------------------------------------------

    public async Task<string> CreateThreadAsync(SessionIdentity identity, CancellationToken ct = default)
    {
        var thread = await sessionService.CreateThreadAsync(identity, ct: ct);
        return thread.Id;
    }

    public async Task<SessionWireThread> ResumeThreadAsync(string threadId, CancellationToken ct = default)
    {
        var thread = await sessionService.ResumeThreadAsync(threadId, ct);
        // Map to wire DTO so callers can use a single type regardless of session backend
        return thread.ToWire(includeTurns: true);
    }

    public Task<IReadOnlyList<ThreadSummary>> FindThreadsAsync(SessionIdentity identity, CancellationToken ct = default)
        => sessionService.FindThreadsAsync(identity, ct: ct);

    public Task ArchiveThreadAsync(string threadId, CancellationToken ct = default)
        => sessionService.ArchiveThreadAsync(threadId, ct);

    public Task DeleteThreadAsync(string threadId, CancellationToken ct = default)
        => sessionService.DeleteThreadPermanentlyAsync(threadId, ct);

    public Task SetThreadModeAsync(string threadId, string mode, CancellationToken ct = default)
        => sessionService.SetThreadModeAsync(threadId, mode, ct);

    /// <summary>
    /// Submits user input to the given thread and renders the turn to the console.
    /// Creates an <see cref="AgentRenderer"/>, wires up session event callbacks, and runs until
    /// the turn completes or is cancelled.
    /// </summary>
    public async Task RunTurnAsync(string threadId, string input, CancellationToken ct = default)
    {
        var tokenTracker = agentFactory?.GetOrCreateTokenTracker(threadId);
        using var renderer = new AgentRenderer(tokenTracker);
        await renderer.StartAsync(ct);
        await renderer.SendEventAsync(RenderEvent.StreamStart(), ct);
        ConsoleApprovalService.SetRenderControl(renderer);
        if (hookRunner != null)
            hookRunner.DebugLogger = renderer.TryEnqueueDebug;

        try
        {
            var handler = new SessionEventHandler
            {
                OnTextDelta = text => renderer.SendEventAsync(RenderEvent.Response(text), ct).AsTask(),

                OnReasoningDelta = reasoning =>
                    renderer.SendEventAsync(RenderEvent.Thinking("💭", "Thinking", reasoning), ct).AsTask(),

                OnToolStarted = async (name, icon, formatted, callId) =>
                    await renderer.SendEventAsync(
                        RenderEvent.ToolStarted(icon, name, string.Empty, null, formatted, callId: callId), ct),

                OnToolCompleted = (callId, result) =>
                    renderer.SendEventAsync(
                        RenderEvent.ToolCompleted(null, null, string.Empty, result, callId: callId), ct).AsTask(),

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
                            RenderEvent.TokenUsage(usage.InputTokens, usage.OutputTokens, usage.TotalTokens), ct);

                        tokenUsageStore?.Record(new TokenUsageRecord
                        {
                            Channel = "cli",
                            UserId = "local",
                            DisplayName = "CLI",
                            InputTokens = usage.InputTokens,
                            OutputTokens = usage.OutputTokens
                        });
                    }

                    await renderer.SendEventAsync(RenderEvent.Completed(string.Empty), ct);
                },

                OnTurnFailed = async errMsg =>
                {
                    await renderer.SendEventAsync(RenderEvent.ErrorEvent(errMsg), ct);
                    await renderer.SendEventAsync(RenderEvent.Completed(string.Empty), ct);
                },

                OnSystemEvent = async sysEvt =>
                {
                    switch (sysEvt.Kind)
                    {
                        case "compacting":
                            await renderer.SendEventAsync(
                                RenderEvent.SystemInfoEvent(sysEvt.Message ?? "Compacting context..."), ct);
                            break;
                        case "compacted":
                            await renderer.SendEventAsync(
                                RenderEvent.SystemInfoEvent(sysEvt.Message ?? "Context compacted successfully."), ct);
                            break;
                        case "compactSkipped":
                            await renderer.SendEventAsync(
                                RenderEvent.SystemInfoEvent(sysEvt.Message ?? "Context compaction skipped."), ct);
                            break;
                        case "consolidating":
                            await renderer.SendEventAsync(
                                RenderEvent.SystemStatusEvent(
                                    sysEvt.Message ?? "Consolidating memory...",
                                    "Memory consolidation complete."), ct);
                            break;
                        case "consolidated":
                            await renderer.SendEventAsync(
                                RenderEvent.SystemInfoEvent(sysEvt.Message ?? "Memory consolidation complete."), ct);
                            break;
                    }
                }
            };

            await handler.ProcessAsync(
                sessionService.SubmitInputAsync(threadId, input, ct: ct),
                (thId, tid, rid, decision) => sessionService.ResolveApprovalAsync(thId, tid, rid, decision, ct),
                ct);
        }
        finally
        {
            ConsoleApprovalService.SetRenderControl(null);
            if (hookRunner != null)
                hookRunner.DebugLogger = null;
        }

        await renderer.StopAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
