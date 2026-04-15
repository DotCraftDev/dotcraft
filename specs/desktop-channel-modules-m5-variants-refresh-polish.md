# DotCraft Desktop Channel Modules — M5: Variant Substitution, Module Refresh, and Polish

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Draft |
| **Date** | 2026-04-15 |
| **Parent Spec** | [typescript-external-channel-module-contract.md](typescript-external-channel-module-contract.md) |
| **Related Specs** | [desktop-channel-modules-m2-discovery-and-config-ui.md](desktop-channel-modules-m2-discovery-and-config-ui.md), [desktop-channel-modules-m3-subprocess-lifecycle.md](desktop-channel-modules-m3-subprocess-lifecycle.md), [desktop-channel-modules-m4-weixin-qr-setup.md](desktop-channel-modules-m4-weixin-qr-setup.md) |

Purpose: Support multiple module packages for the same channel name (variant substitution), add a module refresh/rescan flow, implement auto-start on launch, and polish error diagnostics and edge cases. After this milestone, the Desktop module integration is feature-complete.

---

## Table of Contents

- [1. Overview](#1-overview)
- [2. Goal](#2-goal)
- [3. Scope](#3-scope)
- [4. Non-Goals](#4-non-goals)
- [5. Variant Substitution](#5-variant-substitution)
- [6. Module Refresh](#6-module-refresh)
- [7. Auto-Start on Launch](#7-auto-start-on-launch)
- [8. Error Diagnostics](#8-error-diagnostics)
- [9. Modules Directory Settings UI](#9-modules-directory-settings-ui)
- [10. Edge Cases and Polish](#10-edge-cases-and-polish)
- [11. Acceptance Checklist](#11-acceptance-checklist)
- [12. Open Questions](#12-open-questions)

---

## 1. Overview

After M1–M4, Desktop can discover, configure, start, and monitor TypeScript channel modules, including the Weixin QR login flow. However, several capabilities needed for a polished product experience are still missing:

- If a user installs an enterprise variant of the Feishu adapter alongside the bundled standard one, Desktop cannot display both or let the user choose between them.
- There is no way to trigger a module rescan without restarting Desktop.
- Modules do not auto-start on launch, requiring the user to manually enable them each time.
- Error messages are basic and may not help users diagnose configuration problems.

M5 addresses all of these.

---

## 2. Goal

Make the Desktop module system production-ready by supporting variant substitution, module refresh, auto-start, and polished diagnostics.

---

## 3. Scope

- Variant substitution: multiple modules per `channelName`, with user selection.
- Module refresh: rescan directories and update the UI.
- Auto-start: persist enabled state and restart modules on launch.
- Improved error diagnostics in the UI.
- Modules directory settings UI.
- Edge case handling (missing Node, broken manifests, etc.).

---

## 4. Non-Goals

- npm-based module install/uninstall (users manually place packages in the modules directory).
- Module auto-update or version checking.
- Sandboxing or permission control for module subprocesses.
- Changes to the TypeScript SDK or adapter packages.

---

## 5. Variant Substitution

### 5.1 Concept

Multiple module packages can share the same `channelName` but have different `moduleId` values. For example:

| moduleId | channelName | variant | displayName |
|----------|-------------|---------|-------------|
| feishu-standard | feishu | standard | Feishu (Lark) |
| feishu-enterprise | feishu | enterprise | Feishu Enterprise |

Only one variant per `channelName` can be active at a time.

### 5.2 Discovery Changes

`ModuleScanner` already collects all discovered modules. M5 adds grouping:

```typescript
interface ChannelModuleGroup {
  channelName: string;
  activeModuleId: string;
  modules: DiscoveredModule[];
}
```

The `activeModuleId` is determined by:
1. Persisted selection in `settings.json` under `activeModuleVariants: { [channelName]: moduleId }`.
2. If no persisted selection, the user-installed module wins over bundled (same override rule as M2).
3. If no user-installed module, the first discovered module (by directory scan order).

### 5.3 Sidebar Display

When a `channelName` has only one module, the card displays as before (M2 behavior).

When a `channelName` has multiple modules:
- The card shows the **active** module's `displayName`.
- A small variant badge or dropdown icon indicates alternatives exist.
- Clicking the card opens the active module's config form.

### 5.4 Variant Selector

Inside `ModuleConfigForm`, when the active module has variants:
- A dropdown appears below the header: "Active variant: Feishu (Lark) [Standard] ▼"
- The dropdown lists all discovered modules for that `channelName`, showing `displayName` and `variant` label.
- Selecting a different variant:
  1. Stops the currently running subprocess (if any).
  2. Updates `activeModuleVariants` in `settings.json`.
  3. Reloads the config form with the new module's `configDescriptors`.
  4. The config file may differ (`configFileName` is per-module), so the form loads the new module's config.

### 5.5 IPC Addition

`modules:set-active-variant`

**Request**: `{ channelName: string; moduleId: string }`

**Response**: `{ ok: boolean }`

Persists the selection and stops any running subprocess for that channel.

---

## 6. Module Refresh

### 6.1 Trigger

The "Modules" group header in the sidebar has a refresh icon button. Clicking it calls `modules:rescan` (already defined in M2).

### 6.2 Behavior

On rescan:
1. Scan all module directories.
2. Compare new module list with the current list.
3. **Added modules**: appear in the sidebar immediately.
4. **Removed modules**: if the module was running, stop its subprocess first, then remove from the sidebar.
5. **Changed modules** (same `moduleId`, different manifest content — e.g. user updated the package): update metadata in memory. If the module was running, show a "Module updated — restart to apply changes" toast.

### 6.3 No File Watching for Module Directories

Module directory changes are not detected automatically. The user must click Refresh. This avoids the complexity of watching potentially non-existent or remote directories.

---

## 7. Auto-Start on Launch

### 7.1 Persisted Enabled State

A new `settings.json` key: `enabledModules: string[]` — an array of `moduleId` values that were enabled when the user last quit.

### 7.2 Save on Quit

During `teardownRuntime`, before stopping subprocesses, the `ModuleProcessManager` writes the list of currently running `moduleId` values to `enabledModules` in `settings.json`.

### 7.3 Restore on Launch

After AppServer is connected and the module list is loaded:
1. Read `enabledModules` from `settings.json`.
2. For each `moduleId` in the list:
   - If the module is still discovered and its config file exists in the current workspace, call `modules:start` for it.
   - If the module is no longer discovered or config is missing, skip it silently (don't fail the startup sequence).
3. Auto-start attempts are staggered by 500ms to avoid spawning many subprocesses simultaneously.

### 7.4 Workspace Affinity

`enabledModules` is a global setting (not per-workspace). When switching workspaces, some modules may not have config in the new workspace and will fail to start silently. This is acceptable; the user can re-enable as needed.

---

## 8. Error Diagnostics

### 8.1 Config Validation

When the user clicks Enable:
1. Read the config file.
2. Check that all `required` fields in `configDescriptors` have non-empty values.
3. If validation fails, show an inline error listing the missing fields: "Required fields missing: Feishu App ID, Feishu App Secret".
4. Do not spawn the subprocess.

### 8.2 Node.js Check

On app startup:
1. Check if `node` is available on PATH by running `node --version`.
2. If not found, show a persistent warning banner in the Channels view: "Node.js is required to run channel modules. Install Node.js 20+ and restart Desktop."
3. Module enable toggles are disabled while Node is not available.

### 8.3 Subprocess Failure Details

When a module subprocess crashes:
- Show the last N lines (up to 20) of stderr output in the error banner.
- Include the exit code.
- Common patterns are detected and annotated:
  - `ECONNREFUSED` → "Cannot connect to AppServer. Check that DotCraft is running."
  - `MODULE_NOT_FOUND` → "Module dependency missing. Try reinstalling the module."
  - `ENOENT` → "Config file not found."

### 8.4 Log Viewer

A "View Logs" button in the error banner opens a small modal or expandable section showing the subprocess's recent stdout/stderr output (last 100 lines, stored in a ring buffer by `ModuleProcessManager`).

---

## 9. Modules Directory Settings UI

### 9.1 Setting

A new field in Desktop Settings: "Modules directory" with a text input and a browse button.

Displays the current value (from `settings.json` `modulesDirectory` key, or the default path if unset).

### 9.2 Behavior

Changing the directory:
1. Saves to `settings.json`.
2. Triggers a `modules:rescan`.
3. If the new directory does not exist, shows a warning: "Directory does not exist. Create it or choose another path."

---

## 10. Edge Cases and Polish

### 10.1 Duplicate moduleId

If two directories contain modules with the same `moduleId`, the user-installed version wins (M2 rule). The bundled version is hidden. No warning is shown (this is the intended override mechanism).

### 10.2 Empty Modules Directory

If no modules are discovered (neither bundled nor user-installed), the "Modules" group shows a help message: "No channel modules found. Bundled modules may be missing or the modules directory is empty."

### 10.3 Module Without Matching Logo

If `channelName` does not match any asset in `src/renderer/assets/channels/`, the card shows the first letter of `displayName` in a colored circle (existing fallback from `ChannelCard`).

### 10.4 Large Config Files

Config files are expected to be small (< 10KB). If a config file is unexpectedly large (> 1MB), skip loading and show an error.

### 10.5 Concurrent Config Writes

If the user saves config while a previous save is in flight, the second save waits for the first to complete. No debouncing — each save writes the full file.

### 10.6 Windows Path Handling

All paths must use platform-appropriate separators. `path.join` handles this. Module directory paths in `settings.json` may use forward or back slashes; normalize on read.

---

## 11. Acceptance Checklist

- [ ] When two modules share a `channelName`, the sidebar shows one card for the active variant.
- [ ] The variant selector dropdown appears in `ModuleConfigForm` when alternatives exist.
- [ ] Selecting a different variant stops the running subprocess and switches config.
- [ ] `activeModuleVariants` persists to `settings.json`.
- [ ] Refresh button in the sidebar triggers `modules:rescan`.
- [ ] Added modules appear after rescan; removed modules disappear.
- [ ] Running modules that are removed are stopped first.
- [ ] `enabledModules` is saved to `settings.json` on quit.
- [ ] On launch, previously enabled modules auto-start after AppServer connects.
- [ ] Auto-start silently skips modules that are no longer discovered.
- [ ] Config validation prevents enabling a module with missing required fields.
- [ ] Node.js availability check runs on startup with a warning banner if missing.
- [ ] Subprocess crash shows stderr excerpt and exit code in the error banner.
- [ ] "View Logs" button shows recent subprocess output.
- [ ] Modules directory setting is editable in Desktop Settings.
- [ ] Changing the modules directory triggers a rescan.
- [ ] Empty module list shows a help message.

---

## 12. Open Questions

- Should variant selection be per-workspace or global? Currently proposed as global (`settings.json`). Per-workspace would require storing it in `.craft/config.json`, which involves AppServer. Recommendation: global for this milestone.
- Should the log viewer persist logs across restarts? Recommendation: no, in-memory ring buffer is sufficient. Users can check system logs for historical data.
- Should Desktop offer a "quick install" button that opens the `~/.craft/modules/` directory in the file explorer? Recommendation: yes, low effort and helpful. Add a "Open modules folder" action in the Modules group header.
