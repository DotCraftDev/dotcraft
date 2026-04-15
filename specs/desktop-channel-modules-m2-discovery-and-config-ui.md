# DotCraft Desktop Channel Modules — M2: Module Discovery and Config UI

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Draft |
| **Date** | 2026-04-15 |
| **Parent Spec** | [typescript-external-channel-module-contract.md](typescript-external-channel-module-contract.md) |
| **Related Specs** | [desktop-channel-modules-m1-manifest-and-build-pipeline.md](desktop-channel-modules-m1-manifest-and-build-pipeline.md) |

Purpose: Make Desktop discover installed TypeScript channel modules by reading static `manifest.json` files, display them as first-class channel cards in the Channels view, render config forms driven by `configDescriptors`, and persist configuration to per-channel JSON files in the workspace `.craft/` directory. This milestone replaces the preset external channel entries for feishu and weixin with the new module-based flow.

---

## Table of Contents

- [1. Overview](#1-overview)
- [2. Goal](#2-goal)
- [3. Scope](#3-scope)
- [4. Non-Goals](#4-non-goals)
- [5. Module Discovery](#5-module-discovery)
- [6. IPC Contract](#6-ipc-contract)
- [7. Renderer Data Model](#7-renderer-data-model)
- [8. Channel Card Integration](#8-channel-card-integration)
- [9. Module Config Form](#9-module-config-form)
- [10. Config Persistence](#10-config-persistence)
- [11. Migration from Preset External Channels](#11-migration-from-preset-external-channels)
- [12. Constraints and Compatibility](#12-constraints-and-compatibility)
- [13. Acceptance Checklist](#13-acceptance-checklist)
- [14. Open Questions](#14-open-questions)

---

## 1. Overview

After M1, each adapter package contains a static `manifest.json` at its root. Desktop bundles `channel-feishu` and `channel-weixin` under `resources/modules/`, and users may install additional modules under `~/.craft/modules/` (configurable).

M2 adds the runtime code to scan these directories, expose discovered modules to the renderer via IPC, display them as channel cards, render a dynamic config form from `configDescriptors`, and save configuration to the workspace.

Currently, feishu and weixin appear in the Channels sidebar as "preset external channels" with a generic form (`ExternalChannelConfigForm`) that shows subprocess command/args/env fields. After M2, they appear as module-based cards with friendly, labeled config fields. The old preset entries are removed.

---

## 2. Goal

Let users see and configure bundled and user-installed TypeScript channel modules through the Desktop Channels view, using module-authored config forms instead of generic external channel forms.

---

## 3. Scope

- Implement a module scanner in the Electron main process.
- Expose module metadata to the renderer via IPC.
- Display module cards in `ChannelsView`.
- Build a dynamic `ModuleConfigForm` component driven by `configDescriptors`.
- Read and write per-channel config files (`.craft/<configFileName>`).
- Remove `weixin` and `feishu` from `PRESET_EXTERNAL_CHANNELS`.

---

## 4. Non-Goals

- Starting, stopping, or monitoring adapter subprocesses (M3).
- QR code display or interactive setup (M4).
- Variant substitution when multiple modules share a `channelName` (M5).
- Changes to the AppServer C# codebase.
- Changes to the TypeScript SDK runtime code.

---

## 5. Module Discovery

### 5.1 Scanner

The main process implements a `ModuleScanner` that:

1. Determines the list of module directories to scan:
   - **Bundled**: `path.join(process.resourcesPath, 'modules')` in production; a dev-time fallback path for development mode (e.g. a configured path to `sdk/typescript/packages/`).
   - **User-installed**: read from `settings.json` key `modulesDirectory`; default `path.join(app.getPath('home'), '.craft', 'modules')`.
2. For each directory, lists immediate child subdirectories.
3. For each child, attempts to read and parse `manifest.json`.
4. Validates that required fields exist (`moduleId`, `channelName`, `displayName`, `configDescriptors`).
5. Records the module's `source` (`"bundled"` or `"user"`) and `absolutePath` (the directory containing `manifest.json`).
6. Deduplicates by `moduleId`: if the same `moduleId` appears in both bundled and user directories, the user-installed version wins.
7. Returns the list of discovered modules.

### 5.2 Scan Timing

The scanner runs:
- Once at app startup (after workspace path is resolved).
- On explicit refresh (IPC `modules:rescan`).

The scan is synchronous-safe (manifest files are small, local, and few). It may use `readFileSync` or awaited `readFile`; the result is cached in memory until the next rescan.

### 5.3 Error Handling

- Directories that do not exist are silently skipped (e.g. `~/.craft/modules/` may not exist).
- Subdirectories without `manifest.json` are silently skipped.
- `manifest.json` files that fail to parse or lack required fields are skipped; a warning is logged to console.
- I/O errors on individual directories do not prevent scanning of other directories.

---

## 6. IPC Contract

All new IPC channels follow the existing `namespace:verb` naming convention.

### 6.1 `modules:list`

Returns the list of discovered modules.

**Request**: no arguments.

**Response**:

```typescript
interface DiscoveredModule {
  moduleId: string;
  channelName: string;
  displayName: string;
  packageName: string;
  configFileName: string;
  supportedTransports: string[];
  requiresInteractiveSetup: boolean;
  variant: string;
  source: "bundled" | "user";
  configDescriptors: ConfigDescriptorWire[];
}

interface ConfigDescriptorWire {
  key: string;
  displayLabel: string;
  description: string;
  required: boolean;
  dataKind: string;
  masked: boolean;
  interactiveSetupOnly: boolean;
  defaultValue?: unknown;
  enumValues?: string[];
}
```

### 6.2 `modules:rescan`

Re-runs the module scanner and returns the updated list (same shape as `modules:list`).

### 6.3 `modules:read-config`

Reads a module's config file from the workspace.

**Request**: `{ configFileName: string }` — e.g. `"feishu.json"`.

**Response**: `{ exists: boolean; config: Record<string, unknown> | null }`.

The handler reads `path.join(workspacePath, '.craft', configFileName)`. If the file does not exist, returns `{ exists: false, config: null }`. If it exists, parses as JSON and returns the content.

### 6.4 `modules:write-config`

Writes a module's config file to the workspace.

**Request**: `{ configFileName: string; config: Record<string, unknown> }`.

**Response**: `{ ok: boolean }`.

The handler writes the JSON to `path.join(workspacePath, '.craft', configFileName)` with 2-space indentation. It creates the `.craft/` directory if it does not exist.

---

## 7. Renderer Data Model

### 7.1 Module Store

A new Zustand store or extension of the existing connection store holds the module list:

```typescript
interface ModuleStore {
  modules: DiscoveredModule[];
  loading: boolean;
  loadModules(): Promise<void>;
  rescanModules(): Promise<void>;
}
```

`loadModules` is called once on `ChannelsView` mount. The module list is stable for the lifetime of the view unless the user triggers a rescan.

### 7.2 Module Config State

Per-module config is loaded on card selection using `modules:read-config` and held in local component state within `ChannelsView`, similar to how `externalDraft` works today.

---

## 8. Channel Card Integration

### 8.1 Card Grouping

The Channels sidebar currently has two groups: **Native** (QQ, WeCom) and **External**. After M2, the sidebar has three groups:

1. **Native** — QQ, WeCom (unchanged).
2. **Modules** — discovered TypeScript channel modules (feishu, weixin, plus any user-installed).
3. **External** — remaining generic external channels (telegram preset, plus any custom ones added by the user via `externalChannel/upsert`).

### 8.2 Module Cards

Each discovered module renders a `ChannelCard` with:
- **Logo**: looked up by `channelName` from the existing asset map (`src/renderer/assets/channels/<channelName>.svg`). If no matching asset exists, falls back to the first-letter circle.
- **Label**: `displayName` from the manifest.
- **Status**: `notConfigured` initially. After M3 adds lifecycle status, this will show live connection state.
- **Selection key**: `module:<moduleId>`, e.g. `module:feishu-standard`.

### 8.3 Interaction

Clicking a module card:
1. Sets `selectedChannelKey` to `module:<moduleId>`.
2. Loads the module's config file via `modules:read-config`.
3. Renders `ModuleConfigForm` in the main area.

---

## 9. Module Config Form

### 9.1 Component: `ModuleConfigForm`

A new React component that dynamically renders form fields from a `configDescriptors` array.

**Props**:

```typescript
interface ModuleConfigFormProps {
  module: DiscoveredModule;
  config: Record<string, unknown>;
  onChange: (config: Record<string, unknown>) => void;
  onSave: () => void;
  saving: boolean;
  logoPath?: string;
}
```

### 9.2 Layout

- **Header**: module logo, `displayName`, source badge (`Bundled` / `User`), `StatusPill`.
- **Config fields**: one field per `ConfigDescriptor`, rendered in the order they appear in the array.
- **Footer**: `FormActions` (Save button).

### 9.3 Field Rendering by `dataKind`

| `dataKind` | Rendered as |
|------------|-------------|
| `string` | Text input |
| `secret` | Password input (with show/hide toggle) |
| `path` | Text input with optional browse button (future) |
| `enum` | Select dropdown populated from `enumValues` |
| `boolean` | Toggle switch |
| `number` | Number input |
| `object` | Textarea (JSON) |
| `list` | Textarea (one item per line) |

Each field shows:
- `displayLabel` as the label.
- `description` as help text below the input.
- A "required" indicator for fields where `required === true`.
- `defaultValue` as placeholder text when the field has no user value.

### 9.4 Config Mapping

The config object uses dot-separated keys from `ConfigDescriptor.key` as flat keys in the JSON file:

```json
{
  "dotcraft.wsUrl": "ws://127.0.0.1:9100/ws",
  "dotcraft.token": "",
  "feishu.appId": "cli_xxx",
  "feishu.appSecret": "xxx"
}
```

This flat-key format is consistent with how the adapter packages read config. The form reads `config[descriptor.key]` for each field and writes back to the same flat structure.

### 9.5 Masked Fields

Fields with `masked: true` render as password inputs. The actual value is stored in the config file in plain text (encryption is out of scope for this milestone).

### 9.6 interactiveSetupOnly Fields

Fields with `interactiveSetupOnly: true` are hidden from the config form. They represent values obtained through interactive flows (e.g. QR login) rather than manual entry. This filtering is relevant for weixin, which obtains credentials through QR login rather than config entry.

---

## 10. Config Persistence

### 10.1 File Location

Each module's config is stored at `<workspacePath>/.craft/<configFileName>`, where `configFileName` comes from the module manifest. Examples:

- Feishu: `.craft/feishu.json`
- Weixin: `.craft/weixin.json`

### 10.2 File Format

Plain JSON object with flat dot-separated keys:

```json
{
  "dotcraft.wsUrl": "ws://127.0.0.1:9100/ws",
  "feishu.appId": "cli_abc123",
  "feishu.appSecret": "secret_value"
}
```

### 10.3 Save Flow

1. User edits fields in `ModuleConfigForm`.
2. User clicks Save.
3. Renderer calls `modules:write-config` with `{ configFileName, config }`.
4. Main process writes the file.
5. Renderer shows a success toast.

### 10.4 Load Flow

1. User clicks a module card.
2. Renderer calls `modules:read-config` with `{ configFileName }`.
3. If the file exists, the config populates the form fields.
4. If the file does not exist, fields show `defaultValue` placeholders and are otherwise empty.

---

## 11. Migration from Preset External Channels

### 11.1 Removals

- Remove `weixin` and `feishu` entries from `PRESET_EXTERNAL_CHANNELS` in `presetExternalChannels.ts`.
- The `telegram` preset remains (it is not a TypeScript module).

### 11.2 Legacy Cleanup

- `WeixinConfigForm.tsx` — already orphaned and not wired into `ChannelsView`. Remove the file.
- `TelegramConfigForm.tsx` — check if orphaned; if so, remove.

### 11.3 Existing Config Migration

If a workspace has existing `externalChannel/upsert`-persisted config for `weixin` or `feishu` in `.craft/config.json` under `ExternalChannels`, that config is unrelated to the new module config files. The old config is ignored by the new module flow. Users must reconfigure via the new module form. No automated migration is provided in this milestone.

---

## 12. Constraints and Compatibility

- Module discovery must not execute any JavaScript from module packages.
- The renderer must not import or require any module package code.
- Config files are plain JSON; no schema validation beyond checking that required fields are non-empty before saving (warning-only, save still proceeds).
- The `modules:*` IPC channels must be registered in `ipcBridge.ts` following the existing `handleSafe` pattern.
- Module cards must work correctly even when AppServer is not connected (discovery is filesystem-only).
- The existing native channel (QQ, WeCom) and remaining external channel flows must be unaffected.

---

## 13. Acceptance Checklist

- [ ] `ModuleScanner` in main process discovers bundled modules from `resources/modules/`.
- [ ] `ModuleScanner` discovers user-installed modules from the configurable directory (default `~/.craft/modules/`).
- [ ] User-installed modules override bundled modules with the same `moduleId`.
- [ ] `modules:list` IPC returns discovered modules with full metadata including `configDescriptors`.
- [ ] `modules:rescan` IPC re-scans and returns updated list.
- [ ] `modules:read-config` IPC reads `.craft/<configFileName>` from the workspace.
- [ ] `modules:write-config` IPC writes `.craft/<configFileName>` to the workspace.
- [ ] Module cards appear in the Channels sidebar under a "Modules" group.
- [ ] Module cards show the channel logo from existing assets when available.
- [ ] Clicking a module card loads its config and shows `ModuleConfigForm`.
- [ ] `ModuleConfigForm` renders fields matching the module's `configDescriptors`.
- [ ] All `dataKind` types render appropriate input controls.
- [ ] `masked` fields render as password inputs.
- [ ] `interactiveSetupOnly` fields are hidden from the form.
- [ ] Saving the form writes config to `.craft/<configFileName>`.
- [ ] `weixin` and `feishu` are removed from `PRESET_EXTERNAL_CHANNELS`.
- [ ] `WeixinConfigForm.tsx` is removed.
- [ ] Native QQ/WeCom channel flow is unaffected.
- [ ] Remaining external channels (telegram, custom) are unaffected.
- [ ] Module discovery works when AppServer is not connected.

---

## 14. Open Questions

- Should the "Modules" sidebar group have a "Refresh" button to trigger `modules:rescan`? Recommendation: yes, as a small icon button in the group header. This is cheap to implement and useful during development.
- Should there be a settings UI for the `modulesDirectory` path, or is `settings.json` manual editing sufficient for now? Recommendation: manual editing is acceptable for this milestone; a UI can be added in M5.
- Should config files be created with default values on first save, or only include fields the user has explicitly set? Recommendation: include all fields, using `defaultValue` for unset fields. This makes the config file self-documenting and avoids confusion about which defaults apply.
