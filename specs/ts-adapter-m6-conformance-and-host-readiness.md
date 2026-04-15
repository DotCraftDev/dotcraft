# DotCraft TypeScript Adapter SDK — M6: Conformance Validation and Desktop Host Readiness

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Draft |
| **Date** | 2026-04-15 |
| **Parent Spec** | [typescript-external-channel-module-contract.md](typescript-external-channel-module-contract.md), [typescript-external-channel-packages.md](typescript-external-channel-packages.md) |
| **Related Specs** | [ts-adapter-m1-sdk-module-contract-types.md](ts-adapter-m1-sdk-module-contract-types.md), [ts-adapter-m2-channel-adapter-module-refactor.md](ts-adapter-m2-channel-adapter-module-refactor.md), [ts-adapter-m3-package-infrastructure.md](ts-adapter-m3-package-infrastructure.md), [ts-adapter-m4-feishu-module-migration.md](ts-adapter-m4-feishu-module-migration.md), [ts-adapter-m5-weixin-module-migration.md](ts-adapter-m5-weixin-module-migration.md) |

Purpose: Validate module contract conformance end-to-end for both adapter packages, verify packability, establish the host integration documentation, align versions across all TypeScript packages, and clean up legacy artifacts. After M6, the SDK module contract effort is complete and Desktop has a defined integration surface.

---

## Table of Contents

