# DotCraft Desktop Client Specification

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Draft |
| **Date** | 2026-03-21 |
| **Parent Spec** | [AppServer Protocol](appserver-protocol.md) |

Purpose: Define the product design, UI structure, interaction model, and behavioral contract for **DotCraft Desktop**, an Electron + React desktop application that provides a graphical interface for the DotCraft Agent Harness. The Desktop client connects to the DotCraft AppServer via the Wire Protocol, offering a three-panel workspace for agent conversation, multi-session management, file review, and plan tracking.

## Credits

The DotCraft Desktop interface design is inspired by [OpenAI Codex Desktop](https://openai.com/index/introducing-codex/), an excellent desktop AI agent by OpenAI.

We thank the Codex team for their pioneering work in desktop AI agent UX.

---

## Table of Contents

- [1. Scope](#1-scope)
- [2. Goals and Non-Goals](#2-goals-and-non-goals)
- [3. Technology Selection Rationale](#3-technology-selection-rationale)
- [4. Architecture](#4-architecture)
- [5. Connection Lifecycle](#5-connection-lifecycle)
- [6. Wire Protocol Event Mapping](#6-wire-protocol-event-mapping)
- [7. Information Architecture](#7-information-architecture)
- [8. Layout System](#8-layout-system)
- [9. Sidebar](#9-sidebar)
- [10. Conversation Panel](#10-conversation-panel)
- [11. Detail Panel](#11-detail-panel)
- [12. Input Composer](#12-input-composer)
- [13. Approval Flow](#13-approval-flow)
- [14. Workspace Management](#14-workspace-management)
- [15. Visual Design System](#15-visual-design-system)
- [16. Key User Flows](#16-key-user-flows)
- [17. Keyboard Shortcuts](#17-keyboard-shortcuts)
- [18. Error States and Edge Cases](#18-error-states-and-edge-cases)
- [19. Non-Functional Requirements](#19-non-functional-requirements)
- [20. Phase 2 Reserved Surface](#20-phase-2-reserved-surface)
  - [20.4 Skills Management UI](#204-skills-management-ui)
- [21. Automations View](#21-automations-view)
  - [21.1 Tab Bar](#211-tab-bar)
  - [21.2 Cron Tab — Job List](#212-cron-tab--job-list)
  - [21.3 Cron Review Panel](#213-cron-review-panel)
- [18.5 Windows Native Notifications](#185-windows-native-notifications) _(amendment to §18)_
- [18.6 Cron Job State Conflicts](#186-cron-job-state-conflicts) _(amendment to §18)_

---

## 1. Scope

### 1.1 What This Spec Defines

- The process architecture: how the Electron app launches, manages, and communicates with a DotCraft AppServer.
- The three-panel layout: sidebar, conversation panel, and contextual detail panel.
- The component specifications: what each UI component renders, its states, and its interactions.
- The event mapping: how Wire Protocol notifications are translated into React state updates.
- The interaction model: keyboard shortcuts, input handling, navigation, and modal flows.
- The approval flow: how the Desktop client presents approval prompts and sends decisions.
- The visual design system: color tokens, typography, spacing, theming.
- The workspace management model: how users open, switch, and manage workspaces.

### 1.2 What This Spec Does Not Define

- **Wire protocol semantics**: Thread, Turn, and Item lifecycle, message formats, and transport rules are defined in [appserver-protocol.md](appserver-protocol.md). This spec references them but does not redefine them.
- **Server-side behavior**: Agent execution, tool invocation, hook execution, and session management are server-internal concerns.
- **Implementation details**: React component hierarchy, state management library choice (Zustand, Redux, etc.), bundling configuration, and Electron IPC serialization format are implementation concerns.
- **Phase 2 features**: Task distribution, Kanban board, and GitHub Tracker integration require separate detailed design. This spec reserves navigation surface for Phase 2 but does not define its behavior (see [Section 20](#20-phase-2-reserved-surface)).

---

## 2. Goals and Non-Goals

### 2.1 Goals

1. **Full Wire Protocol surface**: Expose the complete AppServer Protocol through a graphical interface — thread lifecycle, turn execution, streaming events, approval flow, plan tracking, SubAgent progress, cron management.
2. **Multi-session productivity**: Allow users to manage multiple conversation threads simultaneously, switching between them instantly while background turns continue executing.
3. **Code review workflow**: Provide an integrated file diff viewer so users can review, accept, or reject agent-generated code changes without leaving the application.
4. **Low-latency streaming**: Render agent text deltas within one animation frame (16ms) of receipt, providing a responsive real-time conversation experience.
5. **Cross-platform**: Support Windows (primary), macOS, and Linux from a single codebase.
6. **Dark-first design**: Ship with a polished dark theme as the default, with light theme support.
7. **Workspace-centric**: Each window is bound to a single workspace (`.craft/` directory). Multiple windows can be open for different workspaces.

### 2.2 Non-Goals

- **In-process agent**: The Desktop client is a pure protocol client. It does not embed the DotCraft agent runtime or .NET.
- **Code editor**: The Desktop client is not an IDE. It does not provide a general-purpose code editor, file tree browser, or project-wide search. Users open their preferred editor alongside DotCraft Desktop.
- **Terminal emulator**: The Desktop client does not host a general-purpose terminal. Shell output is displayed as read-only formatted text from tool results.
- **Plugin system**: V1 does not support third-party UI plugins or extensions.
- **Mobile support**: The Desktop client targets desktop operating systems only.

---

## 3. Technology Selection Rationale

### 3.1 Framework: Electron + React

| Criterion | Electron + React | Tauri + React | Decision Factor |
|-----------|-----------------|---------------|-----------------|
| Ecosystem maturity | Proven at scale (VS Code, Cursor, Codex Desktop) | Growing but younger | Risk reduction for V1 |
| Native integration | Full Node.js API for subprocess management, filesystem access | Rust backend with WebView | Subprocess spawning of .NET AppServer is simpler in Node.js |
| UI library ecosystem | Rich React component libraries (Monaco, CodeMirror, diff viewers) | Same React libraries, but WebView2 compatibility varies | Diff viewer and Markdown rendering are table-stakes features |
| Bundle size | ~150MB (Chromium included) | ~10MB (uses system WebView) | Acceptable for a desktop application |
| Cross-platform fidelity | Consistent rendering across OS | WebView2 on Windows, WebKitGTK on Linux — rendering differences | Consistency is important for the three-panel layout |

### 3.2 Renderer: React

React provides the component model, one-way data flow, and ecosystem (Markdown rendering, syntax highlighting, diff components) needed for the conversation UI. The immediate-mode rendering of chat messages, streaming deltas, and tool call cards maps naturally to React's declarative update model.

### 3.3 Styling: CSS Modules or Tailwind CSS

Specific CSS methodology is an implementation decision. The spec defines design tokens (colors, spacing, typography) that must be honored regardless of styling approach.

### 3.4 IPC: Electron contextBridge

The Main Process handles Wire Protocol communication (spawning AppServer, WebSocket connection). The Renderer Process communicates with Main via Electron's `contextBridge` / `ipcRenderer` API. Wire Protocol events are forwarded from Main to Renderer as structured messages.

---

## 4. Architecture

### 4.1 Process Model

```
┌────────────────────────────────────────────────────────────────┐
│  Electron Application                                          │
│                                                                │
│  ┌──────────────────────┐    ┌──────────────────────────────┐  │
│  │  Main Process        │    │  Renderer Process            │  │
│  │  (Node.js)           │    │  (React SPA)                 │  │
│  │                      │    │                              │  │
│  │  ┌────────────────┐  │    │  ┌────────────┐             │  │
│  │  │ AppServer      │  │◄──►│  │ App Store  │             │  │
│  │  │ Manager        │  │IPC │  │ (state)    │             │  │
│  │  └───────┬────────┘  │    │  └─────┬──────┘             │  │
│  │          │           │    │        │                     │  │
│  │  ┌───────┴────────┐  │    │  ┌─────┴──────────────────┐ │  │
│  │  │ Wire Protocol  │  │    │  │ React Component Tree   │ │  │
│  │  │ Client         │  │    │  │ ┌─────────┐ ┌────────┐ │ │  │
│  │  │ (JSON-RPC)     │  │    │  │ │Sidebar  │ │Convo   │ │ │  │
│  │  └───────┬────────┘  │    │  │ │         │ │Panel   │ │ │  │
│  │          │           │    │  │ └─────────┘ └────────┘ │ │  │
│  │  ┌───────┴────────┐  │    │  │ ┌────────┐            │ │  │
│  │  │ Transport      │  │    │  │ │Detail  │            │ │  │
│  │  │ stdio / WS     │  │    │  │ │Panel   │            │ │  │
│  │  └───────┬────────┘  │    │  │ └────────┘            │ │  │
│  └──────────┼───────────┘    │  └────────────────────────┘ │  │
│             │                └──────────────────────────────┘  │
└─────────────┼──────────────────────────────────────────────────┘
              │
              │  JSON-RPC 2.0 (JSONL over stdio / WebSocket)
              │
┌─────────────┴──────────────────────────────────────────────────┐
│  DotCraft AppServer  (.NET process)                            │
│  Session Core → Agent → Tools → LLM                           │
└────────────────────────────────────────────────────────────────┘
```

### 4.2 Responsibility Split

| Layer | Responsibility |
|-------|---------------|
| **Main Process** | Spawn/manage AppServer child process (or connect via WebSocket). Maintain Wire Protocol client — send requests, receive responses and notifications. Forward protocol events to Renderer via IPC. Handle system-level concerns: window management, system tray, auto-update, native menus, file dialogs. |
| **Renderer Process** | Render the three-panel UI. Maintain application state (thread list, current thread, active turn, streaming buffers). Translate user actions (click, type, keyboard shortcut) into Wire Protocol requests sent to Main via IPC. |
| **IPC Contract** | Main exposes a typed API to Renderer via `contextBridge`. Renderer calls methods like `appServer.sendRequest(method, params)` and listens for events via `appServer.onNotification(callback)`. The IPC layer is a thin pass-through; it does not add business logic. |

### 4.3 State Architecture

Application state lives in the Renderer Process. It is the single source of truth for all UI rendering. The state is organized into these domains:

| Domain | Key State | Updated By |
|--------|-----------|------------|
| **Connection** | `status` (connecting / connected / disconnected / error), `serverInfo`, `capabilities` | Main Process IPC events |
| **Threads** | `threadList` (ThreadSummary[]), `activeThreadId`, `threadCache` (Map of threadId → full Thread with turns) | `thread/list` response, `thread/started`, `thread/statusChanged` notifications |
| **ActiveTurn** | `turnStatus` (idle / running / waitingApproval), `streamingMessage`, `streamingReasoning`, `activeToolCalls`, `turnStartedAt` | `turn/started`, `item/*` notifications, `turn/completed` / `turn/failed` / `turn/cancelled` |
| **SubAgents** | `entries` (SubAgentEntry[]) | `subagent/progress` notifications (full snapshot replacement) |
| **Plan** | `plan` ({ title, overview, todos }) or null | `plan/updated` notifications (full snapshot replacement) |
| **Tokens** | `inputTokens`, `outputTokens` (cumulative for current turn) | `item/usage/delta` (additive), reset on `turn/started` |
| **Approval** | `pendingApproval` (approval request params) or null | `item/approval/request` server request |
| **FileChanges** | `changedFiles` (Map of filePath → FileDiff), where each `FileDiff` contains: `filePath`, `turnId`, `additions`, `deletions`, `diffHunks`, `status` ('written' or 'reverted'), `isNewFile` | Extracted from completed `toolCall`/`toolResult` items where tool is `FileWrite` / `FileEdit`. Keyed by file path so that multiple edits to the same file across turns produce a single aggregated entry in the Detail Panel, while per-turn entries remain in the Turn Completion Summary. |
| **UI** | `sidebarCollapsed`, `detailPanelTab`, `detailPanelVisible`, `inputValue`, `agentMode`, `activeMainView` ('conversation' \| 'skills' \| 'automations'), `automationsTab` ('tasks' \| 'cron') | User interactions |
| **CronJobs** | `cronJobs` (CronJobInfo[]), `selectedCronJobId`, `cronLoading`, `cronError` | `cron/list` response, `cron/stateChanged` notifications |

---

## 5. Connection Lifecycle

### 5.1 Subprocess Mode (Default)

On app launch for a workspace:

1. Main Process locates the `dotcraft` binary (configurable in settings; defaults to `dotcraft` on PATH or bundled binary).
2. Spawns: `dotcraft app-server --workspace <path>` with stdin/stdout piped.
3. Reads stdout as JSONL. Writes requests to stdin as JSONL.
4. Sends `initialize` request with client info:

```json
{
  "clientInfo": {
    "name": "dotcraft-desktop",
    "title": "DotCraft Desktop",
    "version": "0.1.0"
  },
  "capabilities": {
    "approvalSupport": true,
    "streamingSupport": true,
    "optOutNotificationMethods": []
  }
}
```

5. Receives `initialize` response → sends `initialized` notification → connection ready.
6. Renderer is notified of connection status change; UI transitions from loading to ready.

On window close: stdin is closed → AppServer receives EOF and shuts down.

### 5.2 Remote Mode

The Desktop client can also connect to a running AppServer via WebSocket:

- Configured in workspace settings or via command-line flag: `dotcraft-desktop --remote ws://host:port/ws?token=xxx`
- Full `initialize` / `initialized` handshake over WebSocket.
- On disconnect: reconnection with exponential backoff (1s → 2s → 4s → ... → 30s cap), per [appserver-protocol.md §15.7](appserver-protocol.md#157-reconnection).

### 5.3 Connection Status Indicator

The connection state is shown in the sidebar footer:

| State | Indicator |
|-------|-----------|
| Connecting | Yellow dot + "Connecting..." |
| Connected | Green dot + "Connected" |
| Disconnected | Red dot + "Disconnected — Reconnecting..." |
| Error | Red dot + error message (e.g., "AppServer binary not found") |

---

## 6. Wire Protocol Event Mapping

This section defines how Wire Protocol notifications are mapped to Renderer state updates. All mappings reference the protocol defined in [appserver-protocol.md](appserver-protocol.md).

### 6.1 Thread Events

| Wire Method | State Mutation |
|-------------|---------------|
| `thread/started` | Prepend new thread to `threadList`. If this client initiated the creation, set `activeThreadId` to the new thread. |
| `thread/resumed` | Update thread status in `threadList`. Set `activeThreadId`. |
| `thread/statusChanged` | Update matching thread's status in `threadList`. If the active thread was archived/paused, clear `activeThreadId` or show notification. |

### 6.2 Turn Events

| Wire Method | State Mutation |
|-------------|---------------|
| `turn/started` | Set `turnStatus = running`. Clear streaming buffers (`streamingMessage`, `streamingReasoning`, `activeToolCalls`). Reset token counters. Record `turnStartedAt`. |
| `turn/completed` | Set `turnStatus = idle`. Finalize streaming content into the thread's turn history. Update `tokenUsage` from the turn's final token counts. Clear `activeToolCalls`. |
| `turn/failed` | Set `turnStatus = idle`. Finalize partial content. Append error message to turn history. Show error notification. |
| `turn/cancelled` | Set `turnStatus = idle`. Finalize partial content. Append cancellation notice to turn history. |

### 6.3 Item Events

| Wire Method | State Mutation |
|-------------|---------------|
| `item/started` (type: `toolCall`) | Add new entry to `activeToolCalls`: `{ callId, toolName, arguments, startedAt: Date.now(), status: 'running' }`. |
| `item/started` (type: `agentMessage`) | Initialize `streamingMessage = ""`. |
| `item/started` (type: `reasoningContent`) | Initialize `streamingReasoning = ""`. |
| `item/agentMessage/delta` | Append `delta` to `streamingMessage`. |
| `item/reasoning/delta` | Append `delta` to `streamingReasoning`. |
| `item/completed` (type: `agentMessage`) | Commit `streamingMessage` into the turn's item list as finalized agent message. Clear `streamingMessage`. |
| `item/completed` (type: `reasoningContent`) | Commit `streamingReasoning` into the turn's item list. Clear `streamingReasoning`. |
| `item/completed` (type: `toolResult`) | Mark matching `activeToolCalls` entry as completed. Record elapsed time: `duration = Date.now() - startedAt`. Store result summary. If tool is `FileWrite` / `FileEdit`, extract diff data and upsert into `changedFiles` (keyed by file path, status: `'written'`). Record the `turnId` association for per-turn summary rendering. |
| `item/completed` (type: `error`) | Append error item to turn history. |

### 6.4 Approval Events

| Wire Method | State Mutation |
|-------------|---------------|
| `item/approval/request` (server-initiated request) | Set `pendingApproval` to the request params. Show approval modal overlay. Set `turnStatus = waitingApproval`. |
| `item/approval/resolved` | Clear `pendingApproval`. Dismiss approval modal. Set `turnStatus = running`. |

### 6.5 SubAgent Progress

| Wire Method | State Mutation |
|-------------|---------------|
| `subagent/progress` | **Replace** `entries` with the complete snapshot from the notification. |

### 6.6 Plan Updates

| Wire Method | State Mutation |
|-------------|---------------|
| `plan/updated` | **Replace** `plan` with the complete snapshot. If the detail panel is not visible, auto-show it on the Plan tab. |

### 6.7 Token Usage

| Wire Method | State Mutation |
|-------------|---------------|
| `item/usage/delta` | `inputTokens += delta.inputTokens`, `outputTokens += delta.outputTokens`. |

### 6.8 System Events

| Wire Method (`system/event` kind) | State Mutation |
|------------------------------------|---------------|
| `compacting` | Show transient status message: "Compacting context..." |
| `compacted` | Clear status message. |
| `consolidating` | Show transient status message: "Consolidating memory..." |
| `consolidated` | Clear status message. |

### 6.9 Job Results

| Wire Method | State Mutation |
|-------------|---------------|
| `system/jobResult` | (1) Render the job name and result as a **Markdown toast** (auto-dismiss after 10 seconds). (2) On Windows, emit a **native OS notification** when the window is not focused (see Section 18.5). (3) Call `cron/list` (or apply `cron/stateChanged` data) to refresh the Cron tab job list so `lastResult` and `lastThreadId` are up to date. |

**Toast Markdown rendering**: The `result` field may contain Markdown (headings, lists, bold, code blocks). The toast renders it via `MarkdownRenderer` with a maximum height of 120px and internal overflow scroll. The toast width expands to a maximum of 480px when Markdown content is detected.

**Native notification body**: The `result` field is stripped of Markdown syntax (headings, emphasis markers, code fences, link syntax) before being passed to the OS notification API, producing readable plain text suitable for the notification center.

### 6.10 Cron Job State Events

| Wire Method | State Mutation |
|-------------|---------------|
| `cron/stateChanged` (removed: false) | Upsert the job into `cronJobs` by `job.id`. If the Cron tab is currently visible, the `CronJobCard` for this job re-renders with updated schedule, last run time, and result summary. |
| `cron/stateChanged` (removed: true) | Remove the job with matching `job.id` from `cronJobs`. |

---

## 7. Information Architecture

The application has one primary view: the **Workspace View**, which contains the three-panel layout. All navigation within the workspace is handled by the sidebar and the detail panel tabs.

```
┌──────────────────────────────────────────────────────────────────────┐
│  Window Title Bar: "DotCraft — {workspace_name}"                     │
├──────────┬───────────────────────────────────┬───────────────────────┤
│          │                                   │                       │
│ Sidebar  │  Conversation Panel               │  Detail Panel         │
│          │                                   │  (collapsible)        │
│          │                                   │                       │
├──────────┴───────────────────────────────────┴───────────────────────┤
│  Status Bar (optional, minimal)                                      │
└──────────────────────────────────────────────────────────────────────┘
```

### 7.1 Navigation Model

- **Sidebar** controls which thread is active. Clicking a thread loads it into the Conversation Panel.
- **Detail Panel** is contextual to the active turn. Its tabs show different aspects of the agent's work (file changes, plan, terminal output).
- There is no routing or page navigation. The workspace view is the only view. Modal overlays (settings, approval) appear on top.

### 7.2 Thread Grouping

Threads in the sidebar are grouped by temporal proximity, following the convention of modern chat applications:

| Group | Rule |
|-------|------|
| **Today** | `lastActiveAt` is today |
| **Yesterday** | `lastActiveAt` is yesterday |
| **Previous 7 Days** | `lastActiveAt` is within the last 7 days |
| **Previous 30 Days** | `lastActiveAt` is within the last 30 days |
| **Older** | Everything else |

Groups with no threads are not shown. Within each group, threads are sorted by `lastActiveAt` descending (most recent first).

---

## 8. Layout System

### 8.1 Three-Panel Layout

The workspace view uses a horizontal three-panel layout:

```
┌───────────┬──────────────────────────────┬──────────────────────┐
│           │                              │                      │
│  Sidebar  │    Conversation Panel        │   Detail Panel       │
│  (fixed   │    (flex: 1, fills           │   (fixed or          │
│  or       │    remaining space)          │   collapsible)       │
│  collaps- │                              │                      │
│  ible)    │                              │                      │
│           │                              │                      │
│           │                              │                      │
│           │                              │                      │
│  240px    │                              │   400px              │
│  default  │                              │   default            │
│           │                              │                      │
└───────────┴──────────────────────────────┴──────────────────────┘
```

| Panel | Default Width | Min Width | Behavior |
|-------|--------------|-----------|----------|
| Sidebar | 240px | 200px | Collapsible to icon-only mode (48px). Fixed width (not user-resizable). |
| Conversation | flex: 1 | 400px | Fills remaining horizontal space. Always visible. |
| Detail | 400px | 300px | Collapsible (hidden). Fixed width (not user-resizable). Auto-shows when file changes or plan updates arrive. |

### 8.2 Responsive Behavior

| Window Width | Layout Adaptation |
|-------------|-------------------|
| >= 1200px | All three panels visible at default widths. |
| 900px - 1199px | Detail panel auto-collapses. Can be toggled by user. |
| < 900px | Sidebar collapses to icon-only mode. Detail panel hidden. |

---

## 9. Sidebar

The sidebar is the primary navigation surface. It is organized into a vertical stack of sections.

### 9.1 Structure

```
┌─────────────────────────┐
│  ┌───────────────────┐  │
│  │ Workspace Header  │  │  Section 1: Workspace identity
│  └───────────────────┘  │
│  ┌───────────────────┐  │
│  │ + New Thread      │  │  Section 2: Primary action
│  └───────────────────┘  │
│  ┌───────────────────┐  │
│  │ 🔍 Search         │  │  Section 3: Thread search
│  └───────────────────┘  │
│                         │
│  Threads                │  Section 4: Thread list
│  ─────────────────────  │
│  Today                  │
│    ▸ Voice shortcuts  2h│
│    ▸ Fix login bug    4h│
│  Yesterday              │
│    ▸ Dark mode impl   1d│
│  Previous 7 Days        │
│    ▸ Setup CI/CD      3d│
│                         │
│  ─────────────────────  │
│  Automations            │  Section 5: Reserved (Phase 2 nav)
│  Skills                 │
│                         │
│  ─────────────────────  │
│  ● Connected            │  Section 6: Connection status
│  v0.1.0                 │
└─────────────────────────┘
```

### 9.2 Workspace Header

Displays the workspace identity:

- **Workspace name**: Derived from the directory name (e.g., `dotcraft` from `/home/user/dotcraft`).
- **Workspace path**: Shown as a subtle secondary line, truncated with ellipsis if too long.
- **Click action**: Opens a dropdown with "Open in Explorer", "Switch Workspace", and "Recent Workspaces" options.

### 9.3 New Thread Button

A prominent button at the top of the sidebar.

- **Click**: Creates a new thread by calling `thread/start` with the current workspace identity. The new thread becomes active immediately. The conversation panel shows an empty state with the input composer focused.
- **Keyboard shortcut**: `Ctrl+N` (global).

### 9.4 Thread Search

A search input that filters the thread list by display name. The filter is applied locally (client-side) against the cached thread list. Debounced at 150ms.

### 9.5 Thread List

Each thread entry is a clickable row showing:

- **Display name**: The thread's `displayName` (auto-generated from first user message, or explicitly set). Truncated with ellipsis.
- **Time indicator**: Relative time since `lastActiveAt` (e.g., "2h", "1d", "3d").
- **Active indicator**: The currently selected thread has a highlighted background and a left accent border.
- **Status icon** (subtle, only for non-active states):
  - Active: no icon (default state)
  - Paused: pause icon (dimmed)
  - Archived: archive icon (dimmed)

**Interactions:**

- **Click**: Selects the thread. Loads its data via `thread/read` (with `includeTurns: true`) and renders in the conversation panel. Calls `thread/subscribe` for real-time updates.
- **Right-click context menu**:
  - "Rename" → inline edit of display name
  - "Archive" → calls `thread/archive`
  - "Delete" → confirmation dialog, then calls `thread/delete`
- **Empty state**: When no threads exist, show a centered message: "No conversations yet. Click '+ New Thread' to start."

### 9.6 Bottom Sections

**Automations** and **Skills** are navigation entries below the thread list.

**Automations**:

- Clicking opens the **Automations view** (Section 21), which contains two tabs: **Tasks** and **Cron**.
- Visible when `capabilities.automations` **or** `capabilities.cronManagement` is `true`.
- When only one capability is present, the corresponding tab is shown without a tab bar (single-tab mode — no tab header rendered).
- When neither capability is present, the entry is disabled with tooltip: "Automations module not enabled on the server."

**Skills**:

- Clicking opens the **Skills Management UI** (Section 20.4).
- Visibility and behavior unchanged from the existing implementation.

### 9.7 Connection Status Footer

Fixed to the bottom of the sidebar. Shows:

- Connection status dot and label (see [Section 5.3](#53-connection-status-indicator)).
- Application version number.

### 9.8 Collapsed Mode

When the sidebar is collapsed (icon-only mode, 48px width):

- Workspace header becomes a single icon.
- "+ New Thread" becomes a "+" icon button.
- Thread list shows only status dots and first letter of display name.
- Search is hidden.
- Bottom sections become icon-only.
- Hovering over collapsed entries shows a tooltip with the full text.

---

## 10. Conversation Panel

The conversation panel is the primary interaction surface. It occupies the center of the layout and is always visible.

### 10.1 Structure

```
┌────────────────────────────────────────────────────────┐
│  Thread Header                                          │
│  ─────────────────────────────────────────────────────  │
│                                                         │
│  Message Stream (scrollable)                            │
│                                                         │
│  ┌─────────────────────────────────────────────────┐   │
│  │ User message                                     │   │
│  └─────────────────────────────────────────────────┘   │
│                                                         │
│  ┌─────────────────────────────────────────────────┐   │
│  │ Agent response block                             │   │
│  │  - Thinking indicator                            │   │
│  │  - Tool call cards                               │   │
│  │  - Agent message (markdown)                      │   │
│  └─────────────────────────────────────────────────┘   │
│                                                         │
│  ... more turns ...                                     │
│                                                         │
│  ─────────────────────────────────────────────────────  │
│  Input Composer (see Section 12)                        │
└────────────────────────────────────────────────────────┘
```

### 10.2 Thread Header

A fixed bar at the top of the conversation panel:

- **Thread display name** (left): Editable on double-click. Shows the thread's `displayName`.
- **Repository/project badge** (center-left, optional): If thread metadata contains repository information, show as a subtle badge (e.g., `openai/chatgpt` with a repository icon).
- **Action buttons** (right):
  - **"Open"**: Opens the workspace directory in the system file explorer.
  - **"Commit"**: Opens a commit dialog for staging and committing file changes made during the conversation. Disabled when no file changes exist.

### 10.3 Message Stream

The message stream renders all turns for the active thread in chronological order. Each turn is a visual group containing the user's input and the agent's response.

#### 10.3.1 Empty State

When a thread has no turns:

```
┌─────────────────────────────────────────────┐
│                                             │
│          What can I help you with?          │
│                                             │
│  Type a message below to start a            │
│  conversation with the agent.               │
│                                             │
└─────────────────────────────────────────────┘
```

#### 10.3.2 User Message

Each user message is rendered as a distinct block:

```
┌─────────────────────────────────────────────┐
│  Add an Esc shortcut to exit voice mode.    │
└─────────────────────────────────────────────┘
```

- Right-aligned or full-width with a subtle background tint to distinguish from agent messages.
- Plain text rendering (no markdown). Images (if attached) shown inline.

#### 10.3.3 Agent Response Block

Each agent response is a vertical stack of components, rendered in the order they appear:

**Thinking Indicator:**

```
  Thought 4s ▾
```

- Displayed when `reasoningContent` items exist for this turn.
- Shows elapsed thinking time.
- Collapsible: click the chevron to expand and show the full reasoning text (dimmed, italic).
- Default state: collapsed (showing only the summary line).

**Tool Call Cards:**

Tool calls are the primary way the agent interacts with the workspace. Each tool call is rendered as a **collapsed single-line card by default**, keeping the conversation stream compact. Users click to expand and inspect details.

**Collapsed state (default):**

```
  ┌─ Explored 1 file ──────────────────────────── ✓ ─┐
  └───────────────────────────────────────────────────┘
  ┌─ Edited shortcuts.ts ──────────────────────── ✓ ─┐
  └───────────────────────────────────────────────────┘
```

**Running state (animated, not collapsible):**

```
  ┌─ ⠋ Calling ReadFile... ──────────────── 1.2s ─┐
  └───────────────────────────────────────────────────┘
```

Tool call status rendering:

| Tool Call State | Visual |
|----------------|--------|
| Running | Spinner icon + "Calling {toolName}..." + elapsed time counter. Not collapsible — always shows the running state. |
| Completed (success) | Checkmark icon (✓) + summary text. **Collapsed by default.** |
| Completed (error) | Error icon (✗) + red error summary. **Collapsed by default**, but error text is visible in the collapsed line. |

**Tool call aggregation**: Consecutive tool calls of the same type are aggregated into a single card where appropriate:
- Multiple `ReadFile` / `Search` calls → "Explored N files"
- `FileWrite` / `FileEdit` → "Edited {filename}" (one card per file)
- `Exec` → "Ran `{command}`"
- Other tools → "Called {toolName}" with arguments summary

**Click to expand**: Clicking anywhere on a collapsed tool call card toggles it to its expanded state. The expanded content depends on the tool type:

*General tools (ReadFile, Search, Exec, etc.):*

```
  ┌─ Explored 1 file ──────────────────────────── ✓ ─┐
  │                                                    │
  │  ReadFile("src/voice/shortcuts.ts")                │
  │                                                    │
  │  export const shortcuts = [                        │
  │    { key: "Space", action: "toggle" },             │
  │  ];                                                │
  │  ...                                               │
  └────────────────────────────────────────────────────┘
```

Shows the tool invocation signature and a preview of the result text (truncated to 10 lines with "..." overflow). Full result is scrollable within the card.

*File-editing tools (FileWrite, FileEdit):*

```
  ┌─ Edited shortcuts.ts ──────────────────────── ✓ ─┐
  │                                                    │
  │  voice/shortcuts.ts  +1 -0                         │
  │  ─────────────────────────────────────────────     │
  │    export const shortcuts = [                      │
  │      { key: "Space", action: "toggle" },           │
  │  +   { key: "Esc", action: "exit" },               │
  │    ];                                              │
  │                                                    │
  └────────────────────────────────────────────────────┘
```

Shows an **inline diff preview** with the filename, line change counts, and unified diff view with green additions / red deletions. This gives the user immediate visibility into what the agent changed without needing to open the Detail Panel. The diff content is syntax-highlighted.

*Shell commands (Exec):*

```
  ┌─ Ran `npm test` ───────────────────── 2.1s  ✓ ─┐
  │                                                    │
  │  $ npm test                                        │
  │  PASS  src/utils.test.ts                           │
  │  PASS  src/api.test.ts                             │
  │                                                    │
  │  Test Suites: 2 passed, 2 total                    │
  │  Tests:       8 passed, 8 total                    │
  └────────────────────────────────────────────────────┘
```

Shows the command and its output in monospace font.

**Agent Message:**

```
  I will add `Esc` as an exit shortcut and ensure it doesn't
  conflict with existing bindings. Then I'll update the shortcuts list.
```

- Rendered as Markdown with full formatting support: headings, lists, code blocks (syntax highlighted), links, bold/italic, tables.
- During streaming: text appears progressively as `item/agentMessage/delta` events arrive.
- After completion: full message is rendered statically.

**SubAgent Progress Block:**

When `subagent/progress` events are received during a turn, a live-updating progress block appears:

```
  ──── SubAgents ───────────────────────────────
  ⠋ code-explorer    ReadFile       ↑4.5k ↓1.2k
  ⠋ test-runner      RunTests       ↑2.0k ↓0.6k
  ● reviewer         Done           ↑3.2k ↓0.9k
  ─────────────────────────────────────────────
```

- Active SubAgents show a spinner and their current tool.
- Completed SubAgents show a green dot and "Done".
- After all SubAgents complete, the block collapses to a summary: "✓ 3 SubAgents completed (↑10.5k ↓2.7k tokens)".

**Error Block:**

```
  ┌─ Error ──────────────────────────────────────┐
  │  Model returned an error: context window      │
  │  exceeded                                     │
  └───────────────────────────────────────────────┘
```

Red-tinted background. Shown for `Error` items or `turn/failed` events.

#### 10.3.4 Turn Completion Summary

When a turn completes and the agent has made file changes during that turn, a **file change summary block** is rendered at the bottom of the turn, after the agent's final message. This gives the user an immediate at-a-glance view of everything that was modified.

```
  ┌──────────────────────────────────────────────────┐
  │  7 files changed  +743  -0              Revert ↺ │
  │                                                   │
  │  index.html              +56  -0        ●         │
  │  package.json             +5  -0        ●         │
  │  server.js               +32  -0        ●         │
  │  src/app.js             +181  -0        ●         │
  │  src/game.js            +190  -0        ●         │
  │  styles.css             +167  -0        ●         │
  │  test/game.test.js      +112  -0        ●         │
  └──────────────────────────────────────────────────┘
```

Rendering rules:

- **Only shown when the turn produced file changes.** If the turn had no `FileWrite` / `FileEdit` tool calls, the summary is not rendered.
- **Header line**: Total file count and aggregate line additions/deletions. A "Revert" button (↺) reverts all file changes from this turn.
- **File list**: Each file shows its relative path, `+N` / `-N` line counts, and the same blue status dot (●) as the Detail Panel's Changes tab. The status is synchronized — reverting a file in either location updates both views.
- **Clickable filenames**: Clicking a filename in the summary does two things:
  1. Opens the Detail Panel (if hidden) to the Changes tab.
  2. Selects and scrolls to the clicked file's diff in the diff viewer.
- **Compact mode**: When a turn has only 1-2 file changes, the summary is rendered as a single line: `1 file changed +56 -0 — index.html ●`. File changes with more than 2 files always use the expanded list format.
- **Relationship to Detail Panel**: The Turn Completion Summary and the Detail Panel's Changes tab show the same data. The summary is scoped to a single turn; the Changes tab aggregates across all turns in the thread. Changes reverted in either view are reflected in both.

### 10.4 Inline Approval Card

When an approval request arrives during streaming, an inline card appears in the message stream at the point where the approval was requested:

```
  ┌─ Approval Required ──────────────────────────┐
  │                                               │
  │  🔧 Shell Command                            │
  │  Command: npm test                            │
  │  Directory: /home/dev/myproject               │
  │                                               │
  │  ┌─────────┐ ┌───────────────┐ ┌──────────┐  │
  │  │ Accept  │ │Accept Session │ │ Decline  │  │
  │  └─────────┘ └───────────────┘ └──────────┘  │
  │                                               │
  │  Accept Always          Cancel Turn           │
  └───────────────────────────────────────────────┘
```

See [Section 13](#13-approval-flow) for full approval flow specification.

### 10.5 Scrolling Behavior

- **Auto-scroll**: When the user is at the bottom of the message stream, new content auto-scrolls into view. When the user has scrolled up (manual scroll), auto-scroll is paused. A "Scroll to bottom" floating button appears when not at bottom.
- **Scroll restoration**: When switching threads, the scroll position is restored to where the user left off.
- **Smooth scrolling**: New content appears with a subtle slide-in animation.

### 10.6 Turn Status Indicator

When a turn is running, a status indicator appears above the input composer:

```
  ⠋ Working...  12s elapsed                    esc to cancel
```

- Animated spinner on the left.
- Elapsed time counter.
- "esc to cancel" hint on the right (clicking sends `turn/interrupt`).
- When system events are active (compacting, consolidating), the label changes to reflect the system operation.

### 10.7 Token Usage Display

A subtle token counter appears in the turn status area during active turns:

```
  ↑1.2k ↓350 tokens
```

Updated in real-time from `item/usage/delta` notifications. Reset on each new turn. Hidden when idle.

---

## 11. Detail Panel

The detail panel provides contextual information about the active conversation. It occupies the right side of the layout and is collapsible.

### 11.1 Structure

```
┌──────────────────────────────────┐
│  Tab Bar                         │
│  [Changes] [Plan] [Terminal]     │
│  ──────────────────────────────  │
│                                  │
│  Tab Content (scrollable)        │
│                                  │
│                                  │
│                                  │
│                                  │
│                                  │
└──────────────────────────────────┘
```

### 11.2 Tab Bar

Three tabs, selectable by click:

| Tab | Content | Badge |
|-----|---------|-------|
| **Changes** | File diffs from agent edits | File count badge (e.g., "3") when changes exist |
| **Plan** | Plan/todo progress from `plan/updated` | None |
| **Terminal** | Shell output from `Exec` tool calls | None |

The active tab is highlighted. Tabs without relevant content show an empty state message.

### 11.3 Changes Tab

Displays file changes produced by the agent during the active thread. This is the primary surface for reviewing and managing file writes — the user decides which changes to keep and which to revert.

#### 11.3.1 Summary Header

```
  N files changed  +X  -Y                 Revert All ↺
```

- Total file count, lines added (green `+X`), lines removed (red `-Y`).
- **"Revert All"** button (right-aligned): Reverts all pending file changes. Requires confirmation dialog: "Revert all N file changes? This cannot be undone."

#### 11.3.2 File List

Each changed file is listed as a row with status and actions:

```
  ┌──────────────────────────────────────────────────┐
  │  index.html             +56  -0        ●         │
  │  package.json            +5  -0        ●         │
  │  server.js              +32  -0        ●         │
  │  src/app.js            +181  -0        ●         │
  │  src/game.js           +190  -0        ●         │
  │  styles.css            +167  -0        ●         │
  │  test/game.test.js     +112  -0        ●         │
  └──────────────────────────────────────────────────┘
```

Each file row shows:

| Element | Description |
|---------|-------------|
| **Filename** | Relative path from workspace root. Clickable — clicking the filename selects this file and shows its diff in the diff viewer below. |
| **Line counts** | `+N` (green) additions, `-N` (red) deletions. |
| **Status dot** | Indicates the file's write status (see below). |

**File write status:**

| Status | Dot Color | Meaning |
|--------|-----------|---------|
| Written (pending review) | Blue ● | File has been written to disk by the agent. The change is live but not yet committed. This is the default state after the agent writes a file. |
| Reverted | Dim ○ | File change has been reverted by the user. The original content is restored. |

Each file row has a context menu (right-click) or hover action button:
- **"Revert"** (when status is Written): Reverts the file to its pre-edit state. The file row stays in the list but shows as reverted.
- **"Re-apply"** (when status is Reverted): Re-applies the agent's change.

#### 11.3.3 Diff Viewer

When a file is selected from the file list, its diff is shown below the list in a dedicated diff viewer area:

```
  ┌─ voice/shortcuts.ts ─────────────── +1 -0  ↺ ─┐
  │                                                  │
  │   1   export const shortcuts = [                 │
  │   2     { key: "Space", action: "toggle" },      │
  │ + 3     { key: "Esc", action: "exit" },          │
  │   4   ];                                         │
  │                                                  │
  └──────────────────────────────────────────────────┘
```

Diff viewer features:

- **Unified diff format**: Context lines in default color, additions highlighted with green background and `+` gutter marker, deletions highlighted with red background and `-` gutter marker.
- **Line numbers**: Shown in a left gutter.
- **Syntax highlighting**: Diff content is syntax-highlighted based on file extension.
- **Per-file revert button** (↺): In the diff viewer header, allowing the user to revert this specific file.
- **Scroll**: For large diffs, the viewer is independently scrollable.
- **New file indicator**: When a file is entirely new (all additions, no deletions), the header shows "New file" badge instead of line counts.

#### 11.3.4 File Navigation

- Clicking a file in the list selects it and shows its diff.
- `↑` / `↓` arrow keys navigate between files when the file list is focused.
- The first file is auto-selected when the Changes tab is opened.

#### 11.3.5 Write Model

The agent writes files to disk immediately upon tool execution (this is the standard DotCraft behavior — the AppServer runs tools directly on the workspace). The Changes tab provides a **post-hoc review and revert** workflow:

1. Agent executes `FileWrite` / `FileEdit` → file is written to disk → appears in Changes tab as "Written" (blue dot).
2. User reviews the diff in the Changes tab.
3. User can **revert** individual files (restores the original content from the diff's before-state).
4. User can **commit** accepted changes via the "Commit" button in the thread header.

This model matches Codex Desktop: the agent's writes take effect immediately, and the user has the power to revert what they don't want. There is no "staging" step — the workspace is always in the agent's latest state until the user explicitly reverts.

#### 11.3.6 Empty State

```
  No file changes yet.
  The agent's edits will appear here.
```

### 11.4 Plan Tab

Renders the plan data from `plan/updated` notifications.

**Layout:**

```
  Plan: Implement user authentication
  ──────────────────────────────────────

  Add JWT-based auth with login and
  registration endpoints.

  ☐  Create User model and migration
  ◉  Implement login and register API
  ○  Add JWT validation middleware
  ○  Write integration tests
```

- **Title**: Plan title in bold.
- **Overview**: Plan description in normal text.
- **Todo items**: Each item shows status icon and content text.

Status icons:

| Status | Icon | Style |
|--------|------|-------|
| `pending` | ○ | Default text |
| `in_progress` | ◉ | Highlighted/accent color |
| `completed` | ✓ | Green, ~~strikethrough~~ optional |
| `cancelled` | ✗ | Dimmed, strikethrough |

**Empty State:**

```
  No plan yet.
  The agent's plan will appear here
  when it creates one.
```

### 11.5 Terminal Tab

Displays output from shell command executions (`Exec` tool calls).

**Layout:**

```
  $ npm test                                  (2.1s)
  ──────────────────────────────────────────────────
  PASS  src/utils.test.ts
  PASS  src/api.test.ts

  Test Suites: 2 passed, 2 total
  Tests:       8 passed, 8 total
```

- Each command shown with its command text, elapsed time, and output.
- Output rendered in monospace font with ANSI color support where possible.
- Multiple commands stacked chronologically.
- Read-only (no interactive input).

**Empty State:**

```
  No terminal output yet.
  Shell commands run by the agent
  will appear here.
```

### 11.6 Auto-Show Behavior

The detail panel auto-shows (if currently hidden) when:

- A file change is detected (auto-switches to Changes tab).
- A plan update is received (auto-switches to Plan tab).

The user can manually hide the panel again. Once manually hidden, it stays hidden until the user re-opens it or a new trigger occurs in a new turn.

---

## 12. Input Composer

The input composer is fixed at the bottom of the conversation panel.

### 12.1 Structure

```
┌──────────────────────────────────────────────────────┐
│  ┌──────────────────────────────────────────────┐    │
│  │                                              │    │
│  │  Ask DotCraft anything                       │    │  Text area
│  │                                              │    │
│  └──────────────────────────────────────────────┘    │
│  ┌──────────┐                        ┌────────────┐  │
│  │ Model ▾  │                        │   Send ▶   │  │  Bottom bar
│  └──────────┘                        └────────────┘  │
└──────────────────────────────────────────────────────┘
```

### 12.2 Text Input Area

- **Multi-line**: Grows vertically as the user types (1 line minimum, 8 lines maximum before scrolling internally).
- **Placeholder**: "Ask DotCraft anything" in dimmed text when empty.
- **Submit**: `Enter` sends the message (calls `turn/start`). `Shift+Enter` inserts a newline.
- **Disabled state**: When a turn is running (`turnStatus = running`), the input area shows a subtle disabled overlay. The user can still type; pressing `Enter` queues the message as a pending follow-up (sent automatically when the current turn completes).
- **Image paste**: Pasting an image from clipboard attaches it as a `localImage` input part. A thumbnail preview appears above the text input.

### 12.3 Model Selector

A dropdown button in the bottom-left of the composer:

- Shows the currently selected model name (e.g., "GPT-5.3-Codex" or the configured model from workspace settings).
- In V1, this is read-only/informational — it reflects the model configured in `.craft/config.json`. Future versions may allow per-thread model selection.

### 12.4 Send Button

- **Active state**: Accent-colored arrow icon. Clickable when input is non-empty and turn is idle.
- **Disabled state**: Dimmed when input is empty.
- **Loading state**: Shows a spinner when a turn is being submitted.
- **Cancel state**: When a turn is running, the send button transforms into a "Stop" button (square icon) that sends `turn/interrupt`.

### 12.5 Mode Indicator

A subtle mode badge appears inside the input area or adjacent to it:

- **Agent mode**: Green dot + "Agent"
- **Plan mode**: Blue dot + "Plan"

Clicking the badge toggles between modes (calls `thread/mode/set`). Keyboard shortcut: `Ctrl+Shift+M`.

---

## 13. Approval Flow

### 13.1 Approval Card

When the server sends an `item/approval/request`, an inline approval card appears in the conversation stream at the current streaming position (see [Section 10.4](#104-inline-approval-card)).

### 13.2 Decision Buttons

| Button | Wire Decision | Keyboard Shortcut |
|--------|--------------|-------------------|
| **Accept** | `"accept"` | `Enter` or `A` |
| **Accept for Session** | `"acceptForSession"` | `S` |
| **Accept Always** | `"acceptAlways"` | `Shift+A` |
| **Decline** | `"decline"` | `D` |
| **Cancel Turn** | `"cancel"` | `Esc` |

- "Accept" and "Decline" are primary buttons (larger, prominent).
- "Accept for Session" is a secondary button.
- "Accept Always" and "Cancel Turn" are text links below the buttons.

### 13.3 Approval Type Display

| `approvalType` | Icon | Header Text |
|----------------|------|-------------|
| `"shell"` | Terminal icon | "Shell Command" |
| `"file"` | File icon | "File Operation" |

The `operation` field is shown as the primary detail. The `target` field is shown as a secondary detail. The `reason` field is shown as explanatory text.

### 13.4 Focus Behavior

When an approval card appears:
1. The approval card auto-scrolls into view.
2. Keyboard focus moves to the approval card.
3. The "Accept" button is focused by default.
4. Normal input is blocked until the user makes a decision.
5. After decision, focus returns to the input composer.

---

## 14. Workspace Management

### 14.1 One Window Per Workspace

Each DotCraft Desktop window is bound to a single workspace. The workspace is determined by:

1. Command-line argument: `dotcraft-desktop --workspace /path/to/project`
2. Last-used workspace (persisted in application settings)
3. Folder picker dialog on first launch

### 14.2 Workspace Switching

Users can switch workspaces via:
- Sidebar workspace header dropdown → "Switch Workspace" → folder picker
- Sidebar workspace header dropdown → "Recent Workspaces" → list of recently opened workspaces
- `Ctrl+Shift+O` keyboard shortcut

Switching workspaces:
1. Closes the current AppServer connection (subprocess is terminated).
2. Clears all application state.
3. Spawns a new AppServer for the target workspace.
4. Performs the `initialize` handshake.
5. Loads the thread list for the new workspace.

### 14.3 Multiple Windows

Users can open multiple DotCraft Desktop windows, each bound to a different workspace. Each window manages its own AppServer subprocess. `Ctrl+Shift+N` opens a new window.

### 14.4 Recent Workspaces

The application persists a list of recently opened workspaces (up to 20) in the user's application data directory. Each entry stores:
- Workspace path
- Workspace name (directory name)
- Last opened timestamp

---

## 15. Visual Design System

### 15.1 Color Tokens

The design system defines semantic color tokens that adapt to the active theme (dark or light).

**Dark Theme (Default):**

| Token | Value | Usage |
|-------|-------|-------|
| `--bg-primary` | `#1a1a1a` | Window background |
| `--bg-secondary` | `#242424` | Sidebar background, card backgrounds |
| `--bg-tertiary` | `#2e2e2e` | Hover states, input background |
| `--bg-active` | `#3a3a3a` | Active/selected items |
| `--text-primary` | `#e5e5e5` | Primary text |
| `--text-secondary` | `#a0a0a0` | Secondary text, timestamps |
| `--text-dimmed` | `#666666` | Placeholders, disabled text |
| `--border-default` | `#333333` | Panel borders, separators |
| `--border-active` | `#555555` | Focused input borders |
| `--accent` | `#7C3AED` | Brand color, primary buttons |
| `--accent-hover` | `#9333EA` | Accent hover state |
| `--success` | `#22C55E` | Success states, checkmarks, additions |
| `--warning` | `#EAB308` | Warning states, pending approvals |
| `--error` | `#EF4444` | Error states, deletions |
| `--info` | `#3B82F6` | Informational, plan mode |
| `--user-message-bg` | `#2a2a3a` | User message bubble background |
| `--diff-add-bg` | `rgba(34, 197, 94, 0.15)` | Diff addition line background |
| `--diff-remove-bg` | `rgba(239, 68, 68, 0.15)` | Diff deletion line background |

### 15.2 Typography

| Element | Font | Size | Weight |
|---------|------|------|--------|
| Thread title | System sans-serif | 14px | 600 (semibold) |
| Body text | System sans-serif | 14px | 400 (regular) |
| Secondary text | System sans-serif | 12px | 400 (regular) |
| Code inline | Monospace stack | 13px | 400 (regular) |
| Code block | Monospace stack | 13px | 400 (regular) |
| Input composer | System sans-serif | 14px | 400 (regular) |
| Sidebar thread name | System sans-serif | 13px | 400 (normal), 500 (active) |
| Sidebar group heading | System sans-serif | 11px | 600 (semibold), uppercase |

Monospace stack: `"Cascadia Code", "Fira Code", "JetBrains Mono", "Consolas", "Courier New", monospace`

System sans-serif: `-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif`

### 15.3 Spacing Scale

Based on a 4px grid:

| Token | Value |
|-------|-------|
| `--space-xs` | 4px |
| `--space-sm` | 8px |
| `--space-md` | 12px |
| `--space-lg` | 16px |
| `--space-xl` | 24px |
| `--space-2xl` | 32px |

### 15.4 Elevation and Shadows

| Level | Usage | Shadow |
|-------|-------|--------|
| Level 0 | Panels, background | No shadow |
| Level 1 | Cards, tool call blocks | `0 1px 3px rgba(0,0,0,0.3)` |
| Level 2 | Dropdowns, tooltips | `0 4px 12px rgba(0,0,0,0.4)` |
| Level 3 | Modal overlays | `0 8px 24px rgba(0,0,0,0.5)` |

### 15.5 Animations

| Animation | Duration | Easing | Usage |
|-----------|----------|--------|-------|
| Panel collapse/expand | 200ms | ease-out | Sidebar and detail panel toggle |
| Message appear | 150ms | ease-out | New message slide-in |
| Fade in | 100ms | linear | Status indicators, badges |
| Spinner | 1000ms | linear | Loading indicators (continuous rotation) |
| Streaming cursor blink | 800ms | step-end | Blinking cursor during agent text streaming |

---

## 16. Key User Flows

### 16.1 First Launch

1. Application opens. No workspace is configured.
2. A welcome screen appears: "Welcome to DotCraft Desktop. Select a workspace folder to get started."
3. User clicks "Open Workspace" → system folder picker.
4. Selected folder is validated (must contain `.craft/` or a valid project directory).
5. AppServer subprocess is spawned for the workspace.
6. Connection handshake completes.
7. Sidebar shows thread list (empty on first use).
8. Conversation panel shows empty state.

### 16.2 Start a New Conversation

1. User clicks "+ New Thread" or presses `Ctrl+N`.
2. `thread/start` is called with current workspace identity.
3. New thread appears at the top of the sidebar (selected).
4. Conversation panel shows empty state with input composer focused.
5. User types a prompt and presses `Enter`.
6. `turn/start` is called with the user's input.
7. Turn status indicator appears: "Working..."
8. Agent response streams in: thinking indicator → tool call cards → agent message.
9. Detail panel auto-shows if file changes or plan updates occur.
10. Turn completes. Status indicator disappears. Input composer is re-enabled.

### 16.3 Resume an Existing Thread

1. User clicks a thread in the sidebar.
2. `thread/read` is called with `includeTurns: true`.
3. Conversation panel renders full turn history.
4. `thread/subscribe` is called for real-time updates.
5. Scroll position is restored (if previously viewed) or set to bottom.
6. User can continue the conversation by typing a new message.

### 16.4 Handle an Approval Request

1. During agent execution, server sends `item/approval/request`.
2. Inline approval card appears in the conversation stream.
3. Card auto-scrolls into view. Keyboard focus moves to the card.
4. User reads the operation details and clicks a decision button.
5. Decision is sent as a JSON-RPC response to the server.
6. Approval card updates to show the decision result (e.g., "Accepted" with green checkmark).
7. Agent execution resumes. Focus returns to input composer.

### 16.5 Review and Commit File Changes

1. Agent edits files during a turn (via `FileWrite` / `FileEdit` tools). Files are written to disk immediately by the AppServer.
2. As tool calls complete, collapsed tool call cards appear in the conversation stream (e.g., "Edited shortcuts.ts ✓").
3. When the turn completes, a **Turn Completion Summary** block appears inline in the conversation, listing all changed files with their line counts and status dots.
4. The Detail Panel auto-shows with the Changes tab activated. The file list shows all files changed across the entire thread.
5. User clicks a filename (in either the Turn Completion Summary or the Detail Panel file list) to view its diff in the diff viewer.
6. User reviews the unified diff (green additions, red deletions, syntax highlighted).
7. If the user wants to undo a change, they click "Revert" (↺) on the specific file. The file is restored to its pre-edit state. The status dot changes from blue (●) to dim (○).
8. If the user wants to re-apply a reverted change, they click "Re-apply" on the reverted file.
9. User clicks "Commit" in the thread header.
10. A commit dialog appears:
    - Shows the list of written (non-reverted) files.
    - Reverted files are excluded from the commit.
    - Provides a commit message input (auto-generated from the agent's description).
    - "Commit" button executes `git add` + `git commit` via the AppServer.
11. Success notification: "Changes committed: {message}".

### 16.6 Switch Between Threads

1. User clicks a different thread in the sidebar while a turn is running on the current thread.
2. The running turn continues on the server (no interruption).
3. The new thread loads in the conversation panel.
4. The previous thread's sidebar entry may show a subtle activity indicator if its turn is still running.
5. User switches back to the original thread. The completed turn result is now visible.

---

## 17. Keyboard Shortcuts

### 17.1 Global Shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl+N` | New thread |
| `Ctrl+Shift+O` | Switch workspace |
| `Ctrl+Shift+N` | New window |
| `Ctrl+Shift+M` | Toggle Agent/Plan mode |
| `Ctrl+,` | Open settings |
| `Ctrl+K` | Focus thread search |
| `Ctrl+B` | Toggle sidebar |
| `Ctrl+Shift+B` | Toggle detail panel |
| `Escape` | Close modal / Cancel running turn (when no modal open) |

### 17.2 Conversation Shortcuts

| Shortcut | Action |
|----------|--------|
| `Enter` | Send message |
| `Shift+Enter` | Insert newline |
| `Escape` | Cancel running turn |
| `Ctrl+Shift+C` | Copy last agent response |

### 17.3 Approval Shortcuts

| Shortcut | Action |
|----------|--------|
| `Enter` or `A` | Accept |
| `S` | Accept for Session |
| `Shift+A` | Accept Always |
| `D` | Decline |
| `Esc` | Cancel Turn |

---

## 18. Error States and Edge Cases

### 18.1 Connection Errors

| Scenario | UI Behavior |
|----------|-------------|
| AppServer binary not found | Full-screen error: "DotCraft AppServer not found. Please install DotCraft or configure the binary path in Settings." with a "Open Settings" button. |
| AppServer crashes during operation | Connection status turns red. Banner at top of conversation: "Connection lost. Reconnecting..." with auto-retry. Pending turn is marked as failed. |
| WebSocket connection refused | Connection status turns red. Retry with exponential backoff. Show "Cannot connect to {url}" in status footer. |
| Initialize handshake timeout (10s) | Show error: "AppServer is not responding. Restart?" with a "Restart" button. |

### 18.2 Thread Errors

| Scenario | UI Behavior |
|----------|-------------|
| Thread not found on read | Remove thread from sidebar. Show toast: "Thread not found — it may have been deleted." |
| Thread archived by another client | Update sidebar entry to archived state. If it's the active thread, show inline notice: "This thread has been archived." |
| Concurrent turn (another client started a turn) | Show toast: "A turn is already in progress on this thread." Input remains enabled for when the turn completes. |

### 18.3 Turn Errors

| Scenario | UI Behavior |
|----------|-------------|
| Turn fails (`turn/failed`) | Error block appears in conversation: red-tinted card with the error message. Turn status returns to idle. Input re-enabled. |
| Turn cancelled (`turn/cancelled`) | Subtle notice in conversation: "Turn cancelled." Partial output (if any) is preserved. |
| Approval timeout (`-32020`) | Approval card updates to show "Timed out." Turn may fail or continue based on server behavior. |

### 18.4 Input Edge Cases

| Scenario | Behavior |
|----------|----------|
| User sends message while turn is running | Message is queued as a pending follow-up. A subtle "Queued" indicator appears below the input. The queued message is sent automatically when the current turn completes. Only one message can be queued at a time; subsequent attempts replace the queued message. |
| User pastes extremely long text | Input text is accepted up to 100,000 characters. Beyond that, paste is truncated with a warning toast. |
| User sends empty message | Send button is disabled. `Enter` on empty input is a no-op. |

---

## 19. Non-Functional Requirements

### 19.1 Performance

| Metric | Target |
|--------|--------|
| App startup to window visible | < 2 seconds |
| AppServer connection established | < 5 seconds after window visible |
| Thread list load time | < 500ms for up to 200 threads |
| Thread switch (load and render) | < 300ms |
| Streaming text delta to render | < 16ms (one animation frame) |
| Input responsiveness | < 50ms key-to-character |

### 19.2 Memory

| Metric | Target |
|--------|--------|
| Base memory (idle, no threads) | < 200MB |
| Per-thread cache overhead | < 500KB per cached thread |
| Active streaming overhead | < 5MB during heavy streaming |

### 19.3 Platform Support

| Platform | Minimum Version |
|----------|----------------|
| Windows | Windows 10 (1903+) |
| macOS | macOS 11 (Big Sur) |
| Linux | Ubuntu 20.04+ / Fedora 34+ (X11 and Wayland) |

### 19.4 Distribution

- **Auto-update**: Electron auto-updater for seamless updates. Update check on launch and every 6 hours. User-initiated check via Settings.
- **Installers**: `.exe` (Windows NSIS installer), `.dmg` (macOS), `.AppImage` and `.deb` (Linux).
- **Portable mode**: Support for portable/zip distribution that stores settings alongside the binary.

### 19.5 Accessibility

- All interactive elements must be keyboard-navigable.
- Focus indicators must be visible on all interactive elements.
- Color is never the sole indicator of state (icons, text labels, or patterns accompany color).
- Screen reader support for primary content (conversation messages, approval prompts).

---

## 20. Phase 2 Reserved Surface

Phase 2 will introduce task distribution, a Kanban board, and GitHub Tracker integration. These features require a separate detailed design specification. This section defines only the **navigation surface reserved in Phase 1** to ensure a smooth transition.

### 20.1 Reserved Sidebar Entries

The sidebar includes two entries below the thread list:

- **Automations**: Phase 1 ships the full Automations view (Section 21) with Tasks and Cron tabs. Phase 2 may expand this to include a Kanban board and GitHub Tracker integration as additional views within the same navigation entry.
- **Skills**: Opens the **Skills Management UI** (Section 20.4): list installed skills by source (built-in, workspace, user), open `SKILL.md` in a modal, and enable/disable skills per workspace via AppServer `skills/*` methods.

### 20.2 Reserved Navigation Structure

Phase 2 is expected to introduce:

- A **Task Board** view (Kanban) accessible from the sidebar, showing GitHub Issues organized by status columns.
- A **GitHub Tracker** integration panel, linked from task cards to agent threads.
- **Batch dispatch** workflows for assigning multiple issues to agent threads.

The Phase 1 layout system (sidebar navigation + main content area) is designed to accommodate these views by replacing the conversation panel content when a sidebar navigation entry (like "Automations" or a future "Tasks" entry) is selected.

### 20.3 Data Model Considerations

Phase 2 will likely require:

- Additional Wire Protocol methods or extensions for GitHub issue synchronization.
- Integration with the existing `DotCraft.GitHubTracker` module.
- New state domains for task/issue management.

These are not specified here. Phase 2 design should be initiated as a separate specification document.

### 20.4 Skills Management UI

This subsection normatively describes the Desktop client behavior for the **Skills** sidebar entry.

#### 20.4.1 Goals

- List all skills visible to the agent for the current workspace, with **source** discrimination: **built-in** (deployed under `.craft/skills/` with `.builtin` marker), **workspace** (user-authored under `.craft/skills/` without the marker), and **user** (`~/.craft/skills/` when not shadowed by workspace).
- Show **availability** when frontmatter requirements (bins, env) are not met.
- Allow **per-workspace enable/disable** without deleting files; disabled skills are omitted from agent context (see `Skills.DisabledSkills` in workspace `.craft/config.json` and [AppServer Protocol](appserver-protocol.md) Section 18).
- Display **SKILL.md** content in a **modal** with rendered Markdown (frontmatter stripped for readability in the body).

#### 20.4.2 Wire protocol

The Desktop uses the same JSON-RPC transport as other AppServer methods (`window.api.appServer.sendRequest`):

| Method | Purpose |
|--------|---------|
| `skills/list` | Returns `{ skills: SkillInfoWire[] }` with `includeUnavailable` optional filter. |
| `skills/read` | Returns `{ name, content, metadata }` for the resolved `SKILL.md`. |
| `skills/setEnabled` | Updates workspace config and returns the updated `SkillInfoWire`. |

Clients must treat `capabilities.skillsManagement` from `initialize` as the gate (when false, methods are not available).

#### 20.4.3 UI components

| Component | Responsibility |
|-----------|------------------|
| `SkillsView` | Full-height main column when **Skills** is selected: header (title, short subtitle, callout for workspace vs user paths with mono path chips, Refresh, search), sections grouped by source, responsive card grid. |

The header callout explains that workspace skills live under `.craft/skills/` (including deployed built-ins) and user-level skills under `~/.craft/skills/` when not shadowed by the workspace.
| `SkillCard` | Generic initial or glyph, title, description (clamped), source badge, unavailable/disabled badges, enable **checkbox** (does not open the modal). |
| `SkillDetailDialog` | Modal overlay: title, “Open folder” (skill directory), Markdown body, **Enable/Disable for workspace**, **Close**; **Escape** closes the modal. |

Non-goals for this surface: per-skill custom icons (use generic avatar/glyph), recommended/catalog skills from remote feeds, uninstall/delete from the UI.

#### 20.4.4 Navigation and state

- `uiStore.activeMainView`: `'conversation' | 'skills' | 'automations'`. Selecting **Skills** or **Automations** in the sidebar sets this value; the center column renders `SkillsView`, a placeholder for Automations, or `ConversationPanel` accordingly.
- Selecting a thread, creating a thread, or using quick-start from the welcome screen sets `activeMainView` back to `'conversation'`.
- On connection loss, `activeMainView` resets to `'conversation'`.

#### 20.4.5 Keyboard

| Key | Context | Action |
|-----|---------|--------|
| Escape | Skill detail modal open | Close modal |

#### 20.4.6 Visual design

Follows Section 15 tokens: card surfaces `var(--bg-secondary)`, borders `var(--border-default)`, active sidebar row with `var(--bg-tertiary)` and accent left border, modal backdrop semi-opaque black over `var(--bg-primary)` dialog. The Skills page header callout uses a left accent border (`var(--accent)`), `var(--bg-secondary)` fill, and monospace path chips on `var(--bg-tertiary)`.

---

## 21. Automations View

The Automations view occupies the center column when the **Automations** sidebar entry is selected (`activeMainView = 'automations'`). It contains two tabs: **Tasks** and **Cron**.

### 21.1 Tab Bar

```
┌──────────────────────────────────────────────┐
│  Automations                    [Refresh]     │
│  ──────────────────────────────────────────  │
│  [ Tasks ]  [ Cron ]                          │
└──────────────────────────────────────────────┘
```

| Tab | Visible when | Content |
|-----|-------------|---------|
| **Tasks** | `capabilities.automations === true` | Existing automation task list (unchanged from current implementation) |
| **Cron** | `capabilities.cronManagement === true` | Cron job list — Section 21.2 |

When only one tab is available (single-capability server), the tab bar is not rendered; the corresponding content is shown directly with an appropriate section header.

The active tab is stored in `uiStore.automationsTab` (`'tasks'` \| `'cron'`). Defaults to `'tasks'` when automations is available, otherwise `'cron'`.

### 21.2 Cron Tab — Job List

#### 21.2.1 Layout

```
┌──────────────────────────────────────────────────────┐
│  Automations                          [Refresh]       │
│  ──────────────────────────────────────────────────  │
│  [ Tasks ]  [ Cron ]                                  │
│  ──────────────────────────────────────────────────  │
│                                                       │
│  ┌── CronJobCard ─────────────────────────────────┐  │
│  │  ● drink water reminder           Every 1h     │  │
│  │    Last run: 3h ago · "提醒：该喝水了…"   [View]│  │
│  │                                [Disable] [Del] │  │
│  └────────────────────────────────────────────────┘  │
│                                                       │
│  ┌── CronJobCard ─────────────────────────────────┐  │
│  │  ○ daily report (disabled)        Every 24h    │  │
│  │    Last run: 1d ago · Error: timeout       [View]│ │
│  │                                 [Enable] [Del] │  │
│  └────────────────────────────────────────────────┘  │
│                                                       │
└──────────────────────────────────────────────────────┘
```

The job list is fetched via `cron/list { includeDisabled: true }` on:
- Initial mount of the Cron tab.
- Receiving a `cron/stateChanged` notification.
- User pressing the **Refresh** button.

#### 21.2.2 `CronJobCard` Component

Each cron job is rendered as a card row:

```
┌────────────────────────────────────────────────────────┐
│  ●  drink water reminder             Every 1h          │
│     Last run: 3h ago  ·  "提醒：该喝水了！保持水分…"   │
│                                    [View] [Disable] [✕]│
└────────────────────────────────────────────────────────┘
```

**Status icon** (left):

| State | Icon | Color |
|-------|------|-------|
| Enabled, never run | ● | `var(--text-tertiary)` |
| Enabled, last run ok | ● | `var(--success)` |
| Enabled, last run error | ● | `var(--error)` |
| Disabled | ○ | `var(--text-tertiary)` |
| Currently running (during active turn) | Spinner | `var(--accent)` |

**Center section**:

- **Top row**: Job name (bold, truncated with ellipsis).
- **Schedule badge**: Human-readable schedule derived from `schedule` field:
  - `"every"` + `everyMs`: "Every {N}s" / "Every {N}m" / "Every {N}h" / "Every {N}d" (auto-scaled to largest whole unit).
  - `"at"` + `atMs`: "Once at {date/time}" (formatted in local timezone).
- **Bottom row**: "Last run: {relative time}" (e.g. "3h ago", "Never") · Result preview (first line of `state.lastResult`, truncated to 60 chars). When `lastStatus` is `"error"`, the result preview is shown in `var(--error)`.

**Action buttons** (right, shown on hover or always on touch):

| Button | Condition | Action |
|--------|-----------|--------|
| **View** | `state.lastThreadId` is non-null | Opens the Cron Review Panel (Section 21.3) |
| **Enable** | `enabled === false` | Calls `cron/enable { jobId, enabled: true }` |
| **Disable** | `enabled === true` | Calls `cron/enable { jobId, enabled: false }` |
| **✕ (Delete)** | Always | Opens confirmation dialog, then calls `cron/remove { jobId }` |

**Delete confirmation dialog**:
- Title: "Delete scheduled job?"
- Message: "Remove "{job.name}"? This cannot be undone. The job will stop running immediately."
- Confirm button: "Delete" (danger style)
- Cancel button: "Cancel"

**Interactions**:
- Clicking the card body (not buttons) opens the Cron Review Panel if `state.lastThreadId` is non-null; otherwise no action.
- `Enable`/`Disable` updates the card optimistically (toggle `enabled`) before the response arrives; rolls back on error.

#### 21.2.3 Empty State

When no cron jobs exist:

```
  No scheduled jobs yet.
  Ask the agent to create one in a conversation.
  Example: "Remind me to drink water every hour."
```

#### 21.2.4 Error State

When `cron/list` fails, show:

```
  Could not load scheduled jobs.
  {error message}
  [Retry]
```

### 21.3 Cron Review Panel

When the user clicks **View** on a `CronJobCard`, a review panel slides in from the right side of the Automations view (same layout pattern as `TaskReviewPanel`).

#### 21.3.1 Layout

```
┌──────────────────────────────────────────┐
│  drink water reminder              [×]   │
│  Every 1h  ·  Last run: 3h ago           │
│  ──────────────────────────────────────  │
│                                          │
│  Agent activity                          │
│  ─────────────                           │
│  [Turn block — tool calls, agent msg]    │
│                                          │
│  ...                                     │
└──────────────────────────────────────────┘
```

#### 21.3.2 Data Loading

1. Take `state.lastThreadId` from the `CronJobInfo`.
2. Call `thread/read { threadId: lastThreadId, includeTurns: true }`.
3. Render the full turn history using the shared `AgentResponseBlock` component (same as `TaskReviewPanel`).

The panel is **read-only**: no approval bar, no input. Users can expand tool call cards and read the agent's reasoning and file changes from the last run.

#### 21.3.3 Header

- **Job name** (bold, top-left)
- **Schedule + last run** info line (e.g. "Every 1h · Last run: 3h ago")
- **Status badge**: "Last run: OK" (green) or "Last run: Error" (red)
- **Close button** (×, top-right): closes the panel and clears `selectedCronJobId`

#### 21.3.4 Empty / Loading States

| State | Display |
|-------|---------|
| Loading | "Loading…" placeholder |
| Thread not found | "Execution record not found. The thread may have been deleted." |
| No turns in thread | "No agent activity recorded for this run." |

#### 21.3.5 Navigation

- `Escape` key closes the panel.
- Selecting a different `CronJobCard` replaces the panel content.

---

## 18. Error States and Edge Cases (Amendments)

_The following sub-sections extend Section 18 with cron-specific edge cases._

### 18.5 Windows Native Notifications

When a `system/jobResult` notification arrives and the Desktop window is **not focused** (`mainWindow.isFocused() === false`), the Main Process emits a native OS notification using the Electron `Notification` API.

**Notification fields**:

| Field | Value |
|-------|-------|
| `title` | `jobName ?? 'Scheduled Job Completed'` |
| `body` | `result` stripped of Markdown syntax (see below), truncated to 200 characters. If `result` is null, uses `"Job completed successfully."` or `"Job failed: {error}"`. |
| `icon` | Application icon (same as window icon). |

**Markdown stripping for `body`**: Remove the following patterns before passing to the OS notification:
- ATX headings: `#`–`######` prefix characters and trailing `#`
- Emphasis: `**`, `__`, `*`, `_` wrapping characters
- Inline code: `` ` `` wrapping characters
- Code fences: ` ``` ` blocks replaced with their content (first line only)
- Links: `[text](url)` → `text`
- Images: `![alt](url)` → removed
- List markers: `-`, `*`, `+`, `1.` at line start → removed

**Platform behavior**:

| Platform | Behavior |
|----------|----------|
| Windows 10/11 | Uses `Electron.Notification`. Notification appears in Action Center. Clicking the notification focuses the DotCraft window and switches `activeMainView` to `'automations'` with the Cron tab active. |
| macOS | Same `Electron.Notification` API; appears in Notification Center. |
| Linux | Uses `libnotify` via Electron; behavior depends on the desktop environment. |

**Guard condition**: `Notification.isSupported()` must return `true` before constructing a notification. If not supported, the toast-only path is the sole delivery mechanism.

**Deduplication**: One native notification per `system/jobResult` event. Multiple jobs completing simultaneously each produce their own notification.

### 18.6 Cron Job State Conflicts

| Scenario | UI Behavior |
|----------|-------------|
| `cron/enable` request fails (job not found, `-32031`) | Roll back the optimistic toggle. Show error toast: "Failed to update job — it may have been deleted." Refresh `cron/list` to restore correct state. |
| `cron/remove` request fails | Dismiss the confirmation dialog. Show error toast: "Failed to delete job." |
| `cron/stateChanged` arrives for unknown `jobId` | Treat as a new job: insert into `cronJobs` list. |
| `cron/stateChanged` with `removed: true` for unknown `jobId` | No-op (job already absent from local list). |
| Desktop connects while a cron job is mid-execution | The `cron/list` response shows the job's current `lastStatus` from the previous run (not "running"). The running state is not surfaced at the job-list level; the active turn is only visible in the Cron Review Panel if the user opens it by thread. |
