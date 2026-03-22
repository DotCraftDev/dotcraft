# M8 — Desktop: Review Panel & Live Thread Streaming

| Field | Value |
|-------|-------|
| **Milestone** | M8 |
| **Title** | Desktop: Task Review Panel & Live Thread Streaming |
| **Status** | Pending |
| **Parent Spec** | [Automations Lifecycle](../automations-lifecycle.md) §15.3–6 |
| **Depends On** | M7 |
| **Blocks** | — (final milestone) |

## Overview

This milestone delivers the **task review panel** — the detailed view that opens when a user clicks "Review" or "View" on a task card. It reuses the existing Desktop thread/conversation UI components to display the agent's work in progress or completed output, and adds Approve/Reject actions for tasks in the `awaiting_review` state.

Key capabilities:
- Live streaming of agent events for tasks that are still running (`agent_running` state).
- Completed conversation view for tasks in `awaiting_review`, `approved`, or `rejected`.
- Agent summary section shown above the conversation when available.
- Approve / Reject action bar with confirmation dialog and optional rejection reason.
- Diff viewer (optional, stretch goal) for local tasks that produced file changes.

## Scope

### In Scope

- `TaskReviewPanel` — the side panel or modal that opens when `selectedTaskId` is set.
- Live streaming via `session/events` subscription for the task's `threadId`.
- Reuse of existing `MessageBubble` / `EventList` / `ConversationThread` components.
- Agent summary section.
- Approve/Reject action bar and confirmation dialog.
- Handling the panel closing (deselecting task) and reconnecting if the thread changes.
- Panel state management as an extension of `AutomationsStore` or a co-located `reviewPanelStore`.

### Out of Scope

- Diff viewer / file change inspection — post-M8 enhancement.
- Editing the workflow or re-running a rejected task from the UI — future scope.
- GitHub PR merge UI (approve calls `automation/task/approve` which the backend maps to the GitHub API).

## Requirements

### R8.1 — TaskReviewPanel layout

`desktop/src/components/automations/TaskReviewPanel.tsx`:

```
TaskReviewPanel (side panel, slides in from the right)
├── Header
│   ├── Task title
│   ├── Status badge
│   ├── Source badge ("Local" | "GitHub")
│   └── Close button (× clears selectedTaskId)
├── Agent Summary section (shown only when agentSummary is non-null)
│   ├── "Agent Summary" heading
│   └── Markdown-rendered summary text
├── Conversation section
│   ├── Heading: "Agent Activity"
│   └── EventList (reused from existing thread UI)
│       └── MessageBubble × N (tool calls, assistant messages, errors)
└── Action bar (shown only when status == 'awaiting_review')
    ├── "Approve" button (primary green)
    └── "Reject" button (secondary red)
```

The panel width is 480 px (resizable). It renders as a split-pane alongside the task list, not as a full-screen modal.

### R8.2 — Live streaming for agent_running tasks

When the panel opens for a task with `threadId != null` and `status == "agent_running"`:

1. Subscribe to `session/events` for the thread (using the existing `AppServerClient.subscribeToThread` mechanism).
2. Append events to the `EventList` as they arrive.
3. Auto-scroll to the bottom on each new event.
4. When an `AgentTurnCompleted` or `Interrupted` event is received, stop streaming but keep the events visible.

The `session/events` subscription uses the existing Wire Protocol channel — no new protocol work is required.

### R8.3 — Completed task conversation view

When the panel opens for a task with `status` in `["awaiting_review", "approved", "rejected"]`:

1. Call `thread/history` (existing Wire Protocol method) for `task.threadId` to load the full conversation.
2. Render all events in the `EventList` (non-streaming, static snapshot).
3. Display the agent summary (if any) above the conversation.

If `threadId` is null (task was never dispatched or thread data was lost), show a placeholder: "No agent activity recorded."

### R8.4 — Agent summary section

The `agentSummary` field is loaded from `AutomationsStore` (set when `automation/task/read` returns a non-null `agentSummary`, or updated via `automation/task/updated` notification).

The summary is rendered using the existing Markdown renderer component. If `agentSummary` is null, the section is hidden entirely (no empty heading).