- [1. Overview](#1-overview)
- [2. Goal](#2-goal)
- [3. Scope](#3-scope)
- [4. Non-Goals](#4-non-goals)
- [5. Shared Conformance Test Suite](#5-shared-conformance-test-suite)
- [6. Packability Verification](#6-packability-verification)
- [7. Variant Substitution Test](#7-variant-substitution-test)
- [8. Host Integration Reference Documentation](#8-host-integration-reference-documentation)
- [9. Legacy Artifact Cleanup](#9-legacy-artifact-cleanup)
- [10. Version Alignment](#10-version-alignment)
- [11. Acceptance Criteria for the Full Contract Effort](#11-acceptance-criteria-for-the-full-contract-effort)
- [12. Behavioral Contract](#12-behavioral-contract)
- [13. Constraints and Compatibility](#13-constraints-and-compatibility)
- [14. Acceptance Checklist](#14-acceptance-checklist)
- [15. Open Questions](#15-open-questions)

---

## 1. Overview

After M4 and M5, both adapter packages conform to the SDK module contract individually. M6 validates conformance from the outside — as a host would encounter it — through a shared test suite, verifies that both packages can be packed and distributed, documents the integration surface for Desktop, aligns all package versions, and removes any remaining legacy artifacts.

M6 is the completion milestone for the TypeScript Adapter SDK and Module Restructure feature. It produces no new adapter behavior. Its outputs are: a conformance test suite, packability proof, a host integration guide, and a clean repository state.

---

## 2. Goal

Prove that the module contract works as a stable integration boundary, document how Desktop (or any other host) can consume it, and leave the repository in a clean state with no `examples/` remnants and no version drift between packages.

---

## 3. Scope

- Write a shared conformance test suite that tests both adapter packages as a host would use them.
- Verify packability via `npm pack --dry-run` for all TypeScript packages.
- Write a variant substitution test demonstrating that two modules sharing the same `channelName` but different `moduleId` values can be swapped by a host without changing integration code.
- Write a host integration reference document in `docs/en/` (English) covering how Desktop integrates an adapter module.
- Clean up all remaining legacy artifacts: `examples/` directories, stale config template files, broken doc links.
- Align `dotcraft-wire`, `@dotcraft/channel-feishu`, and `@dotcraft/channel-weixin` to the same version.

---

## 4. Non-Goals

- Implementing Desktop UI for adapter module management (that is a separate Desktop feature).
- Implementing a runtime hot-plug protocol (live adapter swap without restart).
- Publishing packages to npm.
- Changing adapter business logic.
- Writing non-English documentation beyond what already exists.

---

## 5. Shared Conformance Test Suite

### 5.1 Location

A shared conformance test helper lives in `dotcraft-wire` at `src/conformance.test-helper.ts` (or equivalent path). It exports a function:

```typescript
export function runModuleConformanceSuite(
  packageName: string,
  importModule: () => Promise<{ manifest: ModuleManifest; createModule: ModuleFactory; configDescriptors?: ConfigDescriptor[] }>,
  options: ConformanceSuiteOptions
): void
```

Each adapter package calls this function in its own test suite, passing its own module exports and test-specific options. This avoids duplication of conformance logic across packages.

### 5.2 ConformanceSuiteOptions

```typescript
type ConformanceSuiteOptions = {
  expectedModuleId: string;
  expectedChannelName: string;
  expectedConfigFileName: string;
  expectedRequiresInteractiveSetup: boolean;
  expectedVariant: ModuleVariant;
  workspaceContextFixture: WorkspaceContext;   // points to a temp dir with no .craft/ content
  validConfigFixture: unknown;                  // a minimal valid config object for this module
}
```

### 5.3 Required Conformance Assertions

The shared suite must assert all of the following for each adapter package:

**Manifest conformance:**
- `manifest` is importable from the package root without path imports.
- `manifest.moduleId === expectedModuleId`.
- `manifest.channelName === expectedChannelName`.
- `manifest.configFileName === expectedConfigFileName`.
- `manifest.requiresInteractiveSetup === expectedRequiresInteractiveSetup`.
- `manifest.variant === expectedVariant`.
- `manifest.sdkContractVersion === sdkContractVersion` (from `dotcraft-wire`).
- `manifest.supportedProtocolVersions` is a non-empty array.
- `manifest.launcher.supportsWorkspaceFlag === true`.
- `manifest.capabilitySummary` is a non-null object.

**Module entry conformance:**
- `createModule` is importable from the package root.
- `createModule(workspaceContextFixture)` returns an object without throwing.
- The returned object has `start`, `stop`, `onStatusChange`, `getStatus`, `getError` as functions.
- `getStatus()` returns `"stopped"` immediately after `createModule`.
- No network connection is made during `createModule`.

**Config discovery conformance:**
- Calling `instance.start()` with `workspaceContextFixture` (no `.craft/` directory) transitions status to `configMissing`.
- The status is observable via `getStatus()` without any additional calls.
- The status change triggers all registered `onStatusChange` handlers.

**Config descriptor conformance** (if `configDescriptors` is exported):
- `configDescriptors` is an array.
- Each entry has `key`, `displayLabel`, `dataKind`, `required`, `masked`.
- All entries with `dataKind === "secret"` have `masked === true`.

### 5.4 Integration with Adapter Test Suites

Each adapter package includes a test file (e.g. `src/conformance.test.ts`) that calls `runModuleConformanceSuite` with the package-specific options. This test runs as part of `npm run test` for that package and also as part of `npm run test:all` from the workspace root.

---

## 6. Packability Verification

### 6.1 Per-Package Verification

For each of the three TypeScript packages (`dotcraft-wire`, `@dotcraft/channel-feishu`, `@dotcraft/channel-weixin`), the following must succeed in sequence:

```
npm run build      (or build from workspace root)
npm run test
npm pack --dry-run
```

### 6.2 Required Pack Output

For `@dotcraft/channel-feishu` and `@dotcraft/channel-weixin`, the `npm pack --dry-run` output must include at minimum:

- `dist/index.js`
- `dist/index.d.ts`
- `dist/cli.js`
- `README.md`
- `README_ZH.md`

For `dotcraft-wire`:
- `dist/index.js`
- `dist/index.d.ts`
- `README.md`

### 6.3 Excluded from Pack Output

The pack output must not include:
- `src/` directories.
- `node_modules/`.
- Local dev config files (`adapter_config.json`, `.env`, etc.).
- Any file listed in `.gitignore`.

### 6.4 Workspace Root Script

The workspace root must have a script `pack:verify` or equivalent that runs pack verification for all three packages in order. This script may be a shell script or an npm script using `--workspace` flags.

---

## 7. Variant Substitution Test

### 7.1 Purpose

Verify that the module contract's variant substitution model works: a host can swap one module for another that shares the same `channelName` by changing only the `moduleId` used for selection.

### 7.2 Test Setup

A test in `sdk/typescript/src/conformance-variant.test.ts` (or a dedicated conformance test package) must:

1. Import `manifest` from `@dotcraft/channel-feishu`.
2. Construct a mock second manifest with `moduleId: "feishu-enterprise"`, `channelName: "feishu"`, and identical `configFileName`.
3. Assert that a host selection function keyed by `moduleId` can choose between the two modules and that both modules' `channelName` values are identical.
4. Assert that the workspace `WorkspaceContext.channelName` field, which is always set to `manifest.channelName`, does not need to change when the host switches from `moduleId: "feishu-standard"` to `moduleId: "feishu-enterprise"`.

This test is intentionally simple — it validates the selection model, not a real enterprise module implementation.

---

## 8. Host Integration Reference Documentation

### 8.1 Location and Format

A host integration guide is created at `docs/en/typescript-channel-module-host-integration.md`. It is in English only. A stub Chinese version at `docs/typescript-channel-module-host-integration.md` may be added, but its content can be deferred.

### 8.2 Required Sections

The guide must document:

1. **Overview**: What the TypeScript adapter module contract provides to a host.

2. **Loading a module**: How to import `manifest` and `createModule` from a package root.

3. **Discovering modules**: How a host can build a module registry by scanning known package roots or a configured list of `moduleId` values.

4. **Creating and starting a module instance**: The full sequence from `WorkspaceContext` construction to `instance.start()`.

5. **Observing lifecycle**: How to register `onStatusChange` handlers and map each `LifecycleStatus` to a host action (e.g. show error, prompt user, mark as active).

6. **Rendering config UI**: How to use `configDescriptors` to build a configuration form, including field kinds, labels, required flags, and masked fields.

7. **Interactive setup**: How to detect `authRequired` and `authExpired` states, and what action the host takes (e.g. display a QR path, prompt the user, notify a dashboard).

8. **Stopping a module**: How to call `instance.stop()` and observe the `stopped` status.

9. **Variant substitution**: How to configure which `moduleId` to load for a given `channelName`, allowing enterprise variants to replace first-party ones.

10. **Adding new modules**: What a third-party adapter package must export to be loadable through the same host integration model.

### 8.3 Code Examples

The guide must include short TypeScript code snippets for sections 2, 4, 5, 6, and 7. Snippets must compile cleanly against the `dotcraft-wire` types from M1.

---

## 9. Legacy Artifact Cleanup

### 9.1 examples/ Directory

After M4 and M5, no primary implementation should remain in `sdk/typescript/examples/`. M6 verifies this is true and, if any examples remain, removes them or reduces them to README stubs pointing to the new packages.

The canonical end state:

- `sdk/typescript/examples/` either does not exist, or contains only a `README.md` with redirect text.

### 9.2 Stale Documentation Links

All references to `examples/feishu` or `examples/weixin` in `docs/`, `specs/`, and `sdk/typescript/README.md` must be updated to point to `sdk/typescript/packages/channel-feishu` or `sdk/typescript/packages/channel-weixin`.

### 9.3 Stale Config Template Files

The `config.example.json` files that existed in the old example directories are superseded by README documentation. If they were carried over into the package directories during M3, they must either:
- Be included in the `files` allowlist as reference templates (acceptable), or
- Be removed in favor of the README documentation.

The decision from M4 and M5 is applied consistently in M6.

### 9.4 dotcraft-wire README Update

`sdk/typescript/README.md` must be updated to:
- Describe the SDK's role as both a wire protocol client and a module contract type layer.
- List `dotcraft-wire`, `@dotcraft/channel-feishu`, and `@dotcraft/channel-weixin` as the TypeScript package set.
- Link to `docs/en/typescript-channel-module-host-integration.md` for host integration guidance.

---

## 10. Version Alignment

### 10.1 Rule

`dotcraft-wire`, `@dotcraft/channel-feishu`, and `@dotcraft/channel-weixin` must share the same `version` field in their respective `package.json` files.

### 10.2 Version Bumping

If the milestone implementations required version bumps in any package, M6 ensures all three packages advance to the same version. The version number is an implementation decision; what is required is that all three are in sync.

### 10.3 sdkContractVersion vs package version

The `sdkContractVersion` exported by `dotcraft-wire` (introduced in M1) tracks the module contract surface version independently of the package version. It does not need to match the package version, but must be stable from M1 forward.

---

## 11. Acceptance Criteria for the Full Contract Effort

The following criteria apply across the entire M1–M6 effort. M6 is not complete until all of them are true.

Per [typescript-external-channel-module-contract.md](typescript-external-channel-module-contract.md) §17.2:

- [ ] The TypeScript SDK exposes a stable module concept for external channel adapters.
- [ ] A host can integrate an adapter module without importing package-internal files.
- [ ] The SDK standardizes workspace-derived adapter configuration and channel-named config files.
- [ ] The SDK exposes a manifest concept sufficient for host integration and variant substitution.
- [ ] The SDK defines stable concepts for tool and capability registration at the module boundary.
- [ ] The contract supports both first-party and enterprise adapter variants for the same channel family.
- [ ] The package specification can reference this module contract without redefining module behavior.

Per [typescript-external-channel-packages.md](typescript-external-channel-packages.md) §12:

- [ ] Feishu and Weixin adapters live as standard packages, not example-only directories.
- [ ] Each first-party package is a clearly documented single-module package.
- [ ] Each package exports the manifest and module entry required by the module contract.
- [ ] Each package can be built and tested independently inside the repo.
- [ ] Each package can be packed locally with `npm pack`.
- [ ] Package docs reflect the standardized workspace/config UX.
- [ ] `dotcraft-wire` is consumed as a package dependency rather than as a source-relative implementation shortcut.
- [ ] No part of the package specification redefines runtime behavior owned by the module contract.

---

## 12. Behavioral Contract

M6 produces no new runtime behavior. Its behavioral contract is:

1. Every assertion in the shared conformance test suite passes for both adapter packages.
2. `npm run build:all && npm run test:all` succeeds at the workspace root.
3. `npm pack --dry-run` succeeds for all three packages.
4. The host integration guide is accurate: code snippets compile, and the described integration sequence matches the actual module contract.
5. No `examples/` primary implementation code remains.
6. All three packages share the same `version` in `package.json`.

---

## 13. Constraints and Compatibility

- The shared conformance test helper must not be published as a separate package; it lives inside `dotcraft-wire`'s source and is used via the npm workspace.
- The host integration guide must not describe Desktop-specific UI components; it must describe the general integration contract usable by Desktop, CLI tools, or other host processes.
- The variant substitution test does not require an actual enterprise adapter implementation; a mock manifest object is sufficient.
- The guide and all documentation must use English. Chinese documentation (stub or full) is optional for this milestone.

---

## 14. Acceptance Checklist

- [ ] `runModuleConformanceSuite` is exported from `dotcraft-wire`.
- [ ] `@dotcraft/channel-feishu` calls `runModuleConformanceSuite` in its test suite and all assertions pass.
- [ ] `@dotcraft/channel-weixin` calls `runModuleConformanceSuite` and all assertions pass.
- [ ] Variant substitution test exists and passes.
- [ ] `npm run build:all` succeeds from `sdk/typescript/`.
- [ ] `npm run test:all` succeeds from `sdk/typescript/`.
- [ ] `npm pack --dry-run` succeeds for `dotcraft-wire`.
- [ ] `npm pack --dry-run` succeeds for `@dotcraft/channel-feishu` with required files.
- [ ] `npm pack --dry-run` succeeds for `@dotcraft/channel-weixin` with required files.
- [ ] `docs/en/typescript-channel-module-host-integration.md` exists and covers all required sections.
- [ ] Code snippets in the guide compile against `dotcraft-wire` types.
- [ ] `sdk/typescript/examples/` contains no primary implementation code.
- [ ] All docs and spec links to `examples/` paths have been updated to package paths.
- [ ] `sdk/typescript/README.md` references the full package set and links to the host integration guide.
- [ ] `dotcraft-wire`, `@dotcraft/channel-feishu`, and `@dotcraft/channel-weixin` share the same `version`.
- [ ] All acceptance criteria from [typescript-external-channel-module-contract.md §17.2](typescript-external-channel-module-contract.md) are met.
- [ ] All acceptance criteria from [typescript-external-channel-packages.md §12](typescript-external-channel-packages.md) are met.

---

## 15. Open Questions

- Should the host integration guide be included in the Desktop `docs/` folder, in the SDK `sdk/typescript/` folder, or in the top-level `docs/en/` folder? Top-level `docs/en/` is chosen as it is the canonical location for cross-component English documentation in this repository.
- Should a `pack:verify` root script be added to the workspace root `package.json`, or should pack verification be part of the CI configuration only? A root `pack:verify` script makes verification runnable locally; this is preferred over CI-only configuration.
- Is `sdkContractVersion` expected to increment between minor SDK versions, or only on breaking contract changes? Only on breaking contract changes. This matches semver major-version intent and keeps `sdkContractVersion` stable across patch and minor SDK releases.
- Should M6 add any lint or static analysis rules to prevent future regressions (e.g. a lint rule against importing from package-internal paths)? This is a useful addition but is deferred to a follow-up spec; enforcing it mechanically is not required to complete the module contract effort.
