# DotCraft TypeScript Adapter SDK — M3: Package Infrastructure and Repository Layout

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Draft |
| **Date** | 2026-04-15 |
| **Parent Spec** | [typescript-external-channel-packages.md](typescript-external-channel-packages.md) |
| **Related Specs** | [ts-adapter-m1-sdk-module-contract-types.md](ts-adapter-m1-sdk-module-contract-types.md), [ts-adapter-m2-channel-adapter-module-refactor.md](ts-adapter-m2-channel-adapter-module-refactor.md) |

Purpose: Establish the npm workspaces monorepo structure under `sdk/typescript/`, create the two first-party adapter package directories, move existing source files into those directories, and wire up a one-command build and test system covering all TypeScript packages.

---

## Table of Contents

- [1. Overview](#1-overview)
- [2. Goal](#2-goal)
- [3. Scope](#3-scope)
- [4. Non-Goals](#4-non-goals)
- [5. Target Repository Layout](#5-target-repository-layout)
- [6. npm Workspaces Configuration](#6-npm-workspaces-configuration)
- [7. Package Metadata Contract](#7-package-metadata-contract)
- [8. TypeScript Project References](#8-typescript-project-references)
- [9. Source File Migration](#9-source-file-migration)
- [10. Root Scripts Contract](#10-root-scripts-contract)
- [11. Local Dependency Model](#11-local-dependency-model)
- [12. Packability Contract](#12-packability-contract)
- [13. Behavioral Contract During Migration](#13-behavioral-contract-during-migration)
- [14. Constraints and Compatibility](#14-constraints-and-compatibility)
- [15. Acceptance Checklist](#15-acceptance-checklist)
- [16. Open Questions](#16-open-questions)

---

## 1. Overview

Currently, the Feishu and Weixin adapters live under `sdk/typescript/examples/` with ad hoc `file:../..` dependencies on `dotcraft-wire`. They have no stable package names, no canonical exports, and no `npm pack` readiness.

M3 restructures the repository so that:

- `sdk/typescript/` becomes an npm workspaces root.
- `sdk/typescript/packages/channel-feishu/` and `sdk/typescript/packages/channel-weixin/` are standard package directories.
- Source code is moved from `examples/` into the package directories.
- A root-level script set enables one-command CI-style build and test across all TypeScript packages.

M3 is a structural milestone. Adapter behavior does not change. Module contract conformance (manifest exports, module factory) is deferred to M4 and M5. The packages produced by M3 build and pass their existing tests, nothing more.

---

## 2. Goal

Transform the ad hoc `examples/` layout into a proper monorepo where each channel adapter is a standard, buildable, testable, packable Node package with a stable name and a versioned dependency on `dotcraft-wire`.

---

## 3. Scope

- Add npm workspaces configuration to `sdk/typescript/package.json`.
- Create `sdk/typescript/packages/channel-feishu/` with all required package metadata.
- Create `sdk/typescript/packages/channel-weixin/` with all required package metadata.
- Move source files from `sdk/typescript/examples/feishu/src/` to `sdk/typescript/packages/channel-feishu/src/`.
- Move source files from `sdk/typescript/examples/weixin/src/` to `sdk/typescript/packages/channel-weixin/src/`.
- Move test files from each example into the corresponding package directory.
- Move `README.md` and `README_ZH.md` from each example into the corresponding package directory.
- Add `tsconfig.json` per package with TypeScript project references to `dotcraft-wire`.
- Add root scripts that build, test, and typecheck all packages.
- Ensure `npm pack --dry-run` succeeds for both packages after build.
- Remove or reduce `sdk/typescript/examples/` to documentation-only stubs after migration is complete.

---

## 4. Non-Goals

- Implementing module contract conformance (manifest, factory, lifecycle) in the adapter packages. That is M4 and M5.
- Publishing packages to npm or any registry.
- Changing adapter business logic, message handling, or tool registration.
- Changing `dotcraft-wire` source code beyond what was done in M1 and M2.
- Solving native executable bundling or Node runtime distribution.
- Implementing hot-plug or Desktop integration.

---

## 5. Target Repository Layout

```
sdk/typescript/
  package.json                   (dotcraft-wire, now also workspaces root)
  package-lock.json
  tsconfig.json
  tsconfig.build.json            (if split config is used)
  src/
  dist/
  README.md
  README_ZH.md
  packages/
    channel-feishu/
      package.json               (@dotcraft/channel-feishu)
      tsconfig.json
      src/
        index.ts                 (package entry; module contract exports in M4)
        feishu-adapter.ts
        feishu-client.ts
        feishu-types.ts
        feishu-events.ts
        ...                      (all former examples/feishu/src/ files)
      dist/
      README.md
      README_ZH.md
    channel-weixin/
      package.json               (@dotcraft/channel-weixin)
      tsconfig.json
      src/
        index.ts
        weixin-adapter.ts
        auth.ts
        ...                      (all former examples/weixin/src/ files)
      dist/
      README.md
      README_ZH.md
```

The `examples/` directory is either removed or reduced to a stub `README.md` pointing to the new package locations.

---

## 6. npm Workspaces Configuration

`sdk/typescript/package.json` must gain a `workspaces` field:

```json
{
  "workspaces": [
    "packages/channel-feishu",
    "packages/channel-weixin"
  ]
}
```

The workspace root installs dependencies for all packages with a single `npm install`. Symlinks in `node_modules/@dotcraft/channel-feishu` and `node_modules/@dotcraft/channel-weixin` are created automatically by npm workspaces, enabling cross-package imports in development without `file:` paths.

---

## 7. Package Metadata Contract

Each adapter package must have a `package.json` that conforms to the following minimum shape. The actual values for each package are specified in M4 and M5.

### 7.1 Required Fields

| Field | Rule |
|-------|------|
| `name` | `@dotcraft/channel-feishu` or `@dotcraft/channel-weixin` |
| `version` | Must match `dotcraft-wire` version |
| `private` | `true` |
| `type` | `"module"` |
| `description` | Non-empty human-readable description |
| `main` | `./dist/index.js` |
| `types` | `./dist/index.d.ts` |
| `exports` | At minimum: `"."` with `import` and `types` subpaths |
| `bin` | One entry: `dotcraft-channel-feishu` or `dotcraft-channel-weixin` → `./dist/cli.js` |
| `files` | `["dist"]` plus `README.md` and `README_ZH.md` |
| `scripts.build` | Compiles TypeScript to `dist/` |
| `scripts.test` | Runs the package's tests |
| `scripts.typecheck` | Runs `tsc --noEmit` |
| `engines.node` | `">=20.0.0"` |
| `dependencies.dotcraft-wire` | `"*"` (resolved via npm workspaces) |

### 7.2 Exports Shape

```json
{
  "exports": {
    ".": {
      "import": "./dist/index.js",
      "types": "./dist/index.d.ts"
    }
  }
}
```

At M3, the `index.ts` entry may simply re-export the existing adapter class and any public types. The canonical `manifest` and `createModule` exports are added in M4 and M5.

### 7.3 Bin Entry

The `bin` entry must point to a `dist/cli.js` file compiled from `src/cli.ts`. At M3, `src/cli.ts` may be a thin wrapper that calls the existing `main()` logic. The CLI contract (explicit `--workspace` support) is formalized in M4 and M5.

---

## 8. TypeScript Project References

### 8.1 dotcraft-wire tsconfig

`sdk/typescript/tsconfig.json` must be updated to include `packages/channel-feishu` and `packages/channel-weixin` as project references:

```json
{
  "references": [
    { "path": "./packages/channel-feishu" },
    { "path": "./packages/channel-weixin" }
  ]
}
```

### 8.2 Per-Package tsconfig

Each package's `tsconfig.json` must reference the root `dotcraft-wire` tsconfig so that TypeScript can resolve local types:

```json
{
  "extends": "../../tsconfig.json",
  "compilerOptions": {
    "rootDir": "src",
    "outDir": "dist",
    "declarationDir": "dist"
  },
  "references": [
    { "path": "../../" }
  ],
  "include": ["src"]
}
```

The exact `extends` and field set may vary; what is required is that:

- The package compiles independently with `tsc -p tsconfig.json`.
- `dotcraft-wire` types resolve correctly without path aliases.
- Declaration files are emitted to `dist/`.

---

## 9. Source File Migration

### 9.1 Feishu Source Migration

All files under `sdk/typescript/examples/feishu/src/` are moved to `sdk/typescript/packages/channel-feishu/src/`. No file is renamed at this step. Existing import paths within the files are adjusted only if they break due to the new directory depth relative to `dotcraft-wire`.

All test files move with the source. `config.example.json` moves to the package root as documentation. `.gitignore` moves to the package root.

### 9.2 Weixin Source Migration

Same migration rule for `sdk/typescript/examples/weixin/src/` → `sdk/typescript/packages/channel-weixin/src/`.

The `adapter_config.json` (a local dev config file) must not be committed; it moves to `.gitignore` in the package root.

### 9.3 README Migration

`examples/feishu/README.md` and `README_ZH.md` move to `packages/channel-feishu/README.md` and `README_ZH.md`. Content is preserved; links and paths are updated where needed. The M4 spec will extend the README content to include module contract documentation.

Same for Weixin.

### 9.4 examples/ Reduction

After migration, `sdk/typescript/examples/` must either:

- Be removed entirely, or
- Be reduced to a stub directory containing only a `README.md` that says the adapters have moved to `packages/channel-feishu` and `packages/channel-weixin`.

No primary implementation code should remain under `examples/` after M3 is complete.

---

## 10. Root Scripts Contract

`sdk/typescript/package.json` must add or update scripts to support all-package operations:

| Script name | Behavior |
|-------------|----------|
| `build` | Builds `dotcraft-wire` only (existing behavior, preserved) |
| `build:packages` | Builds `@dotcraft/channel-feishu` and `@dotcraft/channel-weixin` after `dotcraft-wire` |
| `build:all` | Runs `build` then `build:packages` |
| `test` | Tests `dotcraft-wire` only (existing behavior, preserved) |
| `test:packages` | Tests `@dotcraft/channel-feishu` and `@dotcraft/channel-weixin` |
| `test:all` | Runs `test` then `test:packages` |
| `typecheck` | Typechecks `dotcraft-wire` only (existing behavior, preserved) |
| `typecheck:packages` | Typechecks both adapter packages |
| `typecheck:all` | Runs `typecheck` then `typecheck:packages` |

Scripts may be implemented using `npm run --workspace` or `npm -ws run` patterns. The exact implementation is deferred to the milestone implementation.

---

## 11. Local Dependency Model

### 11.1 Primary Model: npm Workspaces

Both adapter packages declare `dotcraft-wire` as a dependency with version `"*"`. npm workspaces resolves this to the local `sdk/typescript` package root, creating a symlink in each package's `node_modules`. This replaces the fragile `file:../..` dependency used in the example directories.

### 11.2 Fallback Verification

The implementation must verify that `npm install` run at `sdk/typescript/` correctly installs all workspace dependencies, including platform-specific SDKs (`@larksuiteoapi/node-sdk`, `qrcode-terminal`), without errors.

---

## 12. Packability Contract

### 12.1 Required Verification

For each adapter package, after build, the following must succeed:

```
npm run build
npm pack --dry-run
```

The dry-run output must include:
- `dist/index.js`
- `dist/index.d.ts`
- `dist/cli.js`
- `README.md`
- `README_ZH.md`

### 12.2 No Unshipped Files

The packed output must not include:
- `src/` files
- `node_modules/`
- `config.example.json` (it should be in docs, not the packed output — or include it if it is useful as a template; the M4/M5 specs will decide)
- Local dev config files (e.g. `adapter_config.json`)

---

## 13. Behavioral Contract During Migration

- Adapter business logic must produce identical behavior before and after the file move.
- All existing tests must pass after moving files and updating imports.
- No new tests are required in M3 beyond confirming that existing tests still pass in the new location.
- The `examples/` reduction must not break any documentation link that points into `docs/` or `specs/`. All such links must be updated to point to the new package paths.

---

## 14. Constraints and Compatibility

- npm version must support workspaces (npm ≥ 7). The `package.json` `engines` field at the workspace root should document the minimum required npm version.
- The workspace root `package.json` must remain named `dotcraft-wire` to avoid breaking any existing consumers.
- TypeScript project references require TypeScript ≥ 5.0 (already satisfied by the existing SDK config).
- The `dist/` directories of the adapter packages must not be committed to the repository. They must be added to `.gitignore` at the package root.
- The existing `sdk/typescript/dist/` gitignore rule is preserved.

---

## 15. Acceptance Checklist

- [ ] `sdk/typescript/package.json` has a `workspaces` field listing both adapter packages.
- [ ] `sdk/typescript/packages/channel-feishu/` exists with all required `package.json` fields.
- [ ] `sdk/typescript/packages/channel-weixin/` exists with all required `package.json` fields.
- [ ] Source files have been moved from `examples/` into the package `src/` directories.
- [ ] `examples/` is removed or reduced to a stub README.
- [ ] `npm install` succeeds at `sdk/typescript/` with no errors.
- [ ] `npm run build:all` succeeds (builds dotcraft-wire, then both adapter packages).
- [ ] `npm run test:all` succeeds (all tests pass in all packages).
- [ ] `npm run typecheck:all` succeeds with no type errors.
- [ ] `npm pack --dry-run` succeeds for `@dotcraft/channel-feishu` and includes `dist/index.js`, `dist/cli.js`, `README.md`.
- [ ] `npm pack --dry-run` succeeds for `@dotcraft/channel-weixin` and includes same files.
- [ ] `dist/` directories for both adapter packages are in `.gitignore`.
- [ ] No `file:../..` dependency remains in any package within `sdk/typescript/`.
- [ ] All links in `docs/` and `specs/` that referenced `examples/feishu` or `examples/weixin` are updated.

---

## 16. Open Questions

- Should the workspace root `package.json` gain `private: true` since it is now a monorepo root? This is safe and recommended to prevent accidental `npm publish` of the workspace root itself. The `dotcraft-wire` library package is still publishable from its own root.
- Should `config.example.json` be included in the `files` allowlist so that users who install the package get a config template? This depends on whether the README is sufficient. M4 and M5 will decide.
- Should a `clean` script be added to each package to remove `dist/` before a fresh build? This is a developer convenience; the spec does not require it but the implementation may add it.
