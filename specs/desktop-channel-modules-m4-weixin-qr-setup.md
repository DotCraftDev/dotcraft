# DotCraft Desktop Channel Modules — M4: Weixin QR Interactive Setup

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Draft |
| **Date** | 2026-04-15 |
| **Parent Spec** | [typescript-external-channel-module-contract.md](typescript-external-channel-module-contract.md) |
| **Related Specs** | [desktop-channel-modules-m2-discovery-and-config-ui.md](desktop-channel-modules-m2-discovery-and-config-ui.md), [desktop-channel-modules-m3-subprocess-lifecycle.md](desktop-channel-modules-m3-subprocess-lifecycle.md) |

Purpose: Enable Desktop to handle the Weixin adapter's QR code login flow — detecting the auth-required state, displaying the QR image to the user, and transitioning to the connected state after a successful scan. After this milestone, the Weixin channel works end-to-end from Desktop.

---

## Table of Contents

- [1. Overview](#1-overview)
- [2. Goal](#2-goal)
- [3. Scope](#3-scope)
- [4. Non-Goals](#4-non-goals)
- [5. Background: Weixin QR Login Flow](#5-background-weixin-qr-login-flow)
- [6. QR File Watching](#6-qr-file-watching)
- [7. IPC Contract](#7-ipc-contract)
- [8. Renderer: QR Setup Panel](#8-renderer-qr-setup-panel)
- [9. State Machine](#9-state-machine)
- [10. Re-authentication on Session Expiry](#10-re-authentication-on-session-expiry)
- [11. Constraints and Compatibility](#11-constraints-and-compatibility)
- [12. Acceptance Checklist](#12-acceptance-checklist)
- [13. Open Questions](#13-open-questions)

---

## 1. Overview

The Weixin adapter requires QR code login before it can operate. In the CLI, the QR is rendered in the terminal via `qrcode-terminal`. In Desktop's subprocess model, the adapter runs as a child process and has no direct UI surface.

The adapter writes QR artifacts to a known filesystem path: `.craft/tmp/<moduleId>/qr.png`. Desktop can watch this path after spawning the Weixin subprocess to detect when a QR code is available, display it in the UI, and transition to the connected state when the user scans the code.

---

## 2. Goal

Provide a seamless QR login experience for the Weixin channel in Desktop, matching the convenience of the CLI flow while leveraging the Desktop UI for a richer visual presentation.

---

## 3. Scope

- Watch `.craft/tmp/weixin-standard/` for QR artifacts after spawning the Weixin subprocess.
- Relay QR image data to the renderer via IPC.
- Render a QR setup panel in the module config area.
- Detect login success via `channel/status` polling (from M3).
- Handle QR expiry and re-authentication.

---

## 4. Non-Goals

- Modifying the Weixin adapter's QR generation code.
- Adding QR flows to modules that do not require interactive setup.
- Supporting other interactive setup mechanisms (OAuth redirect, etc.).
- QR code rendering from URL in the renderer (Desktop displays the image file, not a generated QR).

---

## 5. Background: Weixin QR Login Flow

The Weixin adapter (`@dotcraft/channel-weixin`) follows this lifecycle when credentials are missing:

1. **`starting`** — subprocess begins.
2. **`authRequired`** — adapter detects no usable credentials. It fetches a QR code URL from the Tencent iLink API.
3. **QR artifact written** — the adapter writes:
   - `.craft/tmp/weixin-standard/qr-url.txt` — the QR image URL.
   - `.craft/tmp/weixin-standard/qr.png` — the QR image file (fetched from the URL).
4. **User scans QR** — the adapter polls `get_qrcode_status` until `confirmed`.
5. **`starting`** → **`ready`** — credentials obtained, adapter connects to AppServer.
6. Credentials are persisted to `.craft/state/weixin-standard/credentials.json` for future sessions.

On session expiry (existing credentials become invalid):
1. **`authExpired`** → **`authRequired`** — adapter re-enters the QR flow.
2. New QR artifacts overwrite the old files at the same path.

Key detail: the QR image URL may expire (server-side timeout ~120s), at which point the adapter fetches a new QR and overwrites the files. Desktop must handle file changes, not just initial creation.

---

## 6. QR File Watching

### 6.1 Watch Target

The main process watches the directory:

```
<workspacePath>/.craft/tmp/<moduleId>/
```

For the bundled Weixin module, this is `.craft/tmp/weixin-standard/`.

### 6.2 Watch Trigger

File watching starts when:
- The module's manifest has `requiresInteractiveSetup: true`.
- The subprocess has been spawned (via `modules:start`).

File watching stops when:
- `channel/status` reports the channel as `running` (login succeeded).
- The subprocess is stopped by the user.
- The subprocess crashes and is not restarted.

### 6.3 Watch Implementation

Use `fs.watch` on the target directory (or `fs.watchFile` on `qr.png` specifically).

When the watcher detects a change to `qr.png`:
1. Wait a short debounce period (200ms) to let the write complete.
2. Read the file as a `Buffer`.
3. Convert to a data URL: `data:image/png;base64,<base64data>`.
4. Send to the renderer via `modules:qr-update` push channel.

### 6.4 Directory Pre-creation

The adapter creates the `tmp/<moduleId>/` directory itself. However, the watcher must handle the case where the directory does not yet exist when watching starts. Strategy:
1. If the directory exists, watch it immediately.
2. If it does not exist, poll for the directory's existence every 500ms (up to 30 seconds), then start watching once it appears.
3. If the directory never appears within the timeout, log a warning and give up.

---

## 7. IPC Contract

### 7.1 Push: `modules:qr-update`

**Direction**: main → renderer.

**Payload**:

```typescript
interface QrUpdatePayload {
  moduleId: string;
  qrDataUrl: string | null;  // data:image/png;base64,... or null when cleared
  timestamp: number;          // Date.now() at read time
}
```

Sent when:
- A new or updated `qr.png` is detected → `qrDataUrl` contains the image.
- Login succeeds and the QR flow ends → `qrDataUrl: null` (clear signal).

### 7.2 Invoke: `modules:qr-status`

**Request**: `{ moduleId: string }`

**Response**: `{ active: boolean; qrDataUrl: string | null }`

Allows the renderer to query the current QR state on mount (in case it missed a push event).

---

## 8. Renderer: QR Setup Panel

### 8.1 Placement

The QR panel appears inside `ModuleConfigForm` when the module has `requiresInteractiveSetup: true` and the module is in a QR-active state.

It replaces the normal config field area (config fields are below or hidden during QR flow, since the relevant credentials are obtained interactively).

### 8.2 Layout

```
┌─────────────────────────────────────┐
│ [Weixin Logo]  Weixin (iLink)       │
│                                     │
│ ┌─────────────────────────────────┐ │
│ │                                 │ │
│ │         [QR Code Image]        │ │
│ │          200x200 px            │ │
│ │                                 │ │
│ └─────────────────────────────────┘ │
│                                     │
│   Scan with WeChat to log in        │
│                                     │
│   ○ Waiting for scan...             │
│                                     │
└─────────────────────────────────────┘
```

### 8.3 QR Panel States

| State | Display |
|-------|---------|
| **Waiting for QR** | Spinner + "Starting Weixin adapter, preparing QR code..." |
| **QR available** | QR image + "Scan with WeChat to log in" + pulsing dot "Waiting for scan..." |
| **QR expired (new QR loading)** | Previous QR image fades + spinner overlay + "Refreshing QR code..." |
| **Login success** | Green checkmark + "Login successful!" → transitions to normal connected view after 2 seconds |
| **Error** | Red icon + error message + "Retry" button |

### 8.4 State Transitions

The renderer determines the QR panel state from:
- `modules:qr-update` push events (presence or absence of `qrDataUrl`).
- `modules:status-changed` push events from M3 (process state + connected status).

```
                 ┌──────────────┐
    start ──────►│ waitingForQr │
                 └──────┬───────┘
                        │ qr-update (qrDataUrl != null)
                        ▼
                 ┌──────────────┐
            ┌───►│ qrAvailable  │◄───┐
            │    └──────┬───────┘    │
            │           │            │ qr-update (new qrDataUrl)
            │           │            │
            │    ┌──────┴───────┐    │
            │    │ channel/status│────┘
            │    │ not connected │
            │    └──────┬───────┘
            │           │ channel/status: running
            │           ▼
            │    ┌──────────────┐
            │    │ loginSuccess │
            │    └──────────────┘
            │
            │ process crashed
            ▼
     ┌──────────────┐
     │    error      │
     └──────────────┘
```

### 8.5 Config Fields During QR Flow

When the QR panel is showing, config fields that are NOT `interactiveSetupOnly` (e.g. `dotcraft.wsUrl`, `weixin.apiBaseUrl`) remain visible below the QR panel so the user can adjust server settings if needed. Fields with `interactiveSetupOnly: true` are always hidden (they represent credentials obtained from the QR flow).

---

## 9. State Machine

The main process maintains a `QrWatchState` per module:

```typescript
interface QrWatchState {
  moduleId: string;
  phase: "idle" | "waitingForDir" | "watching" | "loginComplete";
  lastQrDataUrl: string | null;
  watcher: FSWatcher | null;
  dirPollTimer: NodeJS.Timeout | null;
}
```

Transitions:
- `idle` → `waitingForDir`: subprocess spawned for a `requiresInteractiveSetup` module.
- `waitingForDir` → `watching`: target directory appears.
- `watching` → `loginComplete`: `channel/status` reports the channel as `running`.
- `watching` → `idle`: subprocess stopped or crashed without restart.
- `loginComplete` → `watching`: `channel/status` reports the channel as not `running` (session expired, re-auth).
- `loginComplete` → `idle`: subprocess stopped.

On transition to `idle`, the watcher and any timers are cleaned up.

---

## 10. Re-authentication on Session Expiry

When the Weixin adapter's session expires (e.g. credentials expire after hours of use):

1. The adapter transitions to `authExpired` → `authRequired` internally.
2. The adapter writes a new `qr.png` to the same path.
3. `channel/status` stops reporting the channel as `running`.
4. Desktop's status poller (from M3) detects the channel is no longer `running`.
5. Desktop's QR watcher (if still active) detects the new `qr.png`.
6. If the QR watcher was in `loginComplete` state, it transitions back to `watching`.
7. The renderer shows the QR panel again with the new QR code.

For this to work, the QR file watcher must remain active (but paused / low-frequency) even after initial login success, or be restarted when the channel status changes from `running` to not `running`.

Recommended approach: keep the watcher alive for the entire lifetime of the subprocess. When in `loginComplete` state, the watcher still fires on file changes but the renderer suppresses the QR panel (since the channel is connected). When the channel disconnects, the watcher's existing `lastQrDataUrl` or new file events immediately surface the QR panel.

---

## 11. Constraints and Compatibility

- File watching must not cause excessive I/O. Use `fs.watch` (event-based) rather than `fs.watchFile` (polling-based) where possible. Apply a debounce of 200ms.
- The QR image file may be written in two steps by the adapter (partial write then complete). The debounce handles this.
- The `qr.png` path convention (`.craft/tmp/<moduleId>/qr.png`) is part of the adapter SDK contract (`resolveModuleTempPath`). Desktop depends on this path.
- This milestone only supports the Weixin QR flow. Other modules with `requiresInteractiveSetup: true` would follow the same mechanism if they write QR artifacts to the same conventional path.
- Desktop must not delete or modify files in `.craft/tmp/<moduleId>/`; it is read-only for Desktop.

---

## 12. Acceptance Checklist

- [ ] File watcher starts after spawning a `requiresInteractiveSetup: true` module subprocess.
- [ ] File watcher detects creation of `.craft/tmp/weixin-standard/qr.png`.
- [ ] QR image data is sent to renderer via `modules:qr-update`.
- [ ] Renderer shows QR panel with the QR image when data is received.
- [ ] QR panel shows "Waiting for scan..." status.
- [ ] When `channel/status` reports the channel as `running`, QR panel transitions to "Login successful!".
- [ ] After success transition, the normal module status (connected) displays.
- [ ] When `qr.png` is overwritten (QR expired), the renderer updates the displayed QR image.
- [ ] On session expiry (channel disconnects after previous success), the QR panel re-appears with the new QR.
- [ ] `modules:qr-status` IPC returns current QR state on renderer mount.
- [ ] File watcher is cleaned up when the subprocess stops.
- [ ] File watcher handles the case where the temp directory does not exist yet (polls for it).
- [ ] Weixin channel works end-to-end: configure → enable → scan QR → connected.

---

## 13. Open Questions

- Should Desktop show a notification (OS-level or in-app) when QR re-authentication is required? This would alert the user even if they are not on the Channels view. Recommendation: add an in-app notification bar (e.g. "Weixin requires re-authentication — click to open Channels") in a future polish pass.
- Should the QR panel offer a "Copy QR URL" button for users who want to open the QR image in another context? Recommendation: defer; the rendered image is sufficient for most use cases.
- How long should Desktop wait for the temp directory to appear before giving up? Recommendation: 30 seconds with 500ms polling. If the adapter fails to reach the QR stage within 30 seconds, something is wrong and the error state from M3 will surface.
