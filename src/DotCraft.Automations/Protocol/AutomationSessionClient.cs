using DotCraft.Hosting;
using DotCraft.Protocol;

namespace DotCraft.Automations.Protocol;

/// <summary>
/// In-process wrapper over <see cref="ISessionService"/> for use by the automations orchestrator.
/// </summary>
public sealed class AutomationSessionClient(ISessionService sessionService, DotCraftPaths paths)
{
    /// <summary>Host project workspace root (same as <see cref="SessionIdentity.WorkspacePath"/> for automations).</summary>
    public string ProjectWorkspacePath => paths.WorkspacePath;

    /// <summary>
    /// Creates a new thread or resumes an existing one for the same workspace + channel + user.
    /// Configures <see cref="ThreadConfiguration"/> (workspace override, tool profile, approval policy).
    /// </summary>
    public async Task<string> CreateOrResumeThreadAsync(
        string channelName,
        string userId,
        ThreadConfiguration config,
        CancellationToken ct,
        string? displayName = null)
    {
        var identity = new SessionIdentity
        {
            ChannelName = channelName,
            UserId = userId,
            WorkspacePath = paths.WorkspacePath
        };

        var existing = await sessionService.FindThreadsAsync(identity, includeArchived: false, ct: ct);
        var match = existing.FirstOrDefault(t =>
            string.Equals(t.OriginChannel, channelName, StringComparison.OrdinalIgnoreCase));

        if (match != null)
        {
            await sessionService.UpdateThreadConfigurationAsync(match.Id, config, ct);
            await sessionService.EnsureThreadLoadedAsync(match.Id, ct);
            return match.Id;
        }

        var thread = await sessionService.CreateThreadAsync(
            identity,
            config,
            displayName: displayName,
            ct: ct);
        return thread.Id;
    }

    /// <summary>
    /// Submits a turn and yields session events until the turn reaches a terminal state.
    /// </summary>
    public async IAsyncEnumerable<SessionEvent> SubmitTurnAsync(
        string threadId,
        string message,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var evt in sessionService.SubmitInputAsync(threadId, message, sender: null, messages: null, ct))
        {
            yield return evt;
            if (evt.EventType is SessionEventType.TurnCompleted
                or SessionEventType.TurnFailed
                or SessionEventType.TurnCancelled)
                yield break;
        }
    }

    /// <summary>
    /// Cancels the active turn on the thread, if any.
    /// </summary>
    public async Task InterruptAsync(string threadId, CancellationToken ct)
    {
        var thread = await sessionService.GetThreadAsync(threadId, ct);
        var running = thread.Turns.LastOrDefault(t =>
            t.Status is TurnStatus.Running or TurnStatus.WaitingApproval);
        if (running != null)
            await sessionService.CancelTurnAsync(threadId, running.Id, ct);
    }
}
