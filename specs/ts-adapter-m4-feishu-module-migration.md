# DotCraft TypeScript Adapter SDK — M4: Feishu Module Migration

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Draft |
| **Date** | 2026-04-15 |
| **Parent Spec** | [typescript-external-channel-module-contract.md](typescript-external-channel-module-contract.md), [typescript-external-channel-packages.md](typescript-external-channel-packages.md) |
| **Related Specs** | [ts-adapter-m1-sdk-module-contract-types.md](ts-adapter-m1-sdk-module-contract-types.md), [ts-adapter-m2-channel-adapter-module-refactor.md](ts-adapter-m2-channel-adapter-module-refactor.md), [ts-adapter-m3-package-infrastructure.md](ts-adapter-m3-package-infrastructure.md) |

Purpose: Migrate `@dotcraft/channel-feishu` from a plain CLI adapter into a fully conforming module package. After M4, a host can load the Feishu module's manifest, create an instance, observe lifecycle, and start/stop the adapter — all through the stable module contract surface, without importing package-internal files.

---

## Table of Contents

- [1. Overview](#1-overview)
- [2. Goal](#2-goal)
- [3. Scope](#3-scope)
- [4. Non-Goals](#4-non-goals)
- [5. User Experience and Behavioral Contract](#5-user-experience-and-behavioral-contract)
- [6. Manifest Contract](#6-manifest-contract)
- [7. Module Factory and Instance Contract](#7-module-factory-and-instance-contract)
- [8. Config Contract](#8-config-contract)
- [9. Lifecycle Contract](#9-lifecycle-contract)
- [10. Tool Registration Contract](#10-tool-registration-contract)
- [11. CLI Contract](#11-cli-contract)
- [12. Package Exports Contract](#12-package-exports-contract)
- [13. Test Contract](#13-test-contract)
- [14. Documentation Contract](#14-documentation-contract)
- [15. Constraints and Compatibility](#15-constraints-and-compatibility)
- [16. Acceptance Checklist](#16-acceptance-checklist)
- [17. Open Questions](#17-open-questions)

---

## 1. Overview

After M3, `@dotcraft/channel-feishu` exists as a standard Node package with the correct directory structure and metadata. It builds and passes its existing tests. However, it does not yet conform to the module contract: there is no `manifest` export, no `createModule` factory, no typed config descriptor, no lifecycle observation, and the CLI does not support `--workspace`.

M4 implements full module contract conformance for the Feishu package. The `FeishuAdapter` class is migrated to extend `ModuleChannelAdapter` (from M2), the package root exports `manifest` and `createModule`, the CLI is updated to accept `--workspace`, and the existing tests are preserved and supplemented with conformance-level tests.

---

## 2. Goal

Make `@dotcraft/channel-feishu` a first-class pluggable module package that any host can integrate through the stable module contract without depending on package-internal implementation files.

---

## 3. Scope

- Define and export a `manifest` object of type `ModuleManifest`.
- Implement and export a `createModule` factory of type `ModuleFactory`.
- Migrate `FeishuAdapter` to extend `ModuleChannelAdapter` (or equivalent from M2).
- Implement `validateConfig`, `buildTransportFromConfig`, and typed Feishu config.
- Define and export a `configDescriptors` array of `ConfigDescriptor`.
- Implement lifecycle status reporting per M2 contract.
- Update the CLI entry to support `--workspace <path>` and `--config <path>`.
- Preserve all existing tests and add module conformance tests.
- Update `README.md` and `README_ZH.md` per the documentation contract.

---

## 4. Non-Goals

- Implementing an enterprise Feishu variant.
- Changing Feishu's message rendering, card formatting, event handling, or approval UX.
- Implementing Desktop UI for Feishu configuration or lifecycle display.
- Publishing the package to npm.
- Changing `dotcraft-wire` or any other package.

---

## 5. User Experience and Behavioral Contract

### 5.1 Host Integration Flow

A host integrating the Feishu module must be able to follow this sequence:

1. Load the module: `import { manifest, createModule } from "@dotcraft/channel-feishu"`.
2. Read `manifest` to determine identity, config file name, transport support, and capabilities.
3. Call `createModule(workspaceContext)` to get a `ModuleInstance`.
4. Register a lifecycle handler: `instance.onStatusChange((status, error) => ...)`.
5. Call `instance.start()`.
6. Observe lifecycle transitions: `configMissing` if no config, `configInvalid` if invalid, `starting` → `ready` on success.
7. Call `instance.stop()` to shut down.

The host must not import `FeishuAdapter`, `FeishuClient`, or any other package-internal file.

### 5.2 CLI Flow

A developer starting the Feishu adapter locally must be able to use:

```
dotcraft-channel-feishu --workspace /path/to/workspace
```

Optionally:
```
dotcraft-channel-feishu --workspace /path/to/workspace --config /override/path/feishu.json
```

The adapter reads config from `.craft/feishu.json` under the specified workspace (or from the override path), connects to AppServer via WebSocket, and begins processing Feishu events.

### 5.3 Preserved Behaviors

All existing Feishu adapter behaviors are preserved:

- Feishu event handling (messages, mentions, card actions).
- Card action deduplication.
- Message delivery (text, file, interactive card).
- Approval request handling.
- Tool call handling (`FeishuSendFileToCurrentChat`).
- Streaming turn reply delivery.
- Session identity mapping from Feishu user/chat IDs.

---

## 6. Manifest Contract

The exported `manifest` must be a `ModuleManifest` value (type from M1) with the following normative field values:

| Field | Value |
|-------|-------|
| `moduleId` | `"feishu-standard"` |
| `channelName` | `"feishu"` |
| `displayName` | `"飞书"` |
| `packageName` | `"@dotcraft/channel-feishu"` |
| `configFileName` | `"feishu.json"` |
| `supportedTransports` | `["websocket"]` |
| `requiresInteractiveSetup` | `false` |
| `sdkContractVersion` | The value of `sdkContractVersion` from `dotcraft-wire` |
| `supportedProtocolVersions` | `["0.2"]` |
| `variant` | `"standard"` |
| `capabilitySummary.hasChannelTools` | `true` |
| `capabilitySummary.hasStructuredDelivery` | `true` |
| `capabilitySummary.requiresInteractiveSetup` | `false` |
| `capabilitySummary.capabilitySetMayVaryByEnvironment` | `false` |
| `launcher.bin` | `"dotcraft-channel-feishu"` |
| `launcher.supportsWorkspaceFlag` | `true` |
| `launcher.supportsConfigOverrideFlag` | `true` |

The manifest object is a plain JavaScript object (no class, no computed fields) defined in `src/manifest.ts` and exported from `src/index.ts`.

---

## 7. Module Factory and Instance Contract

### 7.1 createModule

```typescript
export function createModule(context: WorkspaceContext): ModuleInstance
```

Calling `createModule` must:

1. Create a `FeishuAdapter` instance (or equivalent internal object) that holds the workspace context.
2. Return a `ModuleInstance` handle that wraps the adapter's lifecycle methods.
3. Not connect to any network or read any file at creation time. All I/O happens inside `instance.start()`.

### 7.2 ModuleInstance Implementation

The returned `ModuleInstance` must implement the contract from M1 §8.2:

| Method | Behavior |
|--------|----------|
| `start()` | Calls `startWithContext(context)` on the adapter. |
| `stop()` | Calls `stop()` on the adapter. |
| `onStatusChange(handler)` | Delegates to the adapter's handler registration. |
| `getStatus()` | Returns the adapter's current `LifecycleStatus`. |
| `getError()` | Returns the adapter's current `ModuleError` if any. |

The `ModuleInstance` may be implemented as a thin wrapper object or directly as a class instance if the `FeishuAdapter` itself implements the `ModuleInstance` interface.

---

## 8. Config Contract

### 8.1 Config File Location

The adapter reads its config from:

- `context.craftPath + "/feishu.json"` by default.
- `context.configOverridePath` if set.

This matches the standard discovery rule from M2.

### 8.2 Config Schema

The Feishu config file (`feishu.json`) must contain at minimum:

```json
{
  "dotcraft": {
    "wsUrl": "ws://127.0.0.1:8080/ws",
    "token": "optional-auth-token"
  },
  "feishu": {
    "appId": "cli_xxx",
    "appSecret": "xxx",
    "verificationToken": "xxx",
    "encryptKey": "optional"
  }
}
```

The existing `AppConfig` type in the Feishu package source is updated or replaced by a named type that matches this schema. The type must be usable as the generic parameter in `ModuleChannelAdapter<FeishuConfig>`.

### 8.3 Config Descriptors

The package must export:

```typescript
export const configDescriptors: ConfigDescriptor[]
```

The array must describe all fields in the Feishu config schema, including at minimum:

| Key | displayLabel | dataKind | required | masked |
|-----|--------------|----------|----------|--------|
| `dotcraft.wsUrl` | AppServer WebSocket URL | `"string"` | Yes | No |
| `dotcraft.token` | AppServer Auth Token | `"secret"` | No | Yes |
| `feishu.appId` | Feishu App ID | `"string"` | Yes | No |
| `feishu.appSecret` | Feishu App Secret | `"secret"` | Yes | Yes |
| `feishu.verificationToken` | Verification Token | `"secret"` | Yes | Yes |
| `feishu.encryptKey` | Encrypt Key | `"secret"` | No | Yes |

### 8.4 Config Validation

`validateConfig` must check that `feishu.appId`, `feishu.appSecret`, and `feishu.verificationToken` are non-empty strings, and that `dotcraft.wsUrl` is a valid WebSocket URL. If any required field is missing or invalid, it throws a `ConfigValidationError` with a descriptive message.

---

## 9. Lifecycle Contract

### 9.1 Normal Startup Sequence

```
[created] → start() called → starting
starting → config found and valid → connecting to AppServer
connecting → initialize() succeeded → ready
```

### 9.2 Config Missing

```
[created] → start() called → starting
starting → feishu.json not found → configMissing
(adapter stops; no connection attempted)
```

### 9.3 Config Invalid

```
[created] → start() called → starting
starting → feishu.json found but validation fails → configInvalid
(adapter stops; no connection attempted)
```

### 9.4 Shutdown

```
[ready] → stop() called → stopped
```

### 9.5 Fatal Runtime Error

If the adapter loses its AppServer connection after reaching `ready` and cannot recover, it transitions to `stopped` with a `ModuleError` of code `"unexpectedRuntimeFailure"`. The host observes this via `onStatusChange`.

---

## 10. Tool Registration Contract

### 10.1 Typed Tool Descriptor

The `FeishuSendFileToCurrentChat` tool must be declared using `ChannelToolDescriptor` (from M1):

```typescript
const feishuSendFileTool: ChannelToolDescriptor = {
  name: "FeishuSendFileToCurrentChat",
  displayName: "Send File to Current Chat",
  description: "Sends a file to the current Feishu chat context.",
  inputSchema: { ... },
  approval: {
    required: true,
    promptTemplate: "Allow sending file '{{fileName}}' to the current Feishu chat?"
  }
}
```

### 10.2 getChannelTools Upgrade

`FeishuAdapter.getChannelTools()` is updated to return `ChannelToolDescriptor[]` typed values. The return type in `ChannelAdapter` is still `Record<string, unknown>[] | null` at this milestone (the base class is not changed); the upgrade is in the concrete implementation.

If `ModuleChannelAdapter` is generic, a typed override can be provided. If not, the existing `Record<string, unknown>[]` return type is preserved and the typed descriptor is cast before returning.

---

## 11. CLI Contract

### 11.1 Entry Point

`src/cli.ts` is the CLI entry point compiled to `dist/cli.js` and registered in `package.json` `bin` as `dotcraft-channel-feishu`.

### 11.2 Arguments

The CLI must support:

| Flag | Required | Behavior |
|------|----------|----------|
| `--workspace <path>` | Yes (unless `--config` is used alone) | Sets `workspaceRoot`; `.craft/` is derived as `<path>/.craft`. |
| `--config <path>` | No | Overrides config file path. |

If neither `--workspace` nor `--config` is provided, the CLI falls back to the legacy behavior of reading from `argv[2]` or the `DOTCRAFT_FEISHU_CONFIG` environment variable, for backward compatibility during the transition period.

A deprecation warning is logged to stderr when the legacy fallback is used.

### 11.3 Machine-Readable Startup Failure

If the adapter transitions to `configMissing` or `configInvalid`, the CLI must log a structured message to stderr and exit with code 1. The message must include the error code.

---

## 12. Package Exports Contract

`src/index.ts` must export:

```typescript
export { manifest } from "./manifest.js";
export { createModule } from "./module.js";
export { configDescriptors } from "./config-descriptors.js";
export { FeishuAdapter } from "./feishu-adapter.js";   // preserved for backward compat
export type { FeishuConfig } from "./feishu-types.js";
```

Hosts must be able to use only `manifest`, `createModule`, and `configDescriptors` without importing any other file.

---

## 13. Test Contract

### 13.1 Preserved Tests

All tests from `packages/channel-feishu/src/*.test.*` that passed before M3 must continue to pass after M4. These cover:

- Transcript rendering.
- Feishu event handler behavior.
- Card action deduplication.

### 13.2 New Module Conformance Tests

A new test file `src/module.test.ts` must verify:

- `manifest.moduleId` is `"feishu-standard"`.
- `manifest.channelName` is `"feishu"`.
- `manifest.sdkContractVersion` matches the value exported by `dotcraft-wire`.
- `createModule` returns an object with `start`, `stop`, `onStatusChange`, `getStatus`, `getError`.
- `createModule` does not connect to any network (verifiable by checking `getStatus()` is `"stopped"` immediately after creation).
- `configDescriptors` is a non-empty array and each entry has `key`, `displayLabel`, `dataKind`, `required`, `masked`.

### 13.3 Config Validation Tests

A test file `src/config.test.ts` must verify:

- `validateConfig` throws `ConfigValidationError` when `feishu.appId` is missing.
- `validateConfig` throws when `feishu.appSecret` is missing.
- `validateConfig` throws when `dotcraft.wsUrl` is missing or not a WebSocket URL.
- `validateConfig` succeeds with a minimal valid config object.

---

## 14. Documentation Contract

### 14.1 README.md

`packages/channel-feishu/README.md` must include:

1. Package description and feature summary.
2. Installation section (local workspace or `npm install` path).
3. Workspace config section: the `.craft/config.json` snippet needed to enable the `feishu` external channel.
4. Adapter config section: the `.craft/feishu.json` format with all fields described.
5. CLI usage: `dotcraft-channel-feishu --workspace <path>` with examples.
6. Host integration section: how a host loads `manifest` and calls `createModule`.
7. Interactive setup: state that this module does not require interactive setup.
8. Development notes: building, testing, and using a custom config path.

### 14.2 README_ZH.md

Chinese translation of README.md covering the same sections.

---

## 15. Constraints and Compatibility

- The Feishu Lark SDK (`@larksuiteoapi/node-sdk`) remains a dependency. No change to how the Feishu API client is initialized.
- The `FeishuAdapter` class remains exported for backward compatibility. If there are existing consumers that import it directly, they are not broken.
- The module's `channelName` (`"feishu"`) must match the `channelName` declared in the DotCraft server's `ExternalChannels` config for the session to connect.
- The `moduleId` (`"feishu-standard"`) is used for state and temp path namespacing; any migration of existing `.craft/state/` data is out of scope for this milestone.

---

## 16. Acceptance Checklist

- [ ] `import { manifest, createModule, configDescriptors } from "@dotcraft/channel-feishu"` succeeds from package root.
- [ ] `manifest.moduleId === "feishu-standard"`.
- [ ] `manifest.channelName === "feishu"`.
- [ ] `manifest.configFileName === "feishu.json"`.
- [ ] `manifest.sdkContractVersion` matches `dotcraft-wire`'s `sdkContractVersion`.
- [ ] `createModule(ctx)` returns an object implementing `ModuleInstance`.
- [ ] `createModule(ctx).getStatus()` returns `"stopped"` immediately after creation.
- [ ] Calling `instance.start()` with no `feishu.json` transitions status to `configMissing`.
- [ ] Calling `instance.start()` with invalid config transitions status to `configInvalid`.
- [ ] `configDescriptors` includes entries for all required config fields.
- [ ] All masked fields (`appSecret`, `verificationToken`, `token`) have `masked: true`.
- [ ] `dotcraft-channel-feishu --workspace <path>` starts the adapter using `.craft/feishu.json`.
- [ ] `dotcraft-channel-feishu --config <path>` uses the override config path.
- [ ] CLI exits with code 1 and prints error code to stderr on `configMissing` or `configInvalid`.
- [ ] All pre-M4 tests still pass.
- [ ] New module conformance tests pass.
- [ ] New config validation tests pass.
- [ ] `npm run build && npm run test && npm pack --dry-run` all succeed for `@dotcraft/channel-feishu`.
- [ ] `README.md` includes all required sections.

---

## 17. Open Questions

- Should the Feishu package export a typed `FeishuModuleInstance` interface that extends `ModuleInstance` with Feishu-specific methods (e.g. `getConnectedBotInfo()`), or should it expose only the base `ModuleInstance`? Start with the base interface only; typed extensions can be added if a host needs them.
- Should `FeishuConfig` be exported from the package root and documented, so that enterprise variants can share it as a base? Yes — it should be exported. Enterprise variants can import and extend it without importing internal implementation files.
- The current Feishu adapter connects via WebSocket. Should the manifest declare `supportedTransports: ["websocket"]` as fixed, or allow it to be parameterized by workspace config (e.g. subprocess mode)? Fixed as `["websocket"]` for this milestone; the manifest is static.
