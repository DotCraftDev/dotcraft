# M4 — Automations: Local Task Source, File Store & Review Gate Backend

| Field | Value |
|-------|-------|
| **Milestone** | M4 |
| **Title** | Automations: Local Task Source, File Store & Review Gate Backend |
| **Status** | Pending |
| **Parent Spec** | [Automations Lifecycle](../automations-lifecycle.md) §6–8, §11–13 |
| **Depends On** | M3 |
| **Blocks** | M6, M7 |

## Overview

This milestone implements the **local task source** — the primary new capability of the Automations module. Local tasks are defined by a YAML/Liquid task file on disk (`task.md`) in a tasks directory, and they use the same `workflow.md` format already used by `GitHubTracker`.

The milestone covers:
- `LocalAutomationTask` model and the `LocalAutomationSource` implementation of `IAutomationSource`.
- File-based task store: scanning task directories, parsing task files, persisting state transitions.
- `LocalWorkflowLoader`: hot-reloading `workflow.md` per task.
- Local task lifecycle state machine with the `agent_completed` sentinel.
- Review gate backend: storing the agent summary in the task file and exposing the `Approve`/`Reject` methods consumed by M6.
- Tool profile for local tasks (file I/O, shell, memory, no GitHub tools).

## Scope

### In Scope

- `LocalAutomationTask` — concrete `AutomationTask` subclass with local fields.
- `LocalAutomationSource : IAutomationSource` — full implementation.
- `LocalTaskFileStore` — reads and writes task YAML front-matter.
- `LocalWorkflowLoader` — parses `workflow.md` with Liquid templating, supports hot-reload.
- Local task state machine: `Pending → Dispatched → AgentRunning → AgentCompleted → AwaitingReview → Approved | Rejected`.
- `agent_completed` sentinel: detection mechanism in the task file that signals the orchestrator to stop submitting turns.
- Review gate: `ApproveTaskAsync` / `RejectTaskAsync` called by the Wire Protocol handler (M6).
- Task directory watcher: picks up newly added task files without requiring an AppServer restart.
- Tool profile registration for local tasks.

### Out of Scope

- Wire Protocol methods (`automation/*`) — M6.
- Desktop UI — M7/M8.
- GitHub source — M5.

## Requirements

### R4.1 — Task directory layout

A local tasks root is configured under `AutomationsConfig.LocalTasksRoot` (default: `{workspaceRoot}/.craft/tasks/`). The tasks root contains one subdirectory per task:

```
.craft/tasks/
  {task-id}/
    task.md          ← task front-matter + description (YAML + Markdown body)
    workflow.md      ← agent prompts and metadata (same format as GitHubTracker)
    workspace/       ← agent working directory (provisioned by AutomationWorkspaceManager)
```

`task.md` front-matter schema:

```yaml
---
id: "my-task-001"
title: "Implement feature X"
status: pending          # pending | dispatched | agent_running | agent_completed | awaiting_review | approved | rejected | failed
created_at: "2026-03-22T10:00:00Z"
updated_at: "2026-03-22T10:00:00Z"
thread_id: null          # populated by orchestrator at dispatch
agent_summary: null      # populated by LocalAutomationSource.OnAgentCompletedAsync
---
Task description in Markdown.
```

### R4.2 — LocalAutomationTask

```csharp
public class LocalAutomationTask : AutomationTask
{
    /// <summary>Absolute path to the task directory.</summary>
    public required string TaskDirectory { get; init; }

    /// <summary>Absolute path to task.md.</summary>
    public string TaskFilePath => Path.Combine(TaskDirectory, "task.md");

    /// <summary>Absolute path to workflow.md.</summary>
    public string WorkflowFilePath => Path.Combine(TaskDirectory, "workflow.md");

    /// <summary>
    /// Summary written by the agent upon completion.
    /// Null until the agent completes.
    /// </summary>
    public string? AgentSummary { get; set; }
}
```

### R4.3 — LocalTaskFileStore

Responsible for all disk I/O for task files:

```csharp
public class LocalTaskFileStore
{
    /// <summary>Scans the tasks root and returns all discovered tasks.</summary>
    public Task<IReadOnlyList<LocalAutomationTask>> LoadAllAsync(CancellationToken ct);

    /// <summary>Reads a single task file and returns the task model.</summary>
    public Task<LocalAutomationTask> LoadAsync(string taskDirectory, CancellationToken ct);

    /// <summary>Persists status, threadId, and agentSummary back to task.md front-matter.</summary>
    public Task SaveAsync(LocalAutomationTask task, CancellationToken ct);

    /// <summary>
    /// Watches the tasks root for new subdirectories.
    /// Invokes the callback when a new task.md appears.
    /// </summary>
    public IDisposable WatchForNewTasks(Action<LocalAutomationTask> onNewTask);
}
```

YAML parsing uses `YamlDotNet`. The Markdown body (below the `---` delimiter) is preserved verbatim when saving.

### R4.4 — LocalWorkflowLoader

```csharp
public class LocalWorkflowLoader
{
    /// <summary>
    /// Loads and parses workflow.md for the task.
    /// Supports Liquid template variables from the task context.
    /// Returns a WorkflowDefinition.
    /// </summary>
    public Task<WorkflowDefinition> LoadAsync(LocalAutomationTask task, CancellationToken ct);

    /// <summary>
    /// Watches workflow.md for changes. On change, re-parses and invokes the callback.
    /// Used to support hot-reload during active tasks.
    /// </summary>
    public IDisposable Watch(LocalAutomationTask task, Action<WorkflowDefinition> onReload);
}
```

Template variables available in `workflow.md` for local tasks:

