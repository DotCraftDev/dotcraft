# M3 — Automations Core: Project Skeleton, Abstractions & Orchestrator

| Field | Value |
|-------|-------|
| **Milestone** | M3 |
| **Title** | Automations Core: Project Skeleton, Abstractions & Orchestrator |
| **Status** | Pending |
| **Parent Spec** | [Automations Lifecycle](../automations-lifecycle.md) §4, §5, §10, §13.1, §16–17 |
| **Depends On** | M1, M2 |
| **Blocks** | M4, M5, M6 |

## Overview

This milestone creates the `DotCraft.Automations` project and establishes all the shared abstractions and core infrastructure that both the local task source (M4) and the GitHub migration (M5) will build on. It does **not** implement any automation source (no local files, no GitHub); it builds the framework that sources plug into.

Key deliverables:

1. **`DotCraft.Automations` project** — csproj, DI module, folder layout.
2. **`IAutomationSource` / `AutomationTask` abstractions** — the source plug-in contract.
3. **`AutomationOrchestrator`** — the generalized scheduling loop: poll sources, dispatch tasks, manage per-task thread lifecycle.
4. **`AutomationSessionClient`** — the in-process adapter that wraps `ISessionService` so the orchestrator drives agents through the shared AppServer session infrastructure.
5. **`AutomationWorkspaceManager`** — provisions and cleans up isolated workspace directories for tasks.
6. **`IAutomationsChannelService`** integration — registers the orchestrator as an `IChannelService` so it starts/stops with the AppServer lifecycle.

## Scope

### In Scope

- `DotCraft.Automations` csproj added to the solution.
- `IAutomationSource<TTask>` and base `AutomationTask` model.
- `AutomationsConfig` — root configuration class (list of sources, polling intervals, workspace root).
- `AutomationOrchestrator` — core dispatch loop.
- `AutomationSessionClient` — in-process `ISessionService` wrapper.
- `AutomationWorkspaceManager` — workspace directory lifecycle.
- `AutomationsChannelService : IChannelService` — lifecycle integration.
- `AutomationsModule` — DI registrations.
- Registration of the module in `DotCraft.App`.

### Out of Scope

- `LocalAutomationSource` — covered in M4.
- `GitHubAutomationSource` — covered in M5.
- `automation/*` Wire Protocol methods — covered in M6.
- Desktop UI — covered in M7/M8.

## Requirements

### R3.1 — Project Layout

```
src/DotCraft.Automations/
  DotCraft.Automations.csproj
  AutomationsModule.cs
  AutomationsConfig.cs
  Abstractions/
    IAutomationSource.cs
    AutomationTask.cs
    AutomationTaskStatus.cs
    IAutomationsChannelService.cs
  Orchestrator/
    AutomationOrchestrator.cs
    OrchestratorState.cs
  Protocol/
    AutomationSessionClient.cs
  Workspace/
    AutomationWorkspaceManager.cs
```

### R3.2 — AutomationTask base model

```csharp
/// <summary>
/// Source-agnostic representation of a unit of automation work.
/// Each IAutomationSource produces a concrete subclass.
/// </summary>
public abstract class AutomationTask
{
    /// <summary>Stable unique identifier, unique within its source.</summary>
    public required string Id { get; init; }

    /// <summary>
    /// Human-readable title for display in the Desktop Automations view.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>Current lifecycle state of the task.</summary>
    public AutomationTaskStatus Status { get; set; }

    /// <summary>
    /// Name of the IAutomationSource that owns this task.
    /// Used to route lifecycle calls back to the correct source.
    /// </summary>
    public required string SourceName { get; init; }

    /// <summary>
    /// Thread identifier of the active agent session for this task, if any.
    /// Null when no agent session has been started.
    /// </summary>
    public string? ThreadId { get; set; }
}
```

`AutomationTaskStatus` enum values: `Pending`, `Dispatched`, `AgentRunning`, `AgentCompleted`, `AwaitingReview`, `Approved`, `Rejected`, `Failed`.

### R3.3 — IAutomationSource contract

