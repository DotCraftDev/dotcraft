# DotCraft Hub M3: Client Adoption

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Draft |
| **Date** | 2026-04-29 |
| **Parent Spec** | [Hub Local Coordinator](hub-architecture.md), [Hub M2](hub-m2-protocol-managed-appserver.md), [Desktop Client](desktop-client.md), [TUI Client](tui-client.md) |

Purpose: Define the third Hub milestone: Desktop, TUI, CLI, and related local clients adopt Hub as the default local connection path while preserving explicit remote AppServer connections.

---

## 1. Overview

M3 moves Hub from an available local service to the default local coordination path.

Local clients no longer start independent workspace AppServers by default. Instead, they locate or start Hub, call Hub Protocol to ensure a workspace AppServer, and then connect directly to the returned AppServer WebSocket endpoint.

The AppServer Protocol remains the client conversation protocol. Hub is only involved in local discovery, lifecycle, endpoint metadata, and optional event/notification integration.

---

## 2. Goal

At the end of M3:

- Desktop uses Hub for local workspace connections.
- TUI uses Hub for local workspace connections.
- CLI uses Hub for local workspace connections.
- Explicit remote AppServer connections remain available as "Remote" mode.
- Local mode no longer exposes manual local AppServer port configuration to users.
- Local AppServer restart/stop actions are routed through Hub.
- Clients can subscribe to Hub lifecycle events where useful.
- Clients are prepared for Hub-mediated OS notifications.

---

## 3. Scope

M3 includes:

- Client bootstrap updates for Desktop, TUI, and CLI.
- Local versus Remote connection mode language and settings behavior.
- Removal or hiding of local port settings from client UX.
- Migration of local AppServer restart actions to Hub Protocol.
- Client handling of Hub startup, unavailable, and AppServer ensure failures.
- Optional subscription to Hub lifecycle events.
- Notification request and display flow integration where supported.

M3 may include ACP or external local adapters if they currently spawn workspace AppServers directly, but the required client set is Desktop, TUI, and CLI.

---

## 4. Non-Goals

- M3 does not require a full multi-workspace Desktop redesign.
- M3 does not require a polished tray workspace manager.
- M3 does not require all frontend copy and layout polish to be final.
- M3 does not require remote Hub support.
- M3 does not change AppServer Protocol.
- M3 does not remove direct AppServer modes from the binary.

---

## 5. Connection Modes

### 5.1 Local Mode

Local mode means Hub-managed local AppServer.

Client behavior:

1. Resolve target workspace path.
2. Locate Hub via `hub.lock`.
3. If allowed, start Hub when missing.
4. Call `POST /v1/appservers/ensure`.
5. Connect to returned `endpoints.appServerWebSocket`.
6. Run normal AppServer Protocol handshake.

Local mode must not ask users to configure local AppServer ports. Hub owns those ports.

### 5.2 Remote Mode

Remote mode means direct connection to an AppServer endpoint not managed as the current local workspace.

Client behavior:

- User provides or selects a remote WebSocket URL.
- Client connects directly to the URL.
- Hub is not required.
- Local workspace AppServer management actions are unavailable.

User-facing terminology should distinguish the modes clearly:

| English | Chinese |
|---------|---------|
| Local | 本地 |
| Remote | 远端 |
| Hub-managed local workspace | Hub 管理的本地工作区 |
| Remote AppServer | 远端 AppServer |

### 5.3 Debug or Legacy Mode

Clients may retain an explicit debug or legacy option to spawn stdio AppServer directly. It must not be the default local path after M3.

This mode should communicate that it bypasses Hub and may not prevent duplicate AppServers.

---

## 6. Desktop Contract

Desktop local workspace opening uses Hub by default.

Desktop settings behavior:

- Local AppServer port settings should be removed, hidden, or marked advanced legacy.
- Local AppServer restart should call Hub restart for the active workspace.
- Remote connection settings remain available under Remote wording.
- Connection errors should distinguish Hub startup failure from AppServer startup failure.

