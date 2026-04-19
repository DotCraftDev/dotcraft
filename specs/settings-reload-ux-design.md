# DotCraft Settings Reload UX Design

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Draft |
| **Date** | 2026-04-19 |
| **Parent Spec** | [AppServer Protocol](appserver-protocol.md), [Desktop Client](desktop-client.md) |

Purpose: define the three-tier behavior model that governs how DotCraft settings take effect — immediately, after a user-triggered subsystem restart, or after a full AppServer restart — and the contracts that keep Desktop, AppServer, and module authors in agreement about which tier each field belongs to.

---

## Table of Contents

- [1. Background](#1-background)
- [2. Problem Statement](#2-problem-statement)
- [3. Design Principles](#3-design-principles)
- [4. Three-Tier Behavior Model](#4-three-tier-behavior-model)
- [5. Scope of This Design](#5-scope-of-this-design)
- [6. Proxy Override Constraint](#6-proxy-override-constraint)
- [7. Field Inventory and Classification](#7-field-inventory-and-classification)
- [8. Milestone Roadmap](#8-milestone-roadmap)
- [9. Out of Scope](#9-out-of-scope)
- [10. Open Questions](#10-open-questions)

---

## 1. Background

DotCraft configuration is loaded once at process start by `AppConfig.LoadWithGlobalFallback` and registered as a DI singleton. There is no file watcher, no `IOptionsMonitor`, and no broadcast when configuration changes on disk. A handful of subsystems (MCP, external channels, skills, cron job store, per-thread config) perform their own runtime update when reached through dedicated RPC methods; everything else requires a process restart to take effect.

The Desktop Settings UI has grown three different save semantics in the same view:

- **Immediate persistence** for theme, locale, and visible channels (handled by `window.api.settings.set`).
- **Footer batch Save** for connection mode, WebSocket, Remote, Proxy, and AppServer binary path (the `handleSave` path in `SettingsView.tsx`).
- **Per-item Save / Delete / Test** for MCP entries and archived threads.

Cancel never restores state; it only navigates away. The footer Save button is conditionally hidden on tabs that have their own save flow. Users who change something that happens to require a process restart receive the same generic "Saved" toast as any other edit, with the exception of the Connection tab which swaps in a "restart required" toast after Save.

## 2. Problem Statement

Users cannot tell from the UI whether a given change takes effect immediately, needs a manual subsystem restart, or requires them to restart the whole AppServer. Because Desktop does not surface some of the most frequently adjusted fields (`ApiKey`, `EndPoint`, `Model`) at all, users typically edit them via the Dashboard or the config file directly — only to find their changes do not apply until the process restarts, with no feedback explaining why.

The root cause is twofold:

1. The **backend has no contract** for declaring per-field reload behavior. Every consumer of `AppConfig` captured a snapshot at DI construction and decides on its own whether to honor later mutations.
2. The **frontend has no contract** for rendering the three effect timings differently. The same Save button is used for fields that are immediate and for fields that require a restart.

Fixing either side alone leaves the other side to guess. This design establishes a single declared classification and aligns Desktop, AppServer, and future module authors on top of it.

## 3. Design Principles

1. **Truth over convenience.** If a field requires a restart, the UI must say so before the user commits. Silent "saved" toasts that do not actually apply are worse than a visible restart prompt.
2. **Classification is metadata, not implementation.** Moving a field between tiers in the future should be a metadata change plus the corresponding subsystem work, not a UI rewrite.
3. **Start small; make classification expandable.** Ship with a small set of hot-reload fields (those that are already closed-loop in the backend) rather than risk destabilizing `AgentFactory`, `IChatClient`, or the DI graph with a sweeping refactor.
4. **Default to the safer answer.** Fields without an explicit `ReloadBehavior` declaration default to `ProcessRestart`. Never imply hot-reload unless a subsystem actually honors it.
5. **Respect existing runtime mutations.** Subsystems that already perform live updates (MCP upsert, external channel upsert, skills enable/disable, per-thread config) remain the source of truth; this design formalizes how their changes are announced.
6. **Desktop realities override abstract classification.** When Desktop's own runtime state (for example, whether the managed Proxy is active) invalidates a field's "normal" editability, the UI must reflect that reality even if the schema says the field is editable.

## 4. Three-Tier Behavior Model

Every user-editable configuration field belongs to one of three tiers:

### 4.1 Tier A — Hot

Changes take effect without any restart. The subsystem that consumes the field either re-reads it on every use or is subscribed to a change event and rebuilds its internal state in place. Desktop presents these fields with live-apply interactions (debounce → persist → small "Saved" indicator).

### 4.2 Tier B — Subsystem Restart

Changes take effect after a user-initiated restart of the affected subsystem. The restart is in-process (no AppServer restart) and is exposed as an explicit button or composite action. Desktop presents these fields with a visible restart control and a status indicator for the subsystem.

### 4.3 Tier C — Process Restart

Changes take effect only after the AppServer process is restarted. Desktop presents these fields with an explicit "Apply & Restart" composite action or a banner that summarizes pending changes and offers the restart at the user's convenience. Autosaving a Tier C field without signaling restart is forbidden.

### 4.4 Tier D — Desktop-Local

Fields that live entirely in Electron userData (theme, locale, visible channels, etc.) and never reach AppServer. These are always immediate and are not part of the reload contract, but they share the same Tier A UX to keep the settings experience consistent.

## 5. Scope of This Design

This design defines the overall three-tier model and the contracts that surround it. The concrete milestones in this series cover:

- **M1** — Field behavior contract: `ReloadBehavior` metadata and schema surfacing.
- **M2** — `workspace/configChanged` notification and minimal `IAppConfigMonitor` abstraction, with the two already-closed-loop backends (Skills, MCP) wired to broadcast changes.
- **M3** — Desktop Settings UI rewrite onto the three-tier model, including the new LLM entry with Proxy-aware lock.
- **M4** — Protocol spec update, bilingual documentation, and tests.

The following are **explicitly deferred** to later features:

- Hot-reload for `ApiKey`, `EndPoint`, `Model`, `EnabledTools`, `Tools.*`, LSP, Logging, Heartbeat, Tracing, Hooks, Security — i.e., anything that would require rebuilding `IChatClient`, the tool filter, or other startup-captured state inside `AgentFactory` or related services.
- A `FileSystemWatcher` over `~/.craft/config.json` and `.craft/config.json`. Debouncing editor rename storms and reconciling concurrent write races is non-trivial on Windows and would expand scope beyond what this series can deliver safely.
- Dashboard adoption of the new `workspace/configChanged` broadcast. Dashboard currently writes directly to disk via its HTTP endpoints; whether to route it through AppServer RPC is an open question that does not block this series.

## 6. Proxy Override Constraint

Desktop's managed CLI proxy introduces a constraint that any UI exposing `ApiKey` or `EndPoint` must honor.

When the Desktop user enables the managed proxy, `applyWorkspaceProxyOverrides` in [`desktop/src/main/proxyWorkspaceConfig.ts`](../desktop/src/main/proxyWorkspaceConfig.ts) **persistently rewrites** the workspace `.craft/config.json` so that `ApiKey` and `EndPoint` point at the local proxy endpoint and its generated key. The original values are captured in `.craft/proxy-overrides.json`. When the proxy is disabled, `cleanupWorkspaceProxyOverrides` restores the originals from that snapshot.

The implication for Settings UX is that `ApiKey` and `EndPoint` are **not user-owned** while the proxy is active. Any edit a user makes to these fields while the proxy is running would:

1. Be overwritten by the next `applyWorkspaceProxyOverrides` call on proxy start, or
2. Be treated as the "original" value in the snapshot, and then restored when the proxy is disabled — silently changing the user's configuration without them realizing.

The UI contract is therefore:

- The source of truth for "is proxy currently active" is **Electron main's runtime proxy status**, not the content of `ApiKey` / `EndPoint` in `config.json` (which can coincidentally equal the proxy values).
- When proxy is active, `ApiKey` and `EndPoint` are rendered as read-only, visibly locked, with a clear pointer to the Proxy tab.
- When proxy is inactive, the fields are editable under their normal Tier C rules.

This constraint overrides the field's nominal `ReloadBehavior`. It is specific to Desktop; other clients (Dashboard, Web) are not subject to it unless they develop their own proxy-management concept.

## 7. Field Inventory and Classification

This section documents the classification applied in this series. Fields not listed here default to `ProcessRestart` and retain their current behavior.

### 7.1 Tier A — Hot

| Field | Source of hot-reload | Notes |
|-------|----------------------|-------|
| `Skills.DisabledSkills` (per-skill) | `skills/setEnabled` RPC → `SkillsLoader.SetDisabledSkills` | Already closed-loop; this series adds a `workspace/configChanged` broadcast. |
| MCP server list (CRUD, enable, disable) | `mcp/upsert` / `mcp/remove` → `McpClientManager` | Already closed-loop; existing `mcp/statusChanged` notification remains authoritative for per-server status. |

### 7.2 Tier B — Subsystem Restart

| Field | Subsystem and restart action |
|-------|------------------------------|
| Desktop managed proxy (enable, port, auth dir, binary source/path) | Electron main's proxy process; existing "Restart proxy" button remains the action. These fields are Desktop-local to Electron and are not stored in `AppConfig`. |

### 7.3 Tier C — Process Restart (no backend hot-reload in this series)

| Field | Rationale |
|-------|-----------|
| `ApiKey` | `AgentFactory` captures the `IChatClient` at construction; replacing it safely while turns are in flight requires a dedicated design. |
| `EndPoint` | Same as `ApiKey`. |
| `Model` (global default) | Per-thread Model switching already works via `thread/config/update` and is not affected by this series. The global default in `AppConfig.Model` requires rebuilding the default client and is deferred. |
| `ConnectionMode`, WebSocket host+port, Remote URL+token, Remote token | Bound at Electron main start-up; switching rewires the entire IPC surface. |
| AppServer binary source / path | Chosen by the Desktop launcher before spawning AppServer. |

All other `AppConfig` fields are Tier C by default until a future feature upgrades them.

### 7.4 Tier D — Desktop-Local

Theme, locale, visible channels. Unchanged by this series; mentioned only for completeness.

## 8. Milestone Roadmap

- [M1 — Field Behavior Contract](settings-reload-ux-m1.md): `ReloadBehavior` attribute, schema output, Proxy-aware UI override rule.
- [M2 — Skills / MCP Notification Closure and `workspace/configChanged`](settings-reload-ux-m2.md): minimal `IAppConfigMonitor`, broadcast contract, wiring of existing closed-loop handlers.
- [M3 — Desktop Settings UX Rewrite](settings-reload-ux-m3.md): LLM entry (with Proxy lock), Skills panel, MCP tab cleanup, Connection tab restart flow, edit-race policy.
- [M4 — Documentation and Tests](settings-reload-ux-m4.md): update AppServer protocol spec, bilingual docs, Skills/MCP closure tests, Proxy-lock rendering tests.

Each milestone is delivered and reviewed individually. M2 depends on M1's attribute; M3 depends on M1's schema output and M2's notification; M4 depends on M2 and M3.

## 9. Out of Scope

- Redesigning the Dashboard config UI or moving Dashboard writes through AppServer RPC.
- Hot-reload for `ApiKey` / `EndPoint` / `Model` (global) / `EnabledTools` / tool subsections / LSP / Logging / Heartbeat / Tracing / Hooks / Security.
- Filesystem-level watchers on `config.json`.
- Per-field audit of every `[ConfigSection]` type outside the inventory above.
- Authentication of `workspace/configChanged` notifications beyond existing AppServer transport security.

## 10. Open Questions

1. Should the Proxy-aware UI lock apply to any other field besides `ApiKey` and `EndPoint`? (Current answer: no; the proxy overrides only those two keys.)
2. Does Dashboard need to be adapted to emit `workspace/configChanged` when it writes directly to disk, or is it acceptable to leave Desktop and Dashboard out of sync when the user edits from Dashboard? (Current answer: leave for a follow-up feature; document the limitation.)
3. Should the three-tier model be visible in Dashboard as well, or is it Desktop-specific in this series? (Current preference: Dashboard continues to use its existing form model; only Desktop adopts the tier UX.)
4. For Tier C fields, should Desktop offer an "Apply without restart" escape hatch (for users who know what they are doing)? (Current preference: no, to keep the contract simple.)
