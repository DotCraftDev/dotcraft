# DotCraft Hub M1: Shell and Local Presence

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Draft |
| **Date** | 2026-04-29 |
| **Parent Spec** | [Hub Local Coordinator](hub-architecture.md) |

Purpose: Define the first Hub milestone: a workspace-independent local Hub process that can run from the existing DotCraft binary, expose a minimal local management surface, maintain single-instance presence, and optionally provide Windows tray entry points without managing AppServer processes yet.

---

## 1. Overview

M1 establishes the shape of Hub without taking ownership of workspace AppServers.

Hub is a global process for the current OS user. It is started as `dotcraft hub` from the existing DotCraft binary. It must not bind itself to the current working directory as a DotCraft workspace, must not require a workspace `.craft/` directory, and must not load a workspace runtime.

M1 separates two concerns:

- **Hub Core**: a cross-platform headless local coordinator process.
- **Hub Presence**: optional platform integration for user-visible entry points, initially Windows tray.

M1 is intentionally small. It proves that DotCraft can host a long-lived, workspace-independent local process before adding AppServer management and client adoption.

---

## 2. Goal

At the end of M1:

- `dotcraft hub` starts a single global Hub process for the current user.
- Hub writes a discoverable lock file under global DotCraft state.
- Hub exposes a minimal local API for status and shutdown.
- Hub stores state only under global DotCraft home.
- Hub can run headless on every supported platform.
- On Windows builds that include tray support, Hub may expose a tray icon with basic actions.

---

## 3. Scope

M1 includes:

- A Hub host mode in the existing DotCraft binary.
- Global state directory creation, for example `~/.craft/hub/`.
- A single-instance lock file, for example `~/.craft/hub/hub.lock`.
- A minimal local API with process status and graceful shutdown.
- Startup, readiness, and shutdown behavior.
- Optional Windows tray presence with simple actions.
- User-facing diagnostics for startup conflicts and stale locks.

M1 does not require:

- Starting, stopping, or supervising workspace AppServer processes.
- Allocating AppServer, Dashboard, API, or AG-UI ports.
- Modifying Desktop, TUI, or CLI default connection behavior.
- Delivering cross-workspace notifications.
- Adding multi-workspace UI.

---

## 4. Non-Goals

- Hub must not become a workspace runtime.
- Hub must not call workspace-bound service registration for its own process.
- Hub must not read or mutate workspace `.craft/` state.
- Hub must not proxy AppServer Protocol traffic.
- Hub must not require Electron, Desktop, or another GUI process to run.
- M1 must not make Hub mandatory for existing clients.

---

## 5. Behavioral Contract

### 5.1 Process Shape

Hub runs from the existing DotCraft binary:

```bash
dotcraft hub
```

The process is global to the current OS user. Its state belongs under global DotCraft home, not under a workspace.

Hub startup from any working directory must produce the same global Hub instance. Running `dotcraft hub` from inside a project directory must not cause Hub to treat that directory as its own workspace.

### 5.2 Single Instance

Only one Hub process should run per OS user.

On startup, Hub checks the global lock file:

- If the lock points to a live Hub, the new process exits with a clear message.
- If the lock is stale, Hub may replace it.
- If the lock is malformed, Hub may quarantine or replace it and report a warning.

The lock file must contain enough information for future clients to discover Hub:

```json
{
  "pid": 12345,
  "apiBaseUrl": "http://127.0.0.1:42100",
  "token": "...",
  "startedAt": "2026-04-29T08:00:00Z",
  "version": "0.1.0"
}
```

The token is a local API token. It is not a security boundary against same-user malicious processes, but it prevents accidental unauthenticated calls.

### 5.3 Local API

M1 Hub API is intentionally minimal:

| Endpoint | Purpose |
|----------|---------|
| `GET /v1/status` | Report Hub process health and metadata. |
| `POST /v1/shutdown` | Request graceful Hub shutdown. |

The M1 status response must identify that AppServer management is not active yet:

```json
{
  "hubVersion": "0.1.0",
  "pid": 12345,
  "startedAt": "2026-04-29T08:00:00Z",
  "capabilities": {
    "appServerManagement": false,
    "portManagement": false,
    "events": false,
    "notifications": false,
    "tray": true
  }
}
```

All mutating endpoints require the lock-file token.

### 5.4 Tray Presence

Hub Core must work without tray support. Tray is optional presence, not the product boundary.

On Windows, Hub may expose a system tray icon. M1 tray actions are limited to:

- Show Hub status.
- Open Desktop if Desktop is installed or discoverable.
- Open global DotCraft home or Hub logs if supported.
- Exit Hub.

M1 tray does not need rich per-workspace menus. It may reserve UI structure for recent workspaces, but those entries are not required until M2 or M3 has workspace registry data.

If tray initialization fails, Hub should continue in headless mode and report a warning.

### 5.5 Logging and Diagnostics

Hub must produce diagnostics that help distinguish:

- Hub already running.
- Stale lock recovered.
- Local API bind failure.
- Token failure.
- Tray unavailable.
- Graceful shutdown requested.

Diagnostics must not be written into a workspace `.craft/` directory.

---

## 6. Lifecycle

### 6.1 Startup

Hub startup:

1. Resolve global DotCraft home.
2. Create global Hub state directory.
3. Check single-instance lock.
4. Bind the local API on loopback.
5. Write `hub.lock` atomically.
6. Initialize optional platform presence.
7. Enter the main run loop.

Hub is ready only after the local API responds to `GET /v1/status`.

### 6.2 Shutdown

Hub shutdown:

1. Stop accepting new local API requests except in-flight shutdown.
2. Stop optional platform presence.
3. Remove or mark the lock file stale.
4. Flush logs and state.
5. Exit.

M1 shutdown does not need to stop AppServers because Hub does not manage any.

---

## 7. Constraints and Compatibility

- M1 must not break direct `dotcraft`, `dotcraft app-server`, `dotcraft acp`, or `dotcraft gateway` behavior.
- M1 must not require client changes.
- M1 local API is not AppServer Protocol.
- Hub API binds to loopback only.
- Headless mode is the baseline behavior for non-Windows platforms.
- Windows tray support must be optional at runtime.

---

## 8. Acceptance Checklist

- `dotcraft hub` starts without a workspace `.craft/` directory.
- Starting Hub from two different working directories resolves to the same global process.
- A second Hub process detects the live instance and exits cleanly.
- A stale lock can be recovered.
- `GET /v1/status` returns Hub metadata and M1 capability flags.
- `POST /v1/shutdown` stops Hub when authorized.
- Existing AppServer and CLI flows still work when Hub is not used.
- On platforms without tray support, Hub runs headless.
- On Windows with tray support, tray failure does not crash Hub Core.

---

## 9. Open Questions

1. Should M1 include an explicit `--headless` flag, or should headless be selected automatically when tray is unavailable?
2. Should Windows tray be enabled by default in `dotcraft hub`, or only by `dotcraft hub --tray` until the UX is stable?
3. Should the local API port use a fixed default with fallback, or always use a random loopback port published through `hub.lock`?
