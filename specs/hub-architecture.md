# DotCraft Hub Local Coordinator Specification

| Field | Value |
|-------|-------|
| **Version** | 0.3.0 |
| **Status** | Draft |
| **Date** | 2026-04-30 |
| **Related Specs** | [AppServer Protocol](appserver-protocol.md), [Desktop Client](desktop-client.md), [TUI Client](tui-client.md) |

Purpose: Define DotCraft Hub as a local coordinator that discovers, starts, reuses, monitors, and stops workspace-bound AppServer processes without changing the AppServer Protocol or replacing DotCraft's per-workspace runtime model.

This specification is the canonical Hub design. Earlier milestone specs have been consolidated here and removed.

---

## 1. Motivation

DotCraft is intentionally workspace-centric:

- One AppServer process owns one workspace runtime.
- The AppServer loads that workspace's `.craft/` state, sessions, memory, skills, tools, MCP servers, channels, and configuration.
- Desktop, TUI, CLI, ACP, and future clients speak AppServer Protocol to a workspace-bound server.

This ownership model is a feature and must be preserved.

The local coordination problem is that multiple clients can open the same workspace without knowing an AppServer already exists. If each client starts its own stdio AppServer, the workspace can end up with duplicate processes competing for files, MCP side effects, background work, dashboard ports, API ports, and runtime state.

Hub solves that by acting like a local container manager:

- Each workspace still has its own AppServer.
- Hub does not host workspace runtimes.
- Hub does not proxy normal AppServer Protocol traffic.
- Hub helps local clients find or create the correct AppServer and then gets out of the hot path.

---

## 2. Design Principles

1. **Preserve per-workspace AppServer ownership.** Hub must not become a multi-workspace runtime process.
2. **Do not change AppServer Protocol for local coordination.** No workspace routing fields are added to AppServer methods.
3. **Keep Hub off the conversation hot path.** After bootstrap, clients connect directly to the AppServer WebSocket endpoint.
4. **Use stdio for supervision, WebSocket for sharing.** Hub uses stdio to supervise managed AppServers; local clients use WebSocket to share the same AppServer.
5. **Keep Hub local and single-user.** Hub v1 binds to loopback and uses same-user local trust assumptions.
6. **Keep standalone AppServer valid.** `dotcraft app-server` remains available for explicit remote hosting, CI, bots, and debugging.
7. **Keep UI ownership in Desktop.** Hub is headless; tray and OS notifications belong to Desktop/Electron.

---

## 3. Architecture

```text
dotcraft hub
  - Hub Local API on loopback
  - workspace registry
  - AppServer supervisor
  - lifecycle events

dotcraft app-server, one per workspace
  - cwd = workspace root
  - owns WorkspaceRuntime and .craft state
  - exposes AppServer Protocol over WebSocket
  - holds workspace appserver.lock

Desktop / TUI / CLI
  - locate or start Hub
  - ask Hub to ensure the workspace AppServer
  - connect directly to the returned AppServer WebSocket URL
```

Hub state lives under `~/.craft/hub/`. Hub itself only loads global configuration and must not require the current directory to be a `.craft` workspace.

---

## 4. Hub Process

Hub is started by `dotcraft hub` and runs as a global, per-user background process.

Core properties:

- It is workspace-independent.
- It uses a single-instance `hub.lock` discovery file.
- It exposes a loopback HTTP JSON API.
- It publishes a random bearer token in `hub.lock`.
- It stores best-effort registry metadata under `~/.craft/hub/appservers.json`.
- It reports `tray=false`; tray presence is a Desktop capability, not a Hub capability.

Hub may be started explicitly by the user, or automatically by Desktop, TUI, CLI, or tray bootstrap.

---

## 5. Managed AppServer

A Hub-managed AppServer is still a normal `dotcraft app-server` process.

Managed launch contract:

- Working directory is the workspace root.
- AppServer mode is `StdioAndWebSocket`.
- WebSocket host is loopback.
- WebSocket port and token are allocated by Hub.
- Dashboard/API/AG-UI ports are allocated by Hub when the service is enabled and available.
- Runtime overrides are injected ephemerally and must not rewrite `.craft/config.json`.
- Hub keeps the stdio supervisor connection open for readiness and graceful shutdown.

Readiness requires:

- Process is alive.
- Stdio `initialize` handshake succeeds.
- WebSocket endpoint accepts an AppServer `initialize` probe.
- Workspace `appserver.lock` is owned by the expected process.

---

## 6. Hub Local API

Hub Local API is separate from AppServer Protocol. It must not expose `thread/*`, `turn/*`, `approval/*`, `mcp/*`, `skills/*`, `workspace/config/*`, or normal AppServer extension methods.

Transport:

- HTTP JSON over loopback.
- Discovery through `~/.craft/hub/hub.lock`.
- Mutating and management calls require `Authorization: Bearer <token>`.
- `GET /v1/status` is public for local discovery.

Required endpoints:

