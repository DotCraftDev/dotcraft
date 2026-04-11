# DotCraft Automations Lifecycle Specification

| Field | Value |
|-------|-------|
| **Version** | 0.2.3 |
| **Status** | Draft |
| **Date** | 2026-03-24 |
| **Parent Spec** | Symphony SPEC (GitHubTracker Orchestrator §7–8), [PR Review Lifecycle](pr-review-lifecycle.md), [AppServer Protocol](appserver-protocol.md), [Session Core](session-core.md) |

Defines the architecture and lifecycle of the DotCraft Automations module — a platform-agnostic automation framework that generalizes the existing GitHubTracker into a multi-source orchestrator supporting both local tasks and remote work-item tracking (GitHub Issues/PRs). The orchestrator runs inside the AppServer process and delegates agent execution to the shared `SessionService`, making all automation activity naturally visible to the Desktop client through the standard Wire Protocol event pipeline. Local tasks introduce a human review gate that requires explicit user approval before changes are accepted.

---

## Table of Contents

- [1. Scope](#1-scope)
- [2. Goals and Non-Goals](#2-goals-and-non-goals)
- [3. Architecture Overview](#3-architecture-overview)
- [4. Automation Source Abstraction](#4-automation-source-abstraction)
- [5. AutomationTask Model](#5-automationtask-model)
- [6. Local Task Source](#6-local-task-source)
- [7. Local Task Lifecycle State Machine](#7-local-task-lifecycle-state-machine)
- [8. Human Review Gate](#8-human-review-gate)
- [9. Session Core Extensions](#9-session-core-extensions)
- [10. Orchestrator Generalization](#10-orchestrator-generalization)
- [11. Workflow Format](#11-workflow-format)
- [12. Workspace Management](#12-workspace-management)
- [13. Agent Execution](#13-agent-execution)
- [14. Wire Protocol Extensions](#14-wire-protocol-extensions)
- [15. Desktop Integration](#15-desktop-integration)
- [16. Configuration](#16-configuration)
  - [16.4 Observability, Logging, and Diagnostics](#164-observability-logging-and-diagnostics)
- [17. Module Structure](#17-module-structure)
- [18. Migration Path](#18-migration-path)

---

## 1. Scope

### 1.1 What This Spec Defines

- The Automation Source abstraction (`IAutomationSource`) that decouples the orchestrator from any specific tracker backend.
- The `AutomationTask` data model that unifies local tasks, GitHub Issues, and GitHub Pull Requests under a single type.
- The Local Task Source: file-based task storage, candidate discovery, and completion semantics.
- The local task lifecycle state machine, including the mandatory human review gate.
- The human review flow: how tasks enter review, what decisions the user can make, and how those decisions affect task state.
- The Session Core extensions required to support per-thread workspace scoping, tool profiles, and approval policies.
- The generalized orchestrator (`AutomationOrchestrator`) that accepts multiple sources and dispatches agents via the shared `SessionService`.
- Wire Protocol extensions for automation task management from Desktop and other clients.
- Desktop Automations view layout and interaction model.
- Configuration model and migration path from the existing `GitHubTracker` module.

### 1.2 What This Spec Does Not Define

- The GitHub-specific PR review lifecycle (HEAD SHA tracking, COMMENT-only policy, re-review triggers). Those are governed by the [PR Review Lifecycle Spec](pr-review-lifecycle.md), where `SubmitReview` is defined as the structured `summaryJson/commentsJson` contract.
- The general orchestrator reconciliation loop, poll tick mechanics, retry queue, or stall detection. Those are governed by the Symphony SPEC (§7–8). This spec references but does not redefine them.
- The internal Session Core execution pipeline (agent streaming, tool invocation, memory management). Those are Session Core implementation details defined in [Session Core](session-core.md).
- Desktop visual design tokens, typography, or animations. Those are governed by the [Desktop Client Spec](desktop-client.md) §15.

---

## 2. Goals and Non-Goals

### 2.1 Goals

1. **Source-agnostic orchestration**: The automation orchestrator must not contain any GitHub-specific logic. GitHub becomes one pluggable source among potentially many.
2. **Local task support**: Users can create and manage tasks that run entirely locally, with no external tracker dependency.
3. **Human-in-the-loop review**: Local tasks require explicit user approval before changes are accepted. The agent works autonomously, but a human decides the final outcome.
4. **Desktop observability**: Running automation tasks are visible to the Desktop client in real time through the standard Wire Protocol event pipeline. No separate monitoring channel is required.
5. **Dual interaction mode**: Automations are accessible through the Desktop UI (Automations board) and through headless Workflow files (similar to the current GitHubTracker usage).
6. **Backward compatibility**: Existing GitHubTracker configurations and workflow files continue to work without modification.
7. **Unified monitoring**: All automation sources share a single orchestrator snapshot, observable through the dashboard and Desktop UI.

### 2.2 Non-Goals

- **Multi-repository orchestration**: Each DotCraft workspace operates against one repository (or no repository for purely local tasks). Cross-repo automation is out of scope.
- **Task dependency graphs**: Local tasks are independent units. Dependency chains (task A blocks task B) are not supported for local tasks in this version.
- **Persistent orchestrator state**: Orchestrator state (Running, Claimed, Completed) remains in-memory only, consistent with the existing GitHubTracker design. Local task definitions are persisted, but orchestrator dispatch state is not.
- **Real-time collaboration**: Only one client can interact with the review gate for a given task at a time. No concurrent multi-user review is defined.

---

## 3. Architecture Overview

### 3.1 Process Model

The Automations orchestrator runs as a hosted service **inside the AppServer process**. It shares the same `SessionService`, `AgentFactory`, and `ThreadEventBroker` as the rest of the AppServer. This co-location eliminates the process boundary that made GitHubTracker invisible to the Desktop.

```
┌───────────────────────────────────────────────────────────────────┐
│  AppServer Process  (dotcraft app-server)                         │
│                                                                   │
│  ┌──────────────────┐    ┌──────────────────────────────────────┐ │
│  │  Wire Protocol   │    │  AutomationOrchestrator              │ │
│  │  Server          │    │  (hosted service)                    │ │
│  │  (stdio / WS)    │    │        │                             │ │
│  └────────┬─────────┘    │        │ IAutomationSource[]         │ │
│           │              │        ▼                             │ │
│           │  JSON-RPC    │  AutomationSessionClient             │ │
│           │              │  (wraps ISessionService)             │ │
│           │              └──────────────┬───────────────────────┘ │
│           │                             │                          │
│           ▼                             ▼                          │
│  ┌────────────────────────────────────────────────────────────┐   │
│  │  ISessionService  (shared)                                 │   │
│  │  ThreadEventBroker · ThreadStore · AgentFactory            │   │
│  └─────────────────────────┬──────────────────────────────────┘   │
│                            │                                       │
│                   SessionEvent stream                              │
│                            │                                       │
│                            ▼                                       │
│  ┌────────────────────────────────────────────────────────────┐   │
│  │  AppServerEventDispatcher                                  │   │
│  │  (routes events to all subscribed transports)              │   │
│  └─────────────────────────┬──────────────────────────────────┘   │
└────────────────────────────┼──────────────────────────────────────┘
                             │  JSON-RPC notifications
                             ▼
              ┌──────────────────────────┐
              │  Desktop (Electron)      │
              │  thread/subscribe active │
              └──────────────────────────┘
```

### 3.2 Responsibility Split

| Layer | Responsibility |
|-------|---------------|
| **AutomationOrchestrator** | Poll-tick scheduling, candidate fetching from all sources, dispatch sorting, concurrency gating, `ShouldDispatch` checks, per-task workspace provisioning, multi-turn loop management, worker exit handling, retry scheduling, reconciliation, stall detection. Source-agnostic. |
| **AutomationSessionClient** | In-process adapter over `ISessionService`. Creates threads with automation identity, submits turns, subscribes to event streams, and detects task completion. Does not use Wire Protocol transport. |
| **IAutomationSource** | Fetch candidates, report task states, register tool profiles at startup, define completion semantics, and optionally participate in re-dispatch decisions. |
| **LocalAutomationSource** | File-based task store, local candidate discovery, review gate integration, task state persistence. |
| **GitHubAutomationSource** | GitHub API integration, issue/PR candidate fetching, SHA tracking delegation. Registers GitHub-specific tool profiles. |
| **Session Core** | Agent execution, tool invocation, event emission, thread persistence. Extended to support per-thread workspace scoping, tool profiles, and approval policies (§9). |

### 3.3 Data Flow

```
AutomationOrchestrator.OnTickAsync
    │
    ├── ReconcileAsync (running entries, stall detection, terminal cleanup)
    │
    ├── For each IAutomationSource:
    │       source.FetchCandidateTasksAsync()
    │
    ├── Merge all candidates → DispatchSorter.Sort
    │
    └── For each candidate:
            ShouldDispatch? → source.ShouldReDispatch?
                │
                ▼
            DispatchTask
                │
                ├── WorkspaceManager.EnsureWorkspaceAsync(task)
                │       → prepare task workspace directory
                │       → run after_create hook (if new)
                │
                ├── AutomationSessionClient.CreateThreadAsync(task, taskWorkspacePath)
                │       → sessionService.CreateThreadAsync(identity, config)
                │         identity: { channelName: "automations", userId: "task-{id}",
                │                     workspacePath: mainWorkspacePath }
                │         config:   { workspaceOverride, automationTaskDirectory?,
                │                     toolProfile, approvalPolicy: autoApprove, requireApprovalOutsideWorkspace (§9.3) }
                │
                ├── Multi-turn loop:
                │       → WorkspaceManager.RunBeforeRunHookAsync
                │       → for each turn:
                │           AutomationSessionClient.SubmitTurnAsync(threadId, prompt)
                │               → sessionService.SubmitInputAsync → agent runs
                │               → events flow to Desktop via ThreadEventBroker
                │           after turn: source.FetchTaskStatesByIdsAsync
                │               → check for completion sentinel
                │       → WorkspaceManager.RunAfterRunHookAsync
                │
                └── OnWorkerExitAsync
                        → source.OnTaskCompletedAsync(task, outcome)
                        → [Local] task state → review, emit review notification
                        → [GitHub Issue] ScheduleRetry / Completed
                        → [GitHub PR] RecordSha / ScheduleRetry
```

### 3.4 Thread Identity Convention

Automation threads use a reserved channel identity that distinguishes them from user-initiated conversation threads:

| Identity Field | Value |
|----------------|-------|
| `channelName` | `"automations"` |
| `userId` | `"task-{taskId}"` (e.g., `"task-task-001"`, `"task-42"`) |
| `workspacePath` | The **main** workspace path (AppServer's root workspace), for discoverability |
| `channelContext` | `"automation:{sourceName}"` (e.g., `"automation:local"`, `"automation:github"`) |

`workspacePath` is set to the main workspace so that `thread/list` can return automation threads to clients that query the main workspace. The actual agent execution workspace (the task's isolated directory) is passed via `ThreadConfiguration.WorkspaceOverride` (§9).

### 3.5 Headless Mode

For headless usage (no Desktop client), the Automations module runs inside an AppServer process launched without an interactive client. The orchestrator continues to poll and dispatch tasks. Review decisions are delivered via the Wire Protocol to any connected client; a future CLI command (`dotcraft automations review`) can provide an interactive review flow.

The existing GitHubTracker headless workflow (`WORKFLOW.md` + `dotcraft run` or Gateway mode) is preserved during the migration period via the backward-compatibility path described in §18.

---

## 4. Automation Source Abstraction

### 4.1 IAutomationSource Interface

```csharp
public interface IAutomationSource
{
    string Name { get; }

    /// <summary>
    /// Name of the tool profile this source registers.
    /// Used in ThreadConfiguration.ToolProfile when dispatching tasks.
    /// </summary>
    string ToolProfileName { get; }

    Task<IReadOnlyList<AutomationTask>> FetchCandidateTasksAsync(
        CancellationToken ct = default);

    Task<IReadOnlyList<TaskStateSnapshot>> FetchTaskStatesByIdsAsync(
        IReadOnlyList<string> taskIds,
        CancellationToken ct = default);

    Task<IReadOnlyList<AutomationTask>> FetchTasksByStatesAsync(
        IReadOnlyList<string> stateNames,
        CancellationToken ct = default);

    bool ShouldReDispatch(AutomationTask task, OrchestratorState state);

    void OnTaskDispatched(AutomationTask task);

    Task OnTaskCompletedAsync(AutomationTask task, AgentRunOutcome outcome,
        CancellationToken ct = default);

    /// <summary>
    /// Register source-specific tool providers with the tool profile registry.
    /// Called once at startup. The registered profile is referenced by ToolProfileName.
    /// Profiles should contain ONLY source-specific tools; standard tools are merged
    /// by Session Core at thread creation time.
    /// </summary>
    void RegisterToolProfile(IToolProfileRegistry registry);

    /// <summary>
    /// Optionally provisions a source-specific workspace (e.g. git clone + branch checkout).
    /// Returns the workspace path, or null to fall back to AutomationWorkspaceManager.
    /// </summary>
    Task<string?> ProvisionWorkspaceAsync(AutomationTask task, CancellationToken ct) =>
        Task.FromResult<string?>(null);

    IReadOnlyList<string> ActiveStates { get; }
    IReadOnlyList<string> TerminalStates { get; }
}
```

### 4.2 Interface Contract

| Method | Contract |
|--------|----------|
| `FetchCandidateTasksAsync` | Return all tasks in active states eligible for dispatch. Filtering (e.g., draft PR exclusion) is the source's responsibility. |
| `FetchTaskStatesByIdsAsync` | Return current state snapshots for the given IDs. Used by the orchestrator for reconciliation and completion detection after each turn. |
| `FetchTasksByStatesAsync` | Return tasks currently in the given state names. Used for startup terminal cleanup. |
| `ShouldReDispatch` | Source-specific re-dispatch logic. GitHub uses this for SHA comparison; Local returns `false` for tasks in `review` state. The orchestrator calls this after its own Running/Claimed checks pass. |
| `OnTaskDispatched` | Notification that a task has been claimed and dispatched. Sources may update internal state (e.g., Local source transitions task file to `running`, increments `round`). |
| `OnTaskCompletedAsync` | Called when the agent run finishes (all turns exhausted or completion tool invoked). The source decides the next state: Local transitions to `review`; GitHub records SHA or schedules continuation. |
| `RegisterToolProfile` | Called once at startup. The source registers its **source-specific** tool providers (e.g., `CompleteTask`, `SubmitReview`) under `ToolProfileName` in the `IToolProfileRegistry`. Standard tools (file, shell, web, sandbox) are provided by Session Core and must NOT be included in the profile. |
| `ProvisionWorkspaceAsync` | Optional. Provisions a source-specific workspace for the task (e.g. git clone + branch checkout for GitHub). Returns the workspace path, or `null` to use the default `AutomationWorkspaceManager`. The orchestrator calls this before falling back to the generic provisioner. |
| `ToolProfileName` | The profile name used in `ThreadConfiguration.ToolProfile` when creating threads for this source. Must be globally unique (e.g., `"automation:local"`, `"automation:github-issue"`). |
| `ActiveStates` / `TerminalStates` | State name lists used by the orchestrator for reconciliation and cleanup. |

### 4.3 IToolProfileRegistry

Tool profiles are registered at AppServer startup and resolved by `BuildAgentForConfigAsync` when creating per-thread agents:

```csharp
public interface IToolProfileRegistry
{
    void Register(string profileName, IReadOnlyList<IAgentToolProvider> toolProviders);
    IReadOnlyList<IAgentToolProvider>? Resolve(string profileName);
}
```

Sources call `registry.Register(ToolProfileName, providers)` during `RegisterToolProfile`. The `AutomationSessionClient` passes `ToolProfileName` in `ThreadConfiguration.ToolProfile`; Session Core resolves the providers during `BuildAgentForConfigAsync`.

### 4.4 CompositeAutomationSource

When multiple sources are registered, the orchestrator wraps them in a `CompositeAutomationSource` that:

- Merges candidates from all sources into a single list.
- Routes state queries and completion calls to the correct source by matching `task.SourceName`.
- Combines `ActiveStates` and `TerminalStates` from all sources (union).

---

## 5. AutomationTask Model

### 5.1 Data Model

```csharp
public enum AutomationTaskKind
{
    Local,
    GitHubIssue,
    GitHubPullRequest,
}

public sealed class AutomationTask
{
    public required string Id { get; init; }
    public required string Identifier { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public int? Priority { get; init; }
    public required string State { get; init; }
    public required AutomationTaskKind Kind { get; init; }
    public required string SourceName { get; init; }
    public string? WorkflowPath { get; init; }
    public string? BranchName { get; init; }
    public string? Url { get; init; }
    public IReadOnlyList<string> Labels { get; init; } = [];
    public IReadOnlyList<BlockerRef> BlockedBy { get; init; } = [];
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public IReadOnlyDictionary<string, object?> Metadata { get; init; }
        = new Dictionary<string, object?>();
}
```

### 5.2 Field Semantics

| Field | Description |
|-------|-------------|
| `Id` | Stable source-internal ID. For GitHub: issue/PR number. For local: task file name stem. |
| `Identifier` | Human-readable key. For GitHub: `#42` or `PR#15`. For local: `task-001`. |
| `SourceName` | Source that owns this task: `"local"`, `"github"`. Used for routing. |
| `Kind` | Discriminator for task type. Determines workspace provisioning strategy and tool profile selection. |
| `WorkflowPath` | Optional per-task workflow override. When null, the source's default workflow is used. |
| `Metadata` | Opaque source-specific data. The orchestrator does not inspect this dictionary. Sources and their tools use it to carry extra fields (e.g., `HeadSha`, `HeadBranch`, `ReviewState` for PRs). |

### 5.3 TaskStateSnapshot

```csharp
public sealed class TaskStateSnapshot
{
    public required string Id { get; init; }
    public required string State { get; init; }
    public required string SourceName { get; init; }
}
```

### 5.4 Mapping from TrackedWorkItem

The existing `TrackedWorkItem` maps to `AutomationTask` as follows:

| TrackedWorkItem | AutomationTask | Notes |
|-----------------|----------------|-------|
| `Id` | `Id` | Unchanged |
| `Identifier` | `Identifier` | Unchanged |
| `Kind: Issue` | `Kind: GitHubIssue` | |
| `Kind: PullRequest` | `Kind: GitHubPullRequest` | |
| `HeadSha`, `HeadBranch`, `BaseBranch`, `DiffUrl`, `ReviewState`, `ChecksStatus`, `IsDraft` | `Metadata` | Moved into the opaque metadata dictionary |
| (n/a) | `SourceName: "github"` | New field |
| (n/a) | `WorkflowPath` | Resolved by the source from config |

---

## 6. Local Task Source

> **Implementation note:** The on-disk layout and `IAutomationSource` behavior in [M4](milestones/M4-automations-local-source.md) (per-task directory, `task.md`, profile `local-task`, `ShouldStopWorkflowAfterTurnAsync`) supersede the older single-file YAML sketch in §6.1–6.4 below. For the authoritative format, see the M4 milestone and §13.

### 6.1 Task Store Layout

Local tasks are persisted as individual YAML files under the workspace:

```
.craft/automations/
    tasks/
        task-001.yaml
        task-002.yaml
        refactor-auth.yaml
    workflows/
        WORKFLOW.md           # default local task workflow
        review.md             # alternative workflow
```

### 6.2 Task File Format

```yaml
id: task-001
title: "Refactor auth module to use JWT"
description: |
  Replace session-based auth with JWT tokens.
  Update all middleware and tests.
state: pending
priority: 1
workflow: WORKFLOW.md
labels: [refactor, auth]
created_at: 2026-03-22T10:00:00Z
updated_at: 2026-03-22T10:00:00Z
round: 0
review_feedback: null
agent_summary: null
```

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `id` | string | yes | — | Stable identifier, must match the file name stem |
| `title` | string | yes | — | Short task summary |
| `description` | string | no | `null` | Full task description, used in prompt rendering |
| `state` | string | yes | `pending` | Current lifecycle state |
| `priority` | int | no | `null` | Lower is higher priority; null sorts last |
| `workflow` | string | no | `WORKFLOW.md` | Workflow file relative to `.craft/automations/workflows/` |
| `labels` | string[] | no | `[]` | Free-form tags |
| `created_at` | datetime | yes | creation time | ISO 8601 timestamp |
| `updated_at` | datetime | yes | creation time | Updated on every state transition |
| `round` | int | no | `0` | Current dispatch round (incremented each re-dispatch) |
| `review_feedback` | string | no | `null` | User feedback from the last "Request Changes" review |
| `agent_summary` | string | no | `null` | Summary written by the `CompleteTask` tool; shown in the Desktop review panel |

### 6.3 LocalAutomationSource Behavior

| IAutomationSource Method | Behavior |
|--------------------------|----------|
| `FetchCandidateTasksAsync` | Reads all `.yaml` files in `tasks/`, returns those whose `state` is in `ActiveStates`. |
| `FetchTaskStatesByIdsAsync` | Reads specific task files by ID, returns their current state. This is used after each turn to detect the `agent_completed` sentinel state set by the `CompleteTask` tool. |
| `FetchTasksByStatesAsync` | Reads all task files, filters by the given state names. |
| `ShouldReDispatch` | Returns `false` if the task is in `review` state. Otherwise defers to the orchestrator's default checks. |
| `OnTaskDispatched` | Updates the task file: `state → running`, increments `round`, clears `agent_summary`, updates `updated_at`. |
| `OnTaskCompletedAsync` | Updates the task file: `state → review`, updates `updated_at`. Emits `automation/review/requested` notification via the AppServer event pipeline. |
| `RegisterToolProfile` | Registers `LocalTaskCompletionToolProvider` under the profile name `"automation:local"`. |
| `ToolProfileName` | `"automation:local"` |
| `ActiveStates` | `["pending"]` |
| `TerminalStates` | `["completed", "cancelled"]` |

### 6.4 Completion Sentinel State

When the `CompleteTask` tool is invoked by the agent, it writes the task's `agent_summary` and transitions `state` to `agent_completed` in the task file. This intermediate state is not a terminal state — it signals to the orchestrator that the agent's current run is finished. The orchestrator detects this via `FetchTaskStatesByIdsAsync` after the turn completes, exits the multi-turn loop, and calls `OnTaskCompletedAsync`, which transitions the task to `review`.

| State | Visible to Desktop | Description |
|-------|--------------------|-------------|
| `agent_completed` | No (internal only) | Agent called `CompleteTask`; orchestrator has not yet processed the exit |
| `review` | Yes | Orchestrator has processed the exit; task awaits human decision |

### 6.5 File Watching

The `LocalTaskStore` watches the `tasks/` directory with `FileSystemWatcher` for external changes (e.g., user manually edits a task file or the Desktop client creates one). Changes are picked up on the next poll tick.

---

## 7. Local Task Lifecycle State Machine

### 7.1 State Diagram

```
                    ┌──────────── Request Changes ──────────────┐
                    │             (with feedback)                │
                    │                                           │
┌──────────┐   Dispatch   ┌──────────┐   Agent done   ┌───────┴──┐
│          │─────────────► │          │───────────────► │          │
│ Pending  │               │ Running  │                │  Review  │
│          │◄───┐          │          │                │          │
└──────────┘    │          └──────────┘                └────┬──┬──┘
     │          │                                          │  │
  [created]     │                                          │  │
                │                          ┌───────────────┘  │
                │                          │                  │
                │                     User approves      User rejects
                │                          │                  │
                │                    ┌─────┴──────┐    ┌──────┴─────┐
                │                    │ Completed  │    │ Cancelled  │
                │                    │            │    │            │
                │                    └────────────┘    └────────────┘
                │                          │                  │
                │                    Run after_approve   Clean workspace
                └── max_rounds exceeded: forced to Review ────┘
```

### 7.2 State Definitions

| State | Description |
|-------|-------------|
| **Pending** | Task is defined and waiting to be dispatched. Eligible for candidate selection. |
| **Running** | An agent session is actively working on the task. The task is not eligible for re-dispatch. |
| **Review** | Agent has finished. All changes are preserved in the workspace. Awaiting human decision. |
| **Completed** | User approved the task outcome. Optional `after_approve` hook runs (e.g., git commit). |
| **Cancelled** | User rejected the task outcome or the task was manually cancelled. Workspace is cleaned. |

### 7.3 Transition Rules

| From | To | Trigger | Side Effects |
|------|----|---------|-------------|
| Pending | Running | Orchestrator dispatches the task | `round` incremented, `updated_at` set, workspace provisioned, thread created |
| Running | Review | Agent calls `CompleteTask`, all turns exhausted, or max turns reached | Thread persisted, workspace preserved, review notification emitted |
| Review | Completed | User approves via Wire Protocol or Desktop UI | `after_approve` hook runs, `updated_at` set |
| Review | Pending | User requests changes with feedback | `review_feedback` set, `updated_at` set, agent re-dispatched on next tick |
| Review | Cancelled | User rejects | `before_remove` hook runs, workspace cleaned, `updated_at` set |
| Pending | Cancelled | User cancels before dispatch | `updated_at` set |
| Running | Cancelled | User cancels during execution | Turn interrupted via `sessionService.CancelTurnAsync`, workspace cleaned |
| Any active | Review | `round >= max_rounds` after agent finishes | Forced into review regardless of agent outcome |

### 7.4 Round Tracking

Each time a task is dispatched (including re-dispatches after "Request Changes" reviews), the `round` counter is incremented. When `round >= max_rounds` (configurable, default 3), the task transitions to `Review` after the agent finishes and cannot be re-dispatched via "Request Changes". The user can only approve or reject at that point.

---

## 8. Human Review Gate

### 8.1 Purpose

Local tasks have no external system (like GitHub) to mediate the acceptance of changes. The human review gate ensures that:

- The user can inspect all agent-produced changes before they are accepted.
- The user can provide feedback and request another iteration.
- Unacceptable changes can be discarded entirely.

GitHub-sourced tasks do not use the review gate. Their completion semantics are defined by the GitHub source (close issue, submit PR review) and do not require local human approval.

### 8.2 Review Entry

When the agent run ends for a local task, the orchestrator calls `source.OnTaskCompletedAsync`, which:

1. Updates the task file: `state: review`, stores `agent_summary`.
2. Emits an `automation/review/requested` Wire Protocol notification via the AppServer's event pipeline.

The automation thread (and its full turn history) remains intact in the shared `SessionService`. The workspace directory with all agent changes is preserved. No cleanup runs until the user makes a decision.

### 8.3 Review Decisions

| Decision | Wire Protocol Method | Effect |
|----------|---------------------|--------|
| **Approve** | `automation/task/review` with `decision: "approve"` | Task state → `completed`. Run `after_approve` hook. |
| **Request Changes** | `automation/task/review` with `decision: "requestChanges"`, `feedback: "..."` | Task state → `pending`. `review_feedback` field updated. Agent re-dispatches on next tick. |
| **Reject** | `automation/task/review` with `decision: "reject"` | Task state → `cancelled`. Run `before_remove` hook. Clean workspace. |

### 8.4 Review Context

When a task is in `review` state, clients can inspect:

- **File changes**: The task workspace contains all files modified by the agent. The Desktop reads diffs from the workspace directory.
- **Conversation log**: The automation thread in the shared `SessionService` holds the full turn history. Desktop can subscribe via `thread/subscribe` using the thread ID returned in the `automation/review/requested` notification.
- **Summary**: The `agent_summary` field from the task file, populated by the `CompleteTask` tool call.

### 8.5 Feedback Propagation

When the user chooses "Request Changes" with feedback text:

1. The feedback is written to the task's `review_feedback` field.
2. The task returns to `pending` state and is re-dispatched on the next tick.
3. The agent runner includes the feedback in the first-turn prompt via the Liquid template variable `{{ task.review_feedback }}`.
4. The orchestrator resumes the existing automation thread (same `userId`), so the agent's prior session context is available for continuation.

---

## 9. Session Core Extensions

Three extensions to `DotCraft.Core` are required to support Automations. These changes are prerequisites for Phase 1 implementation (§18.1).

### 9.1 Per-Thread Workspace Override

Currently, `AgentFactory` and `ToolProviderContext` bind to the process-level workspace path at startup. Tools always operate on the main workspace. Automation tasks need tools to operate on the task's isolated workspace directory.

**Required change**: Add `WorkspaceOverride` to `ThreadConfiguration`:

```csharp
public sealed class ThreadConfiguration
{
    public string Mode { get; set; } = "agent";
    public McpServerConfig[]? McpServers { get; set; }
    public string[]? Extensions { get; set; }
    public string[]? CustomTools { get; set; }

    /// <summary>
    /// When set, tools for this thread operate on this workspace path
    /// instead of the AppServer's root workspace. The thread is still
    /// registered under the AppServer's root workspace for discoverability.
    /// </summary>
    public string? WorkspaceOverride { get; set; }

    // ... other new fields below
}
```

**Impact on `BuildAgentForConfigAsync`**: When `config.WorkspaceOverride` is set, create a scoped `ToolProviderContext` with `WorkspacePath = config.WorkspaceOverride` and `BotPath = Path.Combine(config.WorkspaceOverride, ".craft")`. All tools created for this thread operate on the override path.

**Automation task directory (local)**: `ThreadConfiguration` may set `AutomationTaskDirectory` to the absolute path of the local task folder (the directory containing `task.md`). It is copied into `ToolProviderContext.AutomationTaskDirectory` so tools such as **`CompleteLocalTask`** can update `task.md` even when `WorkspaceOverride` is the **project root** (where parent-of-workspace heuristics do not apply). See §12.1 and §13.3.

### 9.2 Tool Profiles

Currently, `BuildAgentForConfigAsync` creates tools using the global `AgentFactory` with the default tool set. Automation sources need to inject source-specific tools (`CompleteTask`, `SubmitReview`, etc.) into the agent for a given thread.

**Required change**: Add `ToolProfile` to `ThreadConfiguration`:

```csharp
public string? ToolProfile { get; set; }
```

And a new `IToolProfileRegistry` interface (see §4.3). The registry is registered as a singleton in DI. Each `IAutomationSource` calls `RegisterToolProfile` at startup.

**Impact on `BuildAgentForConfigAsync`**: When `config.ToolProfile` is set, resolve the tool providers from `IToolProfileRegistry` and **merge** them into the agent's tool collection alongside the standard tools. Concretely:

1. Build the standard tool set via `CreateToolsForMode` (respects `Sandbox.Enabled`, MCP, etc.).
2. Build the profile tool set via `CreateToolsFromProviders(profileProviders, context)`.
3. Append the profile tools to the standard tools.

Profile providers must contain **only source-specific tools** (e.g. `SubmitReview`, `CompleteIssue`, `CompleteLocalTask`). They must NOT include `CoreToolProvider` or other standard providers, as these are already present in the standard tool set. Including them would cause tool duplication.

### 9.3 Per-Thread Approval Policy (local automation)

**Local automation** does **not** use interactive per-tool prompts (`ApprovalPolicy.Default` / `SessionApprovalService`). Dispatch always uses `ApprovalPolicy.AutoApprove` for the thread so gated file/shell operations do not block on a human during the run.

**Outside-workspace tool behavior** is controlled separately via `approval_policy` in `task.md` and optional `ThreadConfiguration.RequireApprovalOutsideWorkspace` → `ToolProviderContext.RequireApprovalOutsideWorkspace`, which overrides `AppConfig.Tools.File/Shell.RequireApprovalOutsideWorkspace` for core file/shell tools:

| `approval_policy` (wire / YAML) | Meaning |
|---------------------------------|---------|
| `workspaceScope` (default) | Operations **outside** the thread agent workspace are **rejected** without prompting (`RequireApprovalOutsideWorkspace = false`). |
| `fullAuto` | Operations outside the workspace may proceed with **auto-approval** (`RequireApprovalOutsideWorkspace = true` + `AutoApprove`). Higher risk. |
| Legacy `autoApprove` | Treated like `fullAuto`. |
| Legacy `default` | Treated like `workspaceScope` (no longer interactive). |

The **task-level** human review gate (approve / reject / request changes) is unchanged and orthogonal to this.

**Desktop**: New Task uses dropdowns for **Agent workspace** (`project` / `isolated`, written to `workflow.md`) and **Tool policy** (`workspaceScope` / `fullAuto`); the review panel shows a tool-policy badge.

### 9.4 `thread/list` Filter Extension

To allow Desktop clients to filter automation threads from conversation threads, `FindThreadsAsync` (and the Wire Protocol `thread/list` method) gains support for filtering by `channelName`:

```json
// thread/list request
{
  "identity": { "workspacePath": "/path/to/project" },
  "channelName": "automations"
}
```

When `channelName` is provided, only threads with matching `channelName` are returned. Existing callers that do not pass `channelName` are unaffected.

---

## 10. Orchestrator Generalization

### 10.1 Changes from GitHubTrackerOrchestrator

The `AutomationOrchestrator` is structurally identical to `GitHubTrackerOrchestrator` with these modifications:

| Aspect | GitHubTrackerOrchestrator | AutomationOrchestrator |
|--------|--------------------------|----------------------|
| Source dependency | Single `IWorkItemTracker` | `IReadOnlyList<IAutomationSource>` via `CompositeAutomationSource` |
| Agent execution | `new AgentFactory / SessionService / AgentRunner` per task | `AutomationSessionClient` over shared `ISessionService` |
| Workflow selection | Hardcoded issue/PR workflow paths | `task.WorkflowPath` or source default |
| `ShouldDispatch` | Inline SHA check for PRs | Delegates to `source.ShouldReDispatch(task, state)` |
| `OnWorkerExit` | Inline SHA recording, issue completion | Delegates to `source.OnTaskCompletedAsync(task, outcome)` |
| Config | `GitHubTrackerConfig` | `AutomationsConfig` (orchestrator-level settings) |
| Tool injection | Hardcoded tool providers per task kind | Tool profiles via `ThreadConfiguration.ToolProfile` |
| Process location | GatewayHost (separate process) | AppServer process (shared SessionService) |

### 10.2 ShouldDispatch Decision Sequence (Generalized)

```
1. if taskId in Running       → not eligible
2. if taskId in Claimed       → not eligible
3. if source.ShouldReDispatch(task, state) == false
                              → not eligible
4. return eligible
```

Step 3 replaces the inline SHA comparison. The GitHub source implements SHA checking in its `ShouldReDispatch`; the Local source returns `false` for tasks in `review` state and `true` otherwise.

### 10.3 Dispatch Flow

```csharp
private void DispatchTask(AutomationTask task, WorkflowDefinition workflow, int? attempt)
{
    var cts = new CancellationTokenSource();
    var taskId = task.Id;

    var workerTask = Task.Run(async () =>
    {
        // 1. Prepare workspace
        var workspace = await workspaceManager.EnsureWorkspaceAsync(task, ct);

        // 2. Notify source (transitions state to "running")
        source.OnTaskDispatched(task);

        // 3. Create automation thread in shared SessionService
        var threadId = await sessionClient.CreateOrResumeThreadAsync(task, workspace.Path, ct);

        // 4. Run multi-turn loop (see §13)
        var outcome = await RunTurnsAsync(task, threadId, workflow, attempt, workspace.Path, ct);

        // 5. Notify source (transitions state to "review" / records SHA / etc.)
        await source.OnTaskCompletedAsync(task, outcome, ct);
    });

    lock (_stateLock)
    {
        _state.Claimed.Add(taskId);
        _state.Running[taskId] = new RunningEntry { ..., WorkerTask = workerTask };
    }
}
```

### 10.4 Unchanged Behaviors

The following orchestrator behaviors are unchanged from the Symphony SPEC and the existing `GitHubTrackerOrchestrator`:

- `HasAvailableSlots` concurrency gating.
- `DispatchSorter.Sort` ordering (priority ascending, `CreatedAt` oldest first, `Identifier` tiebreak).
- `ReconcileAsync` stall detection and terminal-state cleanup.
- Retry scheduling and exponential backoff.
- In-memory state model (`Running`, `Claimed`, `Completed`, `RetryAttempts`).
- `IOrchestratorSnapshotProvider` dashboard integration.

### 10.5 Multi-Source Candidate Merging

On each poll tick:

1. The orchestrator calls `FetchCandidateTasksAsync` on each source concurrently.
2. Results are merged into a single list.
3. `DispatchSorter.Sort` is applied to the merged list.
4. Dispatch proceeds in sort order, respecting global and per-source concurrency limits.

Source-level concurrency limits (e.g., `MaxConcurrentPullRequestAgents`) are enforced by checking the `Kind` field of running entries, consistent with the existing implementation.

---

## 11. Workflow Format

### 11.1 Backward Compatibility

The existing WORKFLOW.md format (YAML front matter between `---` delimiters + Liquid prompt body) is fully preserved. All existing front-matter keys (`tracker`, `polling`, `workspace`, `agent`, `hooks`) continue to work.

### 11.2 New Front-Matter Keys

| Section | Key | Type | Default | Description |
|---------|-----|------|---------|-------------|
| (root) | `workspace` | `project` \| `isolated` | `project` | **Local tasks only**: whether the agent uses the open project root or an isolated folder under the task bundle. See §12.1. |
| `automation` | `source` | string | (inferred) | Source name hint. Not required when the workflow is referenced by a specific source. |
| `automation` | `review_states` | string[] | `["review"]` | States that indicate the task is awaiting human review. |
| `agent` | `max_rounds` | int | `3` | Maximum dispatch rounds before forced review. |
| `hooks` | `after_approve` | string | `null` | Shell command to run after user approves a local task. |
| `hooks` | `after_reject` | string | `null` | Shell command to run after user rejects a local task. |

### 11.3 Example Local Task Workflow

```markdown
---
agent:
  max_turns: 30
  max_rounds: 3
hooks:
  after_create: "git checkout -b automation/{{task.identifier}}"
  after_approve: "git add -A && git commit -m 'automation: {{task.identifier}} - {{task.title}}'"
---
You are working on task {{ task.identifier }}: **{{ task.title }}**.

{{ task.description }}

{% if task.review_feedback %}
## Previous Review Feedback

The user reviewed your previous work and requested changes:

{{ task.review_feedback }}

Please address the feedback and continue working.
{% endif %}

When you have completed the task, call the `CompleteTask` tool with a summary of what you did.
```

### 11.4 Template Variables

| Variable | Type | Description |
|----------|------|-------------|
| `task.id` | string | Task ID |
| `task.identifier` | string | Human-readable identifier |
| `task.title` | string | Task title |
| `task.description` | string | Task description |
| `task.priority` | int? | Priority value |
| `task.state` | string | Current state |
| `task.kind` | string | `"Local"`, `"GitHubIssue"`, `"GitHubPullRequest"` |
| `task.labels` | string[] | Labels |
| `task.round` | int | Current dispatch round |
| `task.review_feedback` | string? | Feedback from last "Request Changes" review |
| `task.branch_name` | string? | Associated git branch |
| `task.url` | string? | External URL (GitHub tasks) |
| `task.workspace_path` | string | Resolved agent workspace directory for this run (depends on `workspace` in `workflow.md`; §12.1). |
| `attempt` | int | Retry attempt within the current round |

GitHub-specific variables (e.g., `task.diff`, `task.head_branch`) are available when the source populates them in the metadata and the workflow loader exposes them. The existing `work_item.*` variable names are supported as aliases for backward compatibility with existing GitHub workflow files.

---

## 12. Workspace Management

### 12.1 Local task workspace mode (two options)

Local tasks persist under `{dotcraftWorkspace}/.craft/tasks/<taskId>/` (`task.md`, `workflow.md`, optional `workspace/`). The **agent tool root** is `ThreadConfiguration.WorkspaceOverride`. For locals, exactly **two** modes are supported, selected by `workflow.md` YAML front matter key **`workspace`**:

| `workspace` | Meaning | `WorkspaceOverride` |
|-----------|---------|---------------------|
| `project` (default) | Agent file/shell tools use the **DotCraft workspace root** (the open project). | Host `WorkspacePath` from `DotCraftPaths`. |
| `isolated` | Agent tools are confined to an empty folder under the task bundle. | `{taskDir}/workspace` (created if missing). |

The default template for Desktop-created tasks includes `workspace: project` so agents can modify real project files. Set `workspace: isolated` for sandbox-style tasks that must not touch the repo.

**Liquid**: `task.workspace_path` in workflow templates reflects the resolved agent workspace (set on the task before the workflow is rendered).

### 12.2 Non-local sources (GitHub)

GitHub issue/PR tasks continue to use source-specific provisioning (clone/checkout under the GitHub tracker workspace root). They do not use the local `workspace:` switch above.

### 12.3 Paths and roots

- **Local task files**: `Automations.LocalTasksRoot`, or default `{dotcraftWorkspace}/.craft/tasks`.
- **Generic automation workspace** (`AutomationWorkspaceManager`): default `%USERPROFILE%\.craft\automations\workspaces` for sources that use the generic provisioner (not the local `project` / `isolated` branch above).

### 12.4 Workspace Lifecycle

| Event | Action |
|-------|--------|
| Task dispatched | Orchestrator resolves local workspace per §12.1; `ThreadConfiguration.WorkspaceOverride` (+ `AutomationTaskDirectory` for locals) set before the thread runs |
| Before each turn | Run `before_run` hook |
| After each turn | Run `after_run` hook |
| Task completed (approved) | Run `after_approve` hook. Workspace retained until manual cleanup. |
| Task cancelled (rejected) | Run `before_remove` hook. Clean workspace. |
| Task in review | Workspace preserved. Thread preserved in SessionService. No cleanup. |

### 12.5 Hook Variables

Hook commands are shell templates that support the same Liquid variables as workflow prompts. For example:

```
after_approve: "cd {{workspace_path}} && git add -A && git commit -m '{{task.title}}'"
```

---

## 13. Agent Execution

### 13.1 AutomationSessionClient

`AutomationSessionClient` is an in-process adapter over `ISessionService`. It does not use Wire Protocol transport (no JSON serialization, no stdio). It provides a task-oriented API that maps to the underlying session methods:

```csharp
public sealed class AutomationSessionClient(ISessionService sessionService, string mainWorkspacePath)
{
    public async Task<string> CreateOrResumeThreadAsync(
        AutomationTask task, string taskWorkspacePath, CancellationToken ct)
    {
        var identity = new SessionIdentity
        {
            ChannelName = "automations",
            UserId = $"task-{task.Id}",
            WorkspacePath = mainWorkspacePath,
            ChannelContext = $"automation:{task.SourceName}",
        };

        var existing = await sessionService.FindThreadsAsync(identity, ct: ct);
        if (existing.FirstOrDefault() is { } thread)
            return (await sessionService.ResumeThreadAsync(thread.Id, ct)).Id;

        var config = new ThreadConfiguration
        {
            Mode = "agent",
            WorkspaceOverride = taskWorkspacePath,
            ToolProfile = ResolveToolProfile(task),
            ApprovalPolicy = "auto",
        };
        return (await sessionService.CreateThreadAsync(identity, config, ct: ct)).Id;
    }

    public IAsyncEnumerable<SessionEvent> SubmitTurnAsync(
        string threadId, string prompt, CancellationToken ct)
        => sessionService.SubmitInputAsync(threadId, prompt, ct: ct);

    public Task CancelTurnAsync(string threadId, CancellationToken ct)
        => sessionService.CancelTurnAsync(threadId, ct);
}
```

Because the Orchestrator and the `SessionService` are co-located in the same process, calls to `SubmitInputAsync` run the agent in-process. The resulting `SessionEvent` stream is broadcast by `ThreadEventBroker` to all subscribed transports, including the Desktop's Wire Protocol connection.

### 13.2 Multi-Turn Loop

The Orchestrator's multi-turn loop calls `SubmitTurnAsync` and consumes the resulting event stream:

```
for round = 1 to max_rounds (unless stopped):
    for each workflow step:
        events = sessionClient.SubmitTurnAsync(threadId, step.Prompt, ct)
        await foreach (evt in events):
            update RunningEntry (turn count, token counts, last event)
            if evt is TurnFailed → stop workflow (failure outcome)
            if evt is TurnCancelled → stop workflow (cancel handling)

        if await source.ShouldStopWorkflowAfterTurnAsync(task, ct) → break outer (completion or sentinel)
        if cancellation requested → break
```

**Local source:** `ShouldStopWorkflowAfterTurnAsync` reloads `task.md` from disk (via `LocalTaskFileStore`) and returns `true` when `status == agent_completed` (YAML `agent_completed`), e.g. after the agent calls **`CompleteLocalTask`**. This replaces any file-based polling by ID in the abstract loop.

**GitHub / remote sources:** completion may still be observed via tracker APIs (e.g. issue closed or state fetched by ID) where `IAutomationSource` implements that contract; the local file store does **not** use `FetchTaskStatesByIdsAsync` for the `task.md` layout.

### 13.3 LocalTaskCompletionToolProvider

Mirrors GitHubTracker's `IssueCompletionToolProvider` / **`CompleteIssue`**: a dedicated tool so the agent can mark the local task complete without hand-editing `task.md`.

| Item | Detail |
|------|--------|
| **Implementation / tool name** | **`CompleteLocalTask`** (draft name `CompleteTask` is synonymous in older text) |
| **Parameters** | `summary` (string) — brief description of what was accomplished (same role as `CompleteIssue`'s `reason`) |
| **Behavior** | `LocalTaskFileStore.LoadAsync(taskDir)` → if allowed, set `AgentSummary` when `summary` is non-empty, set `Status = AgentCompleted` (YAML `agent_completed`) → `SaveAsync` |
| **Task directory** | Prefer `ToolProviderContext.AutomationTaskDirectory` when set (local automation, including **project** workspace mode). Otherwise resolve via `Directory.GetParent(WorkspacePath)` when that parent contains `task.md` (**isolated** mode). If resolution fails or `task.md` is missing, **`CreateTools` yields no tools** (avoids injecting this tool into normal non-automation sessions). |
| **Registration** | Registered with `CoreToolProvider` in `LocalAutomationSource.RegisterToolProfile` under profile **`local-task`** (same as `ToolProfileName`; not the legacy name `automation:local`). |

### 13.4 Completion Detection

After each turn (after the event stream for that turn ends), the orchestrator calls **`ShouldStopWorkflowAfterTurnAsync`**. The workflow round loop exits when:

| Condition | Exit Reason |
|-----------|-------------|
| `ShouldStopWorkflowAfterTurnAsync` is `true` (local: `task.md` shows `agent_completed`) | **`CompleteLocalTask`** (or equivalent) updated the task file; normal path to `OnAgentCompletedAsync` / review |
| `TurnFailed` | Agent turn failed; error exit |
| `TurnCancelled` (and task not already completed) | Cancelled turn; failed / cancel handling |
| Round count reaches `max_rounds` | All rounds exhausted without completion tool |

### 13.5 Desktop Observability

Because `SubmitInputAsync` goes through the shared `SessionService` and `ThreadEventBroker`, all events (`turn/started`, `item/agentMessage/delta`, `item/completed`, `turn/completed`) are delivered to every client subscribed to the thread, including the Desktop. No additional notification infrastructure is required.

When a task starts running, the Desktop can subscribe via `thread/subscribe` using the `threadId` included in the `automation/task/stateChanged` notification.

---

## 14. Wire Protocol Extensions

### 14.1 New Automation Methods

| Method | Direction | Parameters | Returns |
|--------|-----------|------------|---------|
| `automation/task/list` | Client → Server | `{ source?: string, states?: string[] }` | `{ tasks: AutomationTaskWire[] }` |
| `automation/task/create` | Client → Server | `{ title, description?, workflowTemplate?, approvalPolicy?, workspaceMode?: "project" \| "isolated" }` | `{ task: AutomationTaskWire }` |
| `automation/task/read` | Client → Server | `{ taskId }` | `{ task: AutomationTaskWire, threadId?: string }` |
| `automation/task/update` | Client → Server | `{ taskId, title?, description?, priority?, labels? }` | `{ task: AutomationTaskWire }` |
| `automation/task/cancel` | Client → Server | `{ taskId }` | `{}` |
| `automation/task/review` | Client → Server | `{ taskId, decision, feedback? }` | `{ task: AutomationTaskWire }` |
| `automation/snapshot` | Client → Server | `{}` | `{ snapshot: OrchestratorSnapshotWire }` |

### 14.2 Updated thread/list Filter

The existing `thread/list` method gains a `channelName` filter (§9.4):

```json
{
  "identity": { "workspacePath": "/path/to/project" },
  "channelName": "automations"
}
```

Clients can use this to retrieve all automation threads for a workspace.

### 14.3 New Notifications

| Method | Direction | Payload |
|--------|-----------|---------|
| `automation/task/stateChanged` | Server → Client | `{ taskId, identifier, source, previousState, newState, threadId?, updatedAt }` |
| `automation/review/requested` | Server → Client | `{ taskId, identifier, title, source, threadId, workspacePath, round, turnsCompleted, summary? }` |

The `threadId` in `automation/task/stateChanged` and `automation/review/requested` enables the Desktop to subscribe to the automation thread via `thread/subscribe` for real-time event streaming or review.

### 14.4 AutomationTaskWire DTO

```json
{
  "id": "task-001",
  "identifier": "task-001",
  "title": "Refactor auth module to use JWT",
  "description": "Replace session-based auth...",
  "state": "review",
  "kind": "local",
  "source": "local",
  "priority": 1,
  "labels": ["refactor", "auth"],
  "workflow": "WORKFLOW.md",
  "round": 2,
  "reviewFeedback": null,
  "agentSummary": "Replaced session tokens with JWT...",
  "threadId": "thr_abc123",
  "approvalPolicy": "workspaceScope",
  "url": null,
  "createdAt": "2026-03-22T10:00:00Z",
  "updatedAt": "2026-03-22T12:30:00Z"
}
```

### 14.5 ThreadConfiguration Wire Extensions

The `thread/start` request's `config` object is extended to support automation parameters:

```json
{
  "identity": { ... },
  "config": {
    "mode": "agent",
    "workspaceOverride": "/path/to/project",
    "toolProfile": "automation:local",
    "approvalPolicy": "autoApprove",
    "requireApprovalOutsideWorkspace": false
  }
}
```

These fields are consumed by `BuildAgentForConfigAsync` in Session Core (§9). For **local task DTOs**, `approvalPolicy` on `AutomationTaskWire` is `workspaceScope` or `fullAuto` (legacy values may still appear). `thread/start` `config.approvalPolicy` remains the `ThreadConfiguration` enum wire form (`autoApprove`, etc.); automation dispatch sets `AutoApprove` and uses `requireApprovalOutsideWorkspace` from the task for core tools.

### 14.6 Capability Gate

The `initialize` response includes `capabilities.automations: true` when the Automations module is enabled. Clients must check this capability before calling `automation/*` methods.

### 14.7 Review Decision Enum

```
decision: "approve" | "requestChanges" | "reject"
```

The `feedback` field is required when `decision` is `"requestChanges"` and ignored otherwise.

---

## 15. Desktop Integration

### 15.1 Automations View

The sidebar "Automations" entry (reserved in the Desktop Client Spec §20) becomes a full main view when `capabilities.automations` is present.

```
┌──────────────────────────────────────────────────────────────────┐
│  Automations                                     + New Task      │
├──────────────────────────────────────────────────────────────────┤
│  [All]  [Pending]  [Running]  [Review]  [Completed]             │
├──────────────────────────────────────────────────────────────────┤
│                                                                  │
│  ┌────────────────────────────────────────────────────────────┐  │
│  │  ● task-001: Refactor auth module            Review   1  │  │
│  │    local · workflow.md · round 2 · 3 turns · 5m ago       │  │
│  ├────────────────────────────────────────────────────────────┤  │
│  │  ⠋ #42: Fix login redirect                   Running     │  │
│  │    github · turn 5/20 · now                               │  │
│  ├────────────────────────────────────────────────────────────┤  │
│  │  ⠋ PR#15: Add JWT middleware                  Running     │  │
│  │    github · reviewing · 1m ago                            │  │
│  ├────────────────────────────────────────────────────────────┤  │
│  │  ✓ task-002: Update README                    Completed   │  │
│  │    local · round 1 · 2 turns · 1h ago                     │  │
│  └────────────────────────────────────────────────────────────┘  │
│                                                                  │
└──────────────────────────────────────────────────────────────────┘
```

### 15.2 Task List Entry

Each task row displays:

| Element | Description |
|---------|-------------|
| Status indicator | `●` (review, accent color), `⠋` (running, animated), `○` (pending), `✓` (completed, green), `✗` (cancelled, dimmed) |
| Identifier and title | `{identifier}: {title}`, truncated with ellipsis |
| State badge | Right-aligned, colored by state |
| Metadata line | Source, workflow, round count, turn count, relative time |
| Review badge | Notification count badge when review is pending |

### 15.3 Running Task: Live Turn Progress

When a task is in `running` state and the user selects it, the Desktop subscribes to the task's automation thread via `thread/subscribe(threadId)`. The Conversation Panel renders the agent's live turn activity using the same `item/*` event handlers as for user-initiated threads. The thread is read-only — the user cannot send messages to automation threads.

### 15.4 Task Review Panel

When a task in `review` state is selected, the main content area shows a review panel:

```
┌──────────────────────────────────────────────────────────────────┐
│  Review: task-001 — Refactor auth module                         │
│  Round 2 · 3 turns · 847 tokens · [tool policy badge]           │
├──────────────────────────────────────────────────────────────────┤
│                                                                  │
│  [Changes]  [Summary]  [Log]                                     │
│                                                                  │
│  ┌────────────────────────────────────────────────────────────┐  │
│  │  5 files changed  +312  -47                               │  │
│  │                                                           │  │
│  │  src/auth/jwt.ts              +180  -0        ●           │  │
│  │  src/auth/middleware.ts        +45  -12       ●           │  │
│  │  src/auth/session.ts           +0  -35       ●           │  │
│  │  tests/auth.test.ts           +87   -0       ●           │  │
│  │  package.json                  +1   -0       ●           │  │
│  └────────────────────────────────────────────────────────────┘  │
│                                                                  │
├──────────────────────────────────────────────────────────────────┤
│  ┌───────────┐  ┌────────────────────┐  ┌──────────┐            │
│  │  Approve  │  │ Request Changes    │  │  Reject  │            │
│  └───────────┘  └────────────────────┘  └──────────┘            │
└──────────────────────────────────────────────────────────────────┘
```

The header may include a **tool policy** badge (workspace scope vs full auto), matching `approvalPolicy` on the task (§9.3).

### 15.5 Review Panel Tabs

| Tab | Content |
|-----|---------|
| **Changes** | File diff viewer reusing the Desktop Client Spec §11.3 infrastructure. Reads diffs from the task workspace directory (`workspacePath` in the `automation/review/requested` notification). |
| **Summary** | Agent's completion summary from `agentSummary` in `AutomationTaskWire`. Rendered as Markdown. |
| **Log** | The automation thread's full turn history. Desktop calls `thread/read(threadId)` and renders turns using the same ConversationPanel infrastructure as standard threads. Thread is read-only. |

### 15.6 Review Actions

| Button | Action |
|--------|--------|
| **Approve** | Calls `automation/task/review` with `decision: "approve"`. Success toast: "Task approved." |
| **Request Changes** | Opens a text input for feedback. On submit, calls `automation/task/review` with `decision: "requestChanges"` and the feedback text. Toast: "Changes requested. Task will be re-processed." |
| **Reject** | Confirmation dialog: "Reject this task? All changes will be discarded." On confirm, calls `automation/task/review` with `decision: "reject"`. Toast: "Task rejected." |

### 15.7 New Task Dialog

The "+ New Task" button opens a creation dialog:

```
┌──────────────────────────────────────────────────────┐
│  New Automation Task                                  │
│                                                       │
│  Title:       [________________________________]      │
│                                                       │
│  Description: [________________________________]      │
│               [________________________________]      │
│               [________________________________]      │
│                                                       │
│  Agent workspace: [Project ▾]   Tool policy: [Workspace scope ▾]  (?)  │
│                                                       │
│          ┌──────────┐  ┌──────────┐                   │
│          │  Cancel  │  │  Create  │                   │
│          └──────────┘  └──────────┘                   │
└──────────────────────────────────────────────────────┘
```

**Agent workspace** maps to `workspaceMode` (`project` / `isolated`); when no custom `workflowTemplate` is sent, the server writes `workflow: project|isolated` into the generated `workflow.md`. **Tool policy** maps to `approvalPolicy` (persisted as `approval_policy` in `task.md`). Optional `?` expands details for each. The dialog calls `automation/task/create` on submit.

### 15.8 State Store

New Zustand store domain:

| Field | Type | Updated By |
|-------|------|------------|
| `tasks` | `AutomationTaskWire[]` | `automation/task/list` response, `automation/task/stateChanged` notifications |
| `activeFilter` | `string` | User tab selection |
| `selectedTaskId` | `string \| null` | User task selection |
| `reviewPending` | `AutomationTaskWire \| null` | `automation/review/requested` notification |

When a task transitions to `running` and the user has it selected, the Desktop automatically subscribes to the task's `threadId` via `thread/subscribe` for live turn streaming.

### 15.9 Navigation

`uiStore.activeMainView` accepts `'automations'` as a value (already reserved in the Desktop Client Spec §20.4.4). Selecting "Automations" in the sidebar sets this value and renders the Automations view. Selecting a thread or creating a new thread switches back to `'conversation'`.

---

## 16. Configuration

### 16.1 AutomationsConfig

```
[Automations]
  Enabled = true

[Automations.Local]
  WorkflowPath = "WORKFLOW.md"
  TasksDirectory = ".craft/automations/tasks"
  WorkflowsDirectory = ".craft/automations/workflows"

[Automations.Orchestrator]
  PollIntervalMs = 30000
  MaxConcurrentAgents = 3
  MaxTurns = 20
  MaxRounds = 3
  TurnTimeoutMs = 3600000
  StallTimeoutMs = 300000
  MaxRetryBackoffMs = 300000

[Automations.Workspace]
  Root = ""

[Automations.Hooks]
  AfterCreate = ""
  BeforeRun = ""
  AfterRun = ""
  BeforeRemove = ""
  AfterApprove = ""
  AfterReject = ""
  TimeoutMs = 60000
```

### 16.2 Configuration Hierarchy

When a workflow file specifies front-matter overrides, the merge order is:

```
AutomationsConfig defaults
    ← Workflow YAML front matter overrides
        ← Per-task overrides (if any)
```

This is consistent with the existing `WorkflowLoader` behavior where YAML keys override base config fields.

### 16.3 GitHubTracker Configuration Relationship

The existing `GitHubTrackerConfig` continues to own GitHub-specific settings (API key, repository, state labels, assignee filter). Orchestrator-level settings that were previously in `GitHubTrackerConfig.Agent` and `GitHubTrackerConfig.Polling` are now mirrored in `AutomationsConfig.Orchestrator`. When both modules are enabled, `AutomationsConfig.Orchestrator` takes precedence for shared settings.

### 16.4 Observability, Logging, and Diagnostics

This subsection defines **operational visibility** for Automations when running headless (e.g. Gateway without Desktop): operators must be able to rely on **workspace file logs** under `.craft/logs/` (see core `Logging` configuration) to confirm that polling, dispatch, and task state transitions occurred, without opening Session thread JSON or tracing tools.

#### Goals

- With **only** global `Logging` enabled (`Logging.Enabled`, `Logging.Directory`, `Logging.MinLevel` at `Information` or lower), a human can grep `.craft/logs/dotcraft-*.log` and see **poll → dispatch → thread association → task status transitions** for each automation task.
- Logs are **supplementary** to Session thread history and Tracing: they do not duplicate full prompts or tool payloads.

#### Relationship to other mechanisms

| Mechanism | Role |
|-----------|------|
| **Global `Logging`** (`AppConfig.Logging`) | File output via `ILogger`; default directory relative to `.craft` (e.g. `logs/`). No separate `Automations.Logging` section is required; an optional future extension could add Automations-specific paths or levels. |
| **Tracing** | Fine-grained or performance diagnostics; not a substitute for the **minimum orchestrator events** below at `Information`. |
| **Session thread store** (`threads/*.json`) | Authoritative conversation and turn history for the agent; orchestrator logs reference **task id** and **thread id**, not full message bodies. |

#### Minimum log events (orchestrator)

Implementations **SHOULD** emit at least the following at **`LogLevel.Information`** (subject to `Logging.MinLevel`), with structured properties or a consistent text format so that **`taskId`**, **`sourceName`**, and **`threadId`** (when known) can be grepped:

| Phase | Event (conceptual) | Notes |
|-------|-------------------|--------|
| Poll | Poll cycle completed | May include duration; per-source **pending task count** or a **truncated list of task ids** (e.g. first N). |
| Dispatch | Dispatch started | `taskId`, `sourceName`. |
| Thread | Thread created or resumed | `taskId`, `threadId`, `sourceName`. |
| Status | Task enters `AgentRunning` | After thread is bound. |
| Workflow | Turn / round milestones | At least one line per completed round or per workflow step batch (implementation may aggregate). |
| Completion | Task enters `AgentCompleted` / `AwaitingReview` / `Failed` | `taskId`, final status; include summary length or hash, not full summary text. |
| Errors | `GetPendingTasksAsync`, dispatch, or persistence failure | `LogLevel.Error` with exception; include `taskId` and `sourceName` when available. |

#### Non-goals

- No requirement to ship a **central log stack** (ELK, Loki, etc.) in this spec.
- **Wire-delivered log streams** or **Desktop-embedded log panels** are optional and may be specified in Wire Protocol / Desktop milestones (e.g. M6/M7), not as a prerequisite for file logging.

---

## 17. Module Structure

### 17.1 DotCraft.Automations Project

```
src/DotCraft.Automations/
    DotCraft.Automations.csproj
    AutomationsModule.cs
    AutomationsConfig.cs
    Core/
        IAutomationSource.cs
        IToolProfileRegistry.cs
        ToolProfileRegistry.cs
        AutomationTask.cs
        AgentRunOutcome.cs
        TaskStateSnapshot.cs
        CompositeAutomationSource.cs
    Orchestrator/
        AutomationOrchestrator.cs
        OrchestratorState.cs
        DispatchSorter.cs
        RetryQueue.cs
    Protocol/
        AutomationSessionClient.cs       # in-process adapter over ISessionService
        AutomationsRequestHandler.cs     # handles automation/* Wire Protocol methods
    Workflow/
        WorkflowLoader.cs
        WorkflowDefinition.cs
    Workspace/
        AutomationWorkspaceManager.cs
        AutomationWorkspace.cs
    Sources/
        Local/
            LocalAutomationSource.cs
            LocalTaskStore.cs
            LocalTaskCompletionToolProvider.cs
    ChannelService/
        AutomationsChannelService.cs
```

### 17.2 DotCraft.GitHubTracker (Slimmed)

```
src/DotCraft.GitHubTracker/
    DotCraft.GitHubTracker.csproj
    GitHubTrackerConfig.cs
    GitHubTrackerModule.cs
    GitHubAutomationSource.cs
    Tools/
        IssueCompletionToolProvider.cs
        PullRequestReviewToolProvider.cs
```

`GitHubTrackerModule.ConfigureServices` registers `GitHubAutomationSource` as an `IAutomationSource` in the DI container. The Automations module discovers all registered `IAutomationSource` instances and passes them to the orchestrator.

### 17.3 DotCraft.Core Extensions

The following new types are added to `DotCraft.Core` as part of the Session Core extensions (§9):

```
src/DotCraft.Core/
    Protocol/
        ThreadConfiguration.cs           # + WorkspaceOverride, ToolProfile, ApprovalPolicy
    Agents/
        IToolProfileRegistry.cs          # interface moved/added to Core
```

`AppServerRequestHandler` is updated to handle the `channelName` filter on `thread/list` and to pass `ThreadConfiguration` extensions through `BuildAgentForConfigAsync`.

### 17.4 Project Dependencies

```
DotCraft.Automations → DotCraft.Core
DotCraft.GitHubTracker → DotCraft.Core, DotCraft.Automations
DotCraft.App → DotCraft.Core, DotCraft.Automations, DotCraft.GitHubTracker
```

`DotCraft.GitHubTracker` depends on `DotCraft.Automations` for the `IAutomationSource` interface and the `AutomationTask` model. The reverse dependency does not exist — the Automations module has no knowledge of GitHub.

---

## 18. Migration Path

### 18.1 Phase 1: Session Core Prerequisites

Before the Automations module can use the shared `SessionService`, the following Session Core changes must land in `DotCraft.Core` and `DotCraft.App`:

- Add `ThreadConfiguration.WorkspaceOverride` and update `BuildAgentForConfigAsync` to create workspace-scoped tool contexts.
- Add `ThreadConfiguration.ToolProfile`, add `IToolProfileRegistry` to DI, and update `BuildAgentForConfigAsync` to resolve and inject profile tools.
- Add `ThreadConfiguration.ApprovalPolicy` and use `AutoApproveApprovalService` when set to `"auto"`.
- Extend `FindThreadsAsync` and `thread/list` with a `channelName` filter.

### 18.2 Phase 2: Automations Core

- Create `DotCraft.Automations` project with `AutomationOrchestrator`, `AutomationSessionClient`, `WorkflowLoader`, `WorkspaceManager`, and `CompositeAutomationSource`.
- Implement `LocalAutomationSource` with file-based task store and `LocalTaskCompletionToolProvider`.
- Implement `IAutomationSource` interface.
- Refactor `GitHubTrackerAdapter` into `GitHubAutomationSource` implementing `IAutomationSource`.
- Wire `GitHubTrackerModule` to register its source with the Automations orchestrator.
- Add `AutomationsRequestHandler` for `automation/task/*` Wire Protocol methods.
- Register `AutomationOrchestrator` as a hosted service in `AppServerHost`.

### 18.3 Phase 3: Desktop Integration

- Implement the Automations view in the Desktop client.
- Implement the review panel with Changes, Summary, and Log tabs.
- Wire live turn streaming: subscribe to `threadId` when a task transitions to `running`.
- Add the new task creation dialog.
- Wire Zustand store to `automation/*` notifications and `thread/subscribe` for running tasks.

### 18.4 Phase 4: Deprecation

- Deprecate `GitHubTrackerOrchestrator` and its direct `SessionService` instantiation.
- Remove `WorkItemAgentRunnerFactory` (replaced by `AutomationOrchestrator` + `AutomationSessionClient`).
- Remove duplicated orchestration logic from `DotCraft.GitHubTracker`.
- Update documentation and samples.

### 18.5 Backward Compatibility

- Existing `WORKFLOW.md` and `PR_WORKFLOW.md` files work without changes. The `work_item.*` template variables are aliased to `task.*`.
- Existing `GitHubTrackerConfig` settings continue to function.
- The `github-tracker` channel name is preserved for backward compatibility. During the migration period (Phases 1–3), the existing `GitHubTrackerChannelService` continues to operate in Gateway mode unchanged.
- The `IOrchestratorSnapshotProvider` interface is preserved; the snapshot now includes tasks from all sources.
- After Phase 4, GitHubTracker in Gateway mode is no longer supported. Users who ran GitHubTracker headlessly should switch to running `dotcraft app-server` which includes the Automations orchestrator.
