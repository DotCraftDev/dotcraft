# DotCraft Hub M2: Protocol and Managed AppServer

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Draft |
| **Date** | 2026-04-29 |
| **Parent Spec** | [Hub Local Coordinator](hub-architecture.md), [Hub M1](hub-m1-shell.md), [AppServer Protocol](appserver-protocol.md) |

Purpose: Define the second Hub milestone: a lightweight Hub protocol and managed AppServer lifecycle that lets Hub start, reuse, monitor, stop, and report workspace-bound AppServer processes while centrally allocating local ports and reserving event/notification flows.

---

## 1. Overview

M2 turns Hub from a local presence process into the local authority for workspace AppServer discovery and lifecycle.

Hub still does not become a workspace runtime. Each workspace is served by a normal workspace-bound `dotcraft app-server` process. Hub manages that process from the outside and returns direct AppServer WebSocket connection metadata to clients.

M2 also defines the first stable Hub Protocol surface. This protocol is separate from AppServer Protocol and is intentionally smaller. It exists so Desktop, TUI, CLI, and future clients can ask one question before connecting:

> Which local AppServer should I use for this workspace?

---

## 2. Goal

At the end of M2:

- Hub can ensure one managed AppServer per canonical workspace path.
- Managed AppServers expose direct WebSocket endpoints for clients.
- Hub allocates local ports for AppServer WebSocket and eligible workspace services.
- Hub reports managed process status, endpoint metadata, failures, and recent diagnostics.
- Hub exposes a lightweight event stream for lifecycle and future notification use.
- AppServer can accept ephemeral managed-mode runtime overrides without mutating workspace config.

---

## 3. Scope

M2 includes:

- Hub Protocol v1 for local management.
- AppServer ensure/list/get/stop/restart operations.
- Workspace path canonicalization and duplicate ensure behavior.
- Managed AppServer startup using `stdio + websocket`.
- Managed AppServer readiness and health checks.
- Workspace AppServer lock behavior.
- Port allocation policy for AppServer WebSocket.
- Runtime override policy for Dashboard, API, AG-UI, and other eligible local services.
- Event stream reservation for lifecycle and notification events.

M2 may update related specs where necessary:

- [Hub Local Coordinator](hub-architecture.md)
- [AppServer Protocol](appserver-protocol.md), only for managed-mode compatibility notes
- Module-specific specs if a service needs managed port behavior

---

## 4. Non-Goals

- M2 does not require Desktop, TUI, or CLI to use Hub by default.
- M2 does not add multi-workspace AppServer Protocol routing.
- M2 does not proxy normal AppServer traffic.
- M2 does not require a unified Dashboard.
- M2 does not require rich tray menus.
- M2 does not implement remote Hub.

---

## 5. Hub Protocol V1

### 5.1 Transport

Hub Protocol v1 uses HTTP JSON over loopback.

- Base URL is published in `~/.craft/hub/hub.lock`.
- Mutating endpoints require the Hub token.
- Clients must treat Hub Protocol as separate from AppServer Protocol.

An event stream endpoint is reserved for lifecycle events. The concrete transport may be Server-Sent Events or WebSocket, but it must remain Hub Protocol, not AppServer Protocol.

### 5.2 Capabilities

`GET /v1/status` returns Hub capabilities:

```json
{
  "hubVersion": "0.2.0",
  "pid": 12345,
  "capabilities": {
    "appServerManagement": true,
    "portManagement": true,
    "events": true,
    "notifications": true,
    "tray": true
  }
}
```

Capabilities allow clients to degrade gracefully when running against an older M1 Hub or a partially enabled build.

### 5.3 AppServer Management

#### `POST /v1/appservers/ensure`

Ensures that a managed AppServer exists for a workspace.

Request:

```json
{
  "workspacePath": "F:\\dotcraft",
  "client": {
    "name": "dotcraft-desktop",
    "version": "0.1.0"
  },
  "startIfMissing": true
}
```

Response:

```json
{
  "workspacePath": "F:\\dotcraft",
  "canonicalWorkspacePath": "F:\\dotcraft",
  "state": "running",
  "pid": 23456,
  "endpoints": {
    "appServerWebSocket": "ws://127.0.0.1:43121/ws?token=...",
    "dashboard": "http://127.0.0.1:43122/dashboard",
    "api": "http://127.0.0.1:43123",
    "agui": "http://127.0.0.1:43124"
  },
  "serverVersion": "0.0.1.0+abcdef",
  "startedByHub": true
}
```

The endpoint object only includes services that are active for that workspace. A missing endpoint means unavailable, disabled, or not managed.

#### `GET /v1/appservers`

Lists known managed AppServers and their states.

#### `GET /v1/appservers/by-workspace?path=...`

Returns metadata for one workspace without starting it.

#### `POST /v1/appservers/stop`

Stops a managed AppServer for a workspace.

#### `POST /v1/appservers/restart`

Restarts a managed AppServer and returns fresh endpoint metadata.

### 5.4 Port Management

Hub owns local port allocation for managed services that can safely accept runtime overrides.

The managed endpoint set should include, where enabled:

| Service | Managed behavior |
|---------|------------------|
| AppServer WebSocket | Always allocated by Hub for managed AppServers. |
| Dashboard | Allocated by Hub if Dashboard is enabled and override is supported. |
| API | Allocated by Hub if API is enabled and override is supported. |
| AG-UI | Allocated by Hub if AG-UI is enabled and override is supported. |

Hub must not silently rewrite user-configured ports for externally visible webhook or bot callback services unless those services define a managed override contract.

Port allocations are runtime facts. They must not be written back to workspace config.

### 5.5 Event Stream

