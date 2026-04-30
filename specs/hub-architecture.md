# DotCraft Hub Local Coordinator Specification

| Field | Value |
|-------|-------|
| **Version** | 0.2.0 |
| **Status** | Draft |
| **Date** | 2026-04-29 |
| **Parent Spec** | [AppServer Protocol](appserver-protocol.md), [Desktop Client](desktop-client.md), [TUI Client](tui-client.md) |

Purpose: Define DotCraft Hub as a local coordinator that discovers, starts, reuses, monitors, and stops workspace-bound AppServer processes without changing the AppServer protocol or replacing the per-workspace runtime model.

This specification replaces the previous Hub design that hosted multiple workspace runtimes in one process and added workspace routing to the AppServer wire protocol. That direction is explicitly rejected for v1.

---

## Table of Contents

- [1. Motivation](#1-motivation)
- [2. Design Principles](#2-design-principles)
- [3. Goals and Non-Goals](#3-goals-and-non-goals)
- [4. Architecture Overview](#4-architecture-overview)
- [5. Core Components](#5-core-components)
- [6. Hub Local API](#6-hub-local-api)
- [7. Client Bootstrap](#7-client-bootstrap)
- [8. Managed AppServer Lifecycle](#8-managed-appserver-lifecycle)
- [9. Locks, Registry, and State Files](#9-locks-registry-and-state-files)
- [10. Port and Endpoint Management](#10-port-and-endpoint-management)
- [11. Security Model](#11-security-model)
- [12. Compatibility](#12-compatibility)
- [13. Technical Choices](#13-technical-choices)
- [14. Open Questions](#14-open-questions)
- [15. Iteration Plan](#15-iteration-plan)

---

## 1. Motivation

### 1.1 Current Model

DotCraft is intentionally workspace-centric:

- One AppServer process is bound to one workspace.
- The AppServer loads configuration, memory, threads, skills, tools, MCP servers, channels, and `.craft/` state for that workspace.
- Desktop, TUI, CLI, ACP, and other clients speak the AppServer Protocol to a workspace-bound server.

This model is a feature. It keeps ownership clear: the process that serves a workspace also owns that workspace's runtime state.

### 1.2 Current Problem

Local interactive clients do not coordinate AppServer ownership.

Example:

1. Desktop opens workspace `A` and starts `dotcraft app-server` over stdio.
2. TUI or CLI opens the same workspace `A`.
3. The second client does not know Desktop already has an AppServer for `A`.
4. It starts another stdio AppServer for the same workspace.
5. Two AppServer processes now compete for the same `.craft/` directory, background jobs, MCP processes, workspace files, and runtime side effects.

This also creates endpoint problems:

- WebSocket and Dashboard ports can collide.
- Clients have no shared local authority that can answer "which AppServer owns this workspace?"
- Desktop-only locks do not protect CLI, TUI, ACP, or future clients.

### 1.3 Revised Direction

Hub should be analogous to a local container manager:

- Each workspace still has its own AppServer.
- Hub does not sit between the AppServer and the workspace.
- Hub does not proxy normal AppServer protocol traffic.
- Hub helps local applications find or create the correct AppServer for a workspace.

After Hub resolves a workspace, the client connects to that workspace's AppServer exactly like a remote WebSocket AppServer client.

---

## 2. Design Principles

1. **Preserve per-workspace AppServer ownership.** Hub must not turn DotCraft into a multi-workspace in-process runtime.
2. **Do not change the AppServer Protocol for Hub v1.** No `workspacePath` routing parameter, no hub-mode protocol branch, and no multi-workspace JSON-RPC endpoint.
3. **Hub is not on the hot path.** After bootstrap, clients talk directly to the AppServer WebSocket endpoint.
4. **Stdio remains useful for supervision.** Hub may keep a stdio connection to the managed AppServer for readiness checks, lifecycle control, and graceful shutdown.
5. **WebSocket is the shared client transport.** Managed AppServers expose loopback WebSocket endpoints so Desktop, TUI, CLI, and other local clients can share one process.
6. **Local first.** Hub v1 is a single-user, loopback-only facility. Remote, team, and multi-user Hub scenarios require a separate security spec.
7. **Legacy standalone mode remains valid.** `dotcraft app-server` can still be run directly for CI, servers, bots, explicit remote setups, and debugging.

---

## 3. Goals and Non-Goals

### 3.1 Goals

- Provide a single local authority that maps normalized workspace paths to running AppServer instances.
- Ensure local interactive clients reuse the same AppServer for the same workspace.
- Start a managed AppServer when none exists for a workspace.
- Allocate AppServer WebSocket endpoints without user-visible port conflicts.
- Return enough connection metadata for clients to connect using the existing AppServer WebSocket transport.
- Track process health, startup failures, exit diagnostics, and restart eligibility.
- Provide a foundation for later Desktop/TUI multi-workspace UX without requiring AppServer protocol changes.

### 3.2 Non-Goals

- Hosting multiple `WorkspaceRuntime` objects inside Hub.
- Merging multiple workspaces behind one AppServer protocol connection.
- Adding workspace routing fields to existing AppServer methods.
- Replacing the AppServer Protocol with a Hub protocol.
- Proxying normal `thread/*`, `turn/*`, `mcp/*`, `skills/*`, `workspace/config/*`, or extension traffic.
- Providing a unified cross-workspace Dashboard in v1.
- Solving remote access, authentication between different OS users, or team-shared Hub deployments.

---

## 4. Architecture Overview

```
┌───────────────────────────────────────────────────────────────┐
│                         dotcraft hub                          │
│                                                               │
│  ┌─────────────────┐  ┌────────────────┐  ┌────────────────┐ │
│  │ Hub Local API   │  │ Workspace       │  │ AppServer       │ │
│  │ 127.0.0.1:N     │  │ Registry        │  │ Supervisor      │ │
│  └────────┬────────┘  └────────────────┘  └───────┬────────┘ │
│           │                                        │          │
│           │ ensure workspace A                     │ starts   │
└───────────┼────────────────────────────────────────┼──────────┘
            │                                        │
            │ returns ws://127.0.0.1:43121/ws        │ stdio supervision
            ▼                                        ▼
┌─────────────────────┐                    ┌─────────────────────┐
│ Desktop / TUI / CLI │                    │ dotcraft app-server │
│                     │◀──────────────────▶│ cwd = workspace A   │
│ AppServer Protocol  │   WebSocket        │ ws://127.0.0.1:43121│
└─────────────────────┘                    └─────────────────────┘

┌─────────────────────┐                    ┌─────────────────────┐
│ Another local       │                    │ dotcraft app-server │
│ client              │◀──────────────────▶│ cwd = workspace B   │
│                     │   WebSocket        │ ws://127.0.0.1:43122│
└─────────────────────┘                    └─────────────────────┘
```

The Hub API is only a bootstrap and management API. It answers questions such as:

- Is Hub running?
- Is there an AppServer for this workspace?
- If not, start one.
- What WebSocket URL should the client connect to?
- Is the managed AppServer healthy?
- Stop or restart the managed AppServer.

Normal conversation, approval, MCP, skills, automations, channel, and config operations continue to use the existing AppServer Protocol directly against the workspace AppServer.

---

## 5. Core Components

### 5.1 Hub Process

Hub is a global, long-lived local process:

- Command: `dotcraft hub`.
- Scope: current OS user, not a project workspace.
- Default bind: loopback only.
- Configuration: global `~/.craft/config.json`, section `Hub`.
- State: global `~/.craft/hub/`.

Hub may be started by:

- Desktop on application startup.
- CLI/TUI during bootstrap.
- A tray/autostart integration.
- The user explicitly running `dotcraft hub`.

Hub does not call workspace-bound `AddDotCraft` for itself. It only needs services for configuration, process supervision, logging, endpoint allocation, registry persistence, and its local API.

### 5.2 Managed AppServer

A managed AppServer is a normal `dotcraft app-server` process started with:

- `WorkingDirectory` set to the workspace root.
- AppServer mode set to `StdioAndWebSocket`.
- WebSocket host set to loopback.
- WebSocket port assigned by Hub.
- Optional WebSocket token assigned by Hub.

Conceptual command:

```bash
dotcraft app-server --listen ws+stdio://127.0.0.1:43121
```

The AppServer remains the only process that owns the workspace runtime. Hub does not read or mutate workspace Session Core state directly.

Hub keeps the AppServer stdio stream open as a supervisor connection. The supervisor connection can:

- Perform `initialize` / `initialized` to verify readiness.
- Read server metadata such as version, capabilities, and Dashboard URL.
- Keep the dual-mode AppServer alive.
- Gracefully shut down the AppServer by closing stdin.
- Capture recent stderr for diagnostics.

Other clients connect to the AppServer's WebSocket endpoint.

### 5.3 Hub Registry

Hub keeps a registry of known workspaces and currently managed AppServers. The registry is not authoritative over workspace data; it is only local process metadata.

Each entry includes:

| Field | Description |
|-------|-------------|
| `workspacePath` | Canonical absolute workspace root. |
| `displayName` | User-facing name, defaulting to directory name. |
| `state` | `stopped`, `starting`, `running`, `unhealthy`, `stopping`, or `exited`. |
| `pid` | OS process ID of the managed AppServer, if running. |
| `webSocketUrl` | Direct AppServer WebSocket URL for clients. |
| `dashboardUrl` | Per-workspace Dashboard URL if the AppServer reports one. |
| `serverVersion` | Version reported by AppServer initialize. |
| `lastStartedAt` | Last successful start time. |
| `lastSeenAt` | Last client ensure or health observation. |
| `lastExit` | Exit code, time, and recent stderr if the process exited. |

### 5.4 Client

Desktop, TUI, CLI, ACP, and future local clients use the same bootstrap shape:

1. Locate or start Hub.
2. Ask Hub to ensure an AppServer for the target workspace.
3. Connect to the returned AppServer WebSocket URL.
4. Perform the normal AppServer Protocol handshake.
5. Continue exactly as an AppServer WebSocket client.

Clients do not need to know how Hub supervises the process.

---

## 6. Hub Local API

### 6.1 API Boundary

Hub Local API is separate from AppServer Protocol.

It must not expose `thread/*`, `turn/*`, `approval/*`, `mcp/*`, `skills/*`, `workspace/config/*`, or protocol extension methods. Those belong to the AppServer.

### 6.2 Transport

Recommended v1 transport:

- HTTP JSON over loopback: `http://127.0.0.1:{hubPort}`.
- The port and bearer token are published in `~/.craft/hub/hub.lock`.
- All mutating calls require the bearer token.

Named pipes or Unix domain sockets may be added later as platform-specific optimizations, but the cross-platform contract should start with loopback HTTP because all clients can implement it.

### 6.3 Endpoints

#### `GET /v1/status`

Returns Hub process metadata.

```json
{
  "hubVersion": "0.2.0",
  "pid": 12345,
  "startedAt": "2026-04-29T08:00:00Z",
  "statePath": "C:\\Users\\user\\.craft\\hub",
  "appServers": {
    "running": 2,
    "starting": 0,
    "unhealthy": 0
  }
}
```

#### `POST /v1/appservers/ensure`

Ensures an AppServer exists for the given workspace and returns direct connection metadata.

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
  "state": "running",
  "pid": 23456,
  "webSocketUrl": "ws://127.0.0.1:43121/ws?token=...",
  "dashboardUrl": "http://127.0.0.1:43122/dashboard",
  "serverVersion": "0.0.1.0+abcdef",
  "startedByHub": true
}
```

If `startIfMissing` is `false` and no server exists, Hub returns `404`.

#### `GET /v1/appservers`

Lists known AppServers and workspaces.

#### `GET /v1/appservers/by-workspace?path=...`

Returns the registry entry for one workspace without starting it.

#### `POST /v1/appservers/stop`

Stops a managed AppServer.

Request:

```json
{
  "workspacePath": "F:\\dotcraft",
  "mode": "graceful"
}
```

`mode` values:

| Value | Meaning |
|-------|---------|
| `graceful` | Close AppServer stdin and wait for normal shutdown. |
| `force` | Kill the process tree after a short grace period. |

#### `POST /v1/appservers/restart`

Stops and starts the AppServer for a workspace, returning new connection metadata.

### 6.4 Error Shape

Hub API errors use a simple structured JSON shape:

```json
{
  "error": {
    "code": "workspaceNotFound",
    "message": "Workspace path does not exist.",
    "details": {
      "workspacePath": "F:\\missing"
    }
  }
}
```

Common error codes:

| Code | Meaning |
|------|---------|
| `hubNotReady` | Hub has started but cannot serve requests yet. |
| `workspaceNotFound` | The requested workspace path does not exist. |
| `workspaceLocked` | Another live AppServer appears to own the workspace. |
| `appServerStartFailed` | The child process failed before readiness. |
| `appServerUnhealthy` | A known process exists but does not respond. |
| `portUnavailable` | Hub could not allocate a usable local port. |
| `unauthorized` | Missing or invalid Hub API token. |

---

## 7. Client Bootstrap

### 7.1 Default Bootstrap

Local interactive clients should prefer Hub by default once the feature is enabled.

```
1. Determine target workspace path.
2. Locate Hub using ~/.craft/hub/hub.lock.
3. If Hub is alive:
   - call POST /v1/appservers/ensure.
4. If Hub is not alive and auto-start is enabled:
   - start `dotcraft hub` detached.
   - wait for hub.lock and /v1/status readiness.
   - call POST /v1/appservers/ensure.
5. Connect to the returned AppServer WebSocket URL.
6. Run the normal AppServer Protocol initialize / initialized handshake.
7. Continue normal client behavior.
```

### 7.2 Fallback

If Hub is disabled, unavailable, or explicitly bypassed, clients may use the existing behavior:

- CLI/TUI may spawn stdio `dotcraft app-server`.
- Desktop may use its existing AppServer connection path.
- `--remote` or explicit WebSocket URLs continue to connect directly to an AppServer.

The fallback path is for compatibility and debugging. It does not provide cross-client duplicate-process protection.

### 7.3 Client UX Requirements

Clients should present connection failures as AppServer availability problems, not as protocol incompatibilities:

- "Hub could not start AppServer for this workspace."
- "Another process appears to own this workspace."
- "Managed AppServer exited during startup."
- "Connected to Hub, but the workspace AppServer did not become ready."

Once connected to the AppServer WebSocket, existing client UX rules from Desktop and TUI specs apply unchanged.

---

## 8. Managed AppServer Lifecycle

### 8.1 States

```
stopped
   │ ensure(startIfMissing=true)
   ▼
starting
   │ process started + readiness handshake succeeds
   ▼
running
   │ health check fails / process exits
   ▼
unhealthy or exited
   │ restart / ensure
   ▼
starting

running
   │ stop requested / hub shutdown
   ▼
stopping
   │ process exits
   ▼
stopped
```

### 8.2 Start Sequence

1. Canonicalize the workspace path.
2. Validate that the path exists and is a directory.
3. Check the Hub registry for an already running managed AppServer.
4. Verify the process is alive and the WebSocket endpoint is reachable.
5. If valid, return the existing endpoint.
6. If not valid, acquire the workspace AppServer lock.
7. Allocate a loopback WebSocket port and optional token.
8. Start `dotcraft app-server` with cwd set to the workspace and `ws+stdio://127.0.0.1:{port}`.
9. Attach to stdio and stderr.
10. Perform AppServer Protocol initialize over stdio as the supervisor client.
11. Poll or connect to the WebSocket endpoint until ready.
12. Persist registry metadata.
13. Return the WebSocket URL to the caller.

### 8.3 Readiness

A managed AppServer is ready only when:

- The OS process is alive.
- The stdio supervisor handshake completed successfully.
- The WebSocket endpoint accepts connections.
- The workspace lock is still owned by the same process.

### 8.4 Health

Hub should perform lightweight health checks:

- Process liveness.
- WebSocket TCP reachability.
- Optional AppServer `initialize` probe using a short-lived WebSocket connection.

Hub should not subscribe to user threads or perform workspace operations just for health.

### 8.5 Shutdown

For graceful shutdown:

1. Mark the entry as `stopping`.
2. Close the supervisor stdio input stream.
3. Wait for AppServer to exit.
4. If it does not exit within the grace period, kill the process tree.
5. Release the workspace lock.
6. Update registry state.

Hub shutdown should stop AppServers it started unless configuration says to leave them running.

### 8.6 Restart

Restart should be explicit. Hub should not automatically restart an AppServer that exits while clients are connected unless a client subsequently calls `ensure` or the user requests restart.

This avoids surprising background work after a crash.

---

## 9. Locks, Registry, and State Files

### 9.1 Hub Lock

Hub writes `~/.craft/hub/hub.lock` atomically.

```json
{
  "pid": 12345,
  "apiBaseUrl": "http://127.0.0.1:42100",
  "token": "...",
  "startedAt": "2026-04-29T08:00:00Z",
  "version": "0.2.0"
}
```

Clients must verify the process is alive and `/v1/status` responds before trusting the lock. Stale locks are ignored.

### 9.2 Hub Registry

Hub stores registry state in `~/.craft/hub/appservers.json`.

The registry is best-effort recovery metadata. The live OS process and workspace lock are the source of truth.

### 9.3 Workspace AppServer Lock

Each managed AppServer workspace should have an advisory lock file under its `.craft/` directory, for example:

```text
<workspace>/.craft/runtime/appserver.lock
```

The lock records:

```json
{
  "owner": "dotcraft-hub",
  "hubPid": 12345,
  "appServerPid": 23456,
  "webSocketUrl": "ws://127.0.0.1:43121/ws",
  "startedAt": "2026-04-29T08:01:00Z"
}
```

Hub must hold an OS-level exclusive lock where the platform supports it. The JSON file is for diagnostics and stale-lock recovery; the file lock is the concurrency guard.

Standalone AppServer should eventually respect this lock on startup:

- If a live lock exists, fail with a clear diagnostic unless an explicit override is provided.
- If the lock is stale, remove it and continue.

This change is not an AppServer Protocol change. It is local process safety.

### 9.4 Path Canonicalization

Hub must canonicalize workspace paths before using them as registry keys:

- Resolve relative paths.
- Normalize directory separators.
- Resolve symlinks where practical.
- Use case-insensitive comparison on Windows.
- Treat paths that resolve to the same directory as the same workspace.

---

## 10. Port and Endpoint Management

### 10.1 AppServer WebSocket Ports

Hub owns port allocation for managed AppServer WebSocket endpoints.

Recommended policy:

- Bind to `127.0.0.1`.
- Allocate from a configurable range, default `43000-43999`.
- Retry on bind failure.
- Persist the chosen port only for diagnostics; do not require stable ports.

Hub may either:

- Choose a free port before launch and pass it to AppServer.
- Or, if AppServer later supports `port=0` with actual endpoint reporting, let the OS choose the port.

The first option is the v1 recommendation because it requires fewer AppServer startup changes.

### 10.2 Dashboard Ports

Hub v1 does not provide a unified Dashboard. Per-workspace Dashboard remains owned by each AppServer.

To avoid Dashboard conflicts, managed AppServer launch must use one of these policies:

| Policy | Description |
|--------|-------------|
| `disable` | Disable per-workspace Dashboard for managed AppServers. Simplest MVP. |
| `allocate` | Hub allocates a Dashboard port per workspace and passes it as a runtime override. |
| `existing-config` | Use workspace config as-is and report conflicts clearly. Compatibility fallback only. |

Recommended v1 policy: `allocate` if the current Dashboard host supports runtime port override; otherwise `disable` for the first milestone and add allocation before making Hub default.

### 10.3 Native Channels and Webhooks

Hub should not silently rewrite user-configured ports for native channels, bot webhooks, API modules, or external integrations.

If two workspaces configure incompatible callback ports or routes, Hub should report a conflict and leave the affected workspace AppServer unhealthy rather than mutating behavior. Future specs may define module-level port allocation contracts.

---

## 11. Security Model

### 11.1 Local-Only Default

Hub API and managed AppServer WebSocket endpoints bind to loopback only by default.

Hub must not expose managed AppServers on non-loopback interfaces in v1 unless a future remote-access spec defines authentication, authorization, and audit behavior.

### 11.2 Tokens

Hub API uses a bearer token stored in `hub.lock`. The file must be created with user-only permissions where the platform supports it.

Managed AppServer WebSocket endpoints should use per-process tokens when feasible:

- Hub generates a random token for each managed AppServer.
- Hub includes the token in the WebSocket URL returned to clients.
- AppServer enforces the token using the existing WebSocket token behavior.

### 11.3 Trust Boundary

Hub is a same-user local coordinator. It is not a security boundary against malicious processes running as the same OS user.

---

## 12. Compatibility

### 12.1 AppServer Protocol

No AppServer Protocol changes are required for Hub v1.

Clients still perform:

- `initialize`
- `initialized`
- `thread/list`
- `turn/start`
- approvals
- extension methods
- management methods

against the workspace AppServer.

### 12.2 Existing AppServer Modes

Existing modes remain valid:

| Mode | Status |
|------|--------|
| `stdio` | Supported for direct subprocess clients and debugging. |
| `websocket` | Supported for explicit remote/local endpoints. |
| `stdio + websocket` | Required for Hub-managed AppServers. |

### 12.3 Existing Clients

Client changes are limited to bootstrap:

- Add Hub discovery.
- Add `ensure` call.
- Prefer returned WebSocket connection over spawning stdio.
- Keep the existing direct AppServer fallback.

The normal wire client, event handling, approval handling, and UI state do not need Hub-specific protocol branches.

---

## 13. Technical Choices

### 13.1 Hub as DotCraft Host Module

Recommended:

- Module name: `hub`.
- Command: `dotcraft hub`.
- Host factory: `HubHostFactory`.
- Hub does not build a workspace `WorkspaceRuntime`.

Rationale: It keeps packaging simple and lets the same binary provide CLI, AppServer, Gateway, ACP, and Hub entry points.

### 13.2 HTTP JSON for Hub API

Recommended for v1.

Rationale:

- Cross-platform.
- Easy for C#, TypeScript, and Rust clients.
- Easy to inspect during debugging.
- No need to reuse AppServer Protocol for non-AppServer concerns.

Named pipes may be added later for stronger local access semantics.

### 13.3 Managed AppServer Transport

Required:

- `StdioAndWebSocket`.
- Hub uses stdio for supervision.
- End-user clients use WebSocket.

Rationale: Existing AppServer already supports this shape, and it gives Hub a graceful lifetime handle without becoming a protocol proxy.

### 13.4 Port Allocation

Recommended:

- Hub allocates from a configurable local range.
- Avoid `port=0` until AppServer can reliably report the actual bound endpoint to the supervisor.

### 13.5 Runtime Overrides

Hub should pass managed runtime settings as ephemeral launch overrides, not by editing workspace config.

Examples:

- AppServer mode.
- WebSocket host and port.
- WebSocket token.
- Dashboard port or disabled state.

This keeps workspace configuration user-owned and avoids surprising config file churn.

---

## 14. Open Questions

These choices should be resolved before implementation begins.

1. **Hub API transport final choice.** Is loopback HTTP acceptable for v1, or should Windows named pipes be required from the start?
2. **Dashboard policy.** Should Hub-managed AppServers disable Dashboard initially, or should we add runtime Dashboard port allocation before the first usable milestone?
3. **Managed override mechanism.** Should AppServer accept ephemeral overrides via CLI flags, environment variables, or an inherited temporary config file?
4. **Standalone lock enforcement.** Should direct `dotcraft app-server` fail when a Hub-managed lock exists, or warn first during a transition period?
5. **Hub auto-start default.** Should CLI/TUI auto-start Hub by default, or only use Hub when Desktop/tray has already started it?
6. **Idle shutdown.** Should v1 leave managed AppServers running until Hub exits, or should clients acquire leases so Hub can stop idle AppServers safely?

---

## 15. Iteration Plan

The Hub feature is delivered in milestone specs. Each milestone has its own behavior contract:

- [M1: Shell and Local Presence](hub-m1-shell.md)
- [M2: Protocol and Managed AppServer](hub-m2-protocol-managed-appserver.md)
- [M3: Client Adoption](hub-m3-client-adoption.md)
- [M4: Tray Management](hub-m4-tray-management.md)
- [M5: Runtime Completion](hub-m5-runtime-completion.md)

### M1: Shell and Local Presence

Goal: Establish Hub as a workspace-independent local process in the existing DotCraft binary.

Work:

- Add `dotcraft hub` host mode.
- Add global Hub state directory and single-instance lock.
- Add minimal local status/shutdown API.
- Support headless mode on every platform.
- Add optional Windows tray presence as a shell feature, not as the Hub core.

Outcome: Hub can run as a global local coordinator shell without depending on any workspace `.craft/` directory.

### M2: Protocol and Managed AppServer

Goal: Define and implement the lightweight Hub Protocol and managed AppServer lifecycle.

Work:

- Add Hub Protocol v1 for AppServer ensure/list/get/stop/restart.
- Add managed AppServer process supervision.
- Add workspace path canonicalization and duplicate ensure handling.
- Add Hub-owned local port allocation for managed endpoints.
- Add runtime override support for AppServer WebSocket and eligible services such as Dashboard, API, and AG-UI.
- Add lifecycle event stream and notification request reservation.

Outcome: Clients can ask Hub for a workspace endpoint and then connect directly to the returned AppServer WebSocket using the existing AppServer Protocol.

### M3: Client Adoption

Goal: Make Hub-managed local AppServer the default local connection path for interactive clients.

Work:

- Update Desktop local workspace bootstrap to use Hub.
- Update TUI local workspace bootstrap to use Hub.
- Update CLI local workspace bootstrap to use Hub.
- Preserve explicit Remote mode for direct WebSocket AppServer URLs.
- Move local AppServer restart/stop behavior to Hub Protocol.
- Hide or remove local port settings from normal local-mode UX.

Outcome: Desktop, TUI, and CLI reuse the same Hub-managed AppServer for the same local workspace while AppServer Protocol behavior remains unchanged after connection.

### M4: Tray Management

Goal: Add a Desktop-owned system tray manager for the headless Hub and Hub-managed AppServers.

Work:

- Add an independent Desktop tray/background process.
- Keep Hub itself headless and report `tray=false`.
- Show Hub and managed AppServer status in the tray menu.
- Open recent or running workspaces from the tray.
- Route AppServer restart/stop and Hub shutdown actions through Hub Protocol.
- Ensure Hub-managed AppServers run without visible console windows on Windows.

Outcome: Users can keep local DotCraft runtime running in the background and manage it from the system tray without binding tray lifetime to a workspace window.

### M5: Runtime Completion

Goal: Make the Hub-managed local runtime durable for long-running background use.

Work:

- Persist known AppServer registry state under `~/.craft/hub/appservers.json`.
- List both current in-memory managed AppServers and persisted known workspace entries.
- Add lightweight health checks for AppServers supervised by the current Hub process.
- Emit `appserver.unhealthy` when a managed AppServer stops responding without automatically restarting it.
- Keep notification display in Desktop tray while Hub only accepts requests and emits `notification.requested`.
- Let managed AppServers request basic task completed/failed notifications through Hub.

Outcome: Hub can survive restarts with useful known-workspace metadata, detect unhealthy managed AppServers, and route task notifications to the Desktop-owned tray surface.
