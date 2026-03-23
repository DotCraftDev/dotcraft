# M5 — Automations: GitHub Source Migration

| Field | Value |
|-------|-------|
| **Milestone** | M5 |
| **Title** | Automations: Migrate GitHubTracker to IAutomationSource |
| **Status** | Pending |
| **Parent Spec** | [Automations Lifecycle](../automations-lifecycle.md) §5.4, §17.2, §18 |
| **Depends On** | M3 |
| **Blocks** | M6 (partial) |

## Overview

This milestone migrates `DotCraft.GitHubTracker` from its current standalone orchestration model to a thin `IAutomationSource` implementation that plugs into the `AutomationOrchestrator` introduced in M3.

Today, `GitHubTracker`:
- Has its own `GitHubTrackerOrchestrator` that drives a private `SessionService` instance.
- Has its own `IChannelService` (`GitHubTrackerChannelService`) that starts/stops independently.
- Is invisible to the AppServer's `ThreadEventBroker` (Desktop cannot observe its threads).
- Hard-codes an `AutoApproveApprovalService` preventing human review.

After this milestone:
- `GitHubTrackerOrchestrator` is **deleted**; the `AutomationOrchestrator` owns scheduling.
- `GitHubTrackerChannelService` is **deleted**; `AutomationsChannelService` owns the lifecycle.
- A new `GitHubAutomationSource : IAutomationSource` replaces both, adapting the existing `IWorkItemTracker` and workflow machinery.
- All GitHub task threads are created through `AutomationSessionClient` and are therefore visible to the Desktop.
- PR/issue review gate now uses `ApproveTaskAsync` / `RejectTaskAsync` instead of the GitHub-specific post-comment flow, allowing human review via the Desktop panel (M8).

The goal is **feature parity** with today's GitHubTracker plus Desktop observability. No GitHub API surface changes.

## Scope

### In Scope

- New `GitHubAutomationTask : AutomationTask` model.
- New `GitHubAutomationSource : IAutomationSource` implementation.
- Deleting `GitHubTrackerOrchestrator` and `GitHubTrackerChannelService`.
- Moving `WorkItemAgentRunnerFactory` logic into `GitHubAutomationSource.RegisterToolProfile`.
- Adapting `WorkItemWorkspaceManager` to work alongside `AutomationWorkspaceManager` (or delegating to it).
- Updating `GitHubTrackerModule` to deregister the deleted services and register `GitHubAutomationSource` with `AutomationsModule`.
- Ensuring backward compatibility: existing `GitHubTrackerConfig` and `workflow.md` files continue to work unchanged.

### Out of Scope

- Changes to GitHub API calls or `IWorkItemTracker` interface.
- Desktop GitHub-specific UI — M7/M8 cover the shared automation UI that GitHub tasks use too.
- Wire Protocol `automation/*` methods — M6.

## Requirements

### R5.1 — GitHubAutomationTask model

```csharp
public class GitHubAutomationTask : AutomationTask
{
    /// <summary>GitHub repository owner/name, e.g. "org/repo".</summary>
    public required string RepositoryFullName { get; init; }

    /// <summary>GitHub issue or PR number.</summary>
    public required int IssueNumber { get; init; }

    /// <summary>Whether this work item is a PR or an issue.</summary>
    public required WorkItemKind Kind { get; init; }

    /// <summary>The underlying tracked work item from IWorkItemTracker.</summary>
    public required TrackedWorkItem WorkItem { get; init; }
}
```

### R5.2 — GitHubAutomationSource

```csharp
public class GitHubAutomationSource : IAutomationSource
{
    public string Name => "github";
    public string ToolProfileName => "github-task";

    public void RegisterToolProfile(IToolProfileRegistry registry);
    // Registers source-specific tools only (SubmitReview, CompleteIssue).
    // Standard tools (file I/O, shell, web) are provided by Session Core via tool profile merging.

    public Task<IReadOnlyList<AutomationTask>> GetPendingTasksAsync(CancellationToken ct);
    // Delegates to IWorkItemTracker.GetOpenWorkItemsAsync,
    // maps TrackedWorkItem → GitHubAutomationTask with status Pending

    public Task<WorkflowDefinition> GetWorkflowAsync(AutomationTask task, CancellationToken ct);
    // Delegates to existing WorkflowLoader for the repository's workflow.md

    public Task OnStatusChangedAsync(AutomationTask task, AutomationTaskStatus newStatus, CancellationToken ct);
    // Maps generic status changes to GitHub-specific side effects:
    //   AgentRunning → post "DotCraft is working on this..." comment (optional, configurable)
    //   AwaitingReview → post "Agent completed. Human review required." comment (optional)
    //   Approved → merge PR / close issue per workflow config
    //   Rejected → post rejection reason comment

    public Task OnAgentCompletedAsync(AutomationTask task, string agentSummary, CancellationToken ct);
    // Stores agentSummary in OrchestratorState / in-memory; no persistence to GitHub unless approved.
}
```

### R5.3 — Completion detection for GitHub tasks

GitHub tasks do not use a `status:` sentinel in a task file. Instead, the orchestrator detects completion the same way `GitHubTrackerOrchestrator` does today: by inspecting the last agent message for a `##DotCraftComplete##` token or by exhausting `workflow.MaxRounds`.

`GitHubAutomationSource` overrides a virtual method `DetectCompletion(AgentMessage lastMessage): bool` that the orchestrator calls after each turn instead of reading a file.

Alternatively, `IAutomationSource` gains an optional method:

```csharp
/// <summary>
/// Optional. Returns true when the source determines the agent has completed
/// its work, based on the last agent message. Called after each turn.
/// Default implementation always returns false (orchestrator relies on sentinel state).
/// </summary>
virtual bool IsCompletedAfterTurn(AutomationTask task, string lastAgentMessage) => false;
```