Hub Protocol reserves an event stream for lifecycle and notification use.

Initial event kinds:

| Event | Purpose |
|-------|---------|
| `hub.started` | Hub became ready. |
| `hub.stopping` | Hub is shutting down. |
| `appserver.starting` | A workspace AppServer is starting. |
| `appserver.running` | A workspace AppServer became ready. |
| `appserver.exited` | A workspace AppServer exited. |
| `appserver.unhealthy` | Hub detected an unhealthy managed AppServer. |
| `port.allocated` | Hub allocated a local service port. |
| `notification.requested` | A client or AppServer requested a local OS notification. |

Future event kinds may include workspace activity:

- `workspace.turnCompleted`
- `workspace.approvalRequested`
- `workspace.backgroundJobCompleted`

M2 only needs to reserve the contract and support lifecycle events. Workspace activity forwarding may be partial until clients and AppServer emit those events.

### 5.6 Notification Request

Hub Protocol should reserve a notification request endpoint so clients and managed AppServers can ask Hub to surface OS-level notifications later.

Request shape:

```json
{
  "workspacePath": "F:\\dotcraft",
  "kind": "turnCompleted",
  "title": "Task completed",
  "body": "The agent finished a turn in dotcraft.",
  "source": {
    "threadId": "thr_abc",
    "turnId": "turn_def"
  }
}
```

M2 may implement this as a no-op or lifecycle event only if OS notification integration is not ready. The protocol shape should still be reserved before M3 clients depend on it.

### 5.7 Error Shape

Hub Protocol errors use a stable JSON shape:

```json
{
  "error": {
    "code": "appServerStartFailed",
    "message": "Managed AppServer failed during startup.",
    "details": {
      "workspacePath": "F:\\dotcraft"
    }
  }
}
```

Required error codes:

| Code | Meaning |
|------|---------|
| `unauthorized` | Missing or invalid Hub token. |
| `workspaceNotFound` | Workspace path does not exist. |
| `workspaceLocked` | A live process appears to own the workspace. |
| `appServerStartFailed` | AppServer failed before readiness. |
| `appServerUnhealthy` | Process exists but readiness or health checks fail. |
| `portUnavailable` | Hub could not allocate a required port. |
| `serviceOverrideUnsupported` | A requested managed service cannot accept runtime port override. |

---

## 6. Managed AppServer Lifecycle

### 6.1 State Model

Managed AppServer states:

| State | Meaning |
|-------|---------|
| `stopped` | No managed process is running. |
| `starting` | Hub is launching and verifying a process. |
| `running` | Process is ready and endpoint metadata is available. |
| `unhealthy` | Process exists but health checks fail. |
| `stopping` | Hub is stopping the process. |
| `exited` | Process exited and diagnostics are available. |

### 6.2 Ensure Behavior

For a canonical workspace path:

- Concurrent `ensure` requests must converge on one managed AppServer.
- If a healthy process already exists, Hub returns it.
- If a process is starting, Hub waits for the same startup result.
- If the process is unhealthy or exited, Hub may restart only when `startIfMissing` permits it.
- If another live owner holds the workspace lock, Hub returns `workspaceLocked`.

### 6.3 Readiness

A managed AppServer is ready when:

- The process is alive.
- The supervisor stdio handshake succeeds.
- The WebSocket endpoint accepts connections.
- The workspace lock is held by the managed process.
- Required managed service endpoints either bind successfully or report a controlled unavailable state.

### 6.4 Shutdown

Hub stops managed AppServers by graceful shutdown first. Forced termination is a fallback for unresponsive processes.

Hub must preserve recent exit diagnostics in registry state.

---

## 7. Managed Runtime Overrides

Managed runtime overrides are ephemeral launch-time facts.

They may include:

- AppServer WebSocket host, port, and token.
- Dashboard host and port or disabled state.
- API host and port or disabled state.
- AG-UI host and port or disabled state.

Overrides must not modify workspace config files.

Workspace config still controls whether a service is enabled and any user-visible behavior unrelated to port ownership. Hub controls the actual local endpoint when running in managed local mode.

---

## 8. Constraints and Compatibility

- Existing direct AppServer modes remain supported.
- Existing AppServer Protocol messages remain unchanged.
- Managed local endpoints bind to loopback only.
- Hub must not expose non-loopback managed services in M2.
- Hub must tolerate services that do not yet support managed overrides by reporting clear capability or error data.
- M2 must not require Desktop/TUI/CLI adoption, but it must provide the protocol they need in M3.

---

## 9. Acceptance Checklist

- Hub can ensure one AppServer for a workspace and return a direct WebSocket endpoint.
- Two concurrent ensure requests for the same workspace return the same managed process.
- Ensure for different workspaces returns distinct AppServers and distinct endpoint allocations.
- Managed AppServer startup does not edit workspace config.
- AppServer WebSocket port conflicts are avoided by Hub allocation.
- Dashboard/API/AG-UI endpoint metadata is either allocated or explicitly unavailable.
- Hub records and reports startup failures and recent stderr.
- Hub emits lifecycle events for AppServer start, ready, exit, and unhealthy transitions.
- Existing direct `dotcraft app-server` usage still works outside managed mode.

---

## 10. Open Questions

1. Should Hub Protocol events use Server-Sent Events or WebSocket in v1?
2. Which runtime override mechanism should be normative: CLI flags, environment variables, or a temporary override file?
3. Should direct standalone AppServer fail immediately when a managed workspace lock exists, or warn during one transition release?
4. Should notification requests be accepted from AppServer only, clients only, or both?
5. Should Dashboard be disabled when override support is missing, or should Hub fail the workspace ensure?
