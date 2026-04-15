# DotCraft TypeScript Adapter SDK — M2: ChannelAdapter Module-Aware Refactor

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Draft |
| **Date** | 2026-04-15 |
| **Parent Spec** | [typescript-external-channel-module-contract.md](typescript-external-channel-module-contract.md) |
| **Related Specs** | [ts-adapter-m1-sdk-module-contract-types.md](ts-adapter-m1-sdk-module-contract-types.md) |

Purpose: Evolve `ChannelAdapter` in `dotcraft-wire` to support the module contract's lifecycle model, workspace-context-based startup, standardized config and state layout, and interactive setup signaling — while preserving backward compatibility for existing adapters that have not yet migrated.

---

## Table of Contents

- [1. Overview](#1-overview)
- [2. Goal](#2-goal)
- [3. Scope](#3-scope)
- [4. Non-Goals](#4-non-goals)
- [5. Behavioral Contract](#5-behavioral-contract)
- [6. Lifecycle Status Tracking](#6-lifecycle-status-tracking)
- [7. Workspace Context Startup Path](#7-workspace-context-startup-path)
- [8. Config Discovery and Loading](#8-config-discovery-and-loading)
- [9. State and Temp Directory Contract](#9-state-and-temp-directory-contract)
- [10. Config Validation Boundary](#10-config-validation-boundary)
- [11. Interactive Setup Signaling](#11-interactive-setup-signaling)
- [12. Backward Compatibility Rules](#12-backward-compatibility-rules)
- [13. Constraints and Compatibility](#13-constraints-and-compatibility)
- [14. Acceptance Checklist](#14-acceptance-checklist)
- [15. Open Questions](#15-open-questions)

---

## 1. Overview

After M1, `dotcraft-wire` has a complete type vocabulary for the module contract. M2 makes `ChannelAdapter` (or a sibling class) aware of that contract at runtime.

The central challenge is that `ChannelAdapter` was designed as a transport-first class: it receives a `Transport` object at construction time and manages the wire session lifecycle. The module contract introduces a second startup path: workspace-context-driven startup, where the adapter resolves transport parameters from config files found under `.craft/`.

M2 adds this second path and the lifecycle tracking that goes with it, without breaking the existing transport-first construction path.

---

## 2. Goal

Make the SDK runtime layer (specifically `ChannelAdapter` or a new companion class) capable of:

1. Tracking and reporting lifecycle status using the `LifecycleStatus` type from M1.
2. Accepting a `WorkspaceContext` as a startup input to derive config, transport, state paths, and temp paths.
3. Resolving adapter config from `.craft/<channel>.json` based on workspace context.
4. Providing helpers for module-owned persistent state and temp file directories.
5. Signaling interactive setup requirements through structured lifecycle transitions.

---

## 3. Scope

- Add lifecycle status tracking to `ChannelAdapter` or introduce a `ModuleChannelAdapter` subclass that adapters may extend instead.
- Add a workspace-context-driven startup variant alongside the existing constructor.
- Add config discovery helpers that resolve `.craft/<channel>.json` from `WorkspaceContext`.
- Add state and temp path helpers under `.craft/state/<moduleId>/` and `.craft/tmp/<moduleId>/`.
- Add a validation hook that adapters implement to validate their loaded config.
- Add interactive setup lifecycle transitions (`authRequired`, `authExpired`).
- Export all new runtime helpers from `src/index.ts`.

---

## 4. Non-Goals

- Changing the wire session lifecycle, event streaming, or turn handling inside `ChannelAdapter`.
- Moving adapter source files or restructuring packages.
- Implementing adapter-specific config schemas or migration paths.
- Providing a full process supervisor or restart logic.
- Providing a config file write API (hosts write config, modules read it).
- Implementing Desktop UI for lifecycle visualization.

---

## 5. Behavioral Contract

### 5.1 Lifecycle Status Emission

When `ChannelAdapter` (or `ModuleChannelAdapter`) undergoes a lifecycle transition, it must:

1. Update its internal status to the new `LifecycleStatus` value.
2. Call all registered `onStatusChange` handlers synchronously before returning from the method that caused the transition.
3. Always call handlers with the new status and the current `ModuleError` if one is set, or `undefined` if none.

Lifecycle transitions that must be emitted:

| Trigger | New status |
|---------|------------|
| Config file not found | `configMissing` |
| Config file found but validation fails | `configInvalid` |
| `start()` called, before connect completes | `starting` |
| `initialize` handshake completed successfully | `ready` |
| Platform auth is required before ready | `authRequired` |
| Previously established auth expires | `authExpired` |
| `stop()` called or fatal error after ready | `stopped` |

The `degraded` status is optional for this milestone; adapters may emit it manually if needed, but the SDK does not produce it automatically.

### 5.2 Config Discovery

When started with a `WorkspaceContext`, the adapter must locate its config by:

1. If `context.configOverridePath` is set, use that path directly.
2. Otherwise, resolve `path.join(context.craftPath, manifest.configFileName)`.

If the file does not exist, the adapter transitions to `configMissing` and does not attempt to connect.

### 5.3 State and Temp Paths

The adapter must expose helpers that return the module-scoped paths:

- State path: `path.join(context.craftPath, "state", context.moduleId)`
- Temp path: `path.join(context.craftPath, "tmp", context.moduleId)`

These paths are not created automatically. Adapters call these helpers when they need to read or write to their owned directories and are responsible for creating them as needed.

### 5.4 Config Validation Boundary

The adapter is responsible for validating its own config. The SDK provides a hook:

```
protected validateConfig(rawConfig: unknown): asserts rawConfig is TConfig
```

Or the equivalent pattern (the exact signature may use a return-based validation approach). When validation fails, the adapter must:

1. Set the current error to a `ModuleError` with code `"configInvalid"` and a meaningful message.
2. Transition to `configInvalid` status.
3. Not call `connect()` or `initialize()`.

The host is not expected to interpret the validation error beyond the code; the `message` field is for human diagnosis.

---

## 6. Lifecycle Status Tracking

### 6.1 Implementation Location

Lifecycle status tracking may be added in one of two ways:

**Option A — Extend existing `ChannelAdapter`:** Add status tracking directly to the `ChannelAdapter` class. `start()` and `stop()` already exist and can be extended. The new workspace-context startup method becomes an additional entry point.

**Option B — New `ModuleChannelAdapter` subclass:** Add a new class that extends `ChannelAdapter` and adds the module-aware lifecycle layer. Existing adapters extend `ChannelAdapter` as before; new module-conformant adapters extend `ModuleChannelAdapter`.

The spec does not mandate either option. The implementation must choose the option that:

- Requires the least disruption to existing adapter code.
- Allows existing tests to pass without modification.
- Makes the conformant path clearly documented.

### 6.2 Handler Registration

```
onStatusChange(handler: (status: LifecycleStatus, error?: ModuleError) => void): void
```

- Multiple handlers may be registered.
- Handlers are called in registration order.
- Handlers are not automatically removed; the adapter does not need to provide a deregistration API at this milestone.

### 6.3 Status Query

```
getStatus(): LifecycleStatus
getError(): ModuleError | undefined
```

Both are synchronous. `getError()` returns the most recent structured error if the current status reflects a failure, or `undefined` otherwise. Clearing `getError()` happens implicitly when status transitions to `starting` or `ready`.

---

## 7. Workspace Context Startup Path

### 7.1 New Startup Method

In addition to the existing `start()` method (transport-driven), the module-aware adapter must support:

```
startWithContext(context: WorkspaceContext): Promise<void>
```

This method:

1. Emits `starting`.
2. Resolves the config file path from context.
3. If config is missing, emits `configMissing` and returns (does not throw).
4. Loads and parses the config file.
5. Calls `validateConfig(rawConfig)`.
6. If validation fails, emits `configInvalid` and returns.
7. Derives transport parameters from the loaded config (adapter-specific; the base class provides the hook, the subclass fills in the params).
8. Calls the existing `start()` logic (connect, initialize).
9. On success, emits `ready`.
10. On failure, emits `stopped` with an appropriate `ModuleError`.

The base class must provide the hook at step 7:

```
protected abstract buildTransportFromConfig(config: TConfig): Transport
```

Or, if the class is not generic, an equivalent abstract or overridable method that the subclass implements to produce the transport.

### 7.2 Config Type Generics

If `ModuleChannelAdapter` is used, it may be generic over a config type:

```
abstract class ModuleChannelAdapter<TConfig> extends ChannelAdapter
```

This allows the `validateConfig` and `buildTransportFromConfig` hooks to be strongly typed in the subclass without requiring `unknown` casts in adapter code.

The generic is optional; the non-generic variant may use `unknown` internally and cast.

---

## 8. Config Discovery and Loading

### 8.1 resolveConfigPath Helper

The SDK must export a standalone helper function:

```
function resolveConfigPath(context: WorkspaceContext, configFileName: string): string
```

Returns the absolute path to the config file per the rules in §5.2. Does not check whether the file exists.

### 8.2 loadJsonConfig Helper

The SDK must export a standalone helper function:

```
async function loadJsonConfig(configPath: string): Promise<{ found: true; data: unknown } | { found: false }>
```

Attempts to read and parse a JSON file. Returns `found: false` if the file does not exist. Throws on JSON parse errors or unexpected IO errors.

These helpers are exported from `src/index.ts` and are usable by adapters outside the class hierarchy.

---

## 9. State and Temp Directory Contract

### 9.1 Path Resolution Helpers

The SDK must export:

```
function resolveModuleStatePath(context: WorkspaceContext): string
function resolveModuleTempPath(context: WorkspaceContext): string
```

Returning:
- `path.join(context.craftPath, "state", context.moduleId)`
- `path.join(context.craftPath, "tmp", context.moduleId)`

These are pure path computations; they do not create directories.

### 9.2 Adapter Responsibility

Adapters must use these helpers (or the equivalent computation) when reading or writing persistent runtime state or temporary files. Adapters must not store runtime state in the top-level adapter config file.

---

## 10. Config Validation Boundary

### 10.1 Validation Hook

The base module-aware adapter class exposes:

```
protected abstract validateConfig(rawConfig: unknown): void
```

When called, the implementation must either:
- Return normally if the config is valid.
- Throw a `ConfigValidationError` (a new SDK error class) with a descriptive message if invalid.

The base class catches this error in `startWithContext` and converts it to a `configInvalid` lifecycle transition.

### 10.2 ConfigValidationError

```
class ConfigValidationError extends Error {
  constructor(
    message: string,
    readonly fields?: string[]
  )
}
```

`fields` is an optional list of config field keys that failed validation, for host diagnostic purposes.

---

## 11. Interactive Setup Signaling

### 11.1 Purpose

Some adapters (Weixin) cannot reach `ready` without user interaction (e.g. QR login). The SDK must provide a mechanism to signal this state.

### 11.2 Transition Methods

The module-aware adapter exposes two protected methods:

```
protected signalAuthRequired(error?: Partial<ModuleError>): void
protected signalAuthExpired(error?: Partial<ModuleError>): void
```

Both set the adapter's lifecycle status and emit to all registered handlers. The `error` parameter is optional; if not provided, a default `ModuleError` with the matching code is constructed.

### 11.3 Transition to Ready After Auth

After interactive setup completes and the adapter can proceed, it calls the existing `start()` connection logic internally and, on success, transitions to `ready`. The SDK does not define how the adapter knows that auth is complete; that is platform-specific behavior.

### 11.4 Host Visibility

The `authRequired` and `authExpired` states are visible to the host via `getStatus()` and the `onStatusChange` callback. The host is responsible for any UI presentation (prompt, notification, dashboard update). The SDK does not define the host-side UX.

---

## 12. Backward Compatibility Rules

1. All existing public members of `ChannelAdapter` remain available with unchanged signatures.
2. The existing `start()` and `stop()` methods retain their current behavior when called on the old transport-based constructor path.
3. Existing adapters (`FeishuAdapter`, `WeixinAdapter`) do not need to change their base class or constructor until they opt into the module-aware path in M4 and M5.
4. The new `startWithContext`, `getStatus`, `getError`, and `onStatusChange` members are additive. If they are added to the existing `ChannelAdapter` class, they must have safe defaults (e.g. `getStatus()` returns `"stopped"` before start is called; `onStatusChange` is a no-op before any handler is registered).
5. Tests for existing adapter behavior must pass without modification.

---

## 13. Constraints and Compatibility

- The `path` module from Node.js is acceptable as a dependency inside `dotcraft-wire` for path helpers, since `dotcraft-wire` already targets Node.js.
- The helpers added in this milestone must not import from adapter packages or any external platform SDK.
- `ConfigValidationError` must be exported from `src/index.ts`.
- `resolveConfigPath`, `loadJsonConfig`, `resolveModuleStatePath`, and `resolveModuleTempPath` must be exported from `src/index.ts`.
- The `ModuleChannelAdapter` class (if introduced) must be exported from `src/index.ts`.

---

## 14. Acceptance Checklist

- [ ] `dotcraft-wire` builds cleanly after M2 changes.
- [ ] Existing `ChannelAdapter` tests pass without modification.
- [ ] `getStatus()` returns `"stopped"` before `startWithContext` or `start` is called.
- [ ] `startWithContext` transitions through `starting` → `configMissing` when config file is absent.
- [ ] `startWithContext` transitions through `starting` → `configInvalid` when `validateConfig` throws.
- [ ] `startWithContext` transitions to `ready` when config is valid and connection succeeds.
- [ ] `onStatusChange` handlers are called on every transition.
- [ ] Multiple `onStatusChange` handlers can be registered and all are called.
- [ ] `signalAuthRequired` transitions status to `authRequired` and notifies handlers.
- [ ] `signalAuthExpired` transitions status to `authExpired` and notifies handlers.
- [ ] `resolveConfigPath` returns `craftPath/<configFileName>` when `configOverridePath` is not set.
- [ ] `resolveConfigPath` returns `configOverridePath` when it is set.
- [ ] `loadJsonConfig` returns `{ found: false }` when the file does not exist.
- [ ] `loadJsonConfig` returns `{ found: true, data: ... }` for a valid JSON file.
- [ ] `resolveModuleStatePath` returns `craftPath/state/<moduleId>`.
- [ ] `resolveModuleTempPath` returns `craftPath/tmp/<moduleId>`.
- [ ] `ConfigValidationError` is exported and catchable.
- [ ] `npm run typecheck` passes with no new errors.
- [ ] All existing tests pass (`npm run test`).

---

## 15. Open Questions

- Should `startWithContext` replace `start()` for module-conformant adapters, or should they coexist permanently? Coexistence is the current answer (backward compat), but a future major version of the SDK may deprecate the bare transport constructor.
- Should `loadJsonConfig` handle YAML or other config formats? No — JSON only for this milestone; adapter packages may add their own parsers if needed.
- Should lifecycle status handlers be removed on `stop()` to prevent memory leaks in long-lived host processes? This is deferred to M6 conformance validation; the spec does not require deregistration at this milestone.
