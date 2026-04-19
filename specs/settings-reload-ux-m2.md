# DotCraft Settings Reload UX — M2: Config Change Notification

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Draft |
| **Date** | 2026-04-19 |
| **Parent Spec** | [Settings Reload UX Design](settings-reload-ux-design.md), [AppServer Protocol](appserver-protocol.md) |

Purpose: introduce a minimal, additive change-notification surface so that clients of a single AppServer instance can stay in sync when one of them mutates workspace configuration. M2 does not add any new runtime hot-reload behavior to consumers — it formalizes the "something changed" signal around the already-closed-loop paths (Skills, MCP, workspace default Model) and establishes the abstraction that future features will extend when they add real hot-reload.

---

## Table of Contents

- [1. Scope](#1-scope)
- [2. Goals and Non-Goals](#2-goals-and-non-goals)
- [3. `IAppConfigMonitor` Abstraction](#3-iappconfigmonitor-abstraction)
- [4. `workspace/configChanged` Notification](#4-workspaceconfigchanged-notification)
- [5. Trigger Points in Existing RPC Handlers](#5-trigger-points-in-existing-rpc-handlers)
- [6. Skills Closure](#6-skills-closure)
- [7. MCP Closure](#7-mcp-closure)
- [8. Concurrency and Ordering](#8-concurrency-and-ordering)
- [9. Constraints and Compatibility Notes](#9-constraints-and-compatibility-notes)
- [10. Acceptance Checklist](#10-acceptance-checklist)
- [11. Open Questions](#11-open-questions)

---

## 1. Scope

### 1.1 What This Spec Defines

- The `IAppConfigMonitor` service contract: current snapshot access and a `Changed` event.
- The `workspace/configChanged` AppServer notification: when it fires, what payload it carries, and how subscribers should react.
- The set of RPC handlers that must trigger a notification in M2.
- The closure for Skills (no behavior change, only the broadcast) and MCP (no behavior change, only the broadcast).

### 1.2 What This Spec Does Not Define

- Any change to `AgentFactory`, `IChatClient`, `EnabledTools` filtering, or other startup-captured state. Those remain Tier C and are deferred to future features.
- A `FileSystemWatcher` over `config.json`. This is a deliberate omission — see [Design §5](settings-reload-ux-design.md#5-scope-of-this-design).
- Dashboard adoption of the new notification. Dashboard's direct-to-disk write path is unchanged in M2.
- Per-thread configuration (`thread/config/update`), which already has its own lifecycle and is unaffected.

---

## 2. Goals and Non-Goals

### 2.1 Goals

1. **Announce changes, don't drive them.** Consumers that already close the loop (Skills, MCP) keep doing what they do. The monitor exists so that other clients (Desktop, future integrations) can react to a change they did not cause.
2. **Keep the surface minimal.** One abstraction, one notification, a handful of trigger points. Anything larger risks pulling in hot-reload work that belongs to future features.
3. **Stay backward-compatible.** Clients that do not subscribe to `workspace/configChanged` continue to work. Servers with no subscribers continue to function as they do today.
4. **Establish the shape for future work.** The monitor and the notification are designed so that a later feature can add more event types (e.g., `LlmConnectionChanged`) or a filesystem watcher without re-architecting this layer.

### 2.2 Non-Goals

- Refactoring how `AppConfig` is registered in DI. It remains a singleton captured at process start.
- Forcing consumers to migrate to the monitor. Consumers may opt in when they are ready.
- Reconciling edits made directly to `config.json` on disk. Such edits remain invisible to the process until the next restart.
- Authenticating or authorizing `workspace/configChanged` subscribers beyond the transport-level protections already defined in AppServer Protocol §2.

---

## 3. `IAppConfigMonitor` Abstraction

### 3.1 Responsibilities

`IAppConfigMonitor` is a small service that provides:

- **A current snapshot.** A typed accessor returning the live `AppConfig` instance. In M2 this is the same singleton already held by DI; the monitor is a read-through convenience.
- **A change event.** A single `Changed` event that fires after a successful mutation. The event payload identifies which logical region of configuration was touched (see §4.3 for the taxonomy) so that subscribers can filter without parsing the full configuration.

### 3.2 Ownership and Lifetime

- The monitor is a long-lived singleton registered at AppServer start.
- It is the sole authority responsible for firing change events triggered by RPC. RPC handlers call the monitor after a successful write; the monitor fans out to subscribers.
- The monitor never writes to disk itself; writes remain the responsibility of the RPC handler or the subsystem that owns the data (for example, `McpClientManager`).

### 3.3 Subscribers

Two kinds of subscribers exist in M2:

1. **In-process subscribers.** AppServer components that need to know when configuration changed. The only in-process subscriber in M2 is the AppServer host, which translates in-process events into `workspace/configChanged` wire notifications.
2. **Wire subscribers.** External clients that want to know through AppServer's notification channel. They do not interact with `IAppConfigMonitor` directly; they receive `workspace/configChanged` over JSON-RPC.

### 3.4 Deferred Extension Points

The monitor is deliberately minimal. A later feature can add stronger event types (`LlmConnectionChanged`, `EnabledToolsChanged`, etc.), file-based change detection, or diff payloads. M2 guarantees the shape is additive so that such extensions do not require breaking the monitor's M2 subscribers.

---

## 4. `workspace/configChanged` Notification

### 4.1 Direction

Server → client, notification (no response). Emitted when workspace configuration has successfully changed as a result of an RPC method handled by the server.

### 4.2 Params

```
{
  "source": "<string>",
  "regions": [ "<string>", ... ],
  "changedAt": "<ISO-8601 timestamp>"
}
```

- `source` — identifies which RPC caused the change. Current values: `"workspace/config/update"`, `"skills/setEnabled"`, `"mcp/upsert"`, `"mcp/remove"`, `"externalChannel/upsert"`, `"externalChannel/remove"`.
- `regions` — a set of logical region tags describing which part of configuration changed (see §4.3). Multiple regions may be emitted in a single notification if a single RPC touched more than one logical area.
- `changedAt` — server-side timestamp, used by clients for ordering and de-duplication.

A notification never carries the full new configuration. Clients that need the latest value re-query through the appropriate read RPC (for example, `mcp/list`, `skills/list`), or through `workspace/config/read` if and when it is introduced.

### 4.3 Region Taxonomy

The region tags in M2 are:

| Region | Fires when |
|--------|------------|
| `workspace.model` | Workspace default model was updated via `workspace/config/update`. |
| `skills` | `Skills.DisabledSkills` was modified via `skills/setEnabled`. |
| `mcp` | MCP server list was modified via `mcp/upsert` or `mcp/remove`. Note: this is distinct from `mcp/statusChanged`, which reports live per-server health. |
| `externalChannel` | External channel list was modified via `externalChannel/upsert` or `externalChannel/remove`. |

Future features add regions without breaking M2 subscribers. Unknown regions must be ignored by clients that do not care about them.

### 4.4 Capability

A client capability bit (e.g., `configChange`) is declared during the existing initialize handshake so that servers can suppress the notification for clients that opt out. If the capability is absent from the client's declaration, the server behaves as if it were set to `true` (the notification is additive and harmless).

### 4.5 Delivery Semantics

- The notification is delivered **after** the successful write and in-process state update.
- The notification is best-effort: if the transport drops, clients do not receive a retry. Clients must be prepared to re-reconcile on reconnect (the existing session-resume behavior already handles this for threads and extends naturally to settings data).
- No ordering guarantee is made between `workspace/configChanged` and unrelated notifications, other than the ordering that the underlying transport already provides.

### 4.6 Example

```json
{ "jsonrpc": "2.0", "method": "workspace/configChanged", "params": {
    "source": "skills/setEnabled",
    "regions": ["skills"],
    "changedAt": "2026-04-19T10:15:03Z"
} }
```

---

## 5. Trigger Points in Existing RPC Handlers

The following handlers must call `IAppConfigMonitor` after their successful-write path and before returning the response. The monitor is responsible for fanning out the notification to all eligible wire subscribers.

| Handler | Region |
|---------|--------|
| `HandleWorkspaceConfigUpdateAsync` ([`AppServerRequestHandler.cs`](../src/DotCraft.Core/Protocol/AppServer/AppServerRequestHandler.cs)) | `workspace.model` |
| `HandleSkillsSetEnabledAsync` | `skills` |
| `HandleMcpUpsertAsync` | `mcp` |
| `HandleMcpRemoveAsync` | `mcp` |
| `HandleExternalChannelUpsertAsync` | `externalChannel` |
| `HandleExternalChannelRemoveAsync` | `externalChannel` |

The notification fires only on success. Handlers that throw before completing their write do not fire the notification.

Handlers that merely read configuration (`mcp/list`, `skills/list`, etc.) do not trigger the notification.

---

## 6. Skills Closure

### 6.1 Current Behavior

`HandleSkillsSetEnabledAsync` already:

1. Validates the skill exists.
2. Builds the new disabled list.
3. Persists via `SkillsConfigPersistence.WriteWorkspaceDisabledSkills`.
4. Updates the live `SkillsLoader.SetDisabledSkills`.
5. Returns the updated skill descriptor.

This is already closed-loop for the caller. Other clients of the same AppServer — for example, a second Desktop window, or Desktop while Dashboard made the change — do not know.

### 6.2 M2 Addition

After step 4 and before step 5, `HandleSkillsSetEnabledAsync` notifies the monitor. The monitor emits `workspace/configChanged` with `regions: ["skills"]` to all wire subscribers.

No new behavior change, no new state transitions. The subsystem's existing hot-reload remains the source of truth.

### 6.3 Client Reaction Contract

Clients that display Skills state are expected to:

- React to `workspace/configChanged` with region `skills` by re-reading `skills/list`.
- Merge results with any local edit in progress according to their own edit-race policy. Desktop's specific policy is defined in M3.

---

## 7. MCP Closure

### 7.1 Current Behavior

`HandleMcpUpsertAsync` and `HandleMcpRemoveAsync` already:

1. Update `McpClientManager` (which manages live connections).
2. Persist via `SaveWorkspaceMcpServersAsync`.
3. On upsert, emit the existing `mcp/statusChanged` notification via `_broadcastMcpStatusChanged` when the new status is known.

### 7.2 M2 Addition

After the persist step, both handlers notify the monitor. The monitor emits `workspace/configChanged` with `regions: ["mcp"]`.

`mcp/statusChanged` continues to be the authoritative per-server health notification. `workspace/configChanged` with region `mcp` is a coarser signal intended for clients that want to know the list itself changed without having to correlate multiple `mcp/statusChanged` events.

### 7.3 Client Reaction Contract

- Clients that maintain an MCP list should re-read `mcp/list` (or re-render from cached data if they already have it) when they receive the region signal.
- Clients continue to subscribe to `mcp/statusChanged` for live per-server health.

---

## 8. Concurrency and Ordering

### 8.1 Write Serialization

Each affected handler already runs under the AppServer's existing request-processing model. Writes are serialized per handler; there is no new concurrency concern introduced by M2.

### 8.2 Notification Ordering

The monitor emits notifications strictly after the writing handler's in-process state update and strictly before the handler returns its response. This ordering ensures that:

- A client which issued the RPC sees the response after the notification would have reached any other client. If the client requires a consistent sequence, it can read the notification first.
- A broadcaster's notification cannot be observed before the state that caused it has been applied in-process.

### 8.3 Reentrancy

Monitor subscribers must not synchronously call back into RPC handlers. In-process subscribers in M2 are limited to the AppServer host's wire fan-out, which is non-reentrant.

### 8.4 Client Edit Races

Desktop clients often have pending local edits when a notification arrives (for example, an MCP entry that is being edited but not yet saved). The edit-race policy — whether to reload, prompt, or ignore — is client-owned. M3 defines Desktop's policy; other clients are free to choose their own.

---

## 9. Constraints and Compatibility Notes

- The monitor does not change how `AppConfig` is registered or consumed. Consumers that already capture configuration at construction continue to do so.
- `workspace/configChanged` is additive. Clients that ignore it (or do not declare the capability) continue to function.
- The notification carries no sensitive data (no keys, tokens, or model identifiers). Clients that want the actual values re-read them through existing read RPCs, which have their own authorization semantics.
- The monitor's `Changed` event is intentionally coarse. Future features may add narrower events alongside it; M2 does not pre-empt those designs.
- Dashboard's direct-to-disk write path is unaffected. If Dashboard writes while Desktop is connected, Desktop does not receive a notification for that write. This is documented as a known limitation in the design doc and left to a future feature.
- The notification firing is a soft requirement: a failure to notify (for example, because the monitor has no subscribers) must not fail the RPC or invalidate the already-completed write.

---

## 10. Acceptance Checklist

- [ ] `IAppConfigMonitor` is defined with a snapshot accessor and a `Changed` event, and is registered as a singleton in AppServer.
- [ ] The AppServer host subscribes to the monitor and fans events out as `workspace/configChanged` wire notifications to all eligible clients.
- [ ] `workspace/configChanged` is documented in [`specs/appserver-protocol.md`](appserver-protocol.md) with the params shape in §4.2 and the region taxonomy in §4.3.
- [ ] `HandleWorkspaceConfigUpdateAsync`, `HandleSkillsSetEnabledAsync`, `HandleMcpUpsertAsync`, `HandleMcpRemoveAsync`, `HandleExternalChannelUpsertAsync`, and `HandleExternalChannelRemoveAsync` invoke the monitor after their successful write.
- [ ] The notification does not fire for read RPCs or for write RPCs that throw before completing.
- [ ] The client capability for opting in (or opting out) of `workspace/configChanged` is declared in the initialize handshake and honored by the server.
- [ ] Existing MCP and Skills tests continue to pass without modification.
- [ ] A new protocol-level test verifies that each of the six handlers above emits `workspace/configChanged` exactly once per successful invocation, with the expected `source` and `regions`.
- [ ] A new test verifies that a failed write (e.g., invalid params) does not emit the notification.

---

## 11. Open Questions

1. Should `workspace/configChanged` include the RPC method's result identifier (if any) so that clients can correlate notifications with outstanding requests? (Preference: no in M2; it is a best-effort signal, correlation is not required.)
2. Should the region taxonomy carry a sub-identifier (e.g., `mcp:openai-compatible`) rather than the coarser `mcp`? (Preference: keep it coarse in M2; sub-identifiers can be added later without breaking existing subscribers.)
3. Should the monitor also be responsible for deduplicating identical back-to-back notifications? (Preference: no; deduplication is a client concern. The server has no reliable way to know the semantic equivalence of two changes.)
4. When Dashboard eventually moves to RPC-driven writes, should its writes emit `workspace/configChanged` with `source: "dashboard/config/update"`? (Preference: yes, but that is out of scope for M2.)
