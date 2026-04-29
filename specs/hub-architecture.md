# DotCraft Hub Architecture Specification

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Draft |
| **Date** | 2026-04-16 |
| **Parent Spec** | [AppServer Protocol](appserver-protocol.md), [Session Core](session-core.md) |
| **Related Specs** | [Desktop Client](desktop-client.md) |

Purpose: Define the architecture, protocol extensions, process lifecycle, and iteration plan for DotCraft Hub — a single long-lived process that manages multiple workspace runtimes, exposes a unified wire protocol endpoint, hosts a cross-workspace Dashboard, and provides system-tray-resident notification aggregation.

---

## Table of Contents

- [1. Motivation and Problem Statement](#1-motivation-and-problem-statement)
- [2. Scope](#2-scope)
- [3. Architecture Overview](#3-architecture-overview)
- [4. Hub Process Lifecycle](#4-hub-process-lifecycle)
- [5. Workspace Runtime](#5-workspace-runtime)
- [6. Workspace Registry](#6-workspace-registry)
- [7. Hub Wire Protocol](#7-hub-wire-protocol)
- [8. Client Connection Lifecycle](#8-client-connection-lifecycle)
- [9. Notification Routing](#9-notification-routing)
- [10. Unified Dashboard](#10-unified-dashboard)
- [11. System Tray Integration](#11-system-tray-integration)
- [12. Client Bootstrap Protocol](#12-client-bootstrap-protocol)
- [13. Backward Compatibility](#13-backward-compatibility)
- [14. Technical Choices](#14-technical-choices)
- [15. Iteration Plan](#15-iteration-plan)
- [16. Design Decisions (Resolved)](#16-design-decisions-resolved)

---

## 1. Motivation and Problem Statement

### 1.1 Current Architecture

DotCraft follows a strict **1 process = 1 workspace** model. Each client (Desktop, TUI, CLI) spawns or connects to a dedicated `dotcraft app-server` process bound to one workspace via `Directory.GetCurrentDirectory()`. All core services — `AppConfig`, `ThreadStore`, `MemoryStore`, `SessionService`, `AgentFactory`, `CronService`, `Dashboard` — are singletons scoped to that one workspace.

### 1.2 Problems

1. **No unified workspace view.** Users cannot see all their workspaces and threads in a single client window. Switching workspaces requires a full disconnect/reconnect cycle. Operating on multiple workspaces simultaneously requires multiple OS-level processes (Desktop spawns a separate Electron process per workspace).

2. **Duplicate AppServer risk.** If a user opens the same workspace from Desktop and TUI simultaneously, each client spawns its own `dotcraft app-server`. Two AppServer processes compete for the same `.craft/` directory with no coordination. The Desktop workspace lock (`desktop.lock`) is Desktop-only and not respected by TUI or CLI.

3. **Port conflicts.** Dashboard defaults to port 8080. Multiple dotcraft instances on the same machine conflict unless manually reconfigured. There is no dynamic port allocation.

4. **Resource overhead.** Each AppServer process carries the full .NET runtime overhead (~50–100 MB). Users with 3–5 active workspaces would run 3–5 separate processes.

5. **No cross-workspace notifications.** Each client connection receives events from only its connected workspace. There is no unified notification surface.

### 1.3 Design Goal

Introduce a **Hub** — a single long-lived DotCraft process that hosts multiple workspace runtimes in-process, exposes one wire protocol endpoint for all clients, provides a unified Dashboard, and acts as the system-tray-resident notification hub. All clients connect to Hub; Hub manages workspace lifecycles internally.

---

## 2. Scope

### 2.1 What This Spec Defines

- The Hub process model and its relationship to the existing DotCraft module/host system.
- The WorkspaceRuntime abstraction: what it contains, how it is created/destroyed.
- The workspace registry: discovery, persistence, lifecycle states.
- Hub wire protocol extensions: workspace routing, new methods, capability advertisement.
- Client bootstrap: how clients find or start Hub before connecting.
- Notification routing: how events from multiple workspaces reach clients.
- Unified Dashboard: how Hub hosts a single Dashboard for all workspaces.
- System tray integration: presence, notification, quick-access.
- Iteration plan: phased delivery from refactoring to full feature.

### 2.2 What This Spec Does Not Define

- Client-side UI layout, component design, or visual design for Desktop, TUI, or Dashboard.
- Per-workspace agent execution internals, tool implementations, or model orchestration.
- Deployment topology for server-side multi-tenant scenarios (these continue to use standalone `dotcraft app-server` or `dotcraft gateway` as today).
- Mobile clients.

---

## 3. Architecture Overview

### 3.1 Conceptual Model

```
┌──────────────────────────────────────────────────────────────────┐
│                     dotcraft hub (single process)                │
│                                                                  │
│  ┌────────────────┐   ┌────────────────┐   ┌────────────────┐   │
│  │ WorkspaceRuntime│   │ WorkspaceRuntime│   │ WorkspaceRuntime│   │
│  │   Project A     │   │   Project B     │   │   Project C     │   │
│  │  ┌───────────┐  │   │  ┌───────────┐  │   │  ┌───────────┐  │   │
│  │  │AppConfig  │  │   │  │AppConfig  │  │   │  │AppConfig  │  │   │
│  │  │ThreadStore│  │   │  │ThreadStore│  │   │  │ThreadStore│  │   │
│  │  │MemoryStore│  │   │  │MemoryStore│  │   │  │MemoryStore│  │   │
│  │  │Session Svc│  │   │  │Session Svc│  │   │  │Session Svc│  │   │
│  │  │AgentFactory│  │   │  │AgentFactory│  │   │  │AgentFactory│  │   │
│  │  │CronService│  │   │  │CronService│  │   │  │CronService│  │   │
│  │  │Channels   │  │   │  │Channels   │  │   │  │Channels   │  │   │
│  │  └───────────┘  │   │  └───────────┘  │   │  └───────────┘  │   │
│  └────────────────┘   └────────────────┘   └────────────────┘   │
│                                                                  │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────────────┐   │
│  │  WebSocket    │  │   Dashboard  │  │   System Tray        │   │
│  │  Endpoint     │  │   (unified)  │  │   (notifications)    │   │
│  │  :9200/ws     │  │   :9200/dash │  │                      │   │
│  └──────┬───────┘  └──────┬───────┘  └──────────┬───────────┘   │
└─────────┼─────────────────┼──────────────────────┼───────────────┘
          │                 │                      │
     ┌────┴────┐      ┌────┴────┐           ┌─────┴─────┐
     │ Desktop │      │ Browser │           │ OS Toast  │
     │ TUI     │      │         │           │ Tray Menu │
     │ CLI     │      │         │           │           │
     └─────────┘      └─────────┘           └───────────┘
```

### 3.2 Key Architectural Decisions

1. **Hub is a DotCraft Host module** (`dotcraft hub`), not a separate binary. It reuses the existing module registry, config infrastructure, and build pipeline.

2. **WorkspaceRuntimes live in-process**, not as child processes. Each workspace gets its own set of services (config, thread store, memory, session, agent, cron, etc.) as objects in the same .NET process. No child process management, no inter-process communication overhead, no port-per-workspace.

3. **One WebSocket endpoint, one Dashboard port.** Hub binds a single HTTP/WebSocket listener. All clients connect to the same endpoint. Workspace routing happens at the protocol level, not the transport level.

4. **Lazy loading with idle suspension.** Workspace runtimes are created on first access and can be suspended (disposed) after an idle timeout to reclaim memory. Reactivation is transparent to the client.

5. **Hub replaces direct AppServer spawning for local clients.** Desktop, TUI, and CLI connect to Hub instead of spawning their own `dotcraft app-server`. This eliminates duplicate AppServer instances and port conflicts by design.

---

## 4. Hub Process Lifecycle

### 4.1 Startup

Hub runs as `dotcraft hub` and does NOT bind to a specific project workspace. Its working context is the global DotCraft home directory (`~/.craft/`).

Startup sequence:

1. Read Hub configuration from `~/.craft/config.json` (Hub-level section).
2. Load workspace registry from `~/.craft/workspaces.json`.
3. Start the WebSocket + HTTP listener on the configured address (default `127.0.0.1:9200`).
4. Mount the unified Dashboard on the same listener.
5. Initialize the system tray icon (on supported platforms).
6. Compile the module registry (`ModuleRegistrations.RegisterAll`) once — shared across all workspaces.
7. Optionally pre-load workspace runtimes for workspaces marked as `autoStart` in the registry.

Hub does NOT call `AddDotCraft` with a specific workspace path. It maintains its own minimal service container for Hub-level concerns (WebSocket listener, workspace registry, tray integration).

### 4.2 Shutdown

On shutdown (tray quit, SIGTERM, or system shutdown):

1. Stop accepting new client connections.
2. For each active workspace runtime: cancel running turns, flush pending writes, dispose the runtime.
3. Persist workspace registry state (last-active timestamps, runtime status).
4. Stop the HTTP listener.
5. Exit.

### 4.3 Single-Instance Guarantee

Only one Hub process runs per user on a machine. Hub writes a lock file at `~/.craft/hub.lock` containing `{ pid, port, startedAt }`. Clients check this lock to discover a running Hub (see [Section 12](#12-client-bootstrap-protocol)).

If a second `dotcraft hub` is launched while one is already running, it detects the lock, verifies the PID is alive, and either exits with a message or signals the existing Hub to foreground its tray icon.

---

## 5. Workspace Runtime

### 5.1 Definition

A `WorkspaceRuntime` is a self-contained service graph equivalent to what `ServiceRegistration.AddDotCraft` + `AppServerHost.RunAsync` currently build for a single workspace. It encapsulates all per-workspace state and services.

### 5.2 Contents

Each WorkspaceRuntime contains:

- **Paths**: `DotCraftPaths` (workspace root + `.craft/` path).
- **Configuration**: `AppConfig` loaded from the workspace's `.craft/config.json` merged with global `~/.craft/config.json`.
- **Persistence**: `ThreadStore`, `MemoryStore`, `ApprovalStore`, `PlanStore` — all rooted at the workspace's `.craft/`.
- **Agent stack**: `AgentFactory`, `AgentRunner`, `Context.Compaction.CompactionPipeline`, `MemoryConsolidator`. Memory consolidation is a Session Core maintenance workflow independent from context compaction; see [Memory Consolidation](memory-consolidation.md).
- **Session**: `SessionService`, `SessionGate`.
- **Scheduling**: `CronService`, `HeartbeatService`.
- **Skills and tools**: `SkillsLoader`, `CustomCommandLoader`, tool providers collected per-workspace config and enabled modules.
- **Integrations**: `McpClientManager`, `LspServerManager` (per-workspace MCP/LSP connections).
- **Tracing**: `TraceStore`, `TraceCollector`, `TokenUsageStore`.
- **Channels**: Per-workspace native and external channels (via `ChannelRunner`), if enabled in that workspace's config.
- **Hooks**: `HookRunner` for workspace-specific hooks.

### 5.3 Lifecycle States

```
       ┌──────────┐
       │ Registered│  (known in registry, not loaded)
       └─────┬────┘
             │ client accesses workspace
             ▼
       ┌──────────┐
       │  Loading  │  (creating services, reading config)
       └─────┬────┘
             │ success
             ▼
       ┌──────────┐
       │  Active   │  (serving requests, running agents)
       └─────┬────┘
             │ idle timeout / explicit close
             ▼
       ┌──────────┐
       │ Suspended │  (disposed, can be reactivated)
       └─────┬────┘
             │ client accesses again
             ▼
       ┌──────────┐
       │  Loading  │
       └──────────┘

       Error at any stage → Faulted (reported to clients, manual retry)
```

### 5.4 Isolation Boundaries

Each WorkspaceRuntime operates independently:

- **Config isolation**: Each workspace loads its own `.craft/config.json`. Different workspaces can use different models, API keys, enabled modules, and tool configurations.
- **Data isolation**: Thread, memory, trace, and cron data are physically separated in each workspace's `.craft/` directory.
- **Agent isolation**: Each workspace has its own `AgentFactory` and `SessionService`. Conversations in one workspace do not share context with another.
- **Exception isolation**: Hub wraps all workspace-level operations in exception boundaries. A failure in one workspace's agent does not crash other workspaces or the Hub process. A faulted workspace can be reloaded without restarting Hub.

### 5.5 Shared Resources

The following are shared across all workspace runtimes within the Hub process:

- **Module registry**: Compiled once; module enablement is evaluated per-workspace config.
- **WebSocket listener and connection management**.
- **Dashboard HTTP routes**.
- **.NET runtime, GC, thread pool.**

---

## 6. Workspace Registry

### 6.1 Storage

The workspace registry is stored at `~/.craft/workspaces.json`. It tracks all workspaces known to Hub.

### 6.2 Registry Entry

Each entry contains:

- `path` — absolute path to the workspace root.
- `displayName` — user-facing name (defaults to directory name).
- `addedAt` — timestamp when the workspace was registered.
- `lastActiveAt` — timestamp of last client activity.
- `autoStart` — whether to pre-load the runtime on Hub startup.
- `status` — current state as observed by Hub (`registered`, `active`, `suspended`, `faulted`).

### 6.3 Registration

Workspaces are added to the registry when:

- A client connects and requests a workspace not yet in the registry (`workspace/open` with a new path).
- The user adds a workspace via system tray, Dashboard, or CLI (`workspace/register`).

### 6.4 Removal

Workspaces are removed from the registry when:

- The user explicitly removes them via `workspace/remove`.
- The workspace path no longer exists on disk and the user confirms removal.

Removal does NOT delete the workspace's `.craft/` directory or any data. It only removes the entry from the Hub's registry.

---

## 7. Hub Wire Protocol

Hub extends the existing AppServer wire protocol (JSON-RPC 2.0). All existing methods remain valid. Extensions are additive.

### 7.1 Transport

Hub listens on a single WebSocket endpoint (default `ws://127.0.0.1:9200/ws`). All clients — Desktop, TUI, CLI — connect to this endpoint.

Hub does NOT expose a stdio transport. Stdio is reserved for the standalone `dotcraft app-server` mode (which remains available for server deployments and backward compatibility).

### 7.2 Initialize Handshake

The `initialize` / `initialized` handshake is performed once per client connection to Hub, not per workspace.

The `initialize` response includes a new capability flag:

```json
{
  "serverInfo": {
    "name": "dotcraft-hub",
    "version": "...",
    "protocolVersion": "2"
  },
  "capabilities": {
    "hubMode": true,
    "workspaceManagement": true,
    ...existing capabilities omitted...
  }
}
```

When `hubMode` is `true`, clients know they are connected to a Hub and workspace routing is available. Capabilities that are workspace-specific (e.g., `cronManagement`, `mcpManagement`) are reported at the workspace level, not in the global initialize response.

### 7.3 Workspace Methods

New methods for workspace management:

**`workspace/list`** — List all registered workspaces.

```json
// Request
{ "method": "workspace/list", "id": 1, "params": {} }

// Response
{ "id": 1, "result": {
  "workspaces": [
    {
      "path": "/home/user/project-a",
      "displayName": "Project A",
      "status": "active",
      "lastActiveAt": "2026-04-16T10:00:00Z",
      "capabilities": { "cronManagement": true, "mcpManagement": true, ... }
    },
    {
      "path": "/home/user/project-b",
      "displayName": "Project B",
      "status": "registered",
      "lastActiveAt": "2026-04-15T18:00:00Z"
    }
  ]
}}
```

**`workspace/open`** — Ensure a workspace runtime is active. If the workspace is not registered, it is added to the registry. If suspended, it is reactivated.

```json
{ "method": "workspace/open", "id": 2, "params": {
  "path": "/home/user/project-a"
}}
// Response: workspace info + capabilities for that workspace
```

**`workspace/close`** — Suspend a workspace runtime. Active turns are cancelled. The workspace remains in the registry.

**`workspace/register`** — Add a workspace to the registry without activating it.

**`workspace/remove`** — Remove a workspace from the registry. Fails if the workspace has active client subscriptions.

**`workspace/status`** — Get detailed status of a specific workspace (runtime state, active threads, resource usage).

### 7.4 Workspace Routing for Existing Methods

All existing AppServer methods that operate on workspace-scoped data gain an **explicit `workspacePath` parameter** at the top level of `params`:

```json
{ "method": "thread/list", "id": 3, "params": {
  "workspacePath": "/home/user/project-a",
  "identity": { "channelName": "desktop", "userId": "local" }
}}
```

```json
{ "method": "turn/start", "id": 4, "params": {
  "workspacePath": "/home/user/project-a",
  "threadId": "thr_abc",
  "input": [{ "type": "text", "text": "Hello" }]
}}
```

The `workspacePath` routing field is **required in Hub mode** for all workspace-scoped methods. Hub resolves the target `WorkspaceRuntime` and dispatches the request to its `SessionService`.

The `identity.workspacePath` field (used by `SessionIdentity` for thread filtering) is automatically filled from the routing `workspacePath` if omitted — preserving backward compatibility of the identity contract.

### 7.5 Non-Workspace Methods

Some methods are Hub-level and do not require `workspacePath`:

- `initialize` / `initialized`
- `workspace/*` methods
- `hub/status` — Hub process health and resource overview

### 7.6 Error Codes

New error codes for Hub-specific failures:

- `-33001` — Workspace not found (path not in registry and does not exist on disk).
- `-33002` — Workspace faulted (runtime failed to load or crashed; includes diagnostic info).
- `-33003` — Workspace suspended (client attempted an operation on a suspended workspace without triggering reactivation).
- `-33004` — Hub overloaded (too many active workspace runtimes).

---

## 8. Client Connection Lifecycle

### 8.1 Connection Model

A single client connection to Hub can interact with any number of workspaces. There is no "active workspace" at the connection level — each request specifies its target workspace.

### 8.2 Workspace Subscriptions

Clients subscribe to workspace events by calling `workspace/open`. After opening, the client receives notifications for that workspace (thread events, turn events, system events). A client can have multiple workspace subscriptions active simultaneously.

When a client disconnects, all its workspace subscriptions are removed. If a workspace has no remaining subscriptions and no background work (cron, heartbeat), it becomes eligible for idle suspension.

### 8.3 Thread Subscriptions

Thread subscriptions (existing `thread/subscribe` mechanism) work as before, scoped to a workspace. The client must have the workspace open to subscribe to its threads.

---

## 9. Notification Routing

### 9.1 Workspace-Tagged Notifications

All server-initiated notifications from workspace runtimes carry a `workspacePath` field in `params`:

```json
{ "method": "turn/started", "params": {
  "workspacePath": "/home/user/project-a",
  "turn": { "id": "turn_123", "threadId": "thr_abc", ... }
}}
```

```json
{ "method": "thread/started", "params": {
  "workspacePath": "/home/user/project-a",
  "thread": { "id": "thr_new", ... }
}}
```

This allows clients to attribute events to workspaces without maintaining correlation state.

### 9.2 Notification Delivery

Notifications are delivered to all client connections that have the source workspace open (via `workspace/open`). Clients that have not opened a workspace do not receive its notifications.

### 9.3 Hub-Level Notifications

Hub can emit its own notifications:

- `workspace/statusChanged` — when a workspace runtime transitions state (loading, active, suspended, faulted).
- `hub/workspaceAdded` — when a new workspace is registered.
- `hub/workspaceRemoved` — when a workspace is removed from the registry.

---

## 10. Unified Dashboard

### 10.1 Single Endpoint

Hub hosts Dashboard on the same HTTP listener as the WebSocket endpoint (e.g., `http://127.0.0.1:9200/dashboard`). There is exactly one Dashboard URL regardless of how many workspaces are active.

### 10.2 Workspace-Aware Dashboard

Dashboard gains a workspace context:

- A workspace selector allows switching the Dashboard view between registered workspaces.
- Trace viewer, thread list, config editor, and token usage are all scoped to the selected workspace.
- Dashboard reads data directly from the in-process `WorkspaceRuntime` objects (TraceStore, ThreadStore, TokenUsageStore). No wire protocol round-trip needed.
- Clearing trace sessions deletes the selected workspace's trace rows plus usage rows linked by `thread_id` or `session_key`; global usage rows without either link are preserved. Bulk trace clearing may run SQLite WAL truncation and conditional compaction for that workspace's `.craft/state.db`.

### 10.3 Cross-Workspace Views

Dashboard may provide aggregate views:

- Total token usage across all workspaces.
- Recent activity timeline across workspaces.
- Global search across thread names.

These are additive features that build on the per-workspace data already accessible in-process.

### 10.4 Per-Workspace Dashboard Elimination

When Hub is running, individual workspace AppServer processes do NOT need to host their own Dashboard. The Hub Dashboard replaces all per-workspace Dashboard instances. This eliminates all Dashboard port conflicts.

---

## 11. System Tray Integration

### 11.1 Tray Presence

Hub runs as a system tray application on supported platforms (Windows initially). The tray icon provides:

- Visual indicator that Hub is running.
- Workspace list with status indicators.
- Quick access to open Dashboard in browser.
- Quick access to launch Desktop for a specific workspace.
- Quit Hub option.

### 11.2 Notification Aggregation

Hub aggregates notifications from all active workspace runtimes and surfaces them as OS-level toast notifications:

- Turn completed (with workspace name and thread context).
- Approval request pending (with workspace and action context).
- Workspace errors or faults.
- Cron job results.

Notification preferences (which events to surface, quiet hours) are configurable in Hub config.

### 11.3 Auto-Start

Hub can be configured to start on user login. On Windows, this is achieved via a startup registry entry or scheduled task. The tray icon appears in the system tray after login, and Hub is ready for client connections.

---

## 12. Client Bootstrap Protocol

### 12.1 The Bootstrap Problem

Today, each client spawns its own `dotcraft app-server`. In the Hub model, clients must instead connect to the single Hub. If Hub is not running, the client must start it first.

### 12.2 Bootstrap Sequence

All clients (Desktop, TUI, CLI) follow the same bootstrap:

```
1. Check for hub.lock at ~/.craft/hub.lock
2. If lock exists and PID is alive:
   → Read port from lock file
   → Connect to ws://127.0.0.1:{port}/ws
3. If lock does not exist or PID is dead:
   → Start Hub: spawn `dotcraft hub` as a detached background process
   → Poll hub.lock until it appears and contains a valid port (timeout: 10s)
   → Connect to ws://127.0.0.1:{port}/ws
4. Perform initialize handshake
5. Call workspace/open for the desired workspace
6. Proceed with normal operation
```

### 12.3 Hub Lock File

`~/.craft/hub.lock` contains:

```json
{
  "pid": 12345,
  "port": 9200,
  "startedAt": "2026-04-16T10:00:00Z"
}
```

The lock file is created atomically by Hub on startup and removed on clean shutdown. Stale locks (PID no longer alive) are ignored by clients and overwritten by a new Hub instance.

### 12.4 Solving the Duplicate AppServer Problem

The bootstrap protocol solves the existing problem of duplicate AppServer instances:

- **Before Hub**: Desktop opens workspace A and spawns AppServer A. TUI opens workspace A and spawns AppServer A'. Two processes fight over `.craft/`.
- **With Hub**: Desktop starts (or finds) Hub, calls `workspace/open` for A. TUI starts (or finds) the same Hub, calls `workspace/open` for A. Both connect to the same `WorkspaceRuntime`. One process, one set of services, no conflicts.

### 12.5 Workspace Lock Replacement

The current Desktop-only workspace lock (`desktop.lock`) is replaced by the Hub's intrinsic single-runtime guarantee. Since all clients go through Hub, and Hub creates exactly one `WorkspaceRuntime` per workspace path, there is no possibility of duplicate runtimes. The `desktop.lock` mechanism is removed.

---

## 13. Backward Compatibility

### 13.1 Standalone AppServer Mode

The existing `dotcraft app-server` command remains fully functional. It continues to serve a single workspace with stdio or WebSocket transport. This mode is used for:

- Server-side deployments (bots, CI, automated pipelines).
- Environments where Hub is not desired.
- SDK and external channel adapter connections that target a specific workspace.

### 13.2 Standalone Gateway Mode

`dotcraft gateway` remains unchanged. It manages multiple channels within one workspace, which is orthogonal to Hub's multi-workspace management.

### 13.3 Protocol Version

Hub advertises `protocolVersion: "2"` in the initialize response. Protocol version 2 is a superset of version 1:

- All v1 methods work unchanged when `workspacePath` is provided in params.
- New `workspace/*` methods and `hubMode` capability are v2 additions.
- Clients that do not understand v2 can still connect to a standalone AppServer (v1) as today.

### 13.4 Client Compatibility

Clients should support both connection paths:

- **Hub available**: connect to Hub, use v2 protocol with workspace routing.
- **Hub not available, standalone mode requested**: spawn `dotcraft app-server` directly (existing behavior), use v1 protocol.

This dual-path is a transitional mechanism. Once Hub is stable, the standalone local spawn path can be deprecated for interactive clients.

---

## 14. Technical Choices

### 14.1 Hub as a Host Module

Hub is implemented as a new DotCraft Host module:

- Module name: `hub`
- Priority: 300 (higher than AppServer's 250)
- Entry: `dotcraft hub` via CLI args
- Host factory: `HubHostFactory` creating `HubHost`

Hub does not participate in the normal workspace-bound `AddDotCraft` registration. Instead, Hub's `RunAsync` builds its own minimal service container and creates `WorkspaceRuntime` objects on demand.

### 14.2 WorkspaceRuntime Extraction

The service-creation logic currently split across `ServiceRegistration.AddDotCraft` and `AppServerHost.RunAsync` is extracted into a reusable `WorkspaceRuntime` factory. This is the core refactoring that enables both Hub (multi-workspace) and a cleaner AppServer (single-workspace using the same abstraction).

After extraction, `AppServerHost` itself becomes a thin wrapper: create one `WorkspaceRuntime`, wire it to transports, run until shutdown. Hub creates N `WorkspaceRuntime` instances and wires them through a shared transport with routing.

### 14.3 HTTP / WebSocket Stack

Hub reuses the same ASP.NET Core minimal API and WebSocket stack that AppServer's WebSocket mode and Dashboard already use. The Hub listener serves:

- `/ws` — WebSocket endpoint for wire protocol clients.
- `/dashboard/*` — unified Dashboard routes.
- `/healthz`, `/readyz` — health probes.

### 14.4 System Tray Technology

On Windows, Hub uses a lightweight system tray implementation. Options include:

- .NET `System.Windows.Forms.NotifyIcon` (minimal dependency, proven).
- A thin native helper process that communicates with Hub (if WinForms dependency is undesirable in a console app context).

The tray component runs on its own thread and communicates with Hub's main loop via an internal event bus. Tray is an optional feature — Hub functions correctly without it (headless mode for WSL, Linux servers, CI).

### 14.5 Module Registry Sharing

`ModuleRegistry` is populated once at Hub startup via the source-generated `ModuleRegistrations.RegisterAll`. Each `WorkspaceRuntime` receives a reference to the shared registry but evaluates module enablement against its own `AppConfig`. This means:

- The set of *compiled* modules is identical across workspaces (they are part of the binary).
- The set of *enabled* modules can differ per workspace (driven by per-workspace config).
- Tool providers are collected per-workspace: `ToolProviderCollector.Collect(sharedRegistry, workspaceConfig)`.

---

## 15. Iteration Plan

The Hub feature is delivered in phases. Each phase produces a usable increment.

### Phase 1: WorkspaceRuntime Extraction (Refactoring)

**Goal**: Extract the per-workspace service graph into a reusable `WorkspaceRuntime` class without changing any external behavior.

**Work**:
- Define `WorkspaceRuntime` class encapsulating all per-workspace services.
- Extract creation logic from `ServiceRegistration.AddDotCraft` + `AppServerHost.RunAsync` into `WorkspaceRuntime.CreateAsync(path, moduleRegistry)`.
- Refactor `AppServerHost` to create a single `WorkspaceRuntime` and delegate to it.
- Verify all existing tests pass. No protocol changes, no client changes.

**Outcome**: AppServer works exactly as before, but internally uses the new abstraction. This is the foundation for all subsequent phases.

### Phase 2: Hub Host with Workspace Management

**Goal**: Implement the Hub process with workspace registry, multi-runtime management, and WebSocket endpoint.

**Work**:
- Implement `HubHost` as a new Host module.
- Implement workspace registry (`~/.craft/workspaces.json`).
- Implement Hub lock file (`~/.craft/hub.lock`).
- Implement `workspace/*` wire protocol methods.
- Implement workspace routing for existing methods (`workspacePath` in params).
- Implement workspace-tagged notifications.
- Implement lazy loading and idle suspension of workspace runtimes.
- Hub exposes a WebSocket endpoint; clients can connect and perform all operations.

**Outcome**: A functional Hub that clients can connect to via WebSocket and operate on multiple workspaces through one connection. No client changes yet (clients still use their existing connection paths).

### Phase 3: Client Bootstrap and Integration

**Goal**: Clients discover and connect to Hub instead of spawning AppServer directly.

**Work**:
- Implement the client bootstrap protocol (hub.lock discovery, Hub auto-start).
- Update Desktop main process to connect to Hub, manage multiple workspace connections, namespace Zustand stores by workspace.
- Update TUI to support `--hub` mode (connect to Hub, workspace switching).
- Update CLI's `AppServerProcess` to use Hub when available.
- Remove Desktop-only workspace lock in favor of Hub's intrinsic guarantee.

**Outcome**: All clients use Hub as their default connection target. Multiple clients on the same workspace share one `WorkspaceRuntime`. Duplicate AppServer problem is solved.

### Phase 4: Unified Dashboard

**Goal**: Hub hosts a single Dashboard that serves all workspaces.

**Work**:
- Mount Dashboard routes on Hub's HTTP listener.
- Add workspace selector to Dashboard UI.
- Dashboard reads from in-process `WorkspaceRuntime` stores directly.
- Disable per-workspace Dashboard when Hub is the active host.
- Add cross-workspace aggregate views (token usage, activity timeline).

**Outcome**: One Dashboard URL, all workspaces accessible. No more port conflicts.

### Phase 5: System Tray and Notification Hub

**Goal**: Hub runs as a system tray resident with cross-workspace notifications.

**Work**:
- Implement system tray icon with workspace list and status.
- Implement OS-level toast notification integration.
- Implement notification preferences in Hub config.
- Implement auto-start on login.
- Implement "open in Desktop" quick-action from tray.

**Outcome**: Full Hub experience — persistent tray presence, unified notifications, one-click workspace access.

### Phase 6: Client UX for Multi-Workspace

**Goal**: Desktop and TUI provide polished multi-workspace user experiences.

**Work**:
- Desktop: workspace sidebar, cross-workspace thread browsing, notification badges per workspace, workspace management (add/remove/rename).
- TUI: workspace overlay/picker, status line with workspace indicator, workspace switching commands.

**Outcome**: The end-user multi-workspace experience is complete across all client surfaces.

---

## 16. Design Decisions (Resolved)

The following questions were evaluated during the design phase. Decisions are recorded here as binding constraints for milestone implementation.

### 16.1 Hub Config Section Structure

**Decision**: Hub config is a `[ConfigSection("Hub")]` section inside `~/.craft/config.json`.

**Rationale**: All DotCraft configuration follows the `config.json` + `[ConfigSection]` attribute pattern. A separate file would break the unified config loading, Dashboard schema generation, and `AppConfig.GetSection<T>` access pattern. Since Hub is inherently global (not workspace-scoped), its section lives in the global config only.

**Config shape**:

```json
{
  "Hub": {
    "Port": 9200,
    "AutoStartWorkspaces": [],
    "IdleTimeoutMinutes": 30,
    "MaxActiveWorkspaces": 10,
    "Tray": {
      "Enabled": true,
      "NotifyOnTurnCompleted": true,
      "NotifyOnApprovalRequest": true
    }
  }
}
```

### 16.2 Channel Routing in Hub

**Decision**: Native channels register into a Hub-level shared `WebHostPool`. Channels remain logically owned by their `WorkspaceRuntime`, but their HTTP listeners are hosted centrally by Hub.

**Rationale**: The existing `WebHostPool` already merges `IWebHostingChannel` instances that share the same `(scheme, host, port)` into a single Kestrel server with composed routes. Hub reuses this mechanism at the Hub level:

- When a `WorkspaceRuntime` activates and has enabled native channels, those channels register into Hub's shared pool.
- Channels from different workspaces sharing the same address are merged (the pool already supports this).
- If routes collide (e.g., two workspaces both enable QQ on the same callback path and port), the second workspace's channel fails with a clear diagnostic. This is correct behavior — two bots cannot share the same callback URL.
- Workspaces that need independent channel ports configure different ports in their workspace `config.json`; Hub's pool handles them as separate listeners.

This eliminates per-workspace port conflicts while preserving the existing channel registration pattern.

### 16.3 Remote Hub

**Decision**: Deferred to a future spec. Hub is loopback-only (`127.0.0.1`) in v1.

**Rationale**: Remote Hub (non-loopback, team-shared) introduces authentication, authorization, user isolation, encrypted transport, and audit requirements that are orthogonal to the core Hub value (local multi-workspace UX).

The wire protocol is designed to not preclude remote use — `workspacePath` routing, per-connection subscriptions, and `SessionIdentity.userId` are already present. A future remote spec would add:

- TLS on the WebSocket endpoint.
- Token or OAuth authentication on `initialize`.
- Per-user workspace access control.

Until then, Hub follows the same pattern as `AppServer.WebSocket`: non-loopback binding requires a `Hub.Token` and is rejected without one.

### 16.4 Workspace Config Reload

**Decision**: Explicit reload via a `workspace/reload` wire method. No automatic hot-reload.

**Rationale**: Automatic file-watching is dangerous and complex:

- Config changes mid-turn could change the model, API key, or enabled tools, causing unpredictable agent behavior.
- File watchers add platform-specific edge cases (Windows file locking, network drives, editor atomic save patterns).
- Some config changes require full runtime recreation (API endpoint, module enable/disable) and cannot be applied incrementally.

The `workspace/reload` method provides a controlled reload path:

1. Client (Desktop, Dashboard) calls `workspace/reload` after editing config.
2. Hub waits for active turns in that workspace to complete (or the client confirms interruption).
3. Hub disposes the old `WorkspaceRuntime` and creates a new one with fresh config.
4. Clients receive `workspace/statusChanged` notifications through the reload cycle (active -> loading -> active).

Dashboard's config editor calls `workspace/reload` automatically after saving, giving the user the same convenience as hot-reload without the risk. Desktop and TUI can prompt "Config changed, reload?" when they detect the file has been modified (client-side file watch, server-side reload).

### 16.5 Maximum Concurrent Workspace Runtimes

**Decision**: Soft limit with LRU auto-suspension, configurable via `Hub.MaxActiveWorkspaces` (default 10) and `Hub.IdleTimeoutMinutes` (default 30).

**Resource model per active WorkspaceRuntime**:

- Memory: ~10–30 MB (thread cache, agent state, MCP connection state).
- MCP server child processes (if configured in that workspace).
- Cron timers and heartbeat timers.
- Potential background agent work (running turns).

**Suspension rules**:

- Workspaces with no client subscriptions and no active background work (cron, heartbeat, running turns) are eligible for idle suspension after `IdleTimeoutMinutes`.
- When `MaxActiveWorkspaces` is reached and a new workspace is requested, Hub suspends the least-recently-used eligible workspace. If no workspace is eligible (all have active work), Hub returns error `-33004` (Hub overloaded).
- Suspension is transparent to future access: a request targeting a suspended workspace triggers automatic reactivation. The client observes a brief loading delay, not an error.
- Workspaces with active cron jobs or heartbeats are exempt from idle suspension (they have ongoing background responsibilities).

### 16.6 CLI Session Mode

**Decision**: CLI connects to Hub as a wire client, same as Desktop and TUI.

**Rationale**: The current `CliHost` already operates as a wire protocol client — it spawns `dotcraft app-server` as a subprocess and communicates via `AppServerWireClient`. The REPL (`ReplHost`) never calls `SessionService` directly; it goes through the wire. This means CLI is architecturally ready to connect to Hub with minimal changes.

**Bootstrap behavior for CLI**:

1. CLI checks for `~/.craft/hub.lock`.
2. If Hub is running: connect to Hub's WebSocket, call `workspace/open` for the CLI's cwd.
3. If Hub is not running: start Hub as a detached background process, then connect.
4. The `WireCliSession`, `ReplHost`, and notification listener require no changes — only the connection setup in `CliHost.RunAsync` changes.

**UX benefit**: If Desktop is open with Hub running, and the user opens a terminal and runs `dotcraft` in the same workspace, CLI connects to the same Hub and the same `WorkspaceRuntime`. No duplicate processes, shared thread history, consistent cron/heartbeat behavior across clients.