| Endpoint | Purpose |
|----------|---------|
| `GET /v1/status` | Return Hub metadata and capabilities. |
| `POST /v1/shutdown` | Stop Hub and Hub-managed AppServers. |
| `POST /v1/appservers/ensure` | Ensure a workspace AppServer and optional workspace sidecars exist, then return connection metadata. |
| `GET /v1/appservers` | List live and known AppServer registry entries. |
| `GET /v1/appservers/by-workspace?path=...` | Inspect one workspace without starting it. |
| `POST /v1/appservers/stop` | Stop a managed AppServer. |
| `POST /v1/appservers/restart` | Restart a workspace AppServer through Hub. |
| `GET /v1/events` | Stream Hub lifecycle events as SSE. |
| `POST /v1/notifications/request` | Accept a local notification request and emit a Hub event. |

Errors use this shape:

```json
{
  "error": {
    "code": "workspaceLocked",
    "message": "A live process appears to own the workspace.",
    "details": {}
  }
}
```

Common error codes include `unauthorized`, `workspaceNotFound`, `workspaceLocked`, `appServerStartFailed`, `appServerUnhealthy`, `portUnavailable`, and `invalidNotification`.

---

## 7. Registry, Locks, and State

### Hub Lock

`~/.craft/hub/hub.lock` is the discovery file for the live Hub process. It records:

- Hub pid.
- API base URL.
- bearer token.
- start time.
- Hub version.

Clients must verify both process liveness and `/v1/status` before trusting it.

### Hub Registry

`~/.craft/hub/appservers.json` stores best-effort known AppServer metadata:

- workspace path and canonical path.
- display name.
- state.
- pid.
- endpoints and service status.
- server version.
- last started/seen/exited metadata.
- exit diagnostics and recent stderr.

The registry is not the source of truth for workspace ownership. The live OS process and workspace lock are authoritative.

If Hub restarts and sees an old live workspace lock, it may display that AppServer as external/known, but it must not silently take over a process handle it did not start.

### Workspace Lock

Every AppServer, managed or direct, participates in `<workspace>/.craft/appserver.lock`.

The lock records owner metadata such as pid, workspace path, managed-by-Hub flag, Hub URL, version, start time, and published endpoints. A live lock prevents a second AppServer from starting for the same workspace. Stale locks may be recovered.

---

## 8. Lifecycle and Health

Managed AppServer states:

```text
stopped -> starting -> running -> unhealthy/exited
running -> stopping -> stopped
unhealthy/exited -> starting, when ensure or restart is requested
```

Hub start flow:

1. Canonicalize and validate the workspace.
2. Reuse a healthy managed entry if one exists.
3. Refuse to start if a different live process owns the workspace lock.
4. Allocate local endpoints.
5. Start `dotcraft app-server`.
6. Complete stdio and WebSocket readiness checks.
7. Persist registry metadata.
8. Return the AppServer WebSocket endpoint.

Health checks are lightweight and only apply to processes supervised by the current Hub instance:

- process liveness.
- workspace lock ownership.
- short WebSocket `initialize` probe.

If health fails, Hub marks the entry `unhealthy`, records diagnostics, and emits `appserver.unhealthy`. Hub does not automatically restart unhealthy or exited AppServers; restart is explicit or triggered by a later `ensure`.

Closing Desktop or another local client does not stop a healthy Hub-managed AppServer and must not cancel already-started persisted turns. The client WebSocket connection and passive subscriptions are connection-scoped; active turn execution remains AppServer-scoped and continues in the background until completion, failure, cancellation, Hub shutdown, or explicit AppServer stop/restart.

`POST /v1/appservers/ensure` is a non-destructive reconnect/bootstrap operation for a healthy running AppServer. If a local client reconnects with sidecar settings that differ from the running process, Hub must return the existing AppServer endpoint instead of stopping or replacing the process. Services that require AppServer recreation to apply the new settings should be reported with `serviceStatus.<service>.state = "restartRequired"` and a diagnostic reason. Only explicit `POST /v1/appservers/restart`, `POST /v1/appservers/stop`, Hub shutdown, or a later ensure of an unhealthy/exited entry may stop a managed AppServer.

Hub shutdown stops AppServers it manages, releases local state, and removes its `hub.lock`.

---

## 9. Client Bootstrap and UX

Local clients should default to Hub-managed local mode:

1. Determine the target workspace.
2. Locate a live Hub through `hub.lock`.
3. Start Hub if no live Hub exists and auto-start is enabled.
4. Call `POST /v1/appservers/ensure`.
5. Connect to the returned AppServer WebSocket URL.
6. Perform normal AppServer Protocol handshake.
7. Continue without Hub in the normal conversation path.

Desktop, TUI, and CLI expose local mode as Hub-managed local execution. Explicit remote WebSocket mode remains available and bypasses Hub.

Local mode does not require users to configure AppServer, Dashboard, API, or AG-UI ports. Hub owns those runtime allocations for managed processes.