`GitHubAutomationSource` overrides this to check for `##DotCraftComplete##`.

### R5.4 — Template variable aliasing

`workflow.md` files for GitHub tasks continue to use `work_item.*` template variables. `LocalWorkflowLoader` / `GitHubWorkflowLoader` both alias `task.*` → `work_item.*` for backward compatibility. No changes to existing workflow files are required.

### R5.5 — Remove GitHubTrackerOrchestrator

`GitHubTrackerOrchestrator.cs` is deleted. Its logic is now split between:
- Scheduling/dispatch: `AutomationOrchestrator` (M3).
- GitHub-specific side effects: `GitHubAutomationSource.OnStatusChangedAsync`.
- Completion detection: `GitHubAutomationSource.IsCompletedAfterTurn`.
- Workspace management: `AutomationWorkspaceManager` (M3).

### R5.6 — Remove GitHubTrackerChannelService

`GitHubTrackerChannelService.cs` is deleted. `AutomationsChannelService` (M3) is the sole `IChannelService` for all automations, including GitHub tasks.

`GitHubTrackerModule` no longer registers `IChannelService`.

### R5.7 — GitHubTrackerModule update

`GitHubTrackerModule`:
1. Removes registration of `GitHubTrackerOrchestrator`, `GitHubTrackerChannelService`, and `WorkItemAgentRunnerFactory`.
2. Registers `GitHubAutomationSource` as an `IAutomationSource` in the DI container.
3. Remains responsible for registering GitHub-specific services (`IWorkItemTracker`, `GitHubClient`, etc.).

`AutomationsModule` discovers all `IAutomationSource` implementations from the DI container, so no changes to `AutomationsModule` are needed.

### R5.8 — Desktop observability restored

Because GitHub task threads are now created via `AutomationSessionClient.CreateOrResumeThreadAsync` (which uses the shared `ISessionService` and `ThreadEventBroker`), Desktop clients subscribed to `session/events` for `channelName = "automations"` receive GitHub task events in real time. No additional wiring is required.

### R5.9 — OrchestratorState migration

`GitHubTrackerOrchestrator` maintained an in-memory `OrchestratorState` to track active threads and prevent duplicate dispatches. This state is superseded by the `AutomationOrchestrator`'s `OrchestratorState` (M3). `GitHubTracker`'s `OrchestratorState.cs` is deleted.

### R5.10 — Source-specific workspace provisioning

`IAutomationSource` gains an optional `ProvisionWorkspaceAsync` method (default returns `null`). The orchestrator calls it before falling back to the generic `AutomationWorkspaceManager`. `GitHubAutomationSource` implements this by delegating to `WorkItemWorkspaceManager.EnsureWorkspaceAsync`, which performs:

1. `git clone` of the repository into the per-work-item workspace.
2. For PRs: `git fetch` + `git checkout` of the PR's head branch.
3. Running the `after_create` hook if the workspace was newly created.

This preserves the full workspace lifecycle from the old `GitHubTrackerOrchestrator` within the unified Automations dispatch flow.

### R5.11 — Tool profile merge semantics

Tool profiles registered by `IAutomationSource.RegisterToolProfile` contain **only source-specific tools** (e.g. `SubmitReview`, `CompleteIssue`, `CompleteLocalTask`). Session Core merges these with the standard agent tools (file I/O, shell, web, sandbox) at thread creation time. Sources must NOT include `CoreToolProvider` in their profiles to avoid duplication.

## Acceptance Criteria

| # | Criterion |
|---|-----------|
| AC1 | `DotCraft.GitHubTracker` builds without errors after the deletion of `GitHubTrackerOrchestrator` and `GitHubTrackerChannelService`. |
| AC2 | GitHub work items are polled via `GitHubAutomationSource.GetPendingTasksAsync` and dispatched by `AutomationOrchestrator`. |
| AC3 | A GitHub task thread is visible in `thread/list` with `channelName = "automations"`. |
| AC4 | `##DotCraftComplete##` in the last agent message causes `IsCompletedAfterTurn` to return true, stopping the dispatch loop. |
| AC5 | `OnStatusChangedAsync` for `Approved` executes the merge/close action on GitHub. |
| AC6 | Existing `workflow.md` files using `work_item.*` variables continue to work without modification. |
| AC7 | No `IChannelService` registration remains in `GitHubTrackerModule`. |
| AC8 | `AutomationsChannelService.StartAsync` starts GitHub polling alongside local task polling. |
| AC9 | Desktop `session/events` stream receives events from GitHub task threads. |

## Affected Files

| File | Change |
|------|--------|
| `src/DotCraft.GitHubTracker/Orchestrator/GitHubTrackerOrchestrator.cs` | **Deleted** |
| `src/DotCraft.GitHubTracker/Orchestrator/OrchestratorState.cs` | **Deleted** |
| `src/DotCraft.GitHubTracker/GitHubTrackerChannelService.cs` | **Deleted** |
| `src/DotCraft.GitHubTracker/Execution/WorkItemAgentRunnerFactory.cs` | **Deleted** (logic moved) |
| `src/DotCraft.GitHubTracker/GitHub/GitHubAutomationTask.cs` | New |
| `src/DotCraft.GitHubTracker/GitHub/GitHubAutomationSource.cs` | New |
| `src/DotCraft.GitHubTracker/GitHubTrackerModule.cs` | Remove deleted registrations; add `GitHubAutomationSource` |
| `src/DotCraft.Automations/Abstractions/IAutomationSource.cs` | Add `IsCompletedAfterTurn` virtual method |
