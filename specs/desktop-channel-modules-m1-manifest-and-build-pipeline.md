# DotCraft Desktop Channel Modules — M1: Static Manifest Generation and Module Build Pipeline

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Draft |
| **Date** | 2026-04-15 |
| **Parent Spec** | [typescript-external-channel-module-contract.md](typescript-external-channel-module-contract.md) |
| **Related Specs** | [typescript-external-channel-module-contract.md §17](typescript-external-channel-module-contract.md#17-package-and-repository-contract), [typescript-external-channel-module-contract.md §20](typescript-external-channel-module-contract.md#20-conformance-and-acceptance) |

Purpose: Make both first-party TypeScript adapter packages produce a static `manifest.json` file at build time and define the directory convention that Desktop (or any other host) uses to discover installed module packages without executing module code.

---

## Table of Contents

- [DotCraft Desktop Channel Modules — M1: Static Manifest Generation and Module Build Pipeline](#dotcraft-desktop-channel-modules--m1-static-manifest-generation-and-module-build-pipeline)
  - [Table of Contents](#table-of-contents)
  - [1. Overview](#1-overview)
  - [2. Goal](#2-goal)
  - [3. Scope](#3-scope)
  - [4. Non-Goals](#4-non-goals)
  - [5. Static Manifest File](#5-static-manifest-file)
    - [5.1 File Name and Location](#51-file-name-and-location)
    - [5.2 Required Fields](#52-required-fields)
    - [5.3 Generation Rule](#53-generation-rule)
    - [5.4 Generation Timing](#54-generation-timing)
    - [5.5 Conformance](#55-conformance)
  - [6. Module Package Directory Convention](#6-module-package-directory-convention)
    - [6.1 User-Installed Modules](#61-user-installed-modules)
    - [6.2 Discovery Contract](#62-discovery-contract)
    - [6.3 Directory Name](#63-directory-name)
  - [7. Bundled Module Convention for Desktop](#7-bundled-module-convention-for-desktop)
    - [7.1 Location](#71-location)
    - [7.2 Discovery Priority](#72-discovery-priority)
    - [7.3 Packaging](#73-packaging)
  - [8. Build Pipeline](#8-build-pipeline)
    - [8.1 Per-Package Manifest Generation Script](#81-per-package-manifest-generation-script)
    - [8.2 Root Script](#82-root-script)
    - [8.3 Build Output Verification](#83-build-output-verification)
    - [8.4 Pack Inclusion](#84-pack-inclusion)
  - [9. Constraints and Compatibility](#9-constraints-and-compatibility)
  - [10. Acceptance Checklist](#10-acceptance-checklist)
  - [11. Open Questions](#11-open-questions)

---

## 1. Overview

After M1–M6 of the TypeScript Adapter SDK effort, both `@dotcraft/channel-feishu` and `@dotcraft/channel-weixin` export `manifest` and `configDescriptors` as runtime JavaScript values. A host that loads the package can read them programmatically.

However, Desktop uses a subprocess model where it does not `import()` adapter code into its own process. It needs to read module metadata from a static file without executing any module JavaScript. This milestone introduces a `manifest.json` build artifact and defines the directory layout that Desktop scans to discover installed modules.

---

## 2. Goal

Enable any host to discover, identify, and read configuration metadata for installed TypeScript adapter modules by reading a static JSON file, without importing or executing module code.

---

## 3. Scope

- Define the `manifest.json` file format and required fields.
- Add a build step to `@dotcraft/channel-feishu` and `@dotcraft/channel-weixin` that generates `manifest.json` from the runtime `manifest` and `configDescriptors` exports.
- Define the on-disk directory convention for installed module packages.
- Define the bundled module directory convention inside the Desktop Electron package.
- Add root scripts to generate manifests for all packages.
- Include `manifest.json` in each package's `files` allowlist.

---

## 4. Non-Goals

- Desktop runtime code (discovery scanning, subprocess management, UI).
- Changes to the runtime module contract types or behavior.
- Publishing packages to npm.
- Module install/uninstall tooling.
- Changes to the DotCraft C# server.

---

## 5. Static Manifest File

### 5.1 File Name and Location

Each module package produces a `manifest.json` at the package root directory (alongside `package.json`).

### 5.2 Required Fields

`manifest.json` must include all fields from the runtime `ModuleManifest` type plus a `configDescriptors` array:

```json
{
  "moduleId": "feishu-standard",
  "channelName": "feishu",
  "displayName": "飞书",
  "packageName": "@dotcraft/channel-feishu",
  "configFileName": "feishu.json",
  "supportedTransports": ["websocket"],
  "requiresInteractiveSetup": false,
  "capabilitySummary": {
    "hasChannelTools": true,
    "hasStructuredDelivery": true,
    "requiresInteractiveSetup": false,
    "capabilitySetMayVaryByEnvironment": false
  },
  "sdkContractVersion": "1.0.0",
  "supportedProtocolVersions": ["0.2"],
  "variant": "standard",
  "launcher": {
    "bin": "dotcraft-channel-feishu",
    "supportsWorkspaceFlag": true,
    "supportsConfigOverrideFlag": true
  },
  "configDescriptors": [
    {
      "key": "dotcraft.wsUrl",
      "displayLabel": "DotCraft WebSocket URL",
      "description": "AppServer WebSocket endpoint (ws:// or wss://).",
      "required": true,
      "dataKind": "string",
      "masked": false,
      "interactiveSetupOnly": false,
      "defaultValue": "ws://127.0.0.1:9100/ws"
    }
  ]
}
```

The `configDescriptors` array follows the `ConfigDescriptor` type from `dotcraft-wire`. Fields that have `defaultValue` include it; fields without omit it.

### 5.3 Generation Rule

The `manifest.json` file must be mechanically generated from the same `manifest` and `configDescriptors` values that the package exports at runtime. It must not be hand-authored independently, to avoid drift between the static file and the runtime exports.

### 5.4 Generation Timing

`manifest.json` is generated as part of the package build process. After `npm run build` completes for a package, `manifest.json` must exist at the package root and be up to date.

### 5.5 Conformance

The static `manifest.json` must satisfy:

- All fields present in the runtime `ModuleManifest` are present in the JSON.
- `configDescriptors` matches the runtime `configDescriptors` export exactly.
- `manifest.json` is valid JSON and parseable without any JavaScript execution.

---

## 6. Module Package Directory Convention

### 6.1 User-Installed Modules

User-installed module packages live in a configurable directory. The default path is:

- **Windows**: `%USERPROFILE%\.craft\modules\`
- **macOS/Linux**: `~/.craft/modules/`

Each module is a subdirectory containing at minimum:

```
~/.craft/modules/
  channel-feishu/
    manifest.json
    package.json
    dist/
      cli.js
      index.js
      ...
  channel-weixin/
    manifest.json
    package.json
    dist/
      ...
```

### 6.2 Discovery Contract

A host discovers modules by:

1. Listing immediate child directories of the module directory.
2. For each child, checking whether `manifest.json` exists.
3. If it exists, reading and parsing it as the module's metadata.
4. Directories without `manifest.json` are silently ignored.

No recursive scanning is performed. Each module occupies exactly one immediate child directory.

### 6.3 Directory Name

The directory name is not semantically significant. The canonical identity is `moduleId` from `manifest.json`. Directory names are expected to match the package short name by convention (e.g. `channel-feishu`) but hosts must not rely on directory names for identity.

---

## 7. Bundled Module Convention for Desktop

### 7.1 Location

Desktop bundles first-party modules inside the Electron application package:

```
resources/
  modules/
    channel-feishu/
      manifest.json
      package.json
      dist/
    channel-weixin/
      manifest.json
      package.json
      dist/
```

This directory ships with the application and is read-only at runtime.

### 7.2 Discovery Priority

Desktop scans module directories in order:

1. **Bundled**: `resources/modules/` (read-only, ships with Desktop).
2. **User-installed**: configurable directory (default `~/.craft/modules/`).

If a `moduleId` appears in both locations, the user-installed version takes priority. This allows users to override a bundled module with a newer or customized version.

### 7.3 Packaging

Desktop's `electron-builder` configuration must include the `resources/modules/` directory as an extra resource. The build pipeline must copy built adapter packages into this directory before packaging.

---

## 8. Build Pipeline

### 8.1 Per-Package Manifest Generation Script

Each adapter package must have a script (e.g. `generate:manifest`) that:

1. Imports the runtime `manifest` and `configDescriptors` from the built package output.
2. Combines them into a single JSON object.
3. Writes `manifest.json` to the package root.

This script runs after `tsc` compilation and must be part of the `build` script chain.

### 8.2 Root Script

The TypeScript SDK workspace root (`sdk/typescript/package.json`) must have a script that generates manifests for all packages in one command, e.g. `generate:manifests`.

### 8.3 Build Output Verification

After `npm run build` in a package, the following must be true:

- `dist/` contains compiled output (existing requirement).
- `manifest.json` exists at the package root.
- `manifest.json` is valid JSON matching the format in section 5.2.

### 8.4 Pack Inclusion

`manifest.json` must be added to each adapter package's `files` allowlist in `package.json`, so it is included in `npm pack` output.

---

## 9. Constraints and Compatibility

- `manifest.json` generation must not change the runtime behavior of either adapter package.
- The generation script must work on Node.js >= 20 (matching the adapter package engine requirement).
- The `manifest.json` format is a superset of `ModuleManifest` (adding `configDescriptors`). It does not replace the runtime export; both must exist and agree.
- The bundled module directory structure must be compatible with Electron's ASAR packaging or be excluded from ASAR (extra resources are typically unpacked).
- The generation script must not require any dependencies beyond what is already in the workspace.

---

## 10. Acceptance Checklist

- [ ] `@dotcraft/channel-feishu` produces `manifest.json` at the package root after `npm run build`.
- [ ] `@dotcraft/channel-weixin` produces `manifest.json` at the package root after `npm run build`.
- [ ] Both `manifest.json` files contain all `ModuleManifest` fields plus `configDescriptors`.
- [ ] `manifest.json` content matches the runtime `manifest` and `configDescriptors` exports exactly.
- [ ] `manifest.json` is included in each package's `files` allowlist.
- [ ] `manifest.json` is included in `npm pack --dry-run` output for both adapter packages.
- [ ] A root script `generate:manifests` exists and generates manifests for all packages.
- [ ] `npm run build:all` from the workspace root produces both `manifest.json` files.
- [ ] The module directory convention is documented (this spec serves as the reference).
- [ ] The bundled module directory convention for Desktop is documented.
- [ ] `manifest.json` generation does not change any existing test results.

---

## 11. Open Questions

- Should the manifest generation script be a standalone Node script (e.g. `scripts/generate-manifest.js`) or integrated into the TypeScript build via a post-build hook? A standalone script is simpler and more transparent; a post-build hook is more automated. Recommendation: standalone script called from the `build` script chain.
- Should `manifest.json` include the `node_modules/.package-lock.json`-style integrity hash for tamper detection? Not required for this milestone; can be added later if needed.
- Should the bundled modules directory use ASAR packing or be unpacked? Unpacked is recommended since the CLI subprocess needs direct filesystem access to `dist/cli.js`. The `electron-builder` `asarUnpack` pattern or `extraResources` handles this.