```csharp
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
    /// The profile is registered via RegisterToolProfile before the first dispatch.
    /// </summary>
    string ToolProfileName { get; }

    /// <summary>
    /// Called once at startup. The source must call registry.Register(ToolProfileName, providers).
    /// </summary>
    void RegisterToolProfile(IToolProfileRegistry registry);

    /// <summary>Returns tasks eligible for dispatch (status == Pending).</summary>
    Task<IReadOnlyList<AutomationTask>> GetPendingTasksAsync(CancellationToken ct);

    /// <summary>
    /// Returns the workflow definition (agent prompts, metadata, round limit)
    /// for a specific task.
    /// </summary>
    Task<WorkflowDefinition> GetWorkflowAsync(AutomationTask task, CancellationToken ct);

    /// <summary>Called when the orchestrator transitions a task to a new status.</summary>
    Task OnStatusChangedAsync(AutomationTask task, AutomationTaskStatus newStatus, CancellationToken ct);

    /// <summary>
    /// Called when the agent completes (rounds exhausted or completion sentinel detected).
    /// The source stores the summary for later display in the review panel.
    /// </summary>
    Task OnAgentCompletedAsync(AutomationTask task, string agentSummary, CancellationToken ct);
}
```

### R3.4 — AutomationsConfig

```csharp
public class AutomationsConfig
{
    /// <summary>Root directory under which per-task workspace directories are created.</summary>
    public string WorkspaceRoot { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".craft", "automations", "workspaces");

    /// <summary>How often the orchestrator polls each source for new tasks.</summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum concurrent tasks being dispatched across all sources.
    /// Additional tasks wait in Pending state.
    /// </summary>
    public int MaxConcurrentTasks { get; set; } = 3;
}
```

### R3.5 — AutomationWorkspaceManager

Manages isolated workspace directories for tasks:

```csharp
public class AutomationWorkspaceManager
{
    /// <summary>
    /// Creates a new workspace directory for the task and returns its path.
    /// Path: {WorkspaceRoot}/{sourceName}/{taskId}/
    /// </summary>
    public Task<string> ProvisionAsync(AutomationTask task, CancellationToken ct);

    /// <summary>
    /// Returns the workspace path for a task if it exists, or null.
    /// </summary>
    public string? GetExisting(AutomationTask task);

    /// <summary>
    /// Deletes the workspace directory for the task.
    /// Called after approval/rejection if cleanup is configured.
    /// </summary>
    public Task CleanupAsync(AutomationTask task, CancellationToken ct);
}
```

### R3.6 — AutomationSessionClient

An in-process adapter over `ISessionService` that enables the orchestrator to create and drive agent threads while ensuring events are routed through the shared `ThreadEventBroker`:

```csharp
/// <summary>
/// In-process wrapper over ISessionService for use by AutomationOrchestrator.
/// Provides a high-level async API for creating threads and submitting turns,
/// and exposes an IAsyncEnumerable for observing thread events.
/// </summary>
public class AutomationSessionClient
{
    public AutomationSessionClient(ISessionService sessionService, ThreadEventBroker eventBroker);

    /// <summary>
    /// Creates a new thread (or resumes an existing one with the same userId).
    /// Configures WorkspaceOverride, ToolProfile, and ApprovalPolicy as specified.
    /// Returns the assigned threadId.
    /// </summary>
    public Task<string> CreateOrResumeThreadAsync(
        string channelName,
        string userId,
        ThreadConfiguration config,
        CancellationToken ct);

    /// <summary>
    /// Submits a turn to the thread and streams back SessionEvents until the
    /// turn completes (AgentTurnCompleted or Interrupted event).
    /// </summary>
    public IAsyncEnumerable<SessionEvent> SubmitTurnAsync(
        string threadId,
        string message,
        CancellationToken ct);

    /// <summary>
    /// Interrupts an active turn on the thread.
    /// </summary>
    public Task InterruptAsync(string threadId, CancellationToken ct);
}
```

The orchestrator thread identity convention is:
- `channelName`: `"automations"`
- `userId`: `"task-{task.Id}"`
- `workspacePath`: process-level AppServer workspace (for thread registration/discoverability)
- `config.WorkspaceOverride`: the task-specific workspace provisioned by `AutomationWorkspaceManager`

### R3.7 — AutomationOrchestrator dispatch loop

The orchestrator runs as a background service inside the AppServer process:

