# DotCraft Hub M5: Runtime Completion

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Draft |
| **Date** | 2026-04-30 |
| **Parent Spec** | [Hub Local Coordinator](hub-architecture.md), [Hub M4](hub-m4-tray-management.md) |

Purpose: Define the fifth Hub milestone: make the local Hub runtime durable enough for long-running background use by adding persisted AppServer registry state, managed health observation, and Desktop-owned system notifications.

## 1. Overview

M5 keeps the existing Hub architecture intact. Hub remains a headless local coordinator, and clients still connect directly to workspace AppServer WebSocket endpoints after bootstrap.

The milestone fills runtime gaps left after client adoption and tray management:

- Hub-known AppServers survive Hub restarts as best-effort registry metadata.
- Current-Hub managed AppServers are health checked without automatic restart.
- Notification requests become a stable Hub event that Desktop tray can turn into OS notifications.
- Tray can display known AppServers beyond the current in-memory process lifetime.

## 2. Goal

At the end of M5:

- `GET /v1/appservers` returns current live entries and persisted known workspace entries.
- Hub writes `~/.craft/hub/appservers.json` after AppServer lifecycle changes.
- Hub marks unhealthy managed AppServers and emits `appserver.unhealthy`.
- Managed AppServer turn completion/failure can request a Hub notification.
- Desktop tray owns OS notification display for `notification.requested`.
- TUI Hub HTTP bootstrap works with chunked HTTP responses.

## 3. Scope

M5 includes:

- Best-effort persisted registry state under the Hub state directory.
- Lightweight health checks for AppServers owned by the current Hub process.
- Notification request payload normalization and forwarding over Hub SSE.
- Desktop tray notification display and a New Chat menu command.
- Client wording and parsing fixes for local Hub-managed versus remote connections.

M5 does not include:

- AppServer Protocol wire changes.
- Hub proxying or routing normal AppServer traffic.
- C# tray implementation.
- Hub restart adoption of old child processes.
- Advanced notification preferences, notification center, or quiet hours.
- ACP migration to Hub.

## 4. Behavioral Contract

Hub registry persistence is best effort. The live OS process and workspace `appserver.lock` remain the source of truth for ownership. If Hub restarts and finds an old live AppServer lock, it may show the workspace as running/external, but it must not silently take over the process handle.

Health checks only run for AppServers started and supervised by the current Hub process. When a check fails, Hub records diagnostics, marks the entry `unhealthy`, emits `appserver.unhealthy`, and waits for an explicit ensure/restart/stop request.

Notification requests are local coordination events. Hub accepts valid requests, emits `notification.requested`, and does not display OS UI itself. Desktop tray is the notification owner when it is running.

## 5. Acceptance Checklist

- Hub writes and reloads `appservers.json`.
- `GET /v1/appservers` includes persisted stopped/exited/unhealthy entries.
- Managed process exit or failed health check updates registry diagnostics.
- `appserver.unhealthy` and `notification.requested` are observable over SSE.
- Tray shows known AppServers and includes New Chat.
- Tray displays OS notifications from Hub notification events and opens the related workspace on click.
- TUI can parse chunked Hub HTTP responses.
- Existing local/remote Desktop, CLI, and TUI flows remain compatible.
