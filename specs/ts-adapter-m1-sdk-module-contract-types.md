# DotCraft TypeScript Adapter SDK — M1: SDK Module Contract Type Foundation

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Draft |
| **Date** | 2026-04-15 |
| **Parent Spec** | [typescript-external-channel-module-contract.md](typescript-external-channel-module-contract.md) |
| **Related Specs** | [typescript-external-channel-packages.md](typescript-external-channel-packages.md) |

Purpose: Define the complete set of TypeScript types and concepts required by the SDK module contract, to be implemented inside `dotcraft-wire` as a stable, host-facing type layer. This milestone produces types only — no adapter behavior changes, no file moves, no package restructuring.

---

## Table of Contents

- [1. Overview](#1-overview)
- [2. Goal](#2-goal)
- [3. Scope](#3-scope)
- [4. Non-Goals](#4-non-goals)
- [5. Behavioral Contract](#5-behavioral-contract)
- [6. Required Type Concepts](#6-required-type-concepts)
- [7. Module Manifest Contract](#7-module-manifest-contract)
- [8. Module Entry and Factory Contract](#8-module-entry-and-factory-contract)
- [9. Workspace and Launcher Context Contract](#9-workspace-and-launcher-context-contract)
- [10. Configuration Descriptor Contract](#10-configuration-descriptor-contract)
- [11. Lifecycle Status and Error Contract](#11-lifecycle-status-and-error-contract)
- [12. Capability and Tool Descriptor Contract](#12-capability-and-tool-descriptor-contract)
- [13. SDK Export and Versioning Contract](#13-sdk-export-and-versioning-contract)
- [14. Constraints and Compatibility](#14-constraints-and-compatibility)
- [15. Acceptance Checklist](#15-acceptance-checklist)
- [16. Open Questions](#16-open-questions)

---

## 1. Overview

The `dotcraft-wire` TypeScript SDK currently exposes a wire protocol client and `ChannelAdapter` abstract class. All host-facing and module-facing surfaces that deal with capability registration, tool registration, lifecycle reporting, and config discovery use `Record<string, unknown>` rather than stable named types.

M1 introduces the module contract type layer: a set of stable TypeScript types that define the concepts a host needs to integrate an adapter module, and the concepts an adapter module needs to conform to the SDK contract. These types become the shared vocabulary for all subsequent milestones.

---

## 2. Goal

Give `dotcraft-wire` a stable, typed module contract surface so that hosts (Desktop or other) and adapter modules can reason about each other's integration shape without depending on source layout, package-internal files, or ad hoc dictionary conventions.

---

## 3. Scope

- Add new type definitions to `dotcraft-wire` covering all required contract concepts.
- Export all new types through the existing `src/index.ts` public barrel.
- Upgrade the `version` export in `src/index.ts` to reflect the new contract surface version.
- The added types must be implementation-free: pure `type`, `interface`, and `const enum` or equivalent string union declarations. No runtime behavior changes.

---

## 4. Non-Goals

- Changing any existing export or behavior in `dotcraft-wire`.
- Modifying `ChannelAdapter`, `DotCraftClient`, or any transport.
- Adapter-specific code or migration.
- Package restructuring or directory moves.
- Implementing any runtime helper that reads from disk, resolves paths, or manages process lifecycle.

---

## 5. Behavioral Contract

M1 introduces types, not runtime behavior. The behavioral contract for this milestone is:

1. All new types are importable from the `dotcraft-wire` package root without referencing any internal file path.
2. No existing public export is removed or has its type narrowed in a breaking way.
3. The SDK exports a stable `sdkContractVersion` string constant that adapter modules and hosts can use for compatibility checks.
4. All type names follow the `PascalCase` convention matching the existing SDK codebase style.

---

## 6. Required Type Concepts

The following type concepts must be defined. The exact TypeScript spelling of field names is implementation-defined within each concept block; what is normative is the concept name and the set of required fields.

### 6.1 Organization

The new types may be organized into one or more source files inside `sdk/typescript/src/`. Suggested grouping:

- `module.ts` — manifest, module entry, module instance, workspace context, launcher context
- `config.ts` — config descriptor and config field kinds
- `lifecycle.ts` — lifecycle status, module error, error codes
- `capability.ts` — capability summary, tool descriptor, delivery capability descriptor, tool invocation types

All types must be re-exported through `src/index.ts`.

---

## 7. Module Manifest Contract

### 7.1 ModuleManifest

The manifest is the canonical host-readable metadata object that a module exports at its package root. It must contain at least the following fields:

| Concept | TypeScript field name | Type | Required | Notes |
|---------|-----------------------|------|----------|-------|
| Module identifier | `moduleId` | `string` | Yes | Stable, unique. Used by hosts for module selection. |
| Channel name | `channelName` | `string` | Yes | DotCraft runtime channel key (e.g. `"feishu"`, `"weixin"`). |
| Display name | `displayName` | `string` | Yes | Human-readable label for host UI. |
| Package name | `packageName` | `string` | Yes | npm package name distributing this module. |
| Config file name | `configFileName` | `string` | Yes | e.g. `"feishu.json"`. Relative name only; no path. |
| Supported transports | `supportedTransports` | `ModuleTransport[]` | Yes | At least one of `"stdio"` or `"websocket"`. |
| Requires interactive setup | `requiresInteractiveSetup` | `boolean` | Yes | Whether the module may enter `authRequired` before `ready`. |
| Capability summary | `capabilitySummary` | `CapabilitySummary` | Yes | Static description of broad capability categories. |
| SDK contract version | `sdkContractVersion` | `string` | Yes | Must match `sdkContractVersion` from `dotcraft-wire`. |
| Supported protocol versions | `supportedProtocolVersions` | `string[]` | Yes | AppServer wire protocol versions this module supports. |
| Variant | `variant` | `ModuleVariant` | Yes | See §7.2. |
| Launcher | `launcher` | `LauncherDescriptor` | Yes | How the module is started. See §9. |

### 7.2 ModuleVariant

```
type ModuleVariant =
  | "standard"
  | "specialized"
  | "enterprise"
  | "other"
```

`"standard"` is used for the canonical first-party implementation. `"enterprise"` marks variants that extend the standard with additional tools or policies. `"specialized"` marks first-party variants targeting a narrower environment. `"other"` covers externally defined variants.

### 7.3 ModuleTransport

```
type ModuleTransport = "stdio" | "websocket"
```

---

## 8. Module Entry and Factory Contract

### 8.1 ModuleFactory

The module factory is the function a host calls to create a runnable module instance. It must accept a `WorkspaceContext` (see §9) and return a `ModuleInstance`.

```
type ModuleFactory = (context: WorkspaceContext) => ModuleInstance
```

### 8.2 ModuleInstance

A module instance is the runtime handle the host receives after calling the factory. It must support:

| Concept | Method name | Signature | Notes |
|---------|-------------|-----------|-------|
| Start | `start` | `() => Promise<void>` | Initiates startup. Resolves when the module has begun starting. |
| Stop | `stop` | `() => Promise<void>` | Initiates graceful shutdown. |
| Lifecycle observation | `onStatusChange` | `(handler: (status: LifecycleStatus, error?: ModuleError) => void) => void` | Registers a handler called whenever lifecycle status changes. Multiple handlers may be registered. |
| Current status | `getStatus` | `() => LifecycleStatus` | Returns the current lifecycle status synchronously. |
| Current error | `getError` | `() => ModuleError \| undefined` | Returns the last structured error if status reflects a failure condition, otherwise `undefined`. |

The instance does not need to expose the transport or internal client; those are implementation details.

### 8.3 Module Root Export Shape

A conforming module package must export, at its package root entry point:

- A named export `manifest` typed as `ModuleManifest`.
- A named export `createModule` typed as `ModuleFactory`.

Hosts must use only these two named exports to integrate with the module. They must not import package-internal files.

---

## 9. Workspace and Launcher Context Contract

### 9.1 WorkspaceContext

`WorkspaceContext` is the explicit runtime knowledge provided by the host when creating a module instance.

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `workspaceRoot` | `string` | Yes | Absolute path to the workspace root directory. |
| `craftPath` | `string` | Yes | Absolute path to the `.craft/` directory inside the workspace. |
| `channelName` | `string` | Yes | Logical channel name (matches `manifest.channelName`). |
| `moduleId` | `string` | Yes | Module identifier (matches `manifest.moduleId`). |
| `configOverridePath` | `string \| undefined` | No | Explicit config file path for development or testing. If present, takes precedence over the standard location. |

### 9.2 LauncherDescriptor

`LauncherDescriptor` describes how the module can be started for local execution.

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `bin` | `string` | Yes | The CLI command name (as declared in `package.json` `bin`). |
| `supportsWorkspaceFlag` | `boolean` | Yes | Must be `true` for conforming modules. Indicates `--workspace <path>` is supported. |
| `supportsConfigOverrideFlag` | `boolean` | Yes | Whether `--config <path>` or equivalent is supported. |

---

## 10. Configuration Descriptor Contract

### 10.1 ConfigDescriptor

A config descriptor represents one configuration field as host-readable metadata. It enables hosts to render configuration UI, validate user input before writing config files, and distinguish fields needed for interactive setup.

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `key` | `string` | Yes | Dot-notation path to the field in the config file (e.g. `"feishu.appSecret"`). |
| `displayLabel` | `string` | Yes | Human-readable field name. |
| `description` | `string` | Yes | Explanation of the field's purpose. |
| `required` | `boolean` | Yes | Whether the field is required for basic startup. |
| `dataKind` | `ConfigFieldKind` | Yes | The semantic type of the field value. |
| `masked` | `boolean` | Yes | Whether to mask the value in display (for secrets). |
| `interactiveSetupOnly` | `boolean` | Yes | `true` if this field is only relevant during interactive setup (e.g. a QR action). |
| `defaultValue` | `unknown \| undefined` | No | Default value for optional fields. |
| `enumValues` | `string[] \| undefined` | No | Present when `dataKind` is `"enum"`. |

### 10.2 ConfigFieldKind

```
type ConfigFieldKind =
  | "string"
  | "secret"
  | "path"
  | "enum"
  | "boolean"
  | "number"
  | "object"
  | "list"
```

`"secret"` implies `masked: true` and should be stored in a way appropriate for credentials.

### 10.3 Module Config Descriptor Export

Adapter packages may optionally export a `configDescriptors` named export typed as `ConfigDescriptor[]` from the package root. This allows hosts to introspect the config surface without creating a module instance.

---

## 11. Lifecycle Status and Error Contract

### 11.1 LifecycleStatus

```
type LifecycleStatus =
  | "configMissing"
  | "configInvalid"
  | "starting"
  | "ready"
  | "authRequired"
  | "authExpired"
  | "degraded"
  | "stopped"
```

State semantics:

| Status | Meaning |
|--------|---------|
| `configMissing` | The module could not locate its config file. No connection will be attempted. |
| `configInvalid` | The config file was found but failed validation. No connection will be attempted. |
| `starting` | The module is connecting to the platform and/or DotCraft AppServer. |
| `ready` | The module is fully connected and processing messages. |
| `authRequired` | The module requires interactive user authentication (e.g. QR scan) before it can reach `ready`. |
| `authExpired` | Previously established authentication has expired and must be renewed interactively. |
| `degraded` | The module is running but operating in a reduced capacity (e.g. one transport failed but the other is up). |
| `stopped` | The module has been explicitly stopped or has exited after a fatal error. |

### 11.2 ModuleError

A structured error that accompanies a non-`ready` lifecycle status.

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `code` | `ModuleErrorCode` | Yes | Machine-readable error code. |
| `message` | `string` | Yes | Human-readable description. |
| `detail` | `Record<string, unknown> \| undefined` | No | Optional structured diagnostic payload. |
| `timestamp` | `string` | Yes | ISO 8601 timestamp of when the error was recorded. |

### 11.3 ModuleErrorCode

```
type ModuleErrorCode =
  | "configMissing"
  | "configInvalid"
  | "startupFailed"
  | "transportConnectionFailed"
  | "authRequired"
  | "authExpired"
  | "capabilityRegistrationFailed"
  | "unexpectedRuntimeFailure"
```

Each code maps to a distinct host-observable condition. Hosts must not rely on `message` text for routing decisions; `code` is the stable key.

---

## 12. Capability and Tool Descriptor Contract

### 12.1 CapabilitySummary

Static manifest-level description of what the module can do.

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `hasChannelTools` | `boolean` | Yes | Whether the module may register runtime channel tools. |
| `hasStructuredDelivery` | `boolean` | Yes | Whether the module supports structured `ext/channel/send` delivery beyond plain text. |
| `requiresInteractiveSetup` | `boolean` | Yes | Mirror of `manifest.requiresInteractiveSetup` for convenience. |
| `capabilitySetMayVaryByEnvironment` | `boolean` | Yes | Whether tool sets or delivery capabilities may differ by workspace or environment. |

### 12.2 ChannelToolDescriptor

A typed descriptor for a tool registered by the module at runtime through `getChannelTools()`.

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `name` | `string` | Yes | Tool name, must be unique within the module. |
| `displayName` | `string` | Yes | Human-readable name for approval and audit UI. |
| `description` | `string` | Yes | What the tool does. |
| `inputSchema` | `Record<string, unknown>` | Yes | JSON Schema for tool input. |
| `approval` | `ToolApprovalDescriptor \| undefined` | No | Approval metadata for server-side approval gating. |

### 12.3 ToolApprovalDescriptor

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `required` | `boolean` | Yes | Whether this tool always requires approval. |
| `promptTemplate` | `string \| undefined` | No | Template for the approval prompt shown to the user. |

### 12.4 DeliveryCapabilityDescriptor

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `supportedKinds` | `string[]` | Yes | Delivery message kinds beyond plain text (e.g. `"card"`, `"file"`, `"image"`). |
| `supportsGroupDelivery` | `boolean` | Yes | Whether messages can be delivered to group contexts. |
| `supportsDirectDelivery` | `boolean` | Yes | Whether messages can be delivered to individual (DM) contexts. |

### 12.5 ToolInvocationContext

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `tool` | `string` | Yes | Tool name being invoked. |
| `arguments` | `Record<string, unknown>` | Yes | Parsed tool arguments. |
| `threadId` | `string \| undefined` | No | Active thread ID if available. |
| `channelContext` | `string \| undefined` | No | Delivery target context if available. |

### 12.6 ToolInvocationResult

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `success` | `boolean` | Yes | Whether the tool invocation succeeded. |
| `result` | `unknown \| undefined` | No | Tool-specific result payload on success. |
| `errorCode` | `string \| undefined` | No | Machine-readable error code on failure. |
| `errorMessage` | `string \| undefined` | No | Human-readable error message on failure. |

---

## 13. SDK Export and Versioning Contract

### 13.1 sdkContractVersion

`dotcraft-wire` must export a stable string constant:

```
export const sdkContractVersion = "1.0.0"
```

This value is distinct from the package `version`. It is the version of the module contract surface, not the overall package release. Adapter modules must embed this value in their `manifest.sdkContractVersion` field at publish time. Hosts may use this value for compatibility checks.

The existing `version` export (`"0.1.0"`) is preserved unchanged.

### 13.2 Public Export Requirements

All new types and the `sdkContractVersion` constant must be exported from `src/index.ts`. Existing exports must not be removed.

---

## 14. Constraints and Compatibility

- All new types are purely additive to the existing SDK surface.
- The existing `ChannelAdapter`, `DotCraftClient`, wire constants, transport types, and model types are untouched.
- The new types must compile cleanly with the existing `tsconfig.json` settings in `sdk/typescript/`.
- Types that reference each other (e.g. `ModuleManifest` referencing `CapabilitySummary` and `LauncherDescriptor`) must be defined in the same file or with explicit cross-file imports within `src/`.
- No new runtime dependencies are introduced; all new source files are type-only or export only constants.

---

## 15. Acceptance Checklist

- [ ] `dotcraft-wire` builds cleanly (`npm run build`) with all new type files included.
- [ ] `npm run typecheck` passes with no new errors.
- [ ] All new types are importable from the package root (`import { ModuleManifest, ... } from "dotcraft-wire"`) without path imports.
- [ ] `ModuleManifest` contains all required fields listed in §7.1.
- [ ] `ModuleInstance` exposes `start`, `stop`, `onStatusChange`, `getStatus`, `getError`.
- [ ] `WorkspaceContext` contains all required fields listed in §9.1.
- [ ] `LifecycleStatus` union covers all eight states listed in §11.1.
- [ ] `ModuleErrorCode` union covers all eight codes listed in §11.3.
- [ ] `ConfigDescriptor` contains all required fields listed in §10.1.
- [ ] `CapabilitySummary`, `ChannelToolDescriptor`, `DeliveryCapabilityDescriptor`, `ToolInvocationContext`, `ToolInvocationResult` are all exported.
- [ ] `sdkContractVersion` is exported as a string constant.
- [ ] No existing public export is removed or type-narrowed.
- [ ] All tests continue to pass (`npm run test`).

---

## 16. Open Questions

- Should `ModuleInstance` extend `EventEmitter` for lifecycle observation, or use a plain callback registration pattern? The callback pattern (`onStatusChange`) is more portable across environments (browser, Node, Electron) and avoids a Node-specific dependency. The spec opts for the callback pattern; the M2 implementation should confirm this is sufficient for Desktop's needs.
- Should `ChannelToolDescriptor` replace or extend the current `Record<string, unknown>[]` returned by `getChannelTools()`? M2 will decide whether to change the `ChannelAdapter.getChannelTools()` signature or leave it loose until adapter migration.
- Is `sdkContractVersion` a semver string, or should it be a simple integer for easier range comparisons? Semver is chosen here for ecosystem familiarity; adapt if host tooling requires integers.