When a Desktop window closes during a running turn, notification delivery to that window is best-effort and may stop immediately. Reopening the workspace should reuse the same managed AppServer and recover the thread state through normal AppServer Protocol reads or subscriptions.

Clients should present failures as local runtime availability problems, such as:

- Hub could not start.
- Workspace is locked by another live process.
- Managed AppServer failed during startup.
- AppServer endpoint did not become ready.
- Managed AppServer became unhealthy or exited.

---

## 10. Tray and Notifications

Hub remains headless. Desktop owns the tray process.

Tray responsibilities:

- Run as an independent Desktop background process.
- Enforce one tray process per user.
- Start or discover Hub.
- Show Hub and known workspace status without exposing AppServer terminology in user-facing tray labels.
- Open recent or running workspaces.
- Restart or stop Hub-managed AppServers through Hub Protocol.
- Stop Hub and Hub-managed AppServers on tray Exit.
- Display OS notifications for Hub `notification.requested` events.

Notification flow:

1. A client or managed AppServer calls `POST /v1/notifications/request`.
2. Hub validates the request and emits `notification.requested` on SSE.
3. Desktop tray receives the event and displays the OS notification with the DotCraft app icon.
4. Clicking the notification opens the related workspace or action URL.

Turn-related notifications should use the thread display name in user-visible copy instead of the internal thread ID.

Hub itself never displays OS UI.

---

## 11. Port and Endpoint Management

Managed endpoints bind to loopback by default.

Hub allocates ports for:

- AppServer WebSocket.
- Dashboard when enabled.
- API and AG-UI when the module exists and is enabled.
- APIProxy when requested by a local Desktop client.

If optional modules are disabled or unavailable, Hub reports service status as `disabled` or `unavailable` and still starts the AppServer.

APIProxy is an optional workspace sidecar in Hub-managed local mode. Desktop may pass resolved proxy runtime settings in `POST /v1/appservers/ensure`; Hub starts the proxy process before the managed AppServer and injects the proxy endpoint and API key as in-memory AppServer configuration overrides. Hub reports the sidecar URL and state through the workspace `endpoints` and `serviceStatus` maps using the `apiProxy` key. APIProxy secrets must not be emitted in Hub events or status responses.

When a healthy AppServer is already running, an APIProxy mismatch during `ensure` must not restart the AppServer. Hub should keep returning the existing AppServer endpoint and report `serviceStatus.apiProxy.state = "restartRequired"` when the requested proxy enabled state, endpoint, binary, config, or credential identity cannot be applied without recreating the AppServer. The explicit restart endpoint is the boundary for applying those disruptive proxy changes.

If APIProxy is requested and cannot be started or probed, Hub must fail the ensure request instead of returning an AppServer whose model endpoint points at a dead proxy. If APIProxy is not requested, Hub must not start or configure it.

Hub-managed APIProxy does not change the remote AppServer protocol, does not proxy normal conversation traffic, and does not manage TypeScript social channel adapters. Built-in TypeScript channels remain a separate AppServer/adapter lifecycle concern.

Hub must not silently rewrite unrelated user-configured ports for native channels, webhook modules, or future integrations unless a service explicitly participates in Hub-managed runtime overrides.

---

## 12. Security Model

Hub is a same-user local coordinator, not a security boundary against malicious processes running as the same OS user.

Security constraints:

- Hub API binds to loopback.
- Managed AppServer endpoints bind to loopback.
- Hub API uses bearer token authorization for protected endpoints.
- Managed AppServer WebSocket endpoints use per-process tokens when available.
- Remote or multi-user Hub scenarios require a separate security design.

---

## 13. Compatibility

AppServer Protocol is unchanged. Clients still use existing AppServer methods after connecting to the workspace AppServer.

Existing AppServer modes remain valid:

| Mode | Status |
|------|--------|
| `stdio` | Supported for direct subprocess clients and debugging. |
| `websocket` | Supported for explicit remote/local hosting. |
| `stdio + websocket` | Required for Hub-managed AppServers. |

ACP itself remains an AppServer client bridge: it translates editor ACP stdio traffic to the existing AppServer wire protocol. It does not require AppServer Protocol changes. If local ACP mode starts its own workspace AppServer subprocess, only that bootstrap path may later choose to use Hub to avoid duplicate local AppServer ownership.

---

## 14. Remaining Work

The implemented Hub design still leaves several product and hardening areas for future work:

- Optional ACP local bootstrap alignment: ACP's protocol bridge is already AppServer-based; only its default local subprocess startup would need Hub if IDE integrations should share the same managed AppServer as Desktop/TUI/CLI.
- More complete Desktop multi-workspace management UI beyond tray menus.
- Notification preferences such as quiet hours, per-workspace mute, and frequency control.
- Better recovery or explicit cleanup flow for live AppServers left behind after Hub restart.
- Optional named pipe or Unix socket transport for stronger local API ergonomics.
- Configurable Hub-managed port ranges.
- Idle shutdown or lease-based AppServer lifetime management.
- Manual packaged-app verification for tray behavior, OS notifications, and hidden Windows child processes.