```
loop every PollingInterval:
  for each registered IAutomationSource:
    pendingTasks = source.GetPendingTasksAsync()
    for each task in pendingTasks (up to MaxConcurrentTasks):
      if task.Id not in activeTaskSet:
        DispatchTask(source, task)

async DispatchTask(source, task):
  workspacePath = workspaceManager.ProvisionAsync(task)
  threadId = sessionClient.CreateOrResumeThreadAsync(
      channelName: "automations",
      userId: "task-{task.Id}",
      config: {
        WorkspaceOverride: workspacePath,
        ToolProfile: source.ToolProfileName,
        ApprovalPolicy: AutoApprove
      })
  task.ThreadId = threadId
  source.OnStatusChangedAsync(task, AgentRunning)

  workflow = source.GetWorkflowAsync(task)
  for each step in workflow.Steps:
    async for event in sessionClient.SubmitTurnAsync(threadId, step.Prompt):
      if event is AgentTurnCompleted:
        if source detects completion sentinel in task state:
          goto AgentCompleted
      if event is Interrupted:
        goto AgentCompleted
    if round > workflow.MaxRounds:
      goto AgentCompleted

AgentCompleted:
  summary = extract from last agent message
  source.OnAgentCompletedAsync(task, summary)
  source.OnStatusChangedAsync(task, AwaitingReview)
  activeTaskSet.Remove(task.Id)
```

### R3.8 — AutomationsChannelService

Implements `IChannelService` so the `GatewayHost` starts the orchestrator background loop and calls `RegisterToolProfile` on each source:

```csharp
public class AutomationsChannelService : IChannelService
{
    public Task StartAsync(CancellationToken ct);   // starts orchestrator loop, registers profiles
    public Task StopAsync(CancellationToken ct);    // cancels loop, awaits clean shutdown
}
```

### R3.9 — AutomationsModule DI registration

`AutomationsModule` registers:
- `AutomationsConfig` from appsettings `"Automations"` section.
- `AutomationWorkspaceManager` as scoped.
- `AutomationSessionClient` as scoped.
- `AutomationOrchestrator` as singleton.
- `AutomationsChannelService` as `IChannelService` singleton.

`DotCraft.App`'s `HostBuilder` registers `AutomationsModule` alongside other modules.

## Acceptance Criteria

| # | Criterion |
|---|-----------|
| AC1 | `DotCraft.Automations` project builds cleanly with no warnings. |
| AC2 | A test `IAutomationSource` implementation can be registered and polled by the orchestrator. |
| AC3 | `DispatchTask` calls `AutomationSessionClient.CreateOrResumeThreadAsync` with `channelName = "automations"` and `userId = "task-{id}"`. |
| AC4 | The thread created by `CreateOrResumeThreadAsync` is returned by `thread/list` filtered by `channelName = "automations"`. |
| AC5 | The orchestrator respects `MaxConcurrentTasks`; excess tasks remain `Pending` until a slot is free. |
| AC6 | `AutomationsChannelService.StartAsync` calls `RegisterToolProfile` on all registered sources before entering the dispatch loop. |
| AC7 | Stopping the AppServer cleanly stops the orchestrator loop without hanging. |
| AC8 | `AutomationWorkspaceManager.ProvisionAsync` creates `{WorkspaceRoot}/{sourceName}/{taskId}/` on disk. |

## Affected Files

| File | Change |
|------|--------|
| `src/DotCraft.Automations/DotCraft.Automations.csproj` | New project |
| `src/DotCraft.Automations/AutomationsModule.cs` | New: DI registrations |
| `src/DotCraft.Automations/AutomationsConfig.cs` | New: configuration model |
| `src/DotCraft.Automations/Abstractions/IAutomationSource.cs` | New: source contract |
| `src/DotCraft.Automations/Abstractions/AutomationTask.cs` | New: base task model |
| `src/DotCraft.Automations/Abstractions/AutomationTaskStatus.cs` | New: status enum |
| `src/DotCraft.Automations/Orchestrator/AutomationOrchestrator.cs` | New: dispatch loop |
| `src/DotCraft.Automations/Orchestrator/OrchestratorState.cs` | New: in-memory state |
| `src/DotCraft.Automations/Protocol/AutomationSessionClient.cs` | New: in-process adapter |
| `src/DotCraft.Automations/Workspace/AutomationWorkspaceManager.cs` | New: workspace lifecycle |
| `src/DotCraft.Automations/AutomationsChannelService.cs` | New: IChannelService impl |
| `src/DotCraft.App/Hosting/HostBuilder.cs` | Register `AutomationsModule` |
| `DotCraft.sln` | Add new project |
