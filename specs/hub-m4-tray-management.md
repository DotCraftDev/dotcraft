# DotCraft Hub M4: Tray Management

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Draft |
| **Date** | 2026-04-30 |
| **Parent Spec** | [Hub Local Coordinator](hub-architecture.md), [Hub M3](hub-m3-client-adoption.md), [Desktop Client](desktop-client.md) |

Purpose: Define the fourth Hub milestone: Desktop provides a system tray management surface for the local headless Hub and Hub-managed workspace AppServers.

---

## 1. Overview

M4 adds a Desktop-owned tray manager without moving UI responsibilities into the C# Hub.

Hub remains a headless local coordinator. The tray is an Electron background process that discovers or starts Hub, reads Hub state through Hub Protocol, and offers quick local management actions.

The tray process is independent from workspace windows. A user may close every visible Desktop window while the tray process, Hub, and managed AppServers continue running.

---

## 2. Goal

At the end of M4:

- Hub-managed AppServers no longer show console windows when started by Hub on Windows.
- Desktop starts or reuses one tray background process per OS user.
- The tray process survives after all workspace windows are closed.
- The tray menu shows local Hub and AppServer status.
- The tray menu can open Desktop windows for recent or running workspaces.
- The tray menu can restart or stop managed AppServers.
- Exiting from the tray stops Hub and all Hub-managed AppServers.

---

## 3. Scope

M4 includes:

- A Desktop `--tray` mode that creates no workspace window.
- A global Desktop tray lock under user-local DotCraft state.
- Tray menu status backed by Hub Protocol.
- Tray actions for opening workspace windows and managing Hub AppServers.
- Hub shutdown integration for the tray Exit command.
- Windows hidden launch behavior for Hub-managed AppServers.

M4 does not require a full multi-workspace Desktop management UI.

---

## 4. Process Model

The tray manager is a separate Electron process:

```text
DotCraftDesktop --tray
  └─ owns system tray icon and menu

DotCraftDesktop --workspace <path>
  └─ owns one workspace window and one AppServer WebSocket client

dotcraft hub
  └─ owns Hub Protocol and AppServer supervision
      └─ dotcraft app-server per managed workspace
```

Only the tray process creates the system tray icon. Workspace window processes must not create tray icons.

The tray process does not connect to AppServer Protocol and does not acquire workspace Desktop locks.

---

## 5. Tray Ownership

Desktop uses a per-user tray lock file to enforce one tray process.

If `DotCraftDesktop --tray` starts while another live tray process owns the lock, it exits successfully without creating a second icon.

Normal Desktop window processes should ensure the tray process exists after startup. This is best-effort and must not block workspace opening.

---

## 6. Tray Menu Contract

The tray menu should include:

- Hub status: running, starting, or offline.
- Running or known Hub-managed AppServers, grouped by workspace display name.
- Recent workspaces from Desktop settings.
- Commands:
  - Open DotCraft or New Chat.
  - Open workspace window.
  - Restart AppServer for a managed workspace.
  - Stop AppServer for a managed workspace.
  - Open Dashboard when Hub reports a dashboard endpoint.
  - Refresh.
  - Exit.

The menu should rebuild when Hub events arrive and after management actions complete.

---

## 7. Exit Semantics

Tray Exit means stop all local DotCraft background runtime owned by Hub.

The tray process should:

1. Call Hub `POST /v1/shutdown` when Hub is available.
2. Allow Hub to gracefully stop managed AppServers.
3. Exit the tray process.

Already open Desktop windows are not forcibly closed by the tray. They should observe AppServer disconnects through their existing connection recovery path.

---

## 8. Constraints and Compatibility

- Hub capability `tray` remains `false`; tray capability belongs to Desktop.
- AppServer Protocol is unchanged.
- Direct `dotcraft app-server` remains available for explicit server hosting and debugging.
- Hub-managed AppServers must bind and run as background processes without visible console windows on Windows.
- Remote mode does not require Hub or tray.

---

## 9. Acceptance Checklist

- Starting Desktop creates or reuses exactly one tray process.
- Starting multiple Desktop workspace windows does not create multiple tray icons.
- Closing all workspace windows leaves the tray available.
- Tray can open a recent workspace in a new Desktop window.
- Tray can restart and stop a Hub-managed AppServer.
- Tray Exit stops Hub and Hub-managed AppServers.
- Hub-managed AppServer does not display a console window on Windows.
- Existing Desktop local and remote connection behavior remains unchanged.
