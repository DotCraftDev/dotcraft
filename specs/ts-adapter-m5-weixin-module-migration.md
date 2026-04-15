# DotCraft TypeScript Adapter SDK — M5: Weixin Module Migration

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Draft |
| **Date** | 2026-04-15 |
| **Parent Spec** | [typescript-external-channel-module-contract.md](typescript-external-channel-module-contract.md), [typescript-external-channel-packages.md](typescript-external-channel-packages.md) |
| **Related Specs** | [ts-adapter-m1-sdk-module-contract-types.md](ts-adapter-m1-sdk-module-contract-types.md), [ts-adapter-m2-channel-adapter-module-refactor.md](ts-adapter-m2-channel-adapter-module-refactor.md), [ts-adapter-m3-package-infrastructure.md](ts-adapter-m3-package-infrastructure.md), [ts-adapter-m4-feishu-module-migration.md](ts-adapter-m4-feishu-module-migration.md) |

Purpose: Migrate `@dotcraft/channel-weixin` from a plain CLI adapter into a fully conforming module package, including the interactive setup contract for QR-based login, persistent runtime state under the module-owned state directory, and temp file management under the module-owned temp directory.

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
- [9. State and Temp Layout Contract](#9-state-and-temp-layout-contract)
- [10. Interactive Setup Lifecycle Contract](#10-interactive-setup-lifecycle-contract)
- [11. Lifecycle Contract](#11-lifecycle-contract)
- [12. Tool Registration Contract](#12-tool-registration-contract)
- [13. CLI Contract](#13-cli-contract)
- [14. Package Exports Contract](#14-package-exports-contract)
- [15. Test Contract](#15-test-contract)
- [16. Documentation Contract](#16-documentation-contract)
- [17. Constraints and Compatibility](#17-constraints-and-compatibility)
- [18. Acceptance Checklist](#18-acceptance-checklist)
- [19. Open Questions](#19-open-questions)

---

## 1. Overview

Weixin (iLink/企业微信) is structurally similar to Feishu but has two significant differences that affect the module contract:

1. **Interactive setup**: Weixin requires a QR login step before the adapter can begin processing messages. This is an ongoing requirement — if the session expires, the QR login must be repeated. This maps directly to the `authRequired` and `authExpired` lifecycle states from M1/M2.

2. **Persistent runtime state**: Weixin login credentials (session cookies, tokens) are stored on disk and reused across restarts. Under the old adapter, this state lived in an arbitrary `dataDir` path from the config file. Under the module contract, this state must live under `.craft/state/weixin-standard/`.

M5 implements full module contract conformance for the Weixin package, with the additional complexity of interactive setup lifecycle management and state path migration.

---

## 2. Goal

Make `@dotcraft/channel-weixin` a first-class pluggable module package, conforming to the same host-facing contract as `@dotcraft/channel-feishu`, with the addition of structured interactive setup lifecycle signaling and workspace-relative state management.

---

## 3. Scope

- Define and export a `manifest` object of type `ModuleManifest`.
- Implement and export a `createModule` factory of type `ModuleFactory`.
- Migrate `WeixinAdapter` to extend `ModuleChannelAdapter` (or equivalent from M2).
- Implement `validateConfig`, `buildTransportFromConfig`, and typed Weixin config.
- Define and export a `configDescriptors` array of `ConfigDescriptor`.
- Migrate login state storage from the config-specified `dataDir` to `.craft/state/weixin-standard/`.
- Migrate QR and transient download files to `.craft/tmp/weixin-standard/`.
- Implement interactive setup lifecycle signaling (`authRequired`, `authExpired`).
- Update the CLI entry to support `--workspace <path>` and `--config <path>`.
- Preserve all existing tests and add module conformance and state path tests.
- Update `README.md` and `README_ZH.md`.

---

## 4. Non-Goals

- Changing the Weixin/iLink polling mechanism, message parsing, or delivery logic.
- Implementing Desktop-side QR rendering UI.
- Implementing a programmatic QR delivery API (the module signals `authRequired`; the host decides how to present it).
- Publishing the package to npm.
- Changing `dotcraft-wire` or any other package.
- Migrating existing `.craft/` state data from old `dataDir` paths to the new module-scoped paths.

---

## 5. User Experience and Behavioral Contract

### 5.1 First-Time Host Integration Flow

```
1. Load: import { manifest, createModule } from "@dotcraft/channel-weixin"
2. Read manifest: requiresInteractiveSetup = true
3. Call createModule(workspaceContext) → instance
4. Register: instance.onStatusChange((status, error) => ...)
5. Call instance.start()
6. Config missing → status = "configMissing" (stop)
7. Config invalid → status = "configInvalid" (stop)
8. Config valid, no saved credentials → status = "authRequired"
9. (QR is rendered by adapter via CLI or by host through a future protocol extension)
10. User scans QR → login completes → status = "ready"
11. Messages are processed normally.
```

### 5.2 Restart with Saved Credentials Flow

```
1. Call instance.start()
2. Config valid, saved credentials found → status = "starting"
3. Credentials still valid → status = "ready"
4. (No QR required)
```

### 5.3 Auth Expiry Flow

```
[ready] → credentials expire (detected during polling) → status = "authExpired"
→ adapter transitions to "authRequired"
→ QR is shown again
→ User scans → status = "ready"
```

### 5.4 CLI Flow

```
dotcraft-channel-weixin --workspace /path/to/workspace
```

When auth is required, the CLI renders a QR code to the terminal (existing behavior). The host integration flow for non-CLI hosts is UI-neutral; the host observes `authRequired` via `onStatusChange` and decides how to present the need.

### 5.5 Preserved Behaviors

- Weixin polling loop and message ingestion.
- Message delivery via `onDeliver`.
- `WeixinSendFilePreviewToCurrentChat` tool call.
- Approval request handling.
- Streaming turn reply delivery.
- Session identity mapping from Weixin user IDs.

---

## 6. Manifest Contract

| Field | Value |
|-------|-------|
| `moduleId` | `"weixin-standard"` |
| `channelName` | `"weixin"` |
| `displayName` | `"Weixin (iLink/企业微信)"` |
| `packageName` | `"@dotcraft/channel-weixin"` |
| `configFileName` | `"weixin.json"` |
| `supportedTransports` | `["websocket"]` |
| `requiresInteractiveSetup` | `true` |
| `sdkContractVersion` | The value of `sdkContractVersion` from `dotcraft-wire` |
| `supportedProtocolVersions` | `["0.2"]` |
| `variant` | `"standard"` |
| `capabilitySummary.hasChannelTools` | `true` |
| `capabilitySummary.hasStructuredDelivery` | `true` |
| `capabilitySummary.requiresInteractiveSetup` | `true` |
| `capabilitySummary.capabilitySetMayVaryByEnvironment` | `false` |
| `launcher.bin` | `"dotcraft-channel-weixin"` |
| `launcher.supportsWorkspaceFlag` | `true` |
| `launcher.supportsConfigOverrideFlag` | `true` |

---

## 7. Module Factory and Instance Contract

The `createModule` and `ModuleInstance` pattern follows the same contract as M4 (§7). The Weixin-specific aspects:

- `createModule(context)` creates an internal `WeixinAdapter` instance bound to the workspace context.
- `instance.start()` calls `startWithContext(context)`.
- The `ModuleInstance` exposes `getStatus()`, `getError()`, `onStatusChange`, `start`, `stop`.
- At creation time, no network or disk access occurs.

---

## 8. Config Contract

### 8.1 Config File Location

- Default: `context.craftPath + "/weixin.json"`.
- Override: `context.configOverridePath` if set.

### 8.2 Config Schema

```json
{
  "dotcraft": {
    "wsUrl": "ws://127.0.0.1:8080/ws",
    "token": "optional-auth-token"
  },
  "weixin": {
    "apiBaseUrl": "https://weixin.example.com",
    "pollIntervalMs": 3000,
    "pollTimeoutMs": 30000
  }
}
```

The existing `AppConfig` type is replaced by a named `WeixinConfig` type reflecting this schema. The `dataDir` field is removed from the config (it is now derived from workspace context). Any existing `adapter_config.json` with a `dataDir` field is treated as a legacy format and will not be automatically migrated.

### 8.3 Config Descriptors

| Key | displayLabel | dataKind | required | masked |
|-----|--------------|----------|----------|--------|
| `dotcraft.wsUrl` | AppServer WebSocket URL | `"string"` | Yes | No |
| `dotcraft.token` | AppServer Auth Token | `"secret"` | No | Yes |
| `weixin.apiBaseUrl` | Weixin API Base URL | `"string"` | Yes | No |
| `weixin.pollIntervalMs` | Poll Interval (ms) | `"number"` | No | No |
| `weixin.pollTimeoutMs` | Poll Timeout (ms) | `"number"` | No | No |

### 8.4 Config Validation

`validateConfig` must check that `weixin.apiBaseUrl` is a non-empty string and that `dotcraft.wsUrl` is a valid WebSocket URL. If any required field is missing or invalid, it throws `ConfigValidationError`.

---

## 9. State and Temp Layout Contract

### 9.1 State Directory

Persistent runtime state for the Weixin adapter is stored under:

```
<workspaceRoot>/.craft/state/weixin-standard/
```

This directory is resolved using `resolveModuleStatePath(context)` from M2.

Stored artifacts include (but are not limited to):

- Login session credentials (cookies, session tokens).
- Cached platform context data.

### 9.2 Temp Directory

Transient files are stored under:

```
<workspaceRoot>/.craft/tmp/weixin-standard/
```

This directory is resolved using `resolveModuleTempPath(context)` from M2.

Stored artifacts include (but are not limited to):

- QR code image files generated during login.
- Temporarily downloaded media files.

### 9.3 Directory Creation

The adapter must create the state directory and temp directory before writing to them if they do not exist. It must not rely on the host to create these directories.

### 9.4 State Migration Rule

The `dataDir` config field from the old `adapter_config.json` format is removed. Existing login state stored in the old `dataDir` location is not automatically migrated by the module. If a user's old state file exists in the old location, the adapter will treat it as if no state exists and trigger a fresh QR login. This is acceptable for the migration transition.

---

## 10. Interactive Setup Lifecycle Contract

### 10.1 Purpose

Weixin requires QR login before it can process messages. The lifecycle contract makes this requirement visible to hosts without requiring the host to understand Weixin-specific authentication internals.

### 10.2 Detection of Auth Requirement

At startup (inside `startWithContext`), after config validation succeeds, the adapter must:

1. Resolve the state directory.
2. Attempt to load saved credentials from `state/weixin-standard/session.json` (or equivalent).
3. If no credentials exist: call `signalAuthRequired(...)`.
4. If credentials exist: attempt to validate them against the Weixin API.
5. If credentials are valid: proceed to connect AppServer and transition to `ready`.
6. If credentials are invalid/expired: call `signalAuthRequired(...)`.

### 10.3 authRequired State Behavior

When the adapter transitions to `authRequired`:

- It must not connect to AppServer yet.
- It begins the interactive QR login flow internally.
- The QR flow behavior depends on the runtime context:
  - CLI: renders QR to terminal (existing behavior).
  - Non-CLI / host-driven: stores the QR image to `tmp/weixin-standard/qr.png` and optionally emits a structured notification that a QR is available. The exact host notification mechanism is out of scope for this milestone; the spec requires only that the QR artifact is stored in the temp directory.
- The adapter polls for login completion.

### 10.4 Transition from authRequired to ready

After the user scans the QR and login completes:

1. The adapter saves the new credentials to `state/weixin-standard/session.json`.
2. The adapter connects to AppServer via WebSocket.
3. On successful `initialize`, the adapter transitions to `ready`.

### 10.5 Auth Expiry During Operation

When the adapter detects that credentials have expired during the polling loop (e.g. polling returns an auth error):

1. The adapter calls `signalAuthExpired(...)`, transitioning status to `authExpired`.
2. The adapter immediately transitions to `authRequired` and begins the QR flow again.
3. The host observes both transitions via `onStatusChange`.

### 10.6 UI-Neutral Requirement

The host must be able to observe the interactive setup state without requiring a terminal. The adapter must not block startup on `process.stdin` or any terminal-specific primitive in non-CLI environments. The distinction between CLI and non-CLI environments is resolved by checking whether the adapter was started via the CLI entry point or via `createModule`.

---

## 11. Lifecycle Contract

### 11.1 Startup Transitions

```
created →
  start() called →
    starting →
      configMissing         (if weixin.json not found)
      configInvalid         (if validation fails)
      authRequired          (if no saved credentials or credentials invalid)
        → (QR login completes) →
          starting (reconnect to AppServer) →
            ready
      starting →
        ready               (if credentials valid)
```

### 11.2 Auth Expiry After Ready

```
ready →
  authExpired →
    authRequired →
      (QR login) →
        starting →
          ready
```

### 11.3 Shutdown

```
ready → stop() called → stopped
authRequired → stop() called → stopped
```

When `stop()` is called during `authRequired`, the adapter aborts the QR flow and transitions to `stopped`.

### 11.4 Fatal Runtime Error

If AppServer connection is lost after `ready` and cannot be recovered, the adapter transitions to `stopped` with `ModuleError` code `"unexpectedRuntimeFailure"`.

---

## 12. Tool Registration Contract

The `WeixinSendFilePreviewToCurrentChat` tool is declared using `ChannelToolDescriptor` (from M1), following the same pattern as M4. The tool descriptor must include `approval` metadata with `required: true`.

---

## 13. CLI Contract

### 13.1 Entry Point

`src/cli.ts` compiled to `dist/cli.js` and registered as `dotcraft-channel-weixin` in `bin`.

### 13.2 Arguments

| Flag | Required | Behavior |
|------|----------|----------|
| `--workspace <path>` | Yes (unless `--config` is used alone) | Sets `workspaceRoot`; `.craft/` derived as `<path>/.craft`. |
| `--config <path>` | No | Overrides config file path. |

Legacy fallback: reads from `argv[2]` or `DOTCRAFT_WEIXIN_CONFIG` environment variable with a deprecation warning on stderr, for backward compatibility during transition.

### 13.3 QR in CLI Context

When started via the CLI entry and the adapter transitions to `authRequired`, the QR code is rendered to the terminal as it was before. This behavior is unchanged.

### 13.4 Machine-Readable Startup Failure

The CLI exits with code 1 and logs the error code to stderr on `configMissing` or `configInvalid`, matching the M4 Feishu CLI contract.

---

## 14. Package Exports Contract

`src/index.ts` must export:

```typescript
export { manifest } from "./manifest.js";
export { createModule } from "./module.js";
export { configDescriptors } from "./config-descriptors.js";
export { WeixinAdapter } from "./weixin-adapter.js";   // preserved for backward compat
export type { WeixinConfig } from "./weixin-types.js";
```

---

## 15. Test Contract

### 15.1 Preserved Tests

All tests from `examples/weixin/src/` that pass after M3 must continue to pass after M5.

### 15.2 New Module Conformance Tests

A new test file `src/module.test.ts` must verify:

- `manifest.moduleId === "weixin-standard"`.
- `manifest.channelName === "weixin"`.
- `manifest.requiresInteractiveSetup === true`.
- `manifest.sdkContractVersion` matches `dotcraft-wire`'s `sdkContractVersion`.
- `createModule(ctx)` returns a `ModuleInstance` without network access.
- `createModule(ctx).getStatus()` is `"stopped"` immediately after creation.
- `configDescriptors` is a non-empty array.

### 15.3 Config Validation Tests

A test file `src/config.test.ts` must verify:

- `validateConfig` throws `ConfigValidationError` when `weixin.apiBaseUrl` is missing.
- `validateConfig` throws when `dotcraft.wsUrl` is missing.
- `validateConfig` succeeds with a minimal valid config.

### 15.4 Login State Tests

A test file `src/auth.test.ts` must verify:

- When no session file exists in the state directory, the adapter signals `authRequired`.
- When a valid session file exists, the adapter does not signal `authRequired` (it proceeds to connect).
- Login state is written to `resolveModuleStatePath(context) + "/session.json"` (or equivalent path), not to any hardcoded or config-specified path.

### 15.5 State Path Tests

Tests in `src/paths.test.ts` must verify:

- `resolveModuleStatePath(context)` returns `craftPath/state/weixin-standard`.
- `resolveModuleTempPath(context)` returns `craftPath/tmp/weixin-standard`.

---

## 16. Documentation Contract

### 16.1 README.md

Must include:

1. Package description and feature summary.
2. Installation section.
3. Workspace config section: `.craft/config.json` snippet to enable the `weixin` external channel.
4. Adapter config section: `.craft/weixin.json` format and field descriptions.
5. CLI usage with `--workspace` flag.
6. Interactive setup section: explanation that QR login is required on first run and after session expiry.
7. State and temp layout: brief description of `.craft/state/weixin-standard/` and `.craft/tmp/weixin-standard/`.
8. Host integration section: how a host loads `manifest`, calls `createModule`, and observes `authRequired` lifecycle state.
9. Migration note: if the user had an old `adapter_config.json` with `dataDir`, they must re-authenticate (no automatic state migration).
10. Development notes: building, testing, custom config path.

### 16.2 README_ZH.md

Chinese translation covering the same sections.

---

## 17. Constraints and Compatibility

- `qrcode-terminal` remains a dependency for CLI-mode QR rendering. It must not be called when the adapter is started via `createModule` in a non-CLI context.
- The `WeixinAdapter` class remains exported for backward compatibility.
- The module's `channelName` (`"weixin"`) must match the DotCraft server config.
- The `moduleId` (`"weixin-standard"`) is used for state and temp path namespacing. Existing state in old `dataDir` locations is not migrated.
- The credential storage format in `state/weixin-standard/session.json` is internal to the adapter; no host-facing schema is defined for it.

---

## 18. Acceptance Checklist

- [ ] `import { manifest, createModule, configDescriptors } from "@dotcraft/channel-weixin"` succeeds.
- [ ] `manifest.moduleId === "weixin-standard"`.
- [ ] `manifest.channelName === "weixin"`.
- [ ] `manifest.requiresInteractiveSetup === true`.
- [ ] `manifest.configFileName === "weixin.json"`.
- [ ] `manifest.sdkContractVersion` matches `dotcraft-wire`'s `sdkContractVersion`.
- [ ] `createModule(ctx).getStatus()` is `"stopped"` immediately after creation.
- [ ] `instance.start()` with no `weixin.json` transitions to `configMissing`.
- [ ] `instance.start()` with invalid config transitions to `configInvalid`.
- [ ] `instance.start()` with no session file transitions to `authRequired`.
- [ ] `signalAuthExpired` transitions status to `authExpired` then `authRequired`.
- [ ] Login state is written to `craftPath/state/weixin-standard/` (not to a `dataDir` from config).
- [ ] QR artifacts are written to `craftPath/tmp/weixin-standard/`.
- [ ] `dotcraft-channel-weixin --workspace <path>` starts the adapter.
- [ ] CLI renders QR to terminal when `authRequired`.
- [ ] CLI exits with code 1 and error code on `configMissing` or `configInvalid`.
- [ ] All pre-M5 tests still pass.
- [ ] Module conformance tests pass.
- [ ] Config validation tests pass.
- [ ] Login state path tests pass.
- [ ] `npm run build && npm run test && npm pack --dry-run` all succeed for `@dotcraft/channel-weixin`.
- [ ] `README.md` includes all required sections including interactive setup and state layout.

---

## 19. Open Questions

- Should the adapter attempt to reconnect automatically after an `authExpired` transition, or should the host be responsible for calling `stop()` and `start()` again? The current design keeps reconnection internal to the adapter (it re-enters `authRequired` automatically). If a host needs to control this, a future API extension can add an `interruptAuth()` method.
- Should a structured event be emitted to the host when the QR code is available (e.g. `onQrAvailable(qrPath: string)`), or is it sufficient to store the QR in `tmp/` and let the host poll? For this milestone, storing in `tmp/` is sufficient. A richer notification API can be added in a future spec.
- Should `session.json` be encrypted at rest, or is OS-level file permission the expected protection model? OS-level file permissions are sufficient for this milestone. Encryption at rest is a future enhancement.
