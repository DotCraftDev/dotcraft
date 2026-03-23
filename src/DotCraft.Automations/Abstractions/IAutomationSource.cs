using DotCraft.Agents;
using DotCraft.Protocol;

namespace DotCraft.Automations.Abstractions;

/// <summary>
/// Plug-in contract for automation task sources.
/// Implementations provide tasks and respond to lifecycle transitions.
/// </summary>
public interface IAutomationSource
{
    /// <summary>Unique name for this source instance (used in config and routing).</summary>
    string Name { get; }

    /// <summary>
    /// Name of the tool profile to register for tasks produced by this source.
    /// The profile is registered via <see cref="RegisterToolProfile"/> before the first dispatch.
    /// </summary>
    string ToolProfileName { get; }

    /// <summary>
    /// Called once at startup. The source must call <c>registry.Register(ToolProfileName, providers)</c>.
    /// </summary>
    void RegisterToolProfile(IToolProfileRegistry registry);

    /// <summary>Returns tasks eligible for dispatch (status == Pending).</summary>
    Task<IReadOnlyList<AutomationTask>> GetPendingTasksAsync(CancellationToken ct);

    /// <summary>
    /// Returns the workflow definition (agent prompts, metadata, round limit)
    /// for a specific task.
    /// </summary>
    Task<AutomationWorkflowDefinition> GetWorkflowAsync(AutomationTask task, CancellationToken ct);

    /// <summary>Called when the orchestrator transitions a task to a new status.</summary>
    Task OnStatusChangedAsync(AutomationTask task, AutomationTaskStatus newStatus, CancellationToken ct);

    /// <summary>
    /// Called when the agent completes (rounds exhausted or completion sentinel detected).
    /// The source stores the summary for later display in the review panel.
    /// </summary>
    Task OnAgentCompletedAsync(AutomationTask task, string agentSummary, CancellationToken ct);

    /// <summary>
    /// Called after each completed turn. Sources may reload external state (e.g. task.md).
    /// Return true to stop the workflow loop before the next step (e.g. <c>agent_completed</c> sentinel).
    /// </summary>
    Task<bool> ShouldStopWorkflowAfterTurnAsync(AutomationTask task, CancellationToken ct) =>
        Task.FromResult(false);

    /// <summary>Returns all tasks known to this source, regardless of status.</summary>
    Task<IReadOnlyList<AutomationTask>> GetAllTasksAsync(CancellationToken ct);

    /// <summary>
    /// Approves a task in <see cref="AutomationTaskStatus.AwaitingReview"/> status.
    /// Source-specific side effects (e.g. merge PR, run hook) are executed.
    /// </summary>
    Task ApproveTaskAsync(string taskId, CancellationToken ct);

    /// <summary>
    /// Rejects a task in <see cref="AutomationTaskStatus.AwaitingReview"/> status.
    /// Source-specific side effects (e.g. post comment, run hook) are executed.
    /// </summary>
    Task RejectTaskAsync(string taskId, string? reason, CancellationToken ct);
}
