# DotCraft TypeScript External Channel Module Contract Specification

| Field | Value |
|-------|-------|
| **Version** | 0.3.0 |
| **Status** | Draft |
| **Date** | 2026-04-15 |
| **Related Specs** | [external-channel-adapter.md](external-channel-adapter.md), [appserver-protocol.md](appserver-protocol.md) |

Purpose: define the unified TypeScript specification for external channel adapters, including both the SDK module contract and the packaging/distribution requirements needed for host integration.

---

## Table of Contents

- [1. Scope](#1-scope)
- [2. Design Goals](#2-design-goals)
- [3. Non-Goals](#3-non-goals)
- [4. Spec Relationship](#4-spec-relationship)
- [5. Design Principles](#5-design-principles)
- [6. Conceptual Model](#6-conceptual-model)
- [7. Module Manifest Contract](#7-module-manifest-contract)
- [8. Module Entry Contract](#8-module-entry-contract)
- [9. Workspace and Launcher Contract](#9-workspace-and-launcher-contract)
- [10. Configuration Contract](#10-configuration-contract)
- [11. State and Temp Layout Contract](#11-state-and-temp-layout-contract)
- [12. Lifecycle Status and Error Contract](#12-lifecycle-status-and-error-contract)
- [13. Interactive Setup Contract](#13-interactive-setup-contract)
- [14. Capability and Tool Registration Contract](#14-capability-and-tool-registration-contract)
- [15. Modularity and Variant Support](#15-modularity-and-variant-support)
- [16. SDK Surface Requirements](#16-sdk-surface-requirements)
- [17. Package and Repository Contract](#17-package-and-repository-contract)
- [18. Build, Validation, and Documentation Contract](#18-build-validation-and-documentation-contract)
- [19. Migration and Archival Contract](#19-migration-and-archival-contract)
- [20. Conformance and Acceptance](#20-conformance-and-acceptance)

---

## 1. Scope

### 1.1 What This Spec Defines

- The TypeScript SDK contract required for an external channel adapter to behave as a pluggable module.
- The canonical manifest carrier and module entry model used by hosts.
- The standardized concepts the SDK must expose for workspace context, adapter configuration, state layout, capability registration, lifecycle reporting, and runtime integration.
- The boundaries between:
  - host products such as desktop
  - the shared TypeScript SDK
  - first-party adapter modules
  - enterprise or environment-specific adapter variants
- The standardized metadata and startup expectations that allow a host to integrate a module without directly importing package-internal implementation files.

### 1.2 What This Spec Does Not Define

- The wire protocol itself. That is defined by [appserver-protocol.md](appserver-protocol.md).
- The cross-process external channel architecture. That is defined by [external-channel-adapter.md](external-channel-adapter.md).
- The exact TypeScript syntax used to realize this contract.
- Desktop UI implementation details.

---

## 2. Design Goals

The SDK module contract must satisfy the following:

1. A host can integrate an adapter as a module, not as a hard-coded code reference.
2. First-party and enterprise adapter variants can share one integration model.
3. A host can discover the adapter's identity, config file name, startup needs, transport support, and lifecycle states without parsing arbitrary source code.
4. Adapter-specific tools registered through external channel tool registration can vary by environment without changing the host integration contract.
5. Adapter configuration is standardized at the SDK boundary, not reinvented by each adapter package.
6. The contract supports later desktop integration without assuming that desktop is the only host.
7. The contract keeps platform-specific logic inside the module while lifting host-facing metadata and lifecycle concepts into the SDK.

---

## 3. Non-Goals

The following are explicitly deferred:

- Standardizing one common business tool set across all adapter variants.
- Requiring first-party and enterprise variants to share the same internal implementation.
- Defining native executable packaging or Node runtime distribution.
- Replacing platform-specific config fields with one universal cross-channel config schema.
- Making the shared SDK responsible for platform API clients such as Feishu or Weixin SDK bindings.

---

## 4. Spec Relationship

This specification is the unified **TypeScript contract and package layer** for external channel adapters.

Layering:

1. [appserver-protocol.md](appserver-protocol.md) defines the wire-level protocol.
2. [external-channel-adapter.md](external-channel-adapter.md) defines the runtime architecture for external channel adapters.
3. This specification defines:
   - the TypeScript SDK module contract used by adapter implementations
   - the package, build, documentation, and repo-local distribution contract for those modules

Consolidation rule:

- This document supersedes the previously separate package-focused TypeScript specification.
- Runtime behavior, packaging, and validation requirements are all normative in this single document.
- If future package-level guidance conflicts with runtime contract requirements in this document, runtime and host-integration requirements take precedence.

---

## 5. Design Principles

### 5.1 Module, Not Code Reference

Hosts must integrate external channels through a stable module contract, not by importing package-internal files, subclassing example adapters, or depending on source-layout details.

### 5.2 Host Knows Metadata, Module Owns Behavior

The host should know:

- what the module is
- where its config lives
- how it is started
- what transports it supports
- what lifecycle states and errors it may report
- what capability categories it exposes

The module should own:

- platform protocol integration
- platform-specific configuration fields
- approval UX behavior
- platform-specific message formatting and delivery logic
- runtime tool registration and tool execution behavior

### 5.3 Variant-Friendly by Default

The contract must allow:

- a first-party Feishu module
- an enterprise Feishu module with extra tools
- a future environment-specific Weixin module

without forcing the host to learn a new integration model for each one.

### 5.4 Configuration is Workspace-Oriented

The module contract must align with the product decision that adapter configs live under the active workspace `.craft/` directory and are named by channel, not by generic adapter role.

### 5.5 Typed SDK Surface Over Loose Dictionaries

The current `Record<string, unknown>` style is acceptable internally where protocol flexibility is required, but the host-facing and module-facing contract must define stable concepts for:

- manifest metadata
- module entry
- workspace context
- config descriptors
- capability summaries
- tool descriptors
- lifecycle statuses
- machine-readable errors

---

## 6. Conceptual Model

The SDK contract standardizes the following conceptual units.

### 6.1 Adapter Module

An adapter module is the SDK-facing unit that represents one external channel integration variant.

Examples:

- first-party Feishu module
- enterprise Feishu module
- first-party Weixin module

### 6.2 Host

A host is any process that loads or launches an adapter module and provides the environment in which it runs.

Examples:

- desktop
- a dedicated internal launcher
- a future adapter supervisor process

### 6.3 Launcher

A launcher is the concrete startup path used to execute a module locally. The launcher may be the host itself or a thin wrapper process.

### 6.4 Workspace Context

Workspace context is the explicit runtime knowledge the module receives from the host or launcher to locate its config, state, and DotCraft workspace identity.

### 6.5 Module Manifest

A module manifest is the canonical host-readable metadata that describes the adapter module.

### 6.6 Capability Summary

A capability summary is the manifest-level description of what a module can do in broad categories without requiring the full runtime tool set to be known statically.

### 6.7 Variant

A variant is a distinct adapter module for the same channel family that preserves the same host-facing contract but changes platform behavior, registered tools, or environment assumptions.

---

## 7. Module Manifest Contract

### 7.1 Canonical Manifest Carrier

The canonical manifest carrier is a **module-root SDK export**.

The host must be able to load the manifest by loading the package's documented module entry and reading a named manifest export from that entry.

The manifest must not be standardized as:

- a `package.json` custom field
- a standalone `manifest.json`
- an undocumented convention derived from source layout

This keeps the integration contract in the SDK/runtime layer rather than in package manager metadata.

### 7.2 Required Manifest Concepts

Every module manifest must expose at least these concepts:

- `moduleId`
- `channelName`
- `displayName`
- `packageName`
- `configFileName`
- `supportedTransports`
- `requiresInteractiveSetup`
- `capabilitySummary`
- `sdkContractVersion`
- `supportedProtocolVersions`
- `variant`
- `launcher`

The spec standardizes these concepts; exact field spelling is implementation-defined.

### 7.3 Identity Model

The manifest must separate:

- **package identity**: the package that distributes the module
- **module identity**: the specific module contract unit the host selects
- **channel identity**: the logical DotCraft channel name used in runtime session identity and config naming

Rules:

- host selection is by `moduleId`
- DotCraft runtime identity is by `channelName`
- package identity must not be used as the runtime channel key

### 7.4 Variant Semantics

Manifest metadata must declare whether the module is:

- first-party standard
- first-party specialized
- enterprise/internal
- other variant class defined by the host ecosystem

This is metadata for selection and diagnostics only; it does not change the contract shape.

---

## 8. Module Entry Contract

### 8.1 Canonical Entry Model

The SDK contract standardizes a **manifest plus module-factory entry model**.

Every conforming module must expose:

- the manifest
- one canonical module entry/factory export that the host can call to create a runnable module instance

Hosts must not instantiate modules by:

- importing private implementation files
- subclassing implementation classes
- inferring entrypoints from arbitrary file names

### 8.2 Host-Driven Startup

Module startup is host-driven.

The host or launcher must provide explicit startup inputs to the module entry, including at minimum:

- workspace root
- `.craft` path
- effective transport mode or launch mode when needed
- optional explicit config override path

The module must not rely on discovering these via source layout, current working directory assumptions, or environment-variable probing as its standard startup contract.

### 8.3 Entry Outcome

The module entry must yield a runtime object or equivalent runtime handle that allows the host to:

- start the module
- stop the module
- observe lifecycle state changes
- observe machine-readable errors
- query or receive current capability state after startup

The exact API shape is implementation-defined, but these behaviors are mandatory.

---

## 9. Workspace and Launcher Contract

### 9.1 Required Workspace Context

The module contract must standardize a host-to-module workspace context that includes, at minimum:

- active workspace root
- path to the workspace `.craft` directory
- logical channel name
- module identifier

The host context may contain more data, but the contract must not require the host to expose arbitrary implementation details.

### 9.2 Standard Config Location

The standard adapter config locations are:

- `{workspace}/.craft/feishu.json`
- `{workspace}/.craft/weixin.json`

If a variant targets the same `channelName`, it shares the same default config filename unless the manifest explicitly declares otherwise. The default rule is **shared channel config by channel name**.

### 9.3 Launcher Semantics

For local execution, the standard launcher contract must support:

- explicit workspace input
- optional explicit config override input

The required standard host-driven form is equivalent to:

- `--workspace <path>`

The contract does not require this exact flag spelling in the spec text, but the launcher semantics must be equivalent: workspace is an explicit startup argument, not an ambient assumption.

### 9.4 CWD Semantics

Current working directory may be used as a developer convenience in local manual execution, but it is not part of the host integration contract and must not be the only way a module discovers workspace.

---

## 10. Configuration Contract

### 10.1 Shared Configuration Concepts

The SDK must define standardized concepts for adapter configuration:

- `channelName`
- `configFileName`
- required DotCraft connection fields
- adapter-specific fields
- validation boundary
- configuration descriptors

The SDK does not need to flatten all adapter configs into one universal schema, but it must provide a common contract for how adapter configs are declared, located, and validated.

### 10.2 Channel-Named Config

The SDK contract must support channel-named config files rather than generic names such as `adapter_config.json`.

This is required so hosts can reason about:

- which config belongs to which logical channel
- how to present config in a product UI
- how to substitute module variants without changing the workspace mental model

### 10.3 Validation Responsibility

The module is responsible for validating its own adapter-specific configuration.

The validation boundary is:

- host validates host-owned startup inputs
- module validates module-owned config
- protocol/runtime failures are reported as lifecycle errors, not as config-schema failures

### 10.4 Config Descriptor

The SDK must expose a host-readable configuration descriptor model with field-level concepts, including at minimum:

- field key
- display label
- description
- required vs optional
- data kind: string, secret, path, enum, boolean, number, object, list
- masked echo behavior
- interactive-setup relevance

The config descriptor is metadata for hosts. It does not replace the adapter-specific config schema.

---

## 11. State and Temp Layout Contract

### 11.1 Standard Concepts

The SDK must standardize three distinct storage concepts under the workspace:

- adapter config
- persistent runtime state
- temporary files

### 11.2 Standard Layout

The standard conceptual layout is:

- `.craft/<channel>.json` for adapter config
- `.craft/state/<moduleId>/...` for persistent module-owned runtime state
- `.craft/tmp/<moduleId>/...` for temporary module-owned files

This layout is normative at the contract level.

### 11.3 State Ownership

Persistent runtime state includes examples such as:

- Weixin login credentials or session state
- sync cursors
- cached platform context tokens

These must not be stored in the top-level adapter config file unless they are user-authored configuration.

### 11.4 Temp Ownership

Temporary files include examples such as:

- downloaded transient media
- QR setup artifacts
- intermediate delivery files

Temp files must live under the module-owned temp namespace rather than arbitrary package-relative directories.

---

## 12. Lifecycle Status and Error Contract

### 12.1 Status Model

The SDK must standardize a machine-readable lifecycle status model that covers at least:

- `configMissing`
- `configInvalid`
- `starting`
- `ready`
- `authRequired`
- `authExpired`
- `degraded`
- `stopped`

Hosts must be able to observe these states without parsing stderr text.

### 12.2 Error Model

The SDK must standardize machine-readable startup/runtime error codes for at least:

- missing config
- invalid config
- startup failure
- transport connection failure
- authentication required
- authentication expired
- capability registration failure
- unexpected runtime failure

The contract standardizes the presence of stable error codes, not their exact final enum names.

### 12.3 Structured Diagnostics

The runtime must provide a structured diagnostic payload or equivalent error metadata alongside human-readable text where possible.

Human-readable stderr logging is allowed, but it is secondary to machine-readable lifecycle/error reporting.

---

## 13. Interactive Setup Contract

### 13.1 Purpose

Some modules require interactive setup before they can reach `ready`.

Typical example:

- Weixin QR login

### 13.2 Host-Visible Semantics

The contract must distinguish between:

- configuration is missing or malformed
- configuration is valid but interactive setup is required
- configuration is valid but previously established auth has expired

These must map to distinct machine-readable lifecycle states or error codes.

### 13.3 UI-Neutral Contract

The interactive setup contract must remain UI-neutral.

The module may require host participation, but this specification does not require:

- terminal-only QR rendering
- desktop-only dialogs
- one specific interaction surface

It only requires the module to signal the need and state of interactive setup in a structured form.

---

## 14. Capability and Tool Registration Contract

### 14.1 Capability Boundary

The SDK must distinguish clearly between:

- **static manifest metadata**: broad capability categories and integration-relevant flags
- **dynamic runtime capabilities**: actual tool and delivery registrations active for a specific module instance

### 14.2 Manifest-Level Capability Summary

The manifest-level capability summary must be able to express, at minimum:

- whether runtime tool registration exists
- whether structured delivery exists
- whether interactive setup may be required
- whether capability sets may vary by environment or workspace

The manifest is not required to list the full runtime tool set.

### 14.3 Runtime Tool Registration

Runtime-registered tools remain module-owned and may vary by:

- environment
- workspace
- selected variant

The SDK contract must therefore not assume that one channel always has one fixed tool set.

### 14.4 Typed Tool Surface

The SDK should standardize typed concepts for:

- delivery capability descriptor
- channel tool descriptor
- tool invocation context
- tool invocation result
- tool approval metadata

Low-level transport serialization may remain dictionary-based internally, but the host-facing and module-facing surface must be intention-revealing and stable.

---

## 15. Modularity and Variant Support

### 15.1 First-Party and Enterprise Variants

The module contract must support a host choosing between:

- first-party adapter modules
- enterprise adapter modules
- environment-specific variants

without changing the host's integration model.

### 15.2 Selection Model

The recommended selection model is:

- host selects the implementation by `moduleId`
- runtime session identity remains keyed by `channelName`
- default config file naming remains keyed by `channelName`

This means first-party Feishu and enterprise Feishu can be substituted by changing the selected `moduleId` while still using the logical `feishu` channel.

### 15.3 Composition-Friendly Extension Model

The SDK contract should allow an adapter module to be composed from:

- base channel behavior
- optional tool providers
- optional capability providers
- environment-specific policy or configuration layers

This is especially important where enterprise variants differ mainly by additional tools or approval metadata.

---

## 16. SDK Surface Requirements

The SDK must evolve beyond the minimum needed to run examples and expose stable concepts for modular integration.

### 16.1 Required Contract Areas

The SDK must define stable concepts for:

- module manifest
- module entry/factory
- workspace context
- launcher context
- config descriptor
- delivery capability descriptor
- channel tool descriptor
- tool invocation context
- tool invocation result
- lifecycle status
- machine-readable error

### 16.2 Current Gaps This Spec Addresses

The SDK currently shows the following pressures that motivate this contract:

- host-facing behavior relies heavily on `Record<string, unknown>`
- tool registration is tied to adapter implementation details
- configuration discovery is package-local and inconsistent
- capability types are less complete than the broader protocol surface
- state layout is adapter-specific without a shared workspace contract

This specification exists to close those gaps before desktop or enterprise integration hardens them into product assumptions.

---

## 17. Package and Repository Contract

### 17.1 Package Set

The TypeScript external channel surface is standardized into three package classes:

- `dotcraft-wire` (shared SDK package under `sdk/typescript`)
- `@dotcraft/channel-feishu` (first-party Feishu package)
- `@dotcraft/channel-weixin` (first-party Weixin package)

Default first-party rule:

- each first-party package is a single-module package unless explicitly documented otherwise

### 17.2 Repository Layout and Ownership

The package-oriented layout is rooted at `sdk/typescript/packages/`.

Ownership rules:

- `sdk/typescript/src/*` is shared SDK code only
- each channel package owns its platform-specific implementation
- channel packages must not import package-internal implementation files from each other
- shared cross-package code must move into `dotcraft-wire` when it is contract/framework-level

End-state rule:

- no primary implementation remains under `sdk/typescript/examples/`

### 17.3 Package Metadata and Entrypoints

Each adapter package must define at least:

- `name`, `version`, `private`, `type`, `description`
- `main`, `types`, `exports`, `bin`
- `scripts.build`, `scripts.test`, `scripts.typecheck`
- `engines.node`

Each package must export:

- a canonical manifest export
- a canonical module entry/factory export
- required package-local config types/descriptors
- the CLI bootstrap entry where applicable

### 17.4 CLI and Host Consumption

Each first-party package must expose exactly one supported CLI command:

- `dotcraft-channel-feishu`
- `dotcraft-channel-weixin`

CLI behavior must align with this spec:

- explicit workspace input is supported
- explicit config override input is supported for development and testing
- startup failures are machine-readable through lifecycle/error signaling
- fatal startup failures are logged to stderr for diagnosis

Hosts must consume package-root exports only, and must not depend on package-internal implementation paths.

### 17.5 Versioning and Local Dependency Model

- `dotcraft-wire`, `@dotcraft/channel-feishu`, and `@dotcraft/channel-weixin` must use the same repository version by default.
- Adapter packages must depend on `dotcraft-wire` as a package dependency, not via source-relative implementation shortcuts.
- Repo-local dependency resolution must be workspace-based or stable package-root based.
- Per-package fragile `file:../..` dependency patterns that depend on folder depth are not allowed.

---

## 18. Build, Validation, and Documentation Contract

### 18.1 Build and Distribution Model

Each package must build to `dist/` with:

- ESM JavaScript output
- declaration files
- no runtime dependency on `src/`

Supported repo-local distribution forms:

- npm workspace install
- packed tarball via `npm pack`
- copied build artifact directory for local manual execution

### 18.2 Packability Contract

Each package must pass `npm pack --dry-run` with:

- required manifest and module-entry exports included
- required docs and declaration outputs included
- no dependence on unshipped example-only artifacts

### 18.3 Root Script Contract

The TypeScript SDK root must provide one-command package-oriented execution for:

- build all related packages
- test all related packages
- typecheck all related packages

### 18.4 Test and Conformance Contract

Adapter packages must provide tests for startup/config behavior and critical adapter behavior.

A shared conformance suite contract must verify, at minimum:

- package-root manifest loading
- package-root module entry loading
- no required package-internal host imports
- startup behavior consistency with workspace/config contract
- machine-readable missing-config and invalid-config failures

### 18.5 Runtime Documentation Contract

Each adapter package README must document:

- workspace `.craft/config.json` enablement snippet
- adapter config path and example (`.craft/feishu.json` or `.craft/weixin.json`)
- launcher semantics for local execution
- interactive setup behavior summary where relevant

---

## 19. Migration and Archival Contract

### 19.1 Migration End-State

The TypeScript adapter productization effort is complete only when:

- package identities, exports, scripts, and docs align with this unified specification
- primary implementation code lives under package roots, not `examples/`
- host integration guidance reflects the package-root manifest/module-entry model

### 19.2 Archival Lineage

The phased milestone specs `ts-adapter-m1` through `ts-adapter-m6` are implementation milestones for this contract effort. Their long-term normative requirements are consolidated into this document, and milestone files may be archived or removed once repository references are updated.

### 19.3 Scope Boundary for Historical Milestones

Milestone-specific execution details (temporary script wiring, migration-only sequencing, or one-off transitional wording) are not normative after consolidation unless explicitly restated in this document.

---

## 20. Conformance and Acceptance

### 20.1 Module Contract Conformance

A conforming module must satisfy all of the following:

- its manifest can be loaded through the canonical module-root export
- it exposes the canonical module entry/factory export
- it can be started with explicit workspace context
- config discovery resolves `.craft/<channel>.json` from explicit workspace input
- lifecycle statuses and error codes are machine-readable
- runtime capability and tool registration follow the declared contract

### 20.2 Package Conformance

A conforming package set must satisfy all of the following:

- first-party adapters are standard package units, not example-only roots
- each first-party package is clearly defined as a single-module package by default
- each package exports manifest + module entry through package-root exports
- each package can be built and tested independently in the repo
- each package supports local `npm pack --dry-run` validation
- package docs match the standardized workspace/config and startup UX

### 20.3 Unified Acceptance Criteria

This TypeScript external-channel contract effort is complete when all of the following are true:

- The TypeScript SDK exposes a stable module concept for external channel adapters.
- A host can integrate an adapter module without directly importing package-internal implementation files.
- The SDK standardizes workspace-derived adapter configuration and channel-named config files.
- The SDK exposes a manifest concept sufficient for host integration and variant substitution.
- The SDK defines stable concepts for tool and capability registration at the module boundary.
- The contract supports both first-party and enterprise adapter variants for the same channel family.
- Feishu and Weixin adapters live as standard packages, not example-only directories.
- Each package exports the manifest and module entry required by this specification.
- Each package can be built and tested independently inside the repo.
- Each package can be packed locally with `npm pack`.
- Package docs reflect the standardized workspace/config UX.
- `dotcraft-wire` is consumed as a package dependency rather than as a source-relative implementation shortcut.
- No implementation remains under `sdk/typescript/examples/` as a primary runtime path.

---

## Appendix A: Explicit Decisions

- This specification standardizes both the **module contract** and the package/distribution contract.
- The canonical manifest carrier is a module-root SDK export.
- The canonical startup model is manifest plus module-factory entry.
- Workspace is a required explicit startup input from the host or launcher.
- Channel-specific config filenames under workspace `.craft/` are part of the SDK-facing contract.
- Variant substitution is by `moduleId`; logical runtime identity remains `channelName`.