Desktop may show Hub status in connection UI, but M3 does not require a complete Hub management UI.

Desktop should use Hub event subscription when useful for:

- AppServer restart/exited state.
- Workspace endpoint changes.
- Notification requests.

---

## 7. TUI Contract

TUI local startup uses Hub by default.

TUI should:

- Resolve the current working directory as the target workspace unless overridden.
- Use Hub ensure for local mode.
- Preserve explicit remote URL mode.
- Report Hub and managed AppServer startup failures in terminal-friendly language.
- Reconnect to a restarted AppServer endpoint when Hub reports a replacement endpoint, if the current workflow can safely recover.

M3 does not require TUI workspace switching UI.

---

## 8. CLI Contract

CLI local sessions use Hub by default.

CLI should:

- Use the current working directory as the target workspace.
- Start or locate Hub according to local mode policy.
- Connect to returned AppServer WebSocket endpoint.
- Preserve explicit remote mode.
- Preserve an explicit legacy direct stdio mode for debugging if needed.

CLI welcome/status output should identify the backend as Hub-managed local or Remote.

---

## 9. Events and Notifications

Clients may subscribe to Hub events to improve local UX.

Required handling:

- If Hub reports the active AppServer exited, the client moves to disconnected or reconnecting state.
- If Hub reports a restarted AppServer with a new endpoint, the client may reconnect after a fresh AppServer Protocol handshake.
- If Hub reports a notification request relevant to the client, the client may surface or ignore it according to platform capability.

Notification strategy:

- Hub is the preferred owner for OS-level notifications in local mode.
- Clients may still render in-app notifications.
- AppServer or clients may request notifications through Hub Protocol when supported.

M3 must not require every client to implement OS notifications.

---

## 10. Error and Recovery UX

Clients must distinguish:

| Failure | User meaning |
|---------|--------------|
| Hub not found | Local manager is not running and could not be started. |
| Hub unauthorized | Local manager token or lock is invalid. |
| AppServer start failed | Hub ran but the workspace server did not become ready. |
| Workspace locked | Another live process owns this workspace. |
| Endpoint unavailable | The returned AppServer endpoint cannot be reached. |
| Remote connect failed | The remote URL could not be reached. |

Clients should preserve existing AppServer reconnection rules after the AppServer WebSocket is connected.

---

## 11. Constraints and Compatibility

- Local Hub mode must preserve existing thread, approval, tool, and streaming behavior because those remain AppServer Protocol concerns.
- Remote mode must not require Hub.
- Clients should be able to run against older installations that do not have Hub by falling back or explaining the requirement.
- M3 should not require workspace config migration for local ports.
- User-facing copy must support bilingual localization where applicable.

---

## 12. Acceptance Checklist

- Desktop opens a local workspace through Hub-managed AppServer by default.
- TUI opens a local workspace through Hub-managed AppServer by default.
- CLI opens a local workspace through Hub-managed AppServer by default.
- Remote WebSocket connection remains available and is labeled Remote/远端.
- Local mode does not require users to configure AppServer, Dashboard, API, or AG-UI ports.
- Local AppServer restart actions use Hub Protocol.
- Clients show understandable errors for Hub unavailable, AppServer failed, and workspace locked.
- Existing AppServer Protocol conversation behavior remains unchanged after connection.
- Clients can tolerate Hub events even if they do not yet render all of them.

---

## 13. Open Questions

1. Should CLI/TUI auto-start Hub by default, or only use an already running Hub in the first adoption release?
2. Should Desktop always start Hub at app launch, or only when opening the first local workspace?
3. How visible should legacy direct stdio mode be after Hub becomes default?
4. Should Desktop expose Hub restart/quit in settings, tray, both, or neither during M3?
5. Which client should be the first conformance target for Hub adoption: CLI, TUI, or Desktop?
