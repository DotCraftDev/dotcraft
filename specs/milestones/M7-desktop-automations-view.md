# M7 — Desktop: Automations View, Task List & New Task Dialog

| Field | Value |
|-------|-------|
| **Milestone** | M7 |
| **Title** | Desktop: Automations View, Task List & New Task Dialog |
| **Status** | Pending |
| **Parent Spec** | [Automations Lifecycle](../automations-lifecycle.md) §15.1–2, §15.7–9 |
| **Depends On** | M6 |
| **Blocks** | M8 |

## Overview

This milestone builds the Automations section of the Desktop client — the primary UI through which users monitor and manage automation tasks. The view is already reserved in the Desktop layout (per `specs/desktop-client.md`); this milestone populates it with real content.

Deliverables:
- **`AutomationsStore`** (Zustand): subscribes to `automation/task/updated` notifications, caches the task list, and exposes actions for create/approve/reject.
- **`AutomationsView`**: top-level React component rendering the task board.
- **`TaskCard`**: a card component for a single task, showing status, title, source badge, and action buttons.
- **`NewTaskDialog`**: a modal for creating a new local task (title, description, optional workflow template).
- **Status badge** and icon set for the `AutomationTaskStatus` enum values.
- Wire Protocol client extensions in `AppServerClient` for all `automation/*` methods.

## Scope

### In Scope

- `AutomationsStore` Zustand slice with full lifecycle.
- `AutomationsView` layout and `TaskCard` list component.
- `NewTaskDialog` with form validation.
- Status badge component and icon mapping.
- `AppServerClient` method extensions (`listTasks`, `readTask`, `createTask`, `approveTask`, `rejectTask`).
- Connecting `automation/task/updated` notification to real-time store updates.
- Empty state (no tasks yet) and loading state.
- Error toast on failed `create`, `approve`, or `reject` calls.

### Out of Scope

- Task detail / review panel with live thread streaming — M8.
- GitHub-specific UI affordances (GitHub tasks appear in the same board under a `github` source badge).
- Workflow template editor.

## Requirements

### R7.1 — AutomationsStore

Zustand store defined in `desktop/src/stores/automationsStore.ts`:

```typescript
interface AutomationTask {
  id: string;
  title: string;
  status: AutomationTaskStatus;
  sourceName: string;
  threadId: string | null;
  description?: string;
  agentSummary?: string | null;
  createdAt: string;
  updatedAt: string;
}

type AutomationTaskStatus =
  | 'pending'
  | 'dispatched'
  | 'agent_running'
  | 'agent_completed'
  | 'awaiting_review'
  | 'approved'
  | 'rejected'
  | 'failed';

interface AutomationsState {
  tasks: AutomationTask[];
  loading: boolean;
  error: string | null;
  selectedTaskId: string | null;

  // Actions
  fetchTasks: () => Promise<void>;
  createTask: (title: string, description: string, workflowTemplate?: string) => Promise<void>;
  approveTask: (taskId: string, sourceName: string) => Promise<void>;
  rejectTask: (taskId: string, sourceName: string, reason?: string) => Promise<void>;
  selectTask: (taskId: string | null) => void;

  // Called by notification handler
  upsertTask: (task: AutomationTask) => void;
}
```

On store initialisation (when the Desktop connects to AppServer):
1. Check that `"automations"` is in the server's capabilities list. If absent, set `error = "Automations not available"` and return.
2. Call `automation/task/list` and populate `tasks`.
3. Subscribe to `automation/task/updated` notifications; on each notification, call `upsertTask`.

### R7.2 — AppServerClient extensions

New methods on `AppServerClient` (`desktop/src/lib/AppServerClient.ts` or equivalent):

```typescript
listTasks(workspacePath: string, sourceName?: string): Promise<AutomationTask[]>
readTask(workspacePath: string, taskId: string, sourceName: string): Promise<AutomationTask>
createTask(workspacePath: string, title: string, description: string, workflowTemplate?: string): Promise<{ taskId: string; taskDirectory: string }>
approveTask(workspacePath: string, taskId: string, sourceName: string): Promise<void>
rejectTask(workspacePath: string, taskId: string, sourceName: string, reason?: string): Promise<void>
```

These map 1:1 to the Wire Protocol methods defined in M6.

### R7.3 — AutomationsView layout

`desktop/src/components/automations/AutomationsView.tsx`:

```
AutomationsView
├── Header bar
│   ├── Title: "Automations"
│   ├── Source filter tabs: All | Local | GitHub
│   └── "New Task" button (opens NewTaskDialog, visible only for local source)
├── Task list (scrollable)
│   └── TaskCard (× N, sorted by updatedAt desc)
└── Empty state (when no tasks match filter)
```

