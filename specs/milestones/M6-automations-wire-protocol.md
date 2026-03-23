# M6 — Automations: Wire Protocol Methods & Notifications

| Field | Value |
|-------|-------|
| **Milestone** | M6 |
| **Title** | Automations: Wire Protocol Methods & Notifications |
| **Status** | Pending |
| **Parent Spec** | [Automations Lifecycle](../automations-lifecycle.md) §14 |
| **Depends On** | M3, M4 |
| **Blocks** | M7, M8 |

## Overview

This milestone exposes the `DotCraft.Automations` backend to the Wire Protocol, giving the Desktop (and any other client) a typed API for:

- Listing all automation tasks.
- Reading a single task (with `threadId` for live streaming).
- Creating a new local task.
- Approving or rejecting a completed task.
- Receiving push notifications when task status changes.

All methods are prefixed `automation/` and follow the existing Wire Protocol JSON-RPC 2.0 conventions used by `thread/*` and `session/*` methods. The capability is gated — clients must advertise `"automations"` in their capabilities handshake to receive notifications.

## Scope

### In Scope

- `AutomationsRequestHandler` class implementing all `automation/*` request handlers.
- Registration of `AutomationsRequestHandler` in `AppServerRequestHandler`.
- Wire Protocol message schemas for all new methods and notifications.
- Capability gate: server advertises `"automations"` capability; notifications are only sent to subscribed clients.
- `AutomationsEventDispatcher`: pushes `automation/task/updated` notifications to subscribed clients when task status changes.
- `AppServerProtocol` DTO updates for all new message types.

### Out of Scope

- Desktop UI — M7/M8.
- GitHub-specific Wire Protocol extensions (none needed; GitHub tasks use the same `automation/*` methods).
- `thread/*` method changes — these were already completed in M2.

## Requirements

### R6.1 — Capability advertisement

The AppServer's `initialize` response capability list includes `"automations"` when `AutomationsModule` is loaded:

```json
{
  "capabilities": ["session", "threads", "automations"]
}
```

The Desktop's `AutomationsStore` (M7) checks for this capability before enabling the Automations view.

### R6.2 — automation/task/list