### R8.5 — Approve action

Clicking "Approve":
1. Opens a small confirmation dialog: "Approve this task? The agent's changes will be applied."
2. On confirm, calls `AutomationsStore.approveTask(taskId, sourceName)`.
3. Shows a loading spinner on the "Approve" button while the request is in flight.
4. On success: the action bar disappears (status transitions to `approved` via notification).
5. On error: show an inline error message below the action bar.

### R8.6 — Reject action

Clicking "Reject":
1. Opens a dialog with an optional text field: "Reason for rejection (optional)".
2. On confirm, calls `AutomationsStore.rejectTask(taskId, sourceName, reason)`.
3. Shows a loading spinner on the "Reject" button while in flight.
4. On success: the action bar disappears (status transitions to `rejected` via notification).
5. On error: show an inline error message.

### R8.7 — Panel lifecycle and reconnection

- When `selectedTaskId` changes (user selects a different task), the previous `session/events` subscription is cancelled and a new one is established for the new `threadId`.
- If the task transitions from `agent_running` to `awaiting_review` while the panel is open, the stream ends automatically (because `AgentTurnCompleted` is received) and the action bar appears.
- If the panel is open for a `pending` or `dispatched` task (no `threadId` yet), show a "Waiting for agent to start…" placeholder and poll `AutomationsStore` for a `threadId` update.

### R8.8 — Keyboard shortcuts

| Shortcut | Action |
|----------|--------|
| `Escape` | Close the review panel |
| `Ctrl+Enter` | Confirm Approve (when confirmation dialog is open) |

### R8.9 — Store extensions

`AutomationsStore` (or a co-located `reviewPanelStore`) gains:

```typescript
interface ReviewPanelState {
  taskDetail: AutomationTask | null;   // full detail including description and agentSummary
  events: SessionEvent[];              // streamed or loaded events
  streamingActive: boolean;
  approving: boolean;
  rejecting: boolean;
  actionError: string | null;
}
```

Actions:
- `openReviewPanel(taskId)` — fetches full task detail via `automation/task/read`, then subscribes to events if running.
- `closeReviewPanel()` — unsubscribes from events, clears state.
- `appendEvent(event)` — called by the `session/events` subscription callback.

## Acceptance Criteria

| # | Criterion |
|---|-----------|
| AC1 | Clicking "Review" on an `awaiting_review` task opens the panel, showing the agent summary and full conversation. |
| AC2 | Clicking "View" on an `agent_running` task opens the panel and streams live agent events in real time. |
| AC3 | Events stop appearing when the agent completes (stream ends), and the Approve/Reject bar appears. |
| AC4 | Clicking "Approve" → confirm transitions the task to `approved` and hides the action bar. |
| AC5 | Clicking "Reject" with a reason transitions the task to `rejected` and the reason is sent to the backend. |
| AC6 | Closing the panel (`×` or `Escape`) clears `selectedTaskId` and cancels any active `session/events` subscription. |
| AC7 | Opening the panel for a different task cancels the previous subscription and subscribes to the new thread. |
| AC8 | The panel shows "No agent activity recorded." when `threadId` is null. |
| AC9 | The agent summary section is hidden when `agentSummary` is null. |
| AC10 | For tasks in `approved` or `rejected` status, the action bar is not rendered. |
| AC11 | `thread/history` is used to load the conversation for completed tasks (no active streaming). |

## Affected Files

| File | Change |
|------|--------|
| `desktop/src/components/automations/TaskReviewPanel.tsx` | New: review panel component |
| `desktop/src/components/automations/ApproveDialog.tsx` | New: confirm approve dialog |
| `desktop/src/components/automations/RejectDialog.tsx` | New: reject with reason dialog |
| `desktop/src/stores/automationsStore.ts` | Add `ReviewPanelState` and review panel actions |
| `desktop/src/components/automations/AutomationsView.tsx` | Render `TaskReviewPanel` when `selectedTaskId` is set |
| `desktop/src/lib/AppServerClient.ts` | Reuse `subscribeToThread` for `session/events` in review panel |
