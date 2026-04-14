# DotCraft TypeScript External Channel Package Specification

| Field | Value |
|-------|-------|
| **Version** | 0.2.0 |
| **Status** | Draft |
| **Date** | 2026-04-14 |
| **Related Specs** | [typescript-external-channel-module-contract.md](typescript-external-channel-module-contract.md), [external-channel-adapter.md](external-channel-adapter.md), [appserver-protocol.md](appserver-protocol.md) |

Purpose: define how TypeScript external channel modules are packaged, versioned, documented, built, and consumed inside the repository once the SDK module contract is in place.

---

## Table of Contents

- [1. Scope](#1-scope)
- [2. Spec Relationship](#2-spec-relationship)
- [3. Goals](#3-goals)
- [4. Non-Goals](#4-non-goals)
- [5. Package Set](#5-package-set)
- [6. Repository Layout](#6-repository-layout)
- [7. Package Contract](#7-package-contract)
- [8. Runtime Configuration Documentation Contract](#8-runtime-configuration-documentation-contract)
- [9. Build and Distribution Model](#9-build-and-distribution-model)
- [10. Validation and Testing](#10-validation-and-testing)
- [11. Migration Plan](#11-migration-plan)
- [12. Acceptance Criteria](#12-acceptance-criteria)

---

## 1. Scope

### 1.1 What This Spec Defines

- How TypeScript external channel implementations are upgraded from examples into standard package units.
- The required package boundaries between the shared TypeScript SDK and channel-specific adapter packages.
- The repo-local consumption model when packages are **not** published to a registry.
- The required package metadata, build outputs, docs, and test surface for each adapter package.
- The packaging and distribution implications of the SDK module contract.

### 1.2 What This Spec Does Not Define

- Desktop integration, installer UX, or package onboarding flows in the desktop client.
- Changes to the external channel wire protocol, `ExternalChannelHost`, or `ExternalChannelManager`.
- Cross-language packaging for Python or other SDKs.
- Native executable bundling. This spec standardizes Node package outputs first; standalone binaries may be added later.
- Runtime behavior already owned by the module contract spec, including:
  - manifest carrier
  - module entry shape
  - workspace startup contract
  - lifecycle statuses and error codes
  - module-owned state layout

---

## 2. Spec Relationship

This specification is the **distribution and packaging layer** for TypeScript external channel adapters.

It depends on [typescript-external-channel-module-contract.md](typescript-external-channel-module-contract.md), which defines:

- the SDK-facing module contract
- the canonical manifest and module entry model
- the standardized adapter configuration model
- the workspace and launcher contract
- the lifecycle/error model
- the capability and tool registration model
- the modular extension boundary required for first-party and enterprise adapter variants

Layering:

1. [appserver-protocol.md](appserver-protocol.md) defines the wire contract.
2. [external-channel-adapter.md](external-channel-adapter.md) defines the external channel architecture and runtime behavior.
3. [typescript-external-channel-module-contract.md](typescript-external-channel-module-contract.md) defines the TypeScript SDK module contract used by adapter implementations.
4. This specification defines how those modules are packaged, versioned, built, documented, and consumed inside the repository.

If this specification conflicts with the module-contract specification on runtime behavior or host integration rules, the module-contract specification takes precedence and this package specification must be updated to match it.

---

## 3. Goals

The productized TypeScript adapter packages must satisfy the following:

1. They are standard Node/TypeScript packages with stable package names, stable entrypoints, and reproducible builds.
2. They remain fully usable inside the monorepo without registry publication.
3. They no longer depend on ad hoc `example` semantics or example-only naming.
4. They preserve the current platform behavior unless a change is explicitly called out elsewhere.
5. They make the shared SDK dependency (`dotcraft-wire`) explicit and versioned.
6. They distribute modules that conform to the SDK module contract without redefining that contract.
7. They are suitable for later integration into desktop or other hosts without another structural rewrite.

---

## 4. Non-Goals

The following are explicitly deferred:

- Publishing `dotcraft-wire` or adapter packages to npm or any internal registry.
- Solving Node runtime distribution for end users.
- Reworking either adapter to prefer subprocess over WebSocket.
- Unifying Feishu and Weixin into one generic adapter package.
- Redesigning platform-specific approval UX, bot auth, QR login, or media behavior.
- Backward compatibility with old config names or old example conventions.

---

## 5. Package Set

The TypeScript external channel surface is standardized into three package classes.

### 5.1 Shared SDK Package

- Package name remains `dotcraft-wire`.
- Location remains `sdk/typescript`.
- Responsibility:
  - wire transport
  - `DotCraftClient`
  - `ChannelAdapter`
  - shared models and helpers
  - the SDK module contract surface

This package is the only shared runtime dependency allowed between first-party channel packages.

### 5.2 Channel Adapter Packages

Two default first-party adapter packages are defined:

- `@dotcraft/channel-feishu`
- `@dotcraft/channel-weixin`

Default first-party rule:

- each first-party package is a **single-module package** unless explicitly documented otherwise

This removes ambiguity between “one package, one module” and “one package, multiple modules” for the first-party baseline.

### 5.3 Multi-Module Packages

The ecosystem may support packages that distribute multiple conforming modules, but such packages must declare that explicitly and still expose:

- one canonical manifest export per module
- one canonical module entry per module

This specification does not require multi-module packaging for first-party packages.

---

## 6. Repository Layout

The current `examples/*` layout is replaced with a package-oriented layout under `sdk/typescript/packages/`.

### 6.1 Target Layout

```text
sdk/typescript/
  package.json                  # dotcraft-wire
  src/
  dist/
  packages/
    channel-feishu/
      package.json
      tsconfig.json
      src/
      dist/
      README.md
      README_ZH.md
    channel-weixin/
      package.json
      tsconfig.json
      src/
      dist/
      README.md
      README_ZH.md
```

### 6.2 Source Ownership

- `sdk/typescript/src/*` remains shared SDK code only.
- `channel-feishu/src/*` owns all Feishu/Lark-specific logic.
- `channel-weixin/src/*` owns all Weixin/iLink-specific logic.
- No package may import implementation files from another adapter package.
- Shared code discovered during migration must either:
  - move into `dotcraft-wire`, if it is protocol-level or module-framework-level, or
  - remain package-local if it is platform-specific

### 6.3 End-State Rule

The end state is:

- no primary implementation lives under `examples/`
- `examples/` is either removed or reduced to documentation-only references

---

## 7. Package Contract

Each adapter package must satisfy the following contract.

Packaging is a transport layer for modules. It must not weaken, bypass, or redefine the SDK module contract.

### 7.1 `package.json`

Each adapter package must define:

- `name`
- `version`
- `private: true`
- `type: "module"`
- `description`
- `main`
- `types`
- `exports`
- `bin`
- `scripts.build`
- `scripts.test`
- `scripts.typecheck`
- `engines.node`

### 7.2 Public Entrypoints

Each package must export everything required for a host to consume the module without using package-internal files.

Required exports:

- canonical manifest export
- canonical module entry export
- package-local config types and descriptors required by the module contract
- CLI bootstrap entry where applicable

### 7.3 CLI Contract

Each adapter package must expose exactly one supported CLI command through `bin`:

- `dotcraft-channel-feishu`
- `dotcraft-channel-weixin`

The CLI is a launcher surface for local execution and must align with the module contract.

The CLI must:

- support explicit workspace input
- support explicit config override input for development/testing
- fail with machine-readable startup status/error behavior as defined by the module contract
- log fatal startup failures to stderr for human diagnosis

### 7.4 Local Host Consumption

Packages are repo-local distribution units.

Hosts consume:

- the package root
- the module manifest export
- the module entry export

Hosts must not consume:

- package-internal implementation files
- undocumented folder conventions
- example-only entrypoints

### 7.5 Versioning

- All first-party TypeScript packages in this area must use the same repository version by default.
- `dotcraft-wire`, `@dotcraft/channel-feishu`, and `@dotcraft/channel-weixin` must advance together unless there is a documented exception.
- Adapter packages must declare a concrete dependency on the local `dotcraft-wire` package, not on source-relative implementation files.

### 7.6 Local Dependency Model

Because packages are not published to a registry:

- repo-local dependency resolution must be workspace-based or file/tarball based
- implementation must avoid per-package ad hoc `file:../..` paths that depend on the current folder depth

Preferred model:

- use npm workspaces rooted at `sdk/typescript`

Accepted fallback:

- use `file:` dependencies that point to a stable package root location after the repo layout is standardized

### 7.7 Transport Wording

Packages may distribute modules that support both subprocess and WebSocket transports.

However, local host integration language in docs and packaging must stay consistent with the module contract:

- local launches are host-driven
- workspace is explicit startup input
- transport support is declared by the manifest, not inferred from CLI wording alone

---

## 8. Runtime Configuration Documentation Contract

This section is documentation-focused only. Runtime behavior belongs to the module contract.

### 8.1 Workspace Config Documentation

The workspace-level DotCraft configuration example must be documented in each package README instead of being shipped as a separate template file.

### 8.2 Adapter Config Documentation

Each package README must document the channel-specific adapter config path under the workspace:

- `.craft/feishu.json`
- `.craft/weixin.json`

### 8.3 Documentation Scope

Each package README must include:

- the workspace `.craft/config.json` snippet needed to enable the logical external channel
- the adapter config file path and example contents
- the standard launcher semantics for local execution
- a brief description of any interactive setup behavior

The README is documentation only; it must not be treated as a hidden runtime contract outside the module spec.

---

## 9. Build and Distribution Model

### 9.1 Build Output

Each package must compile TypeScript into `dist/` with:

- ESM JavaScript output
- declaration files
- no runtime dependence on `src/`

### 9.2 Distribution Unit

The standard distribution unit for this phase is:

- a built Node package directory in the monorepo

Supported repo-local consumption forms:

- direct workspace install
- packed tarball via `npm pack`
- copied build artifact directory for manual local execution

### 9.3 `npm pack` Readiness

Each adapter package must be packable via `npm pack` without extra repo surgery.

That requires:

- correct `files` allowlist
- no dependence on unshipped example files
- built outputs present before packing
- manifest and module entry exports included in the packed output

### 9.4 Root Scripts

The TypeScript SDK root must gain package-oriented scripts for:

- build shared SDK
- build all channel packages
- test all channel packages
- typecheck all channel packages

Exact command wiring is implementation-defined, but the root must support one-command CI-style execution for all TypeScript packages.

---

## 10. Validation and Testing

### 10.1 Package-Level Expectations

Each adapter package must have:

- startup/config parsing tests
- adapter behavior tests for critical message flow
- package build verification
- package conformance verification against the module contract

### 10.2 Package Conformance

At minimum, package conformance must verify:

- manifest can be loaded from the package root
- module entry can be loaded from the package root
- package exports do not require package-internal file access
- startup behavior aligns with the module contract
- missing-config and invalid-config failures remain machine-readable

### 10.3 Feishu Package Tests

The Feishu package must preserve and formalize tests for:

- transcript rendering
- event handler behavior
- card action deduplication

### 10.4 Weixin Package Tests

The Weixin package must include at least:

- config parsing and validation tests
- login state loading behavior tests
- inbound message handling tests for the adapter boundary
- persistent runtime state path behavior tests

### 10.5 Packability Verification

For each adapter package, CI or local validation must verify:

- `npm run build`
- `npm run test`
- `npm pack --dry-run`

---

## 11. Migration Plan

### 11.1 Phase 1: Align with Module Contract

- revise package planning to match the finalized module contract
- define package exports around manifest plus module entry
- normalize terminology so first-party packages are treated as single-module packages by default

### 11.2 Phase 2: Standardize Package Metadata

- rename package identities from `*-example` to final package names
- add `exports`, `types`, `bin`, and `files`
- add `typecheck` scripts
- normalize Node engine declarations

### 11.3 Phase 3: Move to Package Layout

- create `sdk/typescript/packages/channel-feishu`
- create `sdk/typescript/packages/channel-weixin`
- move existing source, tests, and docs into those package roots

### 11.4 Phase 4: Normalize Docs and Local Consumption

- remove standalone workspace config template files
- move workspace `.craft/config.json` snippets into README
- standardize docs around `.craft/feishu.json` and `.craft/weixin.json`
- replace fragile relative `file:../..` dependency links
- add root scripts for building and testing all TypeScript packages

### 11.5 Phase 5: Remove Example Status

- remove or shrink the old `examples/` directories
- ensure all references in docs and specs point to the new package locations

---

## 12. Acceptance Criteria

This effort is complete when all of the following are true:

- Feishu and Weixin adapters live as standard packages, not example-only directories.
- Each first-party package is a clearly documented single-module package.
- Each package exports the manifest and module entry required by the module contract.
- Each package can be built and tested independently inside the repo.
- Each package can be packed locally with `npm pack`.
- Package docs reflect the standardized workspace/config UX.
- `dotcraft-wire` is consumed as a package dependency rather than as a source-relative implementation shortcut.
- No part of the package specification redefines runtime behavior owned by the module contract.

---

## Appendix A: Explicit Decisions

- The productization target is **repo-local standard packages**, not registry publication.
- Backward compatibility with old example config names is out of scope.
- First-party packages are single-module packages by default.
- Hosts consume package-root manifest and module entry exports, not package-internal files.
- Workspace `config.json` examples belong in README documentation, not in separate shipped template files.