**Request:**
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "automation/task/list",
  "params": {
    "workspacePath": "/workspace",
    "sourceName": "local"     // optional; omit to list all sources
  }
}
```

**Response:**
```json
{
  "result": {
    "tasks": [
      {
        "id": "my-task-001",
        "title": "Implement feature X",
        "status": "awaiting_review",
        "sourceName": "local",
        "threadId": "thread-abc123",
        "createdAt": "2026-03-22T10:00:00Z",
        "updatedAt": "2026-03-22T12:30:00Z"
      }
    ]
  }
}
```

Handler queries `AutomationOrchestrator.GetAllTasks()` filtered by optional `sourceName`.

### R6.3 — automation/task/read

**Request:**
```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "automation/task/read",
  "params": {
    "workspacePath": "/workspace",
    "taskId": "my-task-001",
    "sourceName": "local"
  }
}
```

**Response:**
```json
{
  "result": {
    "id": "my-task-001",
    "title": "Implement feature X",
    "status": "awaiting_review",
    "sourceName": "local",
    "threadId": "thread-abc123",
    "description": "Markdown body of task.md",
    "agentSummary": "I implemented X by...",
    "createdAt": "2026-03-22T10:00:00Z",
    "updatedAt": "2026-03-22T12:30:00Z"
  }
}
```

`description` is the Markdown body of `task.md`. `agentSummary` is null until `agent_completed`. `threadId` is null until dispatched.

### R6.4 — automation/task/create

Creates a new local task. Not applicable to GitHub source (GitHub tasks are discovered automatically).

**Request:**
```json
{
  "jsonrpc": "2.0",
  "id": 3,
  "method": "automation/task/create",
  "params": {
    "workspacePath": "/workspace",
    "title": "Implement feature X",
    "description": "Please implement feature X as described...",
    "workflowTemplate": "default"   // optional; name of a workflow template to copy
  }
}
```

**Response:**
```json
{
  "result": {
    "taskId": "my-task-001",
    "taskDirectory": "/workspace/.craft/tasks/my-task-001"
  }
}
```

Handler:
1. Generates a unique `taskId` (slug from title + timestamp).
2. Creates the task directory and writes `task.md` with `status: pending`.
3. Copies the specified workflow template to `workflow.md`, or creates a default one.
4. `LocalTaskFileStore.WatchForNewTasks` triggers, adding the task to the pending set.

### R6.5 — automation/task/approve

**Request:**
```json
{
  "jsonrpc": "2.0",
  "id": 4,
  "method": "automation/task/approve",
  "params": {
    "workspacePath": "/workspace",
    "taskId": "my-task-001",
    "sourceName": "local"
  }
}
```

**Response:**
```json
{ "result": { "ok": true } }
```

Handler calls `IAutomationSource.ApproveTaskAsync(taskId)` on the source identified by `sourceName`. Errors (task not in `awaiting_review` status, unknown task) are returned as JSON-RPC error responses.

### R6.6 — automation/task/reject

**Request:**
```json
{
  "jsonrpc": "2.0",
  "id": 5,
  "method": "automation/task/reject",
  "params": {
    "workspacePath": "/workspace",
    "taskId": "my-task-001",
    "sourceName": "local",
    "reason": "The implementation is incomplete."   // optional
  }
}
```

**Response:**
```json
{ "result": { "ok": true } }
```

Handler calls `IAutomationSource.RejectTaskAsync(taskId, reason)`.

### R6.7 — automation/task/updated notification

Pushed to subscribed clients (those that advertised `"automations"` capability) whenever a task's status changes:

```json
{
  "jsonrpc": "2.0",
  "method": "automation/task/updated",
  "params": {
    "workspacePath": "/workspace",
    "task": {
      "id": "my-task-001",
      "title": "Implement feature X",
      "status": "awaiting_review",
      "sourceName": "local",
      "threadId": "thread-abc123",
      "updatedAt": "2026-03-22T12:30:00Z"
    }
  }
}
```

`AutomationsEventDispatcher` subscribes to `IAutomationSource.OnStatusChangedAsync` callbacks (via an event on `AutomationOrchestrator`) and publishes this notification.

### R6.8 — AutomationsRequestHandler

New class `AutomationsRequestHandler` in `src/DotCraft.Automations/Protocol/AutomationsRequestHandler.cs`:

```csharp
public class AutomationsRequestHandler
{
    public Task<object?> HandleAsync(string method, JsonElement? paramsElement, CancellationToken ct);
}
```

Registered in `AppServerRequestHandler.RouteAsync` under the `"automation/"` prefix.

### R6.9 — Error codes

Automation methods use the dedicated range `-32050`–`-32059` (see `AppServerErrors` in the main Wire Protocol spec, §8.3) so they do not collide with global codes such as `-32001` (server overloaded) or `-32002` (not initialized).

| Code | Meaning |
|------|---------|
| `-32050` | Reserved for automations capability not available (not currently emitted by the server) |
| `-32051` | Task not found |
| `-32052` | Task not in expected status (e.g., approve called on non-`awaiting_review` task) |
| `-32053` | Source not found |
| `-32054` | Task already exists (e.g., create with duplicate task ID) |

## Acceptance Criteria

| # | Criterion |
|---|-----------|
| AC1 | `automation/task/list` returns all tasks for the workspace, with correct status and `threadId` fields. |
| AC2 | `automation/task/list` with `sourceName = "local"` returns only local tasks. |
| AC3 | `automation/task/read` returns the full task including `description` and `agentSummary`. |
| AC4 | `automation/task/create` creates `task.md` on disk and returns the task directory path. |
| AC5 | The created task appears in the next `automation/task/list` response without restarting the server. |
| AC6 | `automation/task/approve` on a task in `awaiting_review` transitions it to `approved` and returns `{ ok: true }`. |
| AC7 | `automation/task/approve` on a task in `agent_running` returns error `-32052`. |
| AC8 | `automation/task/reject` with a reason stores the reason and transitions to `rejected`. |
| AC9 | A subscribed client receives `automation/task/updated` notification within 1 second of a status transition. |
| AC10 | A client that did not advertise `"automations"` capability does not receive `automation/task/updated` notifications. |
| AC11 | The `initialize` response lists `"automations"` in the capabilities array. |

## Affected Files

| File | Change |
|------|--------|
| `src/DotCraft.Automations/Protocol/AutomationsRequestHandler.cs` | New: request handler |
| `src/DotCraft.Automations/Protocol/AutomationsEventDispatcher.cs` | New: push notification dispatcher |
| `src/DotCraft.Core/Protocol/AppServer/AppServerRequestHandler.cs` | Route `automation/*` to `AutomationsRequestHandler` |
| `src/DotCraft.Core/Protocol/AppServer/AppServerEventDispatcher.cs` | Forward `automation/task/updated` to capability-filtered clients |
| `src/DotCraft.Core/Protocol/AppServer/AppServerProtocol.cs` | Add DTOs for all new message types |
| `src/DotCraft.App/AppServer/AppServerHost.cs` | Add `"automations"` to capabilities list |
| `src/DotCraft.Automations/AutomationsModule.cs` | Register `AutomationsRequestHandler`, `AutomationsEventDispatcher` |