The view is the content area rendered when the user clicks the Automations nav item. It is rendered inside the existing Desktop layout shell.

### R7.4 — TaskCard component

`desktop/src/components/automations/TaskCard.tsx`:

```
TaskCard
├── Left: Status icon (animated spinner for agent_running, checkmark for approved, etc.)
├── Body:
│   ├── Title (bold)
│   ├── Source badge ("Local" or "GitHub" label with icon)
│   └── Updated-at timestamp (relative, e.g. "2 minutes ago")
└── Right: Action button
    ├── awaiting_review → "Review" button (primary)
    ├── agent_running   → "View" button (secondary, opens detail panel)
    ├── approved        → "Done" badge (no button)
    └── rejected        → "Rejected" badge (no button)
```

Clicking "Review" or "View" sets `selectedTaskId` in the store (opens M8 review panel).

Clicking anywhere on the card body (outside action buttons) also sets `selectedTaskId`.

### R7.5 — Status icon and badge mapping

| Status | Icon | Badge colour |
|--------|------|-------------|
| `pending` | Clock | Gray |
| `dispatched` | Arrow right | Blue |
| `agent_running` | Spinner (animated) | Blue |
| `agent_completed` | Check (outline) | Blue |
| `awaiting_review` | Eye | Amber |
| `approved` | Check (filled) | Green |
| `rejected` | X | Red |
| `failed` | Warning triangle | Red |

### R7.6 — NewTaskDialog

`desktop/src/components/automations/NewTaskDialog.tsx`:

```
NewTaskDialog (modal)
├── Title: "New Automation Task"
├── Form:
│   ├── Title field (required, max 120 chars)
│   ├── Description field (textarea, Markdown supported, required)
│   └── Workflow template selector (optional dropdown; lists templates from .craft/templates/)
├── Footer:
│   ├── "Cancel" button
│   └── "Create Task" button (primary, disabled while submitting)
└── Error message area (shown on API error)
```

On submit:
1. Call `AutomationsStore.createTask(title, description, workflowTemplate)`.
2. On success, close the dialog. The new task appears in the list via `upsertTask` triggered by the `automation/task/updated` notification.
3. On error, display the error message in the dialog (do not close).

### R7.7 — Source filter tabs

Three tabs: **All**, **Local**, **GitHub**. Selecting a tab sets a local `filterSource` state (not persisted) that filters the `tasks` array client-side. The "New Task" button is hidden on the "GitHub" tab.

### R7.8 — Real-time updates

When an `automation/task/updated` notification arrives from the Wire Protocol subscription, `AutomationsStore.upsertTask` is called. If the task already exists in the store, it is updated in place (preserving scroll position). If it is new, it is prepended to the list.

### R7.9 — Loading and error states

- While `fetchTasks` is in progress, the task list shows a skeleton loader (3 placeholder cards).
- If `fetchTasks` fails, show an inline error message with a "Retry" button.
- If `createTask`, `approveTask`, or `rejectTask` fails, show a toast notification with the error message.

## Acceptance Criteria

| # | Criterion |
|---|-----------|
| AC1 | The Automations view renders when the user clicks the Automations nav item. |
| AC2 | All tasks from `automation/task/list` appear as `TaskCard` components on initial load. |
| AC3 | A status change notification updates the corresponding card in real time without a page refresh. |
| AC4 | "New Task" button is visible only on the "All" and "Local" filter tabs, not on "GitHub". |
| AC5 | Submitting the `NewTaskDialog` calls `automation/task/create` and the new task appears in the list. |
| AC6 | A task in `awaiting_review` shows a "Review" action button. |
| AC7 | A task in `agent_running` shows an animated spinner icon and a "View" button. |
| AC8 | Clicking "Review" or "View" sets `selectedTaskId` and opens the detail panel (M8). |
| AC9 | Source filter "Local" hides GitHub tasks; "GitHub" hides local tasks; "All" shows both. |
| AC10 | When `"automations"` is absent from server capabilities, the Automations nav item is disabled and shows a tooltip. |

## Affected Files

| File | Change |
|------|--------|
| `desktop/src/stores/automationsStore.ts` | New: Zustand store |
| `desktop/src/components/automations/AutomationsView.tsx` | New: top-level view |
| `desktop/src/components/automations/TaskCard.tsx` | New: task card |
| `desktop/src/components/automations/NewTaskDialog.tsx` | New: create task dialog |
| `desktop/src/components/automations/StatusBadge.tsx` | New: status icon/badge |
| `desktop/src/lib/AppServerClient.ts` | Add `automation/*` method wrappers |
| `desktop/src/App.tsx` (or layout router) | Wire Automations nav item to `AutomationsView` |
| `desktop/src/main/index.ts` | Handle `automation/task/updated` notification, forward to renderer |
