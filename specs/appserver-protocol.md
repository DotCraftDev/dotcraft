# DotCraft AppServer Protocol Specification

| Field | Value |
|-------|-------|
| **Version** | 0.2.0 |
| **Status** | Living |
| **Date** | 2026-03-18 |
| **Parent Spec** | [Session Core](session-core.md) (Section 19) |

Purpose: Define a language-neutral JSON-RPC wire protocol that exposes Session Core (`ISessionService`) to out-of-process clients, enabling non-C# adapters to create and resume threads, submit turns, stream events, and participate in approval flows. The protocol also covers server management operations — specifically cron job lifecycle — that are owned by the AppServer process but need to be accessible to wire clients.

## Table of Contents

- [1. Scope](#1-scope)
- [1.4 V1 Contract Snapshot](#14-v1-contract-snapshot)
- [2. Protocol Fundamentals](#2-protocol-fundamentals)
- [3. Initialization](#3-initialization)
- [4. Thread Methods](#4-thread-methods)
- [5. Turn Methods](#5-turn-methods)
- [6. Event Notifications](#6-event-notifications)
  - [6.5 SubAgent Notifications](#65-subagent-notifications)
  - [6.6 Usage Notifications](#66-usage-notifications)
  - [6.7 System Notifications](#67-system-notifications)
- [6.8 Plan Notifications](#68-plan-notifications)
- [7. Approval Flow](#7-approval-flow)
- [8. Error Handling](#8-error-handling)
- [9. Backpressure](#9-backpressure)
- [10. Notification Opt-Out](#10-notification-opt-out)
- [11. Extension Methods](#11-extension-methods)
- [12. Versioning and Compatibility](#12-versioning-and-compatibility)
- [13. Full Turn Example](#13-full-turn-example)
- [14. Relationship to Codex App Server](#14-relationship-to-codex-app-server)
- [15. WebSocket Transport](#15-websocket-transport)
- [16. Cron Management Methods](#16-cron-management-methods)
- [17. Heartbeat Management Methods](#17-heartbeat-management-methods)

---

## 1. Scope

### 1.1 What This Spec Defines

This specification defines the wire protocol — message formats, methods, notifications, and transport rules — that a DotCraft server exposes to external clients over stdio or WebSocket. It is primarily the network-facing projection of the Session Core `ISessionService` API, and additionally covers server management operations (cron job lifecycle) that the AppServer process owns and executes independently of any session.

### 1.2 What This Spec Does Not Define

- **Domain model semantics**: Thread, Turn, and Item lifecycle rules, persistence layout, and state machine invariants are defined in the [Session Core Specification](session-core.md). This spec references them but does not redefine them.
- **Agent execution internals**: The Microsoft.Extensions.AI pipeline, tool invocation, and hook execution are unchanged and invisible to the wire client.
- **Channel-specific UX**: How a client renders events (streaming text, diffs, approval dialogs) is a client concern.
- **In-process adapter patterns**: `SessionEventHandler`, `SessionEventChannel`, and the adapter pattern for in-process channels (CLI, QQ, WeCom) are internal to the C# codebase and not part of this wire protocol.

### 1.3 Design Reference

This protocol is modeled after the [Codex App Server](https://github.com/openai/codex/tree/main/codex-rs/app-server) JSON-RPC protocol, adapted to DotCraft's domain model. The Thread/Turn/Item primitives, event streaming, and bidirectional approval flow follow the same patterns described in [Unlocking the Codex Harness](https://openai.com/index/unlocking-the-codex-harness/).

### 1.4 V1 Contract Snapshot

The current v1 contract is based on the refactored Session Core, not on the earlier draft assumptions. For implementation planning, features fall into three buckets:

| Bucket | V1 Items |
|-------|----------|
| **Guaranteed in v1** | Rich approval decisions (`accept`, `acceptForSession`, `acceptAlways`, `decline`, `cancel`), thread-scoped event subscription, accurate per-turn origin/initiator metadata, strict `historyMode` rules, separate wire DTO serialization with camelCase enums and lossless delta typing. Cron management methods (`cron/list`, `cron/remove`, `cron/enable`) with the `cronManagement` server capability flag. Heartbeat trigger method (`heartbeat/trigger`) with the `heartbeatManagement` capability flag. |
| **Guaranteed with narrowed semantics** | `thread/list` is deterministic but **not cursor-paginated** in v1; archived threads are excluded by default and included only via an explicit filter. |
| **Deferred from v1** | Structured extension capability registry beyond a flat namespace advertisement. Clients must treat extension namespaces as optional and discoverable, not required for core Session behavior. |

---

## 2. Protocol Fundamentals

### 2.1 JSON-RPC 2.0

The wire protocol uses **JSON-RPC 2.0** with the `"jsonrpc": "2.0"` header included on every message.

Three message kinds:

| Kind | Has `id` | Has `method` | Direction |
|------|----------|--------------|-----------|
| Request | yes | yes | either |
| Response | yes | no | reply to request |
| Notification | no | yes | either |

- **Client-to-server requests**: thread and turn lifecycle operations.
- **Server-to-client notifications**: event stream (thread/turn/item events).
- **Server-to-client requests**: approval prompts that require a client response.
- **Client-to-server notifications**: `initialized` handshake acknowledgement.

### 2.2 Transports

| Transport | Wire Format | Status |
|-----------|-------------|--------|
| stdio | Newline-delimited JSON (JSONL): one complete JSON-RPC message per line, UTF-8 encoded, over stdin (client→server) and stdout (server→client). | Primary |
| WebSocket | One JSON-RPC message per WebSocket text frame. | Experimental |

**stdio transport**: The server reads JSON-RPC requests from `stdin` and writes responses/notifications to `stdout`. Diagnostic and log output goes to `stderr`. This matches the transport used by ACP and Codex App Server. Stdio is a 1:1 transport — exactly one client per server process.

**WebSocket transport**: When listening on `ws://HOST:PORT/ws`, the server supports multiple concurrent client connections. Each connection is fully independent and maintains its own initialization state and thread subscriptions. Full behavior is specified in [Section 15](#15-websocket-transport).

### 2.3 Serialization Rules

- All JSON property names use **camelCase** (e.g., `threadId`, `tokenUsage`, `approvalType`).
- Timestamps are **ISO 8601 UTC** strings (e.g., `"2026-03-15T10:00:00Z"`).
- Enums are serialized as **camelCase strings** (e.g., `"active"`, `"running"`, `"toolCall"`, `"waitingApproval"`).
- Nullable fields are omitted from the JSON when `null`, unless explicitly stated otherwise.
- Wire DTOs are distinct from the on-disk persistence models. Persisted thread JSON may keep internal compatibility quirks; the wire contract must remain lossless and transport-stable.
- `item/delta` payloads must carry a `deltaKind` field so clients can distinguish agent text from reasoning text without inspecting surrounding state.
- `id` fields in JSON-RPC messages may be strings or integers. The server preserves the type and value when responding.

---

## 3. Initialization

### 3.1 Handshake

The client must send an `initialize` request as the very first message on a new connection. Any other method sent before initialization is rejected with error code `-32002` (`"Not initialized"`). Repeated `initialize` calls on the same connection are rejected with error code `-32003` (`"Already initialized"`).

After receiving the `initialize` response, the client must send an `initialized` notification to signal readiness. The server may begin sending notifications (e.g., for in-progress threads) after receiving `initialized`.

```
Client                              Server
  |                                   |
  | initialize (request, id: 0)      |
  |---------------------------------->|
  |                                   |
  | (response, id: 0)                |
  |<----------------------------------|
  |                                   |
  | initialized (notification)        |
  |---------------------------------->|
  |                                   |
  | (protocol ready, both directions) |
```

### 3.2 `initialize`

**Direction**: client → server (request)

**Params**:

```json
{
  "clientInfo": {
    "name": "dotcraft-vscode",
    "title": "DotCraft VS Code Extension",
    "version": "1.0.0"
  },
  "capabilities": {
    "approvalSupport": true,
    "streamingSupport": true,
    "optOutNotificationMethods": []
  }
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `clientInfo.name` | string | yes | Machine-readable client identifier. |
| `clientInfo.title` | string | no | Human-readable client name. |
| `clientInfo.version` | string | yes | Client version string. |
| `capabilities.approvalSupport` | boolean | no | Whether the client can handle server-initiated approval requests. Default `true`. |
| `capabilities.streamingSupport` | boolean | no | Whether the client can consume `item/*/delta` notifications. Default `true`. |
| `capabilities.optOutNotificationMethods` | string[] | no | Exact notification method names to suppress for this connection. See [Section 10](#10-notification-opt-out). |

**Result**:

```json
{
  "serverInfo": {
    "name": "dotcraft",
    "version": "0.2.0",
    "protocolVersion": "1",
    "extensions": ["acp"]
  },
  "capabilities": {
    "threadManagement": true,
    "threadSubscriptions": true,
    "approvalFlow": true,
    "modeSwitch": true,
    "configOverride": true,
    "cronManagement": true,
    "heartbeatManagement": true
  }
}
```

| Field | Type | Description |
|-------|------|-------------|
| `serverInfo.name` | string | Always `"dotcraft"`. |
| `serverInfo.version` | string | DotCraft server version. |
| `serverInfo.protocolVersion` | string | Wire protocol version. Currently `"1"`. |
| `serverInfo.extensions` | string[] | Optional flat list of available extension namespaces. Structured extension capability metadata is deferred from v1. |
| `capabilities.threadManagement` | boolean | Server supports thread CRUD operations. |
| `capabilities.threadSubscriptions` | boolean | Server supports passive `thread/subscribe` observers independent from `turn/start`. |
| `capabilities.approvalFlow` | boolean | Server may send approval requests. |
| `capabilities.modeSwitch` | boolean | Server supports `thread/mode/set`. |
| `capabilities.configOverride` | boolean | Server supports `thread/config/update`. |
| `capabilities.cronManagement` | boolean | Server supports cron job management methods (`cron/list`, `cron/remove`, `cron/enable`). Absent or `false` when the cron service is not configured. |
| `capabilities.heartbeatManagement` | boolean | Server supports heartbeat management methods (`heartbeat/trigger`). Absent or `false` when the heartbeat service is not configured. |

### 3.3 `initialized`

**Direction**: client → server (notification, no `id`)

**Params**: `{}` (empty object)

No response. Signals the client is ready to receive notifications.

---

## 4. Thread Methods

Thread methods correspond to `ISessionService` thread lifecycle operations defined in the [Session Core Specification, Section 5.1](session-core.md#51-thread-lifecycle).

### 4.1 `thread/start`

Create a new thread. The server generates a Thread ID and persists initial state.

**Direction**: client → server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `identity` | SessionIdentity | yes | Channel identity for thread ownership. See [Session Core, Section 4.1.4](session-core.md#414-sessionidentity). |
| `config` | ThreadConfiguration | no | Per-thread agent configuration. Null means workspace defaults. |
| `historyMode` | string | no | `"server"` (default) or `"client"`. |
| `displayName` | string | no | Explicit thread display name. |

`SessionIdentity` on the wire:

```json
{
  "channelName": "vscode",
  "userId": "user-123",
  "channelContext": "workspace:/path/to/project",
  "workspacePath": "/path/to/project"
}
```

**Result**:

```json
{
  "thread": {
    "id": "thread_20260316_x7k2m4",
    "workspacePath": "/path/to/project",
    "userId": "user-123",
    "originChannel": "vscode",
    "displayName": null,
    "status": "active",
    "createdAt": "2026-03-16T10:00:00Z",
    "lastActiveAt": "2026-03-16T10:00:00Z",
    "metadata": {},
    "turns": []
  }
}
```

The server also emits a `thread/started` notification after the response.

**Example**:

```json
{ "jsonrpc": "2.0", "method": "thread/start", "id": 1, "params": {
    "identity": {
      "channelName": "vscode",
      "userId": "user-123",
      "channelContext": "workspace:/home/dev/myproject",
      "workspacePath": "/home/dev/myproject"
    },
    "historyMode": "server"
} }

{ "jsonrpc": "2.0", "id": 1, "result": {
    "thread": {
      "id": "thread_20260316_x7k2m4",
      "status": "active",
      "workspacePath": "/home/dev/myproject",
      "createdAt": "2026-03-16T10:00:00Z",
      "lastActiveAt": "2026-03-16T10:00:00Z",
      "turns": []
    }
} }

{ "jsonrpc": "2.0", "method": "thread/started", "params": {
    "thread": { "id": "thread_20260316_x7k2m4", "status": "active" }
} }
```

### 4.2 `thread/resume`

Resume a paused or previously loaded thread. Session Core loads the thread from persistence, reconstructs the agent session, and sets status to Active.

**Direction**: client → server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `threadId` | string | yes | Thread ID to resume. |

**Result**: `{ "thread": Thread }` — the resumed thread object.

The server emits a `thread/resumed` notification.

**Example**:

```json
{ "jsonrpc": "2.0", "method": "thread/resume", "id": 2, "params": {
    "threadId": "thread_20260316_x7k2m4"
} }

{ "jsonrpc": "2.0", "id": 2, "result": {
    "thread": { "id": "thread_20260316_x7k2m4", "status": "active" }
} }
```

### 4.3 `thread/list`

List threads matching a given identity.

**Direction**: client → server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `identity` | SessionIdentity | yes | Identity to filter by. |
| `includeArchived` | boolean | no | Default `false`. When `true`, archived threads are included in the result set. |

**Result**:

```json
{
  "data": [
    {
      "id": "thread_20260316_x7k2m4",
      "displayName": "Fix login bug",
      "status": "active",
      "originChannel": "vscode",
      "createdAt": "2026-03-16T10:00:00Z",
      "lastActiveAt": "2026-03-16T10:05:00Z"
    }
  ]
}
```

Results are ordered by `lastActiveAt` descending. Cursor pagination is deferred from v1 because the current Core only guarantees deterministic full-list ordering.

### 4.4 `thread/read`

Read a thread by ID without resuming it. Optionally includes turn history.

**Direction**: client → server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `threadId` | string | yes | Thread ID to read. |
| `includeTurns` | boolean | no | If `true`, include the full `turns` array. Default `false`. |

**Result**: `{ "thread": Thread }` — the thread object, with `turns` populated if requested.

**Semantics**: `thread/read` may load the thread into the server’s in-memory cache for discovery UX, but it is a **read-only** operation: it does not connect MCP servers or rebuild the execution-time agent from `thread.configuration`. Per [Session Core](session-core.md), that hydration happens when a turn is executed — the server runs an ensure-loaded step inside `turn/start` before agent work begins.

### 4.5 `thread/subscribe`

Subscribe the current connection to future lifecycle events for a thread. Multiple passive subscribers may observe the same thread concurrently.

**Direction**: client → server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `threadId` | string | yes | Thread to observe. |
| `replayRecent` | boolean | no | Default `false`. When `true`, the server may replay a small recent buffer for reconnect smoothing. |

**Result**: `{}`

After subscription succeeds, the server may emit future `thread/*`, `turn/*`, and `item/*` notifications for that thread even when the current connection did not originate the turn.

### 4.6 `thread/unsubscribe`

Remove the current connection's passive subscription to a thread.

**Direction**: client → server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `threadId` | string | yes | Thread to stop observing. |

**Result**: `{}`

Cancellation of the transport connection also implicitly unsubscribes all active thread subscriptions owned by that connection.

### 4.7 `thread/pause`

Pause an active thread. A paused thread cannot accept new turns until resumed.

**Direction**: client → server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `threadId` | string | yes | Thread ID to pause. |

**Result**: `{}`

The server emits a `thread/statusChanged` notification.

### 4.8 `thread/archive`

Archive a thread. Archived threads are read-only — they can be listed and read but not resumed or turned.

**Direction**: client → server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `threadId` | string | yes | Thread ID to archive. |

**Result**: `{}`

The server emits a `thread/statusChanged` notification.

### 4.9 `thread/delete`

Permanently delete a thread and its associated session data.

**Direction**: client → server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `threadId` | string | yes | Thread ID to delete. |

**Result**: `{}`

### 4.10 `thread/mode/set`

Set the agent mode for a thread (e.g., `"plan"`, `"agent"`).

**Direction**: client → server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `threadId` | string | yes | Thread ID. |
| `mode` | string | yes | New agent mode. |

**Result**: `{}`

**Behavior**: The server recreates the agent for the specified thread with the new mode's tool set. The resulting agent must have the same mode-specific tools as an equivalent in-process agent (see Session Core spec §16.3.1). In particular, the AppServer process must supply `PlanStore` to `AgentFactory` so that plan-mode tools (`CreatePlan`) and agent-mode plan tools (`UpdateTodos`, `TodoWrite`) are correctly injected.

### 4.9 `thread/config/update`

Update per-thread agent configuration (MCP servers, extensions, etc.).

**Direction**: client → server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `threadId` | string | yes | Thread ID. |
| `config` | ThreadConfiguration | yes | Configuration patch. |

**Result**: `{}`

---

## 5. Turn Methods

Turn methods correspond to `ISessionService` turn lifecycle operations defined in the [Session Core Specification, Section 5.2](session-core.md#52-turn-lifecycle).

### 5.1 `turn/start`

Submit user input to a thread and begin agent execution. The server creates a new Turn, records the user input as a `UserMessage` Item, and starts the agent.

Before starting the agent, the server **must** ensure the in-memory thread is loaded from persistence if needed and that any persisted `thread.configuration` (mode, MCP servers, etc.) is applied to the execution-time agent, so turns do not silently use workspace-default tooling after a cold load or when only `thread/read` was used earlier ([Session Core](session-core.md) `EnsureThreadLoaded`).

The response is returned **immediately** with the initial Turn object (status `"running"`, empty `items`). The agent's output then streams as notifications: `turn/started`, followed by `item/*` events, and finally `turn/completed` (or `turn/failed` / `turn/cancelled`).

**Direction**: client → server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `threadId` | string | yes | Target thread. Must be `"active"` with no running turn. |
| `input` | InputPart[] | yes | User input. At least one part required. |
| `sender` | SenderContext | no | Sender identity for group sessions. |
| `messages` | ChatMessage[] | conditional | Required when the thread uses `historyMode = "client"`. Forbidden when the thread uses `historyMode = "server"`. |

`InputPart` is a tagged union:

- `{ "type": "text", "text": "..." }` — plain text input.
- `{ "type": "image", "url": "https://..." }` — remote image URL.
- `{ "type": "localImage", "path": "/tmp/screenshot.png" }` — local image file path.

`SenderContext`:

```json
{
  "senderId": "user-456",
  "senderName": "Alice",
  "senderRole": "admin",
  "groupId": "group-123"
}
```

The server records two separate provenance fields:

- `thread.originChannel`: the channel that originally created the thread.
- `turn.originChannel`: the channel that initiated this specific turn.

Each persisted Turn also records an `initiator` object with durable actor metadata (`channelName`, `userId`, `userName`, `userRole`, `channelContext`, `groupId`) so cross-channel replay and auditing remain accurate after resume.

**Result**:

```json
{
  "turn": {
    "id": "turn_001",
    "threadId": "thread_20260316_x7k2m4",
    "status": "running",
    "items": [],
    "startedAt": "2026-03-16T10:05:00Z"
  }
}
```

**Example**:

```json
{ "jsonrpc": "2.0", "method": "turn/start", "id": 10, "params": {
    "threadId": "thread_20260316_x7k2m4",
    "input": [
      { "type": "text", "text": "Run the tests and fix any failures" }
    ]
} }

{ "jsonrpc": "2.0", "id": 10, "result": {
    "turn": {
      "id": "turn_001",
      "threadId": "thread_20260316_x7k2m4",
      "status": "running",
      "items": [],
      "startedAt": "2026-03-16T10:05:00Z"
    }
} }
```

### 5.2 `turn/interrupt`

Request cancellation of an in-progress turn. The server cancels the agent execution via `CancellationToken` and emits `turn/cancelled` once shutdown completes.

**Direction**: client → server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `threadId` | string | yes | Thread ID. |
| `turnId` | string | yes | Turn ID to cancel. |

**Result**: `{}`

The actual cancellation is asynchronous. Rely on the `turn/cancelled` notification to know when the turn has stopped.

---

## 6. Event Notifications

Event notifications are server-initiated messages (no `id`) that stream the turn lifecycle to the client. They correspond 1:1 to the `SessionEvent` types defined in the [Session Core Specification, Section 6](session-core.md#6-event-model).

All notifications share the pattern:

```json
{ "jsonrpc": "2.0", "method": "<event-method>", "params": { ... } }
```

### 6.1 Thread Notifications

#### `thread/started`

Emitted when a new thread is created via `thread/start`.

**Params**: `{ "thread": Thread }`

#### `thread/resumed`

Emitted when a thread is resumed via `thread/resume`.

**Params**: `{ "thread": Thread, "resumedBy": "<channelName>" }`

#### `thread/statusChanged`

Emitted when a thread's status changes (Active → Paused, Active → Archived, etc.).

**Params**: `{ "threadId": "<id>", "previousStatus": "<status>", "newStatus": "<status>" }`

### 6.2 Turn Notifications

#### `turn/started`

Emitted when a turn begins execution (after `turn/start` response).

**Params**: `{ "turn": Turn }`

The `turn` object includes the `UserMessage` input item.

#### `turn/completed`

Emitted when a turn finishes successfully.

**Params**:

```json
{
  "turn": {
    "id": "turn_001",
    "threadId": "thread_...",
    "status": "completed",
    "items": [ ... ],
    "startedAt": "2026-03-16T10:05:00Z",
    "completedAt": "2026-03-16T10:07:30Z",
    "tokenUsage": {
      "inputTokens": 1200,
      "outputTokens": 800,
      "totalTokens": 2000
    }
  }
}
```

#### `turn/failed`

Emitted when a turn fails due to an unrecoverable error.

**Params**: `{ "turn": Turn, "error": "<message>" }`

The `turn.status` is `"failed"` and `turn.error` contains the error description.

#### `turn/cancelled`

Emitted when a turn is cancelled via `turn/interrupt` or client disconnect.

**Params**: `{ "turn": Turn, "reason": "<description>" }`

### 6.3 Item Notifications

Items follow the lifecycle: `item/started` → zero or more `item/*/delta` → `item/completed`. See [Session Core, Section 5.3](session-core.md#53-item-lifecycle).

#### `item/started`

Emitted when a new item is created within a turn.

**Params**:

```json
{
  "threadId": "thread_...",
  "turnId": "turn_001",
  "item": {
    "id": "item_002",
    "turnId": "turn_001",
    "type": "toolCall",
    "status": "started",
    "payload": {
      "toolName": "Exec",
      "arguments": { "command": "npm test" },
      "callId": "call_001"
    },
    "createdAt": "2026-03-16T10:05:12Z"
  }
}
```

The canonical item payload schemas are defined in [Session Core, Section 4.2](session-core.md#42-item-payload-schemas). On the wire, clients should treat `item.type` as the discriminator and apply the following mapping rules:

| `item.type` | Wire-specific notes |
|-------------|---------------------|
| `userMessage` | Payload shape matches Session Core; property names are camelCase and nullable fields are omitted when absent. |
| `agentMessage` | Text deltas stream through `item/agentMessage/delta`; snapshots still use the canonical payload schema. |
| `reasoningContent` | Reasoning deltas stream through `item/reasoningContent/delta`; snapshots still use the canonical payload schema. |
| `toolCall` | Tool invocation payload uses camelCase fields such as `toolName`, `arguments`, and `callId`. |
| `toolResult` | Result payload uses the canonical fields; transport serialization preserves nested JSON values losslessly. |
| `approvalRequest` | Approval payload uses the canonical fields plus wire enum/string serialization rules from this spec. |
| `approvalResponse` | Response payload uses the canonical fields; decision values are serialized as wire strings. |
| `error` | Error payload uses the canonical fields; transport-level JSON-RPC errors remain separate from item-level error items. |

#### `item/agentMessage/delta`

Streamed text delta for an `agentMessage` item. Concatenate `delta` values in order to reconstruct the full reply.

**Params**:

```json
{
  "threadId": "thread_...",
  "turnId": "turn_001",
  "itemId": "item_004",
  "deltaKind": "agentMessage",
  "delta": "Here is my analysis of the"
}
```

#### `item/reasoning/delta`

Streamed text delta for a `reasoningContent` item.

**Params**:

```json
{
  "threadId": "thread_...",
  "turnId": "turn_001",
  "itemId": "item_003",
  "deltaKind": "reasoningContent",
  "delta": "I need to check the test output first"
}
```

#### `item/completed`

Emitted when an item is finalized. The `item.status` is `"completed"` and the payload contains the final accumulated value.

**Params**:

```json
{
  "threadId": "thread_...",
  "turnId": "turn_001",
  "item": {
    "id": "item_004",
    "turnId": "turn_001",
    "type": "agentMessage",
    "status": "completed",
    "payload": {
      "text": "Here is my analysis of the test failures..."
    },
    "createdAt": "2026-03-16T10:05:30Z",
    "completedAt": "2026-03-16T10:06:15Z"
  }
}
```

### 6.4 Approval Notifications

#### `item/approval/resolved`

Emitted after the client responds to an approval request and the server processes the decision. This is distinct from `item/completed` for the `approvalResponse` item — `item/approval/resolved` is emitted first, then the regular `item/completed` follows.

**Params**:

```json
{
  "threadId": "thread_...",
  "turnId": "turn_001",
  "item": {
    "id": "item_006",
    "type": "approvalResponse",
    "status": "completed",
    "payload": {
      "requestId": "approval_001",
      "approved": true
    }
  }
}
```

### 6.5 SubAgent Notifications

#### `subagent/progress`

Emitted periodically (~200ms) when one or more SubAgent tool calls (`SpawnSubagent`) are active during a Turn. Each notification carries a **complete snapshot** of all tracked SubAgents' progress, allowing clients to replace their local state on each receipt.

This notification is a sideband signal — it may interleave with `item/*` and `turn/*` notifications. Clients should use it to update SubAgent progress displays (e.g., Live Tables showing per-SubAgent activity and token consumption).

**Params**:

```json
{
  "threadId": "thread_...",
  "turnId": "turn_001",
  "entries": [
    {
      "label": "code-explorer",
      "currentTool": "ReadFile",
      "inputTokens": 4500,
      "outputTokens": 1200,
      "isCompleted": false
    },
    {
      "label": "test-runner",
      "currentTool": null,
      "inputTokens": 2000,
      "outputTokens": 600,
      "isCompleted": true
    }
  ]
}
```

| Field | Type | Description |
|-------|------|-------------|
| `threadId` | string | Parent thread. |
| `turnId` | string | Active turn. |
| `entries` | SubAgentEntry[] | Snapshot of all tracked SubAgents. |

`SubAgentEntry` fields:

| Field | Type | Description |
|-------|------|-------------|
| `label` | string | SubAgent identifier/label (matches the `label` argument passed to `SpawnSubagent`). |
| `currentTool` | string? | Name of the tool the SubAgent is currently executing. `null` when the SubAgent is thinking (waiting for model response). |
| `inputTokens` | integer | Cumulative input token consumption. |
| `outputTokens` | integer | Cumulative output token consumption. |
| `isCompleted` | boolean | Whether the SubAgent has finished execution. |

**Emission rules**:

- The server emits this notification at ~200ms intervals while SubAgents are active. The exact interval is an implementation detail and may vary.
- Each notification contains the **complete set** of tracked SubAgents for the current Turn — not incremental deltas.
- The server stops emitting once all tracked SubAgents have completed and a final snapshot with all `isCompleted = true` has been sent.
- Clients that do not need SubAgent progress can opt out via `optOutNotificationMethods: ["subagent/progress"]` during `initialize`.

**Example sequence**:

```
Server                                          Client
  |                                               |
  | item/started (notification)                   |
  |  item: { type: "toolCall",                    |
  |    toolName: "SpawnSubagent",                 |
  |    arguments: { label: "code-explorer" } }    |
  |---------------------------------------------->|
  |                                               |
  | subagent/progress (notification)              |
  |  entries: [{ label: "code-explorer",          |
  |    currentTool: "ReadFile",                   |
  |    inputTokens: 1200, outputTokens: 300,      |
  |    isCompleted: false }]                      |
  |<----------------------------------------------|
  |                                               |
  | subagent/progress (notification)  (~200ms)    |
  |  entries: [{ label: "code-explorer",          |
  |    currentTool: "SearchContent",              |
  |    inputTokens: 3500, outputTokens: 900,      |
  |    isCompleted: false }]                      |
  |<----------------------------------------------|
  |                                               |
  | subagent/progress (notification)              |
  |  entries: [{ label: "code-explorer",          |
  |    currentTool: null,                         |
  |    inputTokens: 4500, outputTokens: 1200,     |
  |    isCompleted: true }]                       |
  |<----------------------------------------------|
  |                                               |
  | item/completed (notification)                 |
  |  item: { type: "toolResult",                  |
  |    callId: "...", success: true }             |
  |---------------------------------------------->|
```

### 6.6 Usage Notifications

#### `item/usage/delta`

Emitted each time the agent completes an LLM iteration and produces a `UsageContent` with non-zero token counts. Carries the **incremental** token consumption for that single iteration, allowing clients to maintain a running total for real-time display (e.g., Thinking/Tool spinner token counters).

**Params**:

```json
{
  "threadId": "thread_...",
  "turnId": "turn_001",
  "inputTokens": 1200,
  "outputTokens": 350
}
```

| Field | Type | Description |
|-------|------|-------------|
| `threadId` | string | Parent thread. |
| `turnId` | string | Active turn. |
| `inputTokens` | integer | Input tokens consumed in this LLM iteration (delta, not cumulative). |
| `outputTokens` | integer | Output tokens consumed in this LLM iteration (delta, not cumulative). |

**Emission rules**:

- Emitted once per LLM iteration, immediately after the provider's `UsageContent` is processed.
- Each notification carries only the delta for the current iteration. Clients must accumulate deltas locally.
- The sum of all `item/usage/delta` notifications for a Turn's main agent equals the main-agent portion of `turn/completed.tokenUsage`.
- SubAgent tokens are reported separately via `subagent/progress` and are not included in `item/usage/delta`.
- Clients that do not need real-time token display can opt out via `optOutNotificationMethods: ["item/usage/delta"]` during `initialize`.

**Example sequence**:

```
Server                                          Client
  |                                               |
  | item/usage/delta (notification)               |
  |  inputTokens: 1200, outputTokens: 350         |
  |<----------------------------------------------|
  |                                               |
  | (tool calls execute...)                       |
  |                                               |
  | item/usage/delta (notification)               |
  |  inputTokens: 2100, outputTokens: 480         |
  |<----------------------------------------------|
  |                                               |
  | turn/completed (notification)                 |
  |  tokenUsage: { inputTokens: 3300,             |
  |    outputTokens: 830, totalTokens: 4130 }     |
  |<----------------------------------------------|
```

### 6.7 System Notifications

#### `system/event`

Emitted when a system-level maintenance operation occurs during a Turn's post-processing phase. These operations (context compaction, memory consolidation) are not part of the agent's conversational output but affect the session's internal state.

**Params**:

```json
{
  "threadId": "thread_...",
  "turnId": "turn_001",
  "kind": "compacting",
  "message": "Context token limit reached, compacting conversation..."
}
```

| Field | Type | Description |
|-------|------|-------------|
| `threadId` | string | Parent thread. |
| `turnId` | string | Active turn. |
| `kind` | string | Event kind. One of: `"compacting"`, `"compacted"`, `"compactSkipped"`, `"consolidating"`, `"consolidated"`. |
| `message` | string? | Human-readable description. May be null. |

**Defined `kind` values**:

| Kind | Meaning |
|------|---------|
| `compacting` | Context compaction is starting. |
| `compacted` | Context compaction completed successfully. |
| `compactSkipped` | Context compaction was skipped (insufficient history). |
| `consolidating` | Memory consolidation is starting. |
| `consolidated` | Memory consolidation completed successfully. |

**Emission rules**:

- System events are emitted during the Turn's post-processing phase, before `turn/completed`.
- Compaction events are synchronous pairs: `compacting` → `compacted` or `compactSkipped`.
- Consolidation events bracket an async operation: `consolidating` → (await) → `consolidated`.
- Clients that do not need system maintenance status can opt out via `optOutNotificationMethods: ["system/event"]` during `initialize`.

**Example sequence**:

```
Server                                          Client
  |                                               |
  | system/event (notification)                   |
  |  kind: "compacting",                          |
  |  message: "Context token limit reached..."    |
  |<----------------------------------------------|
  |                                               |
  | system/event (notification)                   |
  |  kind: "compacted",                           |
  |  message: "Context compacted successfully."   |
  |<----------------------------------------------|
  |                                               |
  | system/event (notification)                   |
  |  kind: "consolidating",                       |
  |  message: "Consolidating memory..."           |
  |<----------------------------------------------|
  |                                               |
  | system/event (notification)                   |
  |  kind: "consolidated",                        |
  |  message: "Memory consolidation complete."    |
  |<----------------------------------------------|
  |                                               |
  | turn/completed (notification)                 |
  |  turn: { ... }                                |
  |<----------------------------------------------|
```

### 6.8 Plan Notifications

#### `plan/updated`

Emitted when the agent creates or updates a structured plan via the `CreatePlan`, `UpdateTodos`, or `TodoWrite` tools. The notification carries the complete plan snapshot, allowing the client to render a Todolist progress panel.

This notification is independent of the Turn event stream — it is sent directly by the server host when the `onPlanUpdated` callback fires in `AgentFactory`. Clients that do not need plan progress display can opt out via `optOutNotificationMethods: ["plan/updated"]` during `initialize`.

**Params**:

```json
{
  "title": "Implement user authentication",
  "overview": "Add JWT-based auth with login and registration endpoints",
  "todos": [
    {
      "id": "setup-models",
      "content": "Create User model and migration",
      "priority": "high",
      "status": "completed"
    },
    {
      "id": "auth-endpoints",
      "content": "Implement login and register API endpoints",
      "priority": "high",
      "status": "in_progress"
    },
    {
      "id": "jwt-middleware",
      "content": "Add JWT validation middleware",
      "priority": "medium",
      "status": "pending"
    }
  ]
}
```

| Field | Type | Description |
|-------|------|-------------|
| `title` | string | Plan title. |
| `overview` | string | Brief plan overview/description. May be empty. |
| `todos` | PlanTodo[] | Complete list of plan tasks. |

`PlanTodo` fields:

| Field | Type | Description |
|-------|------|-------------|
| `id` | string | Short kebab-case task identifier. |
| `content` | string | Human-readable task description. |
| `priority` | string | One of: `"high"`, `"medium"`, `"low"`. |
| `status` | string | One of: `"pending"`, `"in_progress"`, `"completed"`, `"cancelled"`. |

**Emission rules**:

- Emitted each time any plan tool (`CreatePlan`, `UpdateTodos`, `TodoWrite`) completes successfully.
- Each notification carries the **complete plan snapshot** — not incremental deltas. Clients should replace their local plan state on each receipt.
- The notification is sent outside the `SessionEvent` stream; it is a direct JSON-RPC notification from the host to all connected transports.
- Clients that do not need plan progress can opt out via `optOutNotificationMethods: ["plan/updated"]` during `initialize`.

---

## 7. Approval Flow

When the agent encounters a sensitive operation (file write, shell command) that requires user consent, the server initiates a bidirectional approval exchange. This is a **server-to-client request** — the server sends a JSON-RPC request with an `id`, and the client must respond.

### 7.1 Sequence

```
Server                              Client
  |                                   |
  | item/started (notification)       |
  |   type: "approvalRequest"         |
  |---------------------------------->|
  |                                   |
  | item/approval/request (request)   |
  |   id: <server-assigned>           |
  |---------------------------------->|
  |                                   |
  |   (client shows approval UI)      |
  |                                   |
  | response (id: <same>)             |
  |   result: { decision: "..." }     |
  |<----------------------------------|
  |                                   |
  | item/approval/resolved (notify)   |
  |---------------------------------->|
  |                                   |
  | item/completed (notification)     |
  |   (for the tool call item)        |
  |---------------------------------->|
```

The turn enters `"waitingApproval"` status while the server waits for the client's response.

### 7.2 `item/approval/request`

**Direction**: server → client (request)

**Params**:

| Field | Type | Description |
|-------|------|-------------|
| `threadId` | string | Parent thread. |
| `turnId` | string | Active turn. |
| `itemId` | string | The `approvalRequest` item ID. |
| `requestId` | string | Unique correlation ID for this approval. |
| `approvalType` | string | `"shell"` or `"file"`. |
| `operation` | string | For shell: the command. For file: `"read"`, `"write"`, `"edit"`, `"list"`. |
| `target` | string | For shell: working directory. For file: the file path. |
| `scopeKey` | string | Session-scoped cache key used when the client returns `acceptForSession`. In v1, DotCraft Core uses coarse scopes such as `file:write` and `shell:*`. |
| `reason` | string | Human-readable explanation of why approval is needed. |

**Example**:

```json
{ "jsonrpc": "2.0", "method": "item/approval/request", "id": 100, "params": {
    "threadId": "thread_20260316_x7k2m4",
    "turnId": "turn_001",
    "itemId": "item_005",
    "requestId": "approval_001",
    "approvalType": "shell",
    "operation": "npm test",
    "target": "/home/dev/myproject",
    "scopeKey": "shell:*",
    "reason": "Agent wants to execute a shell command"
} }
```

### 7.3 Client Response

The client responds with the standard JSON-RPC response format:

```json
{ "jsonrpc": "2.0", "id": 100, "result": {
    "decision": "accept"
} }
```

**Decision values**:

| Value | Meaning |
|-------|---------|
| `"accept"` | Approve this single operation. |
| `"acceptForSession"` | Approve this operation and similar operations for the remainder of the thread's lifetime. |
| `"acceptAlways"` | Approve this operation permanently. The server persists the approval so future sessions do not prompt again. Also suppresses further prompts for the current session. |
| `"decline"` | Reject the operation. The agent receives a rejection signal and may try an alternative approach. |
| `"cancel"` | Reject and cancel the entire turn. Equivalent to `turn/interrupt`. |

When approval resolution is persisted or echoed back in a later event, the response item carries both:

- `approved`: boolean convenience field for legacy consumers.
- `decision`: the exact rich decision value chosen by the user.

### 7.4 Clients Without Approval Support

If a client declared `capabilities.approvalSupport = false` during initialization, the server must not send `item/approval/request`. Instead, the server applies the workspace's default approval policy (auto-approve or auto-reject based on configuration).

---

## 8. Error Handling

### 8.1 JSON-RPC Error Response

Errors follow the standard JSON-RPC 2.0 error response format:

```json
{
  "jsonrpc": "2.0",
  "id": 10,
  "error": {
    "code": -32600,
    "message": "Invalid request",
    "data": { "detail": "Thread not found: thread_invalid" }
  }
}
```

### 8.2 Standard Error Codes

| Code | Name | When |
|------|------|------|
| `-32700` | Parse error | Malformed JSON. |
| `-32600` | Invalid request | Missing required fields, invalid params, or constraint violation. |
| `-32601` | Method not found | Unknown method name. |
| `-32602` | Invalid params | Params present but do not match the expected schema. |
| `-32603` | Internal error | Unexpected server failure. |

### 8.3 DotCraft-Specific Error Codes

| Code | Name | When |
|------|------|------|
| `-32001` | Server overloaded | Backpressure: too many in-flight requests. Retryable. |
| `-32002` | Not initialized | Method called before `initialize` handshake. |
| `-32003` | Already initialized | `initialize` called more than once on the same connection. |
| `-32010` | Thread not found | The specified `threadId` does not exist. |
| `-32011` | Thread not active | Operation requires an active thread but the thread is paused or archived. |
| `-32012` | Turn in progress | A turn is already running or waiting for approval on this thread. |
| `-32013` | Turn not found | The specified `turnId` does not exist on the thread. |
| `-32014` | Turn not running | `turn/interrupt` called on a turn that is not in progress. |
| `-32020` | Approval timeout | The client took too long to respond to an approval request. |
| `-32030` | Channel rejected | The channel adapter name is not registered in server configuration. |
| `-32031` | Cron job not found | The specified cron job ID does not exist. |

### 8.4 Turn-Level Errors

Errors during agent execution are delivered as `turn/failed` notifications (not as JSON-RPC error responses to the `turn/start` request, because the request itself succeeded — it is the asynchronous agent run that failed).

The `turn/failed` notification includes the error in `turn.error`:

```json
{ "jsonrpc": "2.0", "method": "turn/failed", "params": {
    "turn": {
      "id": "turn_001",
      "threadId": "thread_...",
      "status": "failed",
      "error": "Model returned an error: context window exceeded"
    }
} }
```

If an `Error` item was created during the turn, it appears in the `items` array and is also emitted via `item/started` / `item/completed` before the `turn/failed` notification.

---

## 9. Backpressure

### 9.1 Server-Side Queuing

The server uses bounded internal queues between transport ingress, request processing, and outbound writes. When the inbound queue is saturated:

- New requests are rejected with error code `-32001` and message `"Server overloaded; retry later."`.
- Clients should treat this as retryable and use **exponential backoff with jitter**.

### 9.2 Client-Side Considerations

- Clients should not send a `turn/start` while a turn is already in progress on the same thread. The server rejects this with error code `-32012`.
- Clients should consume notifications promptly. If a client falls behind on reading stdout (stdio transport) or WebSocket frames, the server may buffer up to a limit and then drop the connection.

### 6.9 Job Result Notifications

#### `system/jobResult`

Emitted by the AppServer after a server-managed cron or heartbeat job completes. This allows connected wire clients (e.g. the CLI) to receive the agent's response as an out-of-band notification, without the client initiating a turn.

This notification is **only emitted in standalone AppServer mode** (the CLI subprocess/WebSocket scenario). In Gateway mode the result is delivered through the social channel that originally created the job (e.g. `MessageRouter.DeliverAsync` → `IChannelService.DeliverMessageAsync` → `ext/channel/deliver` for external channel adapters). The delivery channel is determined by `CronPayload.Channel` captured at job creation time from `ChannelSessionScope`.

Clients can opt out via `optOutNotificationMethods: ["system/jobResult"]` during `initialize`.

**Params**:

```json
{
  "source": "cron",
  "jobId": "9c933b01",
  "jobName": "喝水提醒",
  "threadId": "thread_abc123",
  "result": "提醒：该喝水了！保持水分对健康很重要。",
  "error": null,
  "tokenUsage": { "inputTokens": 420, "outputTokens": 38 }
}
```

| Field | Type | Description |
|-------|------|-------------|
| `source` | string | `"cron"` or `"heartbeat"`. |
| `jobId` | string? | Cron job ID. Present when `source` is `"cron"`; absent for heartbeat. |
| `jobName` | string? | Human-readable job name. |
| `threadId` | string? | The thread ID used for execution. |
| `result` | string? | Agent's text response. Null if the turn failed or produced no text output. |
| `error` | string? | Error message if the turn failed. |
| `tokenUsage` | object? | `{ inputTokens, outputTokens }`. |

**Targeting rules**:

- Emitted via the broadcast mechanism (`_activeTransports`) used by `plan/updated`. In stdio mode there is exactly one connected client; in WebSocket mode all initialized clients receive it.
- Only emitted when the job's `CronPayload.Channel` is `"cli"` or null (i.e. no social channel delivery target). Jobs created from QQ, WeCom, or ExternalChannel adapters deliver their result through the respective channel's delivery mechanism and do **not** emit `system/jobResult`.
- Clients that do not wish to display cron/heartbeat results can opt out via `optOutNotificationMethods: ["system/jobResult"]`.

**Example sequence**:

```
Server                                         Client
  |                                               |
  | (60 s after job was scheduled)                |
  |                                               |
  | [CronService timer fires, AgentRunner runs]   |
  |                                               |
  | system/jobResult (notification)               |
  |  source: "cron",                              |
  |  jobId: "9c933b01",                           |
  |  jobName: "喝水提醒",                         |
  |  result: "该喝水了！"                          |
  |<----------------------------------------------|
```

---

## 10. Notification Opt-Out

Clients can suppress specific notification methods per connection by listing exact method names in `initialize.params.capabilities.optOutNotificationMethods`.

- Matching is **exact** — no wildcards or prefix matching.
- Unknown method names are accepted and silently ignored.
- Applies only to server-to-client notifications, not to requests or responses.
- Opt-out is negotiated once at initialization time and cannot be changed for the connection's lifetime.

**Common opt-out targets**:

| Method | When to opt out |
|--------|-----------------|
| `item/agentMessage/delta` | Client does not support streaming; will wait for `item/completed`. |
| `item/reasoning/delta` | Client does not display reasoning content. |
| `thread/started` | Client does not need thread lifecycle events. |
| `thread/statusChanged` | Client manages thread status locally. |
| `subagent/progress` | Client does not display SubAgent real-time progress. |
| `item/usage/delta` | Client does not need real-time token consumption display; will use `turn/completed.tokenUsage` for final totals. |
| `system/event` | Client does not need system maintenance status (compaction, consolidation). |
| `plan/updated` | Client does not need real-time plan/todo progress display. |
| `system/jobResult` | Client does not need cron/heartbeat result notifications (e.g. batch or headless client). |

**Example**:

```json
{
  "clientInfo": { "name": "batch-runner", "version": "1.0.0" },
  "capabilities": {
    "streamingSupport": false,
    "optOutNotificationMethods": [
      "item/agentMessage/delta",
      "item/reasoning/delta"
    ]
  }
}
```

---

## 11. Extension Methods

The core wire protocol (Sections 3–10) covers the `ISessionService` surface. Channels or integrations that need additional capabilities can expose **extension methods** under a channel-specific namespace.

### 11.1 Design Rules

- Extension methods must use a namespace prefix that does not collide with core methods: `ext/<namespace>/...` (e.g., `ext/acp/fs/readFile`, `ext/acp/terminal/create`).
- Extension methods are not part of the core protocol and not covered by the versioning guarantee in Section 12.
- In v1, the `initialize` response may advertise available extensions only as a flat namespace array in `serverInfo.extensions`.
- A richer structured extension registry is deferred from v1; clients must not require per-extension schema metadata to use the core Session protocol.

### 11.2 ACP Tool Proxy (Reference Extension)

ACP currently exposes bidirectional tool proxy methods (`FsReadTextFile`, `TerminalCreate`, etc.) that allow the agent's tools to access the client's filesystem and terminals. Under the wire protocol, these become extension methods:

| ACP Method | Wire Extension Method |
|------------|----------------------|
| `FsReadTextFile` | `ext/acp/fs/readTextFile` |
| `FsWriteTextFile` | `ext/acp/fs/writeTextFile` |
| `TerminalCreate` | `ext/acp/terminal/create` |
| `TerminalGetOutput` | `ext/acp/terminal/getOutput` |
| `TerminalWaitForExit` | `ext/acp/terminal/waitForExit` |
| `TerminalKill` | `ext/acp/terminal/kill` |
| `TerminalRelease` | `ext/acp/terminal/release` |

This extension is not normative — it documents the intended migration path for ACP's current capabilities.

---

## 12. Versioning and Compatibility

### 12.1 Protocol Version

The protocol version is a single integer string (`"1"`, `"2"`, etc.) returned in `initialize` as `serverInfo.protocolVersion`.

### 12.2 Compatibility Rules

- **Within a major version**: The server may add new optional fields to existing method params/results, add new notification methods, and add new error codes. Clients must ignore unknown fields and unknown notification methods.
- **Breaking changes** (removing fields, changing semantics, removing methods) require incrementing the protocol version.
- **Method additions**: New methods may be added within a major version. Clients that call an unknown method receive a `-32601` error and can fall back gracefully.

### 12.3 Negotiation

The client and server agree on the protocol version during `initialize`. If the server's `protocolVersion` is higher than what the client supports, the client should log a warning and proceed with best-effort compatibility (ignoring unknown fields and methods). If the server's version is lower, the client should restrict itself to the server's supported surface.

---

## 13. Full Turn Example

This section shows the complete message sequence for a turn where the agent reads a file, runs a test (requiring approval), and responds.

```
Client                                          Server
  |                                               |
  | turn/start (request, id: 10)                  |
  |  threadId, input: "Run tests and fix"         |
  |---------------------------------------------->|
  |                                               |
  | (response, id: 10)                            |
  |  turn: { id: "turn_001", status: "running" }  |
  |<----------------------------------------------|
  |                                               |
  | turn/started (notification)                   |
  |  turn: { id: "turn_001", ... }                |
  |<----------------------------------------------|
  |                                               |
  | item/started (notification)                   |
  |  item: { type: "userMessage", text: "..." }   |
  |<----------------------------------------------|
  |                                               |
  | item/completed (notification)                 |
  |  item: { type: "userMessage", ... }           |
  |<----------------------------------------------|
  |                                               |
  | item/started (notification)                   |
  |  item: { type: "toolCall",                    |
  |    toolName: "ReadFile", callId: "c1" }       |
  |<----------------------------------------------|
  |                                               |
  | item/completed (notification)                 |
  |  item: { type: "toolResult",                  |
  |    callId: "c1", success: true }              |
  |<----------------------------------------------|
  |                                               |
  | item/usage/delta (notification)               |
  |  inputTokens: 1200, outputTokens: 350         |
  |<----------------------------------------------|
  |                                               |
  | item/started (notification)                   |
  |  item: { type: "approvalRequest",             |
  |    approvalType: "shell",                     |
  |    operation: "npm test" }                    |
  |<----------------------------------------------|
  |                                               |
  | item/approval/request (request, id: 100)      |
  |  requestId: "approval_001",                   |
  |  approvalType: "shell",                       |
  |  operation: "npm test"                        |
  |<----------------------------------------------|
  |                                               |
  | (response, id: 100)                           |
  |  decision: "accept"                           |
  |---------------------------------------------->|
  |                                               |
  | item/approval/resolved (notification)         |
  |  requestId: "approval_001", approved: true    |
  |<----------------------------------------------|
  |                                               |
  | item/started (notification)                   |
  |  item: { type: "toolCall",                    |
  |    toolName: "Exec", callId: "c2" }           |
  |<----------------------------------------------|
  |                                               |
  | item/completed (notification)                 |
  |  item: { type: "toolResult",                  |
  |    callId: "c2", success: true }              |
  |<----------------------------------------------|
  |                                               |
  | item/started (notification)                   |
  |  item: { type: "toolCall",                    |
  |    toolName: "SpawnSubagent",                 |
  |    arguments: { label: "analyzer" } }         |
  |<----------------------------------------------|
  |                                               |
  | subagent/progress (notification)              |
  |  entries: [{ label: "analyzer",               |
  |    currentTool: "ReadFile", ... }]            |
  |<----------------------------------------------|
  |                                               |
  | subagent/progress (notification)  (~200ms)    |
  |  entries: [{ label: "analyzer",               |
  |    isCompleted: true, ... }]                  |
  |<----------------------------------------------|
  |                                               |
  | item/completed (notification)                 |
  |  item: { type: "toolResult",                  |
  |    callId: "c3", success: true }              |
  |<----------------------------------------------|
  |                                               |
  | item/started (notification)                   |
  |  item: { type: "agentMessage" }               |
  |<----------------------------------------------|
  |                                               |
  | item/agentMessage/delta (notification) x N    |
  |  delta: "I found 2 failing tests..."          |
  |<----------------------------------------------|
  |                                               |
  | item/completed (notification)                 |
  |  item: { type: "agentMessage",                |
  |    text: "I found 2 failing tests..." }       |
  |<----------------------------------------------|
  |                                               |
  | turn/completed (notification)                 |
  |  turn: { status: "completed",                 |
  |    tokenUsage: { ... }, items: [...] }        |
  |<----------------------------------------------|
```

---

## 14. Relationship to Codex App Server

This protocol is modeled after Codex App Server. The following table summarizes key differences:

| Aspect | Codex App Server | DotCraft Wire Protocol |
|--------|------------------|------------------------|
| JSON-RPC header | Omitted (`"jsonrpc":"2.0"` not sent) | Included (standard JSON-RPC 2.0 compliance) |
| Primary transport | stdio JSONL | stdio JSONL |
| Optional transport | WebSocket (experimental) | WebSocket (experimental) |
| Thread primitives | `thread/start`, `resume`, `fork`, `list`, `read`, `archive`, `unarchive` | `thread/start`, `resume`, `list`, `read`, `pause`, `archive`, `delete` |
| Turn primitives | `turn/start`, `turn/steer`, `turn/interrupt` | `turn/start`, `turn/interrupt` |
| Item types | `userMessage`, `agentMessage`, `reasoning`, `commandExecution`, `fileChange`, `mcpToolCall`, etc. | `userMessage`, `agentMessage`, `reasoningContent`, `toolCall`, `toolResult`, `approvalRequest`, `approvalResponse`, `error` |
| Approval model | Per-item-type requests (`commandExecution/requestApproval`, `fileChange/requestApproval`) | Unified `item/approval/request` with `approvalType` discriminator |
| Turn failure | Encoded in `turn/completed` with `status: "failed"` | Separate `turn/failed` notification |
| Turn cancel | Encoded in `turn/completed` with `status: "interrupted"` | Separate `turn/cancelled` notification |
| Auth | Built-in (`account/login`, ChatGPT OAuth) | Outside wire protocol scope; handled by bearer token or channel auth |
| Config | `config/read`, `config/value/write` | `thread/config/update`, `thread/mode/set` |
| Review | `review/start`, `enteredReviewMode`, `exitedReviewMode` | Not in v1 (future extension) |
| Skills/Apps | `skills/list`, `app/list`, `plugin/list` | Not in v1 core; extension surface |
| Command exec | `command/exec` (standalone sandbox execution) | Not in v1 core; ACP extension surface |
| Filesystem | `fs/readFile`, `fs/writeFile`, etc. | Not in v1 core; ACP extension surface |
| Extension model | Experimental API opt-in via `capabilities.experimentalApi` | `ext/<namespace>/...` method namespace |

### 14.1 Design Rationale for Key Differences

**Unified approval request**: Codex uses separate request methods per item type (`commandExecution/requestApproval`, `fileChange/requestApproval`). DotCraft uses a single `item/approval/request` with an `approvalType` discriminator. This matches the Session Protocol's unified `ApprovalRequest` Item type and reduces the number of methods clients need to implement.

**Separate failure/cancel notifications**: Codex encodes turn failure and interruption as `turn/completed` with different status values. DotCraft uses distinct `turn/failed` and `turn/cancelled` notifications. This matches the Session Protocol's `SessionEventType` enum, which has separate `TurnFailed` and `TurnCancelled` event types, making it easier for clients to switch on the notification method without inspecting the payload.

**No `turn/steer`**: Codex supports `turn/steer` to inject additional user input into a running turn. DotCraft's Session Protocol does not currently model mid-turn user input injection. This may be added as a future extension.

**No `thread/fork`**: Codex supports forking a thread into a new branch. DotCraft's Session Protocol does not currently model thread forking. This may be added as a future extension.

---

## 15. WebSocket Transport

### 15.1 Overview

The WebSocket transport is a network-accessible alternative to the stdio transport. It is the primary transport for external channel adapters (see the [External Channel Adapter Specification](external-channel-adapter.md)) and for any client that cannot be co-located with the server process.

Both transports use identical JSON-RPC 2.0 message shapes. The only differences are at the framing and connection-lifecycle layers described in this section.

| Property | stdio | WebSocket |
|----------|-------|-----------|
| Connection model | 1:1 (one client per server process) | N:1 (multiple concurrent clients per server process) |
| Frame format | Newline-delimited JSON (JSONL) | One JSON-RPC message per WebSocket text frame (UTF-8) |
| Client lifecycle | Bounded to process lifetime | Independent per-connection |
| Authentication | Not applicable (process isolation) | Optional bearer token (see §15.4) |
| Health probes | Not applicable | HTTP `GET /healthz` and `GET /readyz` |

### 15.2 Endpoint

The server listens on a configurable host and port. The WebSocket upgrade endpoint is:

```
ws://HOST:PORT/ws
```

The same HTTP server also serves the health probe endpoints:

- `GET /healthz` — returns `200 OK` with body `{"status":"ok"}` when the server process is alive.
- `GET /readyz` — returns `200 OK` when the server has completed startup and is ready to accept connections.

The default listen address binds to `127.0.0.1` only. Binding to `0.0.0.0` or a public interface must be explicitly configured and requires authentication to be enabled (see §15.4).

### 15.3 Connection Lifecycle

Each WebSocket connection is fully independent:

1. Client opens a WebSocket connection to `ws://HOST:PORT/ws` (with optional `?token=` query parameter, see §15.4).
2. Server accepts the connection and creates a new `AppServerConnection` state object. At this point the connection is **unauthenticated and uninitialized**.
3. Client sends `initialize` as the first JSON-RPC message (same as stdio, see §3.1).
4. Server responds and the standard initialization handshake proceeds.
5. Normal protocol operation: client sends requests, server sends responses and notifications.
6. On connection close (either side), the server cancels all active thread subscriptions for that connection.

```
Client                                    Server
  |                                         |
  | WebSocket upgrade (GET /ws)             |
  |---------------------------------------->|
  |                                         |
  | 101 Switching Protocols                 |
  |<----------------------------------------|
  |                                         |
  | initialize (request, id: 0)             |
  |---------------------------------------->|
  |                                         |
  | (response, id: 0)                       |
  |<----------------------------------------|
  |                                         |
  | initialized (notification)              |
  |---------------------------------------->|
  |                                         |
  | (protocol ready)                        |
```

### 15.4 Authentication

When the server is configured with a bearer token, the token must be provided by the client in the WebSocket upgrade request URL:

```
ws://HOST:PORT/ws?token=<token>
```

The server validates the token before completing the WebSocket upgrade. If the token is missing or invalid, the server closes the connection with HTTP `401 Unauthorized` before the WebSocket handshake completes. The client never reaches the JSON-RPC `initialize` step.

Token validation rules:

- Tokens are compared using constant-time equality to resist timing attacks.
- An empty string is not a valid token. If the server is configured with an empty token, authentication is disabled.
- Token values must be URL-safe (alphanumeric plus `-`, `_`, `.`). Tokens that do not meet this requirement must be URL-percent-encoded by the client.

When the server is bound to `127.0.0.1` only, authentication is optional. When the server is bound to a non-loopback address, authentication must be enabled — the server refuses to start without a token in this configuration.

### 15.5 Multi-Connection Behavior

Multiple clients may be connected simultaneously. Each connection has isolated state:

- Its own `initialize`/`initialized` handshake.
- Its own set of active thread subscriptions (registered via `thread/subscribe`).
- Its own backpressure gate (32 concurrent in-flight requests, same as stdio).

Shared state across all connections on the same server process:

- The `ISessionService` instance (and therefore thread persistence) is shared. A thread started by one connection is visible to other connections that look it up via `thread/list` or `thread/read`.
- A `thread/subscribe` from Connection A will receive notifications for events triggered by Connection B on the same thread.

There is no built-in per-connection identity isolation. Callers with different privilege levels must use separate server processes or implement identity enforcement in the `SessionIdentity` layer.

### 15.6 Framing

Each JSON-RPC message is sent as a single WebSocket **text frame** (opcode `0x1`). The message must be a complete, valid JSON object. Binary frames are not used.

Servers and clients must not split a single JSON-RPC message across multiple frames, and must not combine multiple JSON-RPC messages into a single frame.

Maximum message size is 4 MB by default. Messages exceeding this limit cause the connection to be closed with WebSocket close code `1009` (message too big).

### 15.7 Reconnection

The WebSocket transport does not provide built-in session resumption. When a client reconnects after a disconnect:

- The client must perform the full `initialize` / `initialized` handshake again.
- Active thread subscriptions are lost and must be re-registered via `thread/subscribe`.
- Any turn that was in progress when the disconnect occurred continues executing on the server. The client can re-subscribe to the thread to receive subsequent notifications, but events emitted during the disconnection period are not replayed unless `replayRecent = true` is used in `thread/subscribe`.
- Server-to-client approval requests (`item/approval/request`) that were in flight when the client disconnected will time out according to the approval timeout policy (error code `-32020`), and the turn will fail.

Clients should implement reconnection with **exponential backoff with jitter** starting at 1 second, capping at 30 seconds.

### 15.8 Native WebSocket Ping/Pong

The server sends native WebSocket ping frames every 30 seconds to detect stale connections. If a client does not respond with a pong frame within 10 seconds, the server closes the connection. Clients that use compliant WebSocket libraries will handle pong responses automatically.

### 15.9 Differences from Stdio

| Behavior | stdio | WebSocket |
|----------|-------|-----------|
| Connection count | One (process boundary) | Many (network connections) |
| Authentication | N/A | Optional token query param |
| Turn cancellation on disconnect | Turn is cancelled (process exit) | Turn continues; client must re-subscribe |
| Event replay on reconnect | N/A | Via `thread/subscribe replayRecent: true` |
| Approval request on disconnect | Turn cancelled (process exit) | Turn fails with `-32020` approval timeout |
| Diagnostic output | stderr | Not available on wire; use server logs |

---

## 16. Cron Management Methods

### 16.1 Scope

These methods extend the protocol beyond `ISessionService` to cover server-managed cron job lifecycle. The AppServer process owns a `CronService` that fires jobs on a timer — independently of any session or wire client. Cron management methods allow wire clients (e.g. the CLI) to inspect and mutate that service's in-memory state directly, so changes take effect immediately without relying on the client writing to disk and the server eventually reloading.

Unlike thread/turn methods, cron methods are not scoped to a session, thread, or channel identity. They operate on the server's shared `CronService` singleton. All connections on the same server process observe the same cron state.

Clients must check `capabilities.cronManagement` in the `initialize` response before calling any `cron/*` method. If the flag is absent or `false`, the server returns `-32601` (method not found).

### 16.2 `CronJobInfo` Wire DTO

All cron methods that return job data use the following `CronJobInfo` wire object. It is a transport-safe projection of the internal `CronJob` domain model.

```json
{
  "id": "9c933b01",
  "name": "drink water reminder",
  "schedule": {
    "kind": "every",
    "everyMs": 3600000,
    "atMs": null
  },
  "enabled": true,
  "createdAtMs": 1710590400000,
  "deleteAfterRun": false,
  "state": {
    "nextRunAtMs": 1710594000000,
    "lastRunAtMs": 1710590400000,
    "lastStatus": "ok",
    "lastError": null
  }
}
```

| Field | Type | Description |
|-------|------|-------------|
| `id` | string | Short opaque job identifier (8 hex chars). |
| `name` | string | Human-readable job name. |
| `schedule.kind` | string | `"every"` for recurring or `"at"` for one-time. |
| `schedule.everyMs` | integer? | Interval in milliseconds. Present when `kind` is `"every"`. |
| `schedule.atMs` | integer? | Unix timestamp (ms) for one-time execution. Present when `kind` is `"at"`. |
| `enabled` | boolean | Whether the job is active and will fire when due. |
| `createdAtMs` | integer | Unix timestamp (ms) when the job was created. |
| `deleteAfterRun` | boolean | If `true`, the job is removed after its first successful execution. |
| `state.nextRunAtMs` | integer? | Unix timestamp (ms) of the next scheduled run. `null` if the job has no valid schedule or is disabled. |
| `state.lastRunAtMs` | integer? | Unix timestamp (ms) of the last execution. `null` if never run. |
| `state.lastStatus` | string? | `"ok"` or `"error"`. `null` if never run. |
| `state.lastError` | string? | Error message from the last failed run. `null` when `lastStatus` is `"ok"` or never run. |

### 16.3 `cron/list`

List cron jobs managed by the server.

**Direction**: client → server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `includeDisabled` | boolean | no | Default `false`. When `true`, disabled jobs are included in the result. |

**Result**:

```json
{
  "jobs": [
    {
      "id": "9c933b01",
      "name": "drink water reminder",
      "schedule": { "kind": "every", "everyMs": 3600000, "atMs": null },
      "enabled": true,
      "createdAtMs": 1710590400000,
      "deleteAfterRun": false,
      "state": {
        "nextRunAtMs": 1710594000000,
        "lastRunAtMs": 1710590400000,
        "lastStatus": "ok",
        "lastError": null
      }
    }
  ]
}
```

**Behavior**: Returns the server's current in-memory job list. When `includeDisabled` is `false` (default), only jobs with `enabled: true` are returned.

**Example**:

```json
{ "jsonrpc": "2.0", "method": "cron/list", "id": 50, "params": {
    "includeDisabled": true
} }

{ "jsonrpc": "2.0", "id": 50, "result": {
    "jobs": [
      {
        "id": "9c933b01",
        "name": "drink water reminder",
        "schedule": { "kind": "every", "everyMs": 3600000, "atMs": null },
        "enabled": true,
        "createdAtMs": 1710590400000,
        "deleteAfterRun": false,
        "state": { "nextRunAtMs": 1710594000000, "lastRunAtMs": null, "lastStatus": null, "lastError": null }
      }
    ]
} }
```

### 16.4 `cron/remove`

Permanently remove a cron job from the server.

**Direction**: client → server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `jobId` | string | yes | ID of the cron job to remove. |

**Result**:

```json
{ "removed": true }
```

**Errors**:

| Code | When |
|------|------|
| `-32031` | The specified `jobId` does not exist. |

**Behavior**: Removes the job from the server's in-memory `CronService` and persists the change to disk (`cron/jobs.json`) immediately. If the job's timer fires concurrently, the removal is applied after the current execution completes.

**Example**:

```json
{ "jsonrpc": "2.0", "method": "cron/remove", "id": 51, "params": {
    "jobId": "9c933b01"
} }

{ "jsonrpc": "2.0", "id": 51, "result": { "removed": true } }
```

### 16.5 `cron/enable`

Enable or disable a cron job.

**Direction**: client → server (request)

**Params**:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `jobId` | string | yes | ID of the cron job to update. |
| `enabled` | boolean | yes | `true` to enable the job; `false` to disable it. |

**Result**:

```json
{
  "job": { ... }
}
```

The `job` field contains the updated `CronJobInfo` object reflecting the new `enabled` state. When enabling a job, `state.nextRunAtMs` is recomputed from the current time.

**Errors**:

| Code | When |
|------|------|
| `-32031` | The specified `jobId` does not exist. |

**Behavior**: Updates the job's `enabled` field in the server's in-memory `CronService`. If enabling, `nextRunAtMs` is recomputed. Persists the change to disk immediately.

**Example**:

```json
{ "jsonrpc": "2.0", "method": "cron/enable", "id": 52, "params": {
    "jobId": "9c933b01",
    "enabled": false
} }

{ "jsonrpc": "2.0", "id": 52, "result": {
    "job": {
      "id": "9c933b01",
      "name": "drink water reminder",
      "schedule": { "kind": "every", "everyMs": 3600000, "atMs": null },
      "enabled": false,
      "createdAtMs": 1710590400000,
      "deleteAfterRun": false,
      "state": { "nextRunAtMs": null, "lastRunAtMs": null, "lastStatus": null, "lastError": null }
    }
} }
```

### 16.6 Notification Opt-Out

Cron management methods (`cron/list`, `cron/remove`, `cron/enable`) are request/response pairs — they do not produce notifications. The existing `system/jobResult` notification (Section 6.9) is the cron result delivery mechanism and remains independent. Clients that do not need cron result notifications can opt out via `optOutNotificationMethods: ["system/jobResult"]`.

---

## 17. Heartbeat Management Methods

### 17.1 Scope

Like cron management (Section 16), these methods cover a server-managed background service. The AppServer owns a `HeartbeatService` that periodically reads `HEARTBEAT.md` and runs the agent. The `heartbeat/trigger` method lets wire clients trigger a heartbeat run on demand.

Clients must check `capabilities.heartbeatManagement` before calling any method in this section. If the capability is absent or `false`, the server does not have a heartbeat service configured and will return a `-32601` (Method not found) error.

### 17.2 `heartbeat/trigger`

Trigger an immediate heartbeat run on the server.

**Direction**: client → server (request)

**Params**: `{}` (empty object, no parameters required)

**Result**:

```json
{
  "result": "HEARTBEAT_OK",
  "error": null
}
```

| Field | Type | Description |
|-------|------|-------------|
| `result` | string? | Agent response text. `null` if no `HEARTBEAT.md` was found or its content was empty. |
| `error` | string? | Error message if the heartbeat run failed. `null` on success. |

**Errors**:

| Code | When |
|------|------|
| `-32601` | The heartbeat service is not configured on this server. |

**Timeout note**: This is a **long-running request** — the agent may take tens of seconds to complete. Clients should use a generous timeout (e.g. 120 s). The result is also separately broadcast via `system/jobResult` with `source: "heartbeat"` to all subscribed clients.

**Example**:

```json
{ "jsonrpc": "2.0", "method": "heartbeat/trigger", "id": 60, "params": {} }

{ "jsonrpc": "2.0", "id": 60, "result": {
    "result": "Reviewed open issues and updated tracking.",
    "error": null
} }
```

### 17.3 Capability Advertisement

Clients must check `capabilities.heartbeatManagement` before calling `heartbeat/trigger`. The capability is present and `true` only when the AppServer has a `HeartbeatService` configured.
