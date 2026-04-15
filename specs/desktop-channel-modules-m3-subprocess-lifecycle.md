# DotCraft Desktop Channel Modules — M3: Subprocess Lifecycle and Status

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Draft |
| **Date** | 2026-04-15 |
| **Parent Spec** | [typescript-external-channel-module-contract.md](typescript-external-channel-module-contract.md) |
| **Related Specs** | [desktop-channel-modules-m1-manifest-and-build-pipeline.md](desktop-channel-modules-m1-manifest-and-build-pipeline.md), [desktop-channel-modules-m2-discovery-and-config-ui.md](desktop-channel-modules-m2-discovery-and-config-ui.md) |

Purpose: Enable Desktop to spawn, monitor, and stop TypeScript channel adapter subprocesses, and display live connection status in the channel cards. After this milestone, a module like feishu (which does not require interactive setup) works end-to-end: configure in Desktop, enable, and the adapter connects to AppServer.

---

## Table of Contents

- [1. Overview](#1-overview)
- [2. Goal](#2-goal)
- [3. Scope](#3-scope)
- [4. Non-Goals](#4-non-goals)
- [5. Module Process Manager](#5-module-process-manager)
- [6. Subprocess Launch Contract](#6-subprocess-launch-contract)
- [7. Enable / Disable Flow](#7-enable--disable-flow)
- [8. Process Monitoring](#8-process-monitoring)
- [9. Status Polling and Display](#9-status-polling-and-display)
- [10. IPC Contract](#10-ipc-contract)
- [11. Renderer Integration](#11-renderer-integration)
- [12. Lifecycle on App Quit and Workspace Switch](#12-lifecycle-on-app-quit-and-workspace-switch)
- [13. Constraints and Compatibility](#13-constraints-and-compatibility)
- [14. Acceptance Checklist](#14-acceptance-checklist)
- [15. Open Questions](#15-open-questions)

---

## 1. Overview

After M2, Desktop can discover modules and save their configuration. However, enabling a module has no runtime effect — no subprocess is spawned, and no adapter connects to AppServer.

M3 adds the `ModuleProcessManager` in the Electron main process. When the user enables a configured module, Desktop spawns the adapter CLI as a child process with the appropriate flags. The manager monitors process health and periodically polls AppServer's `channel/status` to determine whether the adapter has connected successfully. Status is relayed to the renderer for display in channel cards.

---

## 2. Goal

Make configured TypeScript channel modules functional by managing their adapter subprocesses from the Desktop main process, with live status feedback in the UI.

---

## 3. Scope

- Implement `ModuleProcessManager` in the Electron main process.
- Spawn adapter CLI subprocesses with `--workspace <path>`.
- Monitor child process exit and restart on unexpected crashes.
- Poll `channel/status` via AppServer and relay per-channel status to the renderer.
- Add enable/disable toggle behavior to `ModuleConfigForm`.
- Display live status in module channel cards.
- Clean up subprocesses on app quit and workspace switch.

---

## 4. Non-Goals

- QR code display or interactive authentication (M4).
- Variant substitution (M5).
- Auto-starting modules on Desktop launch (deferred; can be added as a future enhancement).
- Subprocess management for non-TypeScript modules.

---

## 5. Module Process Manager

### 5.1 Class: `ModuleProcessManager`

A main-process service that tracks running module subprocesses keyed by `moduleId`.

```typescript
interface ManagedModuleProcess {
  moduleId: string;
  channelName: string;
  process: ChildProcess;
  state: "starting" | "running" | "stopping" | "stopped" | "crashed";
  restartCount: number;
  lastExitCode: number | null;
}
```

### 5.2 Singleton Lifetime

One `ModuleProcessManager` instance exists per Desktop window. It is created during `registerIpcHandlers` and disposed during `teardownRuntime`.

---

## 6. Subprocess Launch Contract

### 6.1 Resolving the CLI Executable

The adapter CLI is a Node.js script. The executable path is resolved from the module's `absolutePath` (set during discovery):

```
node <absolutePath>/dist/cli.js --workspace <workspacePath>
```

Where:
- `node` is the system Node.js binary (or a bundled one if Desktop ships Node).
- `<absolutePath>` is the module package directory (e.g. `resources/modules/channel-feishu/`).
- `<workspacePath>` is the current Desktop workspace path.

### 6.2 CLI Entry Point

Each adapter package has a `dist/cli.js` that accepts:
- `--workspace <path>` — sets the workspace root, from which `.craft/` and config files are resolved.
- `--config <path>` — optional override for the config file path.

In workspace mode, the CLI reads `.craft/<configFileName>` and connects to AppServer via WebSocket.

### 6.3 Environment

The subprocess inherits the Desktop process environment. No additional environment variables are set by default.

### 6.4 Working Directory

The subprocess `cwd` is set to `<workspacePath>`.

### 6.5 stdio

- **stdout**: piped, logged to Desktop's console (debug level).
- **stderr**: piped, logged to Desktop's console (warn level).
- **stdin**: not used (piped but not written to).

---

## 7. Enable / Disable Flow

### 7.1 Enabling a Module

1. User toggles "Enable" in `ModuleConfigForm`.
2. Renderer calls `modules:start` IPC with `{ moduleId }`.
3. Main process:
   a. Reads the module's config file to verify it exists and has required fields.
   b. If config is missing or invalid, returns an error without spawning.
   c. Spawns the adapter CLI subprocess.
   d. Sets the managed process state to `"starting"`.
   e. Returns `{ ok: true }`.
4. The subprocess connects to AppServer; `channel/status` eventually reports it as `running`.

### 7.2 Disabling a Module

1. User toggles "Disable" or clicks Stop.
2. Renderer calls `modules:stop` IPC with `{ moduleId }`.
3. Main process:
   a. Sends SIGTERM to the child process (or `process.kill()` on Windows).
   b. Waits up to 5 seconds for graceful exit.
   c. If the process does not exit, sends SIGKILL.
   d. Sets state to `"stopped"`.
   e. Returns `{ ok: true }`.

### 7.3 Config Change While Running

If the user saves new config while the module is running, the renderer shows a toast: "Configuration saved. Restart the channel for changes to take effect." The user must manually stop and re-enable the module. Automatic restart on config change is out of scope for this milestone.

---

## 8. Process Monitoring

### 8.1 Exit Handling

When a child process exits:
- If exit code is 0 and the state was `"stopping"`: set state to `"stopped"` (graceful shutdown).
- If exit code is non-zero or the process was not being stopped: set state to `"crashed"`.
- Log the exit code and signal.

### 8.2 Crash Restart Policy

On unexpected crash:
- Increment `restartCount`.
- If `restartCount <= 3` and last crash was more than 10 seconds after spawn: restart automatically.
- If `restartCount > 3` or crash occurred within 10 seconds: set state to `"crashed"` and do not restart. The user must manually re-enable.

### 8.3 Restart Reset

`restartCount` resets to 0 when:
- The process has been running for more than 60 seconds without crashing.
- The user manually stops and re-enables the module.

---

## 9. Status Polling and Display

### 9.1 Polling Mechanism

Desktop already communicates with AppServer via `WireProtocolClient`. The `channel/status` RPC returns an array of `{ name, category, enabled, running }` for all channels.

A status poller runs in the main process:
- When at least one module subprocess is in `"starting"` or `"running"` state, poll `channel/status` every 3 seconds.
- When no modules are running, polling is paused.

### 9.2 Status Derivation

For each running module, Desktop derives a display status by combining:
- **Process state** from `ModuleProcessManager` (`starting` / `running` / `stopped` / `crashed`).
- **Channel status** from `channel/status` RPC (matched by `channelName`, case-insensitive).

| Process State | channel/status `running` | Display Status |
|---------------|--------------------------|----------------|
| starting | false or absent | `connecting` |
| starting/running | true | `connected` |
| running | false | `enabledNotConnected` |
| stopped | — | `notConfigured` or `stopped` |
| crashed | — | `error` |

### 9.3 Push to Renderer

Status updates are pushed to the renderer via `webContents.send('modules:status-changed', statusMap)` whenever the derived status changes. The `statusMap` is:

```typescript
type ModuleStatusMap = Record<string, {
  processState: string;
  connected: boolean;
  restartCount: number;
  lastExitCode: number | null;
}>
```

---

## 10. IPC Contract

### 10.1 `modules:start`

**Request**: `{ moduleId: string }`

**Response**: `{ ok: boolean; error?: string }`

Starts the adapter subprocess for the given module.

### 10.2 `modules:stop`

**Request**: `{ moduleId: string }`

**Response**: `{ ok: boolean; error?: string }`

Stops the adapter subprocess for the given module.

### 10.3 `modules:running`

**Request**: no arguments.

**Response**: `ModuleStatusMap` — current status of all managed modules.

### 10.4 Push: `modules:status-changed`

**Direction**: main → renderer.

**Payload**: `ModuleStatusMap`.

Sent whenever a process state change or `channel/status` poll produces a different derived status.

---

## 11. Renderer Integration

### 11.1 Enable Toggle in ModuleConfigForm

`ModuleConfigForm` gains an "Enable channel" toggle at the top of the form (similar to the existing external channel toggle):
- Toggle on → calls `modules:start`.
- Toggle off → calls `modules:stop`.
- Toggle is disabled while the module is in `"starting"` or `"stopping"` state.

The toggle state is derived from the module status map, not from a config field. A module is "enabled" in the UI if its process is in `starting`, `running`, or `connected` state.

### 11.2 Status in Channel Cards

Module `ChannelCard` status is derived from `ModuleStatusMap`:
- `connected` → green dot, "Connected" label.
- `connecting` → yellow dot, "Connecting..." label.
- `enabledNotConnected` → yellow dot, "Not connected" label.
- `error` → red dot, "Error" label.
- `stopped` or absent → gray dot, "Stopped" or "Not configured" label.

### 11.3 Error Display

When a module is in `error` (crashed) state, the config form shows:
- A red banner: "Channel process exited unexpectedly (exit code: N). Check logs for details."
- A "Restart" button that calls `modules:start`.

---

## 12. Lifecycle on App Quit and Workspace Switch

### 12.1 App Quit

During `teardownRuntime` (triggered by `before-quit` or window close):
1. `ModuleProcessManager.stopAll()` sends SIGTERM to all running module subprocesses.
2. Wait up to 5 seconds for all to exit.
3. SIGKILL any remaining.

### 12.2 Workspace Switch

When the user switches workspaces:
1. `ModuleProcessManager.stopAll()` — stops all running modules.
2. `ModuleScanner` rescans (bundled modules are the same; user modules may differ per workspace).
3. Status resets for all modules.
4. Modules do not auto-start in the new workspace; the user must enable them.

---

## 13. Constraints and Compatibility

- The subprocess must be a Node.js process; Desktop must either rely on the system `node` or bundle a Node.js runtime. For this milestone, system `node` on PATH is assumed. Bundling Node is an open question.
- Subprocess management must not interfere with AppServer's own external channel subprocess management. Desktop-managed modules are not configured via `externalChannel/upsert`; they connect directly as WebSocket clients.
- Desktop's `WireProtocolClient` (stdio or WebSocket to AppServer) is used for `channel/status` polling. This client must already be connected before polling can start.
- On Windows, child process termination uses `process.kill(pid)` which sends `SIGTERM`; if that does not work, `taskkill /pid <pid> /f` may be needed. Handle both gracefully.

---

## 14. Acceptance Checklist

- [ ] `ModuleProcessManager` can spawn an adapter CLI subprocess for a configured module.
- [ ] Subprocess receives `--workspace <path>` and reads config from `.craft/<configFileName>`.
- [ ] Enable toggle in `ModuleConfigForm` starts the subprocess.
- [ ] Disable toggle stops the subprocess gracefully (SIGTERM → SIGKILL fallback).
- [ ] Subprocess stdout/stderr are logged to Desktop console.
- [ ] Exit code 0 with intentional stop sets state to `stopped`.
- [ ] Non-zero exit without intentional stop triggers crash restart (up to 3 times).
- [ ] `channel/status` polling detects when the adapter connects to AppServer.
- [ ] Module card shows `connected` when the adapter is running and connected.
- [ ] Module card shows `connecting` during startup.
- [ ] Module card shows `error` after crash with no remaining restarts.
- [ ] Error banner with exit code and restart button appears on crash.
- [ ] Config save while running shows "restart required" toast.
- [ ] All module subprocesses are stopped on app quit.
- [ ] All module subprocesses are stopped on workspace switch.
- [ ] Feishu module works end-to-end: configure → enable → connected.

---

## 15. Open Questions

- Should Desktop bundle a Node.js runtime or require `node` on PATH? Bundling ensures reliability but increases package size (~50MB). Relying on system node is simpler but may fail if the user doesn't have Node installed. Recommendation: for this milestone, require system `node`; add a startup check that warns if `node` is not found. Bundling can be addressed later.
- Should modules auto-start on Desktop launch if they were enabled when the user last quit? This is a UX convenience but adds complexity (persisting enabled state). Recommendation: defer to M5 or a future enhancement; users manually enable after launch for now.
- Should the crash restart policy be configurable? Recommendation: not for this milestone; hardcoded policy is sufficient.