| Variable | Value |
|----------|-------|
| `task.id` | `LocalAutomationTask.Id` |
| `task.title` | `LocalAutomationTask.Title` |
| `task.description` | Markdown body of `task.md` |
| `task.workspace_path` | Provisioned workspace directory path |
| `work_item.id` | Alias for `task.id` (backward compat) |
| `work_item.title` | Alias for `task.title` |

### R4.5 — Agent completion detection via sentinel state

The orchestrator detects task completion by reading the task file after each agent turn. When the agent writes `status: agent_completed` to `task.md` (via the file-write tool), the orchestrator stops submitting new turns and transitions to `AwaitingReview`.

`LocalAutomationSource.GetPendingTasksAsync` never returns tasks with status `agent_completed` or higher — these are handled by the review gate.

The agent is instructed in the system prompt (via workflow.md template) to write `status: agent_completed` to `task.md` front-matter when it has finished all assigned work.

### R4.6 — LocalAutomationSource implementation

```csharp
public class LocalAutomationSource : IAutomationSource
{
    public string Name => "local";
    public string ToolProfileName => "local-task";

    public void RegisterToolProfile(IToolProfileRegistry registry)
    {
        // Registers file I/O, shell, memory, and search tools.
        // Does NOT include GitHub tools.
        registry.Register("local-task", BuildLocalTaskProviders());
    }

    public Task<IReadOnlyList<AutomationTask>> GetPendingTasksAsync(CancellationToken ct)
        => fileStore.LoadAllAsync(ct)
               .Where(t => t.Status == AutomationTaskStatus.Pending);

    public Task<WorkflowDefinition> GetWorkflowAsync(AutomationTask task, CancellationToken ct)
        => workflowLoader.LoadAsync((LocalAutomationTask)task, ct);

    public async Task OnStatusChangedAsync(AutomationTask task, AutomationTaskStatus newStatus, CancellationToken ct)
    {
        task.Status = newStatus;
        await fileStore.SaveAsync((LocalAutomationTask)task, ct);
    }

    public async Task OnAgentCompletedAsync(AutomationTask task, string agentSummary, CancellationToken ct)
    {
        var localTask = (LocalAutomationTask)task;
        localTask.AgentSummary = agentSummary;
        await fileStore.SaveAsync(localTask, ct);
    }
}
```

### R4.7 — Review gate: Approve and Reject

Two methods are exposed on `LocalAutomationSource` (and ultimately on the orchestrator for Wire Protocol routing in M6):

```csharp
/// <summary>
/// Approves the task. Transitions status to Approved and optionally
/// runs a post-approval hook (e.g., git commit, push) if defined in workflow.md.
/// </summary>
public Task ApproveTaskAsync(string taskId, CancellationToken ct);

/// <summary>
/// Rejects the task. Transitions status to Rejected.
/// Optionally runs a post-rejection hook if defined in workflow.md.
/// </summary>
public Task RejectTaskAsync(string taskId, string? reason, CancellationToken ct);
```

### R4.8 — Workflow post-approval hooks

`WorkflowDefinition` gains an optional `on_approve` and `on_reject` hook field. When the hook is defined, `LocalAutomationSource` executes the specified shell command in the task workspace after status transition:

```yaml
---
name: "Feature implementation workflow"
max_rounds: 20
on_approve: "git add -A && git commit -m 'Automated: {{task.title}}'"
on_reject: "git checkout ."
---
```

Hook execution failures are logged and do not revert the status transition.

### R4.9 — Task directory watcher

`LocalAutomationSource` subscribes to `LocalTaskFileStore.WatchForNewTasks`. When a new task directory is detected, the task is added to the in-memory pending set and will be dispatched on the next orchestrator poll cycle (no restart required).

## Acceptance Criteria

| # | Criterion |
|---|-----------|
| AC1 | A `task.md` with `status: pending` in the tasks root is picked up by `GetPendingTasksAsync` on the next poll cycle. |
| AC2 | The orchestrator dispatches the task, sets `status: dispatched`, and writes `thread_id` to `task.md`. |
| AC3 | When the agent writes `status: agent_completed` to `task.md`, the orchestrator stops submitting turns and transitions the task to `AwaitingReview`. |
| AC4 | `OnAgentCompletedAsync` stores the agent summary in `task.md` front-matter under `agent_summary`. |
| AC5 | `ApproveTaskAsync` transitions `status` to `approved` and runs the `on_approve` hook if defined. |
| AC6 | `RejectTaskAsync` transitions `status` to `rejected` and runs the `on_reject` hook if defined. |
| AC7 | Hook execution failure logs the error but does not change the status back. |
| AC8 | A task file added to the tasks root after AppServer startup is discovered without restarting the server. |
| AC9 | `workflow.md` template variables `task.id`, `task.title`, and `task.description` are substituted correctly. |
| AC10 | `work_item.id` and `work_item.title` resolve to the same values as `task.id` and `task.title`. |
| AC11 | The `local-task` tool profile is registered before the first task is dispatched. |
| AC12 | Local task agents do not have access to GitHub-related tools. |

## Affected Files

| File | Change |
|------|--------|
| `src/DotCraft.Automations/AutomationsConfig.cs` | Add `LocalTasksRoot` field |
| `src/DotCraft.Automations/Local/LocalAutomationTask.cs` | New: local task model |
| `src/DotCraft.Automations/Local/LocalAutomationSource.cs` | New: IAutomationSource impl |
| `src/DotCraft.Automations/Local/LocalTaskFileStore.cs` | New: YAML file I/O |
| `src/DotCraft.Automations/Local/LocalWorkflowLoader.cs` | New: workflow parsing for local tasks |
| `src/DotCraft.Automations/AutomationsModule.cs` | Register `LocalAutomationSource` |
| `src/DotCraft.GitHubTracker/Workflow/WorkflowDefinition.cs` | Add `OnApprove`, `OnReject` hook fields (shared model) |
