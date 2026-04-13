# DotCraft Desktop UX Specification

| Field | Value |
|-------|-------|
| **Version** | 0.2.1 |
| **Status** | Living |
| **Date** | 2026-04-11 |
| **Parent Spec** | [AppServer Protocol](appserver-protocol.md) |

Purpose: Define the stable user-experience behavior of **DotCraft Desktop** as a protocol client for DotCraft AppServer. This document specifies user-visible flows, interaction rules, state transitions, and recovery behavior. It does not define frontend implementation details, visual design, or framework choices.

---

## Table of Contents

- [1. Scope](#1-scope)
- [2. Goals and Non-Goals](#2-goals-and-non-goals)
- [3. Connection and Session Lifecycle](#3-connection-and-session-lifecycle)
- [4. Protocol Event to UX Behavior](#4-protocol-event-to-ux-behavior)
- [5. Core Interaction Flows](#5-core-interaction-flows)
- [6. Secondary Flows](#6-secondary-flows)
- [7. Keyboard Accessibility and Localization](#7-keyboard-accessibility-and-localization)
- [8. Error Handling and Recovery](#8-error-handling-and-recovery)
- [9. Non-Functional UX Requirements](#9-non-functional-ux-requirements)
- [10. Phase 2 Reserved Surface](#10-phase-2-reserved-surface)

---

## 1. Scope

### 1.1 What This Spec Defines

- The user-visible behavior of the Desktop client while connected to DotCraft AppServer.
- How users open workspaces, connect, browse threads, send turns, review results, and respond to approvals.
- How protocol events change user-visible state.
- How secondary surfaces such as Skills and Automations behave from the user's perspective.
- How the client communicates failure, recovery, and availability constraints.
- Localization, accessibility, and performance expectations at the UX level.

### 1.2 What This Spec Does Not Define

- Wire protocol payloads, transport rules, or server semantics already defined in [appserver-protocol.md](appserver-protocol.md).
- Frontend frameworks, component trees, IPC method signatures, process architecture, or state-store structure.
- Layout geometry, colors, typography, icons, spacing, animation, or other visual design details.
- Platform-specific implementation APIs for notifications, menus, file search, or file persistence.

---

## 2. Goals and Non-Goals

### 2.1 Goals

1. Expose the AppServer protocol as a desktop workflow optimized for persistent threads and long-running agent work.
2. Support multi-thread productivity, including switching between threads while background work continues.
3. Preserve a clear review loop for approvals, file changes, plans, tool output, and automation runs.
4. Make connection state and recovery paths understandable without requiring users to understand protocol internals.
5. Keep workspace behavior predictable across reconnects, restarts, and concurrent clients.

### 2.2 Non-Goals

- Embedding the DotCraft runtime in-process.
- Acting as a full IDE, terminal emulator, or general-purpose file browser.
- Freezing a specific visual layout or frontend architecture.
- Defining remote plugin UI, mobile UX, or future task-board behavior in detail.

---

## 3. Connection and Session Lifecycle

### 3.1 Workspace Entry

- The Desktop client is workspace-centric. A user opens one workspace at a time in one window.
- Opening a workspace starts a connection attempt to an AppServer instance for that workspace.
- The client may support multiple connection modes, but the UX contract is the same: the user selects or opens a workspace, the client connects, and thread operations remain unavailable until initialization succeeds.

### 3.2 Connection States

The client exposes four user-visible connection states:

| State | Meaning |
|-------|---------|
| `connecting` | The client is attempting to establish transport and complete protocol initialization. |
| `connected` | Initialization is complete and thread operations are available. |
| `disconnected` | A previously working connection is currently unavailable, but recovery may still be possible. |
| `error` | Connection failed or cannot proceed without user intervention. |

### 3.3 Initial Load

- After connection becomes `connected`, the client loads the minimum data needed for conversation workflows:
  - server capabilities
  - thread list for the active workspace identity
  - optional capability-gated surfaces such as skills, automations, or model catalog
- If no thread exists, the client presents an empty ready state that clearly allows starting a new conversation.

### 3.4 Reconnection

- On unexpected disconnect, the client transitions to `disconnected` and attempts reconnection automatically.
- During reconnect, the user must be able to tell that prior thread data is still local UI state while live updates are temporarily unavailable.
- After reconnect, the client completes a fresh protocol handshake and restores the active session context:
  - reload active capabilities
  - re-establish the active thread subscription when applicable
  - refresh thread and automation data that may have changed while offline
- If recovery succeeds, the state returns to `connected` without requiring the user to rebuild local context manually.
- If recovery fails persistently, the state becomes `error` and the user is given a retry path.

### 3.5 Workspace Switching

- Switching workspace is treated as leaving one session context and entering another.
- The previous workspace window state may be remembered, but protocol state must not leak across workspaces.
- A switch resets the active thread selection unless the new workspace has a valid equivalent remembered locally.

### 3.6 Multiple Windows

- Each window is scoped to one workspace.
- Multiple windows may be open concurrently for different workspaces.
- User actions in one window must not implicitly change thread selection or visible state in another window.

---

## 4. Protocol Event to UX Behavior

This section defines how protocol messages affect user-visible behavior. It intentionally describes UX outcomes rather than internal state implementation.

### 4.1 Thread Events

| Protocol event | UX behavior |
|---------------|-------------|
| `thread/started` | The new thread appears in the thread navigation area and may become selectable immediately. |
| `thread/renamed` | Any visible thread label updates everywhere the thread is referenced. |
| `thread/deleted` | The thread is removed from navigation and from any active context. If currently open, the user is moved to a safe fallback state. |
| `thread/statusChanged` | Thread availability updates immediately. Actions that are no longer valid must be disabled or blocked. |
| `thread/resumed` | The thread returns to an active, turn-capable state. |

### 4.2 Turn Events

| Protocol event | UX behavior |
|---------------|-------------|
| `turn/started` | The active thread enters a running state. Sending a new turn on the same thread is blocked. |
| `turn/completed` | Running indicators clear and final turn results are shown. |
| `turn/failed` | The user sees that the turn ended unsuccessfully and is given a path to retry or continue. |
| `turn/cancelled` | The running state clears and the user sees that the turn was interrupted. |

### 4.3 Item Events

| Protocol event | UX behavior |
|---------------|-------------|
| `item/started` | New agent work becomes visible in the current thread. |
| `item/agentMessage/delta` | Agent text streams incrementally when streaming is enabled. |
| `item/reasoning/delta` | Reasoning content is exposed only if the client chooses to show reasoning. |
| `item/commandExecution/outputDelta` | Running shell output is appended live to the matching command block in both the conversation view and the Terminal review surface. |
| `item/completed` | The final item output replaces or finalizes any in-progress representation. |
| `item/usage/delta` | Token usage counters update when the client exposes real-time usage. |

### 4.4 Approval Events

| Protocol event | UX behavior |
|---------------|-------------|
| `item/approval/request` | The current thread enters a waiting-for-user-decision state. The approval request becomes the highest-priority interaction. |
| `item/approval/resolved` | The approval decision is reflected immediately and the thread resumes or terminates according to the decision. |

### 4.5 Supplemental Events

| Protocol event | UX behavior |
|---------------|-------------|
| `subagent/progress` | The client may surface background worker progress if useful, but must not block the main conversation. |
| `plan/updated` | Structured task progress becomes available in the current conversation context. |
| `system/event` | Maintenance steps may be surfaced when relevant but must not overshadow core turn output. |
| `system/jobResult` | Automation or heartbeat output becomes visible as an out-of-band result associated with its source run. |
| `cron/stateChanged` | Automation status views refresh to reflect the current job state. |

### 4.6 General Rules

- If the active thread is subscribed, updates should appear without requiring manual refresh.
- If the user is viewing another thread when an inactive thread changes, the client may indicate background activity but must not forcibly switch context.
- When a capability is absent, the corresponding UX surface is disabled or hidden rather than failing late.

---

## 5. Core Interaction Flows

### 5.1 Open a Workspace

1. User opens or selects a workspace.
2. Client begins connecting and makes connection state visible.
3. After initialization succeeds, the client loads threads and any capability-gated data needed for the default workspace view.
4. If no thread is selected, the user is shown a clear starting point for a new conversation.

### 5.2 Start a New Conversation

1. User chooses to create a thread.
2. Client calls `thread/start`.
3. The new thread becomes active immediately after success.
4. The input area becomes ready for the first message.
5. If thread creation fails, the user remains in the prior safe state with a retry path.

### 5.3 Resume or Open an Existing Thread

1. User selects a thread from the navigation area.
2. Client loads the thread content with enough history to make the conversation understandable.
3. Client subscribes to future updates for the selected thread when real-time updates are needed.
4. If the thread is not turn-capable, the user sees why and which actions remain allowed.

### 5.4 Send a Message

1. User composes input and submits it.
2. If the thread is idle and turn-capable, the client calls `turn/start`.
3. The thread enters a running state and duplicate submissions for the same thread are blocked.
4. Incremental output appears as events arrive.
5. When the turn finishes, the thread returns to an idle, completed, failed, or cancelled state.

### 5.5 Input Rules

- The input area accepts plain text and any supported structured attachments or references.
- The client must prevent submission of an empty turn.
- If the thread is currently running, the client must either block a second submission on that thread or convert it into an explicit queued-follow-up behavior. The behavior must be consistent and visible to the user.
- If attachments cannot be preserved in a queued or deferred path, the user must be warned before the message is sent.

### 5.6 Approval Handling

1. An approval request arrives while a turn is running.
2. The active thread enters a waiting-approval state.
3. The approval request is surfaced with enough information for the user to decide.
4. The user can approve, decline, session-approve when supported, or cancel as allowed by the protocol surface.
5. After the decision:
   - approved work continues
   - declined work reflects rejection and may continue with an alternative path
   - cancelled work terminates the turn
6. If approval times out or is no longer valid, the user sees the resulting turn outcome.

### 5.7 View Changes, Plans, and Tool Output

- File changes produced during a thread remain discoverable until reverted or superseded.
- Plan updates remain associated with the active thread and reflect the latest complete plan snapshot.
- Tool output remains readable in-thread and must remain distinguishable from agent conversational text.
- `commandExecution` items are the Desktop client's primary source of shell output. Running commands remain visible before completion and continue updating even if the user opens the Terminal detail surface.
- The Terminal detail surface shows all `commandExecution` items for the current thread history, including in-progress commands.
- If the user switches to another thread while a command is still running, the output continues updating in the background thread state without forcing a focus change.
- This milestone does not require interactive terminal input; shell output is read-only from the Desktop client's perspective.
- The client may reveal related context automatically when new changes or plans appear, but the rule should be based on relevance, not on any fixed panel design.

### 5.8 Interrupt a Running Turn

1. User requests interruption while a turn is running.
2. Client calls `turn/interrupt`.
3. The running state remains visible until interruption is confirmed by protocol outcome.
4. When `turn/cancelled` arrives, the client returns the thread to a safe idle state.

### 5.9 Archive and Delete

- Archived threads remain readable but not turn-capable.
- The client may expose a dedicated archived-thread management surface for browsing and restore actions.
- Restoring an archived thread returns it to the active thread set without forcing automatic navigation into that conversation.
- Deleted threads disappear from the client once deletion is confirmed.
- If a thread is archived or deleted elsewhere while open locally, the user must see the updated state immediately and lose only the actions that are no longer valid.

### 5.10 Cross-Channel Visibility

- If the server supports cross-channel thread discovery, the Desktop client may present threads whose origin differs from the desktop client itself.
- The UX contract is that origin differences must not make the thread list confusing:
  - origin may be shown when useful
  - unsupported actions must be disabled rather than failing unexpectedly
  - read and resume behavior must follow server capabilities and thread status

---

## 6. Secondary Flows

### 6.1 Skills

The Skills surface remains within Desktop scope as a workflow, not a UI design.

Required behavior:

- Users can enter a Skills view if the server exposes skills capabilities.
- Users can browse installed skills.
- Users can inspect the content of a selected skill.
- Users can enable or disable a skill when the server supports that action.
- If a skill is unavailable because server-side requirements are unmet, the client explains that the skill exists but is currently unusable.
- If skills capability is absent, the entry point is hidden or disabled with a clear reason.

### 6.2 Automations

The Automations surface remains within Desktop scope as a workflow, not a UI design.

Required behavior:

- Users can enter an Automations view if at least one relevant automation capability is available.
- The client separates capability availability from current data availability:
  - unsupported features are disabled
  - supported but empty features show empty states
- Automation data refreshes on entry and after server-side state changes.

### 6.3 Cron Jobs

Required behavior:

- Users can list cron jobs when `cronManagement` is available.
- Users can inspect each job's enabled state, recent result summary, and most recent associated thread when available.
- Users can enable, disable, or remove jobs when supported by the server.
- If job state changes elsewhere, the list refreshes through `cron/stateChanged` or explicit reload.
- If a job has a recent execution thread, users can open that thread's history for review.

### 6.4 Cron Run Review

- Reviewing a cron run is a read-only workflow.
- The review experience must expose the conversation and outputs associated with the most recent run thread.
- Users must be able to leave the review state without losing their place in the automations list.

### 6.5 Model Selection

- If model catalog capability is available, the client may offer model selection using server-provided values.
- If model catalog capability is absent or temporarily fails, the conversation workflow remains usable.
- Updating workspace default model and updating active-thread model must remain distinct actions when both are supported.

### 6.6 Archived Threads

Required behavior:

- Users can enter an archived-thread management surface from Settings when thread-management capability is available.
- The archived-thread list follows the same workspace identity and cross-channel visibility rules as the main thread list, but queries with archived inclusion enabled.
- The archived-thread surface is read-only apart from restore actions; it does not provide message sending.
- Restoring a thread removes it from the archived list immediately and makes it eligible to reappear in the main thread list after local refresh or status synchronization.
- If a thread is restored or deleted elsewhere while the archived-thread surface is open, the visible list reconciles automatically without requiring a full app restart.

---

## 7. Keyboard Accessibility and Localization

### 7.1 Keyboard Expectations

- High-frequency actions must be keyboard-accessible:
  - create thread
  - send message
  - interrupt turn
  - navigate threads
  - respond to approvals
  - dismiss transient blocking overlays when safe
- If a shortcut is unavailable on one platform, an equivalent keyboard path must still exist.

### 7.2 Accessibility

- All critical workflows must be usable without relying on color alone.
- Focus order must remain predictable during thread navigation, sending, approvals, and review flows.
- Approval requests and blocking errors must move focus in a way that makes the next required action clear.
- Streaming content must remain readable as it updates.
- Hidden or disabled features must communicate why they are unavailable.

### 7.3 Localization

- All client-owned user-facing strings must be localizable.
- Server-provided identifiers, model ids, thread ids, and similar protocol values must remain stable and must not be translated as routing keys.
- Changing display language must update client-owned UX within a short and predictable refresh path.
- Locale-sensitive formatting such as time and date should follow the selected language or locale policy consistently.

---

## 8. Error Handling and Recovery

### 8.1 Connection Errors

- If connection fails before initialization, the user sees a startup failure state with retry.
- If connection drops after initialization, the user sees a disconnected state and automatic recovery begins.
- The client must not silently discard active context during reconnection.

### 8.2 Thread Errors

- If a thread cannot be read, resumed, or updated, the user sees a clear failure message and remains in a safe prior context.
- If a selected thread disappears remotely, the client removes it and falls back to a safe empty or next-valid thread state.
- If another client starts a turn first, the user sees that the thread is busy rather than experiencing a silent send failure.

### 8.3 Turn Errors

- Failed turns remain visible in-thread with enough information to understand that work stopped.
- Users must be able to continue the conversation after a failure unless the thread itself is no longer valid.
- Interrupted turns and failed turns must be distinguishable in user-visible language.

### 8.4 Approval Errors

- If approval is no longer valid, times out, or cannot be delivered, the user sees the resulting turn outcome.
- If the client does not support approval handling for a given environment, that limitation must be known before a turn reaches a blocked state whenever possible.

### 8.5 Input and Attachment Errors

- Invalid input, unsupported attachments, oversized attachments, or failed attachment preparation must be surfaced before or at submission time.
- If a degraded fallback is used, the client must say exactly what was dropped or changed.
- Search or attachment helper failures must not corrupt the rest of the composer workflow.

### 8.6 Automation Errors

- If cron list loading fails, the Automations view remains usable enough to retry.
- If a cron action fails due to stale state, the client refreshes server truth and reconciles the visible state.
- If automation review data is missing, the user sees that the run exists but cannot currently be inspected.

---

## 9. Non-Functional UX Requirements

### 9.1 Responsiveness

- Streaming text should appear quickly enough to feel live rather than batch-delivered.
- User actions such as thread selection, approval response, and interrupt should visibly acknowledge input immediately, even if final protocol completion arrives later.

### 9.2 Reliability

- The client must tolerate reconnects, out-of-order user navigation, and concurrent updates from other clients without corrupting visible thread state.
- Protocol capability changes across reconnects must be reflected by enabling or disabling affected UX surfaces.

### 9.3 Platform Coverage

- The UX contract applies across supported desktop platforms.
- Platform differences may change implementation details, but not the meaning of connection state, thread state, approval flow, or automation flow.

### 9.4 Accessibility and Readability

- Long-running sessions must remain understandable over time.
- Thread history, tool output, plan progress, and automation output must remain legible in the presence of long content and repeated updates.

---

## 10. Phase 2 Reserved Surface

- The Desktop client may later expose task-oriented surfaces beyond conversation, skills, and automations.
- This document reserves that expansion without defining future layout or visual form.
- Any future task-board or GitHub-tracker UX must preserve the same principles used here:
  - protocol-driven behavior
  - explicit status and recovery
  - clear separation between workflow rules and visual implementation
