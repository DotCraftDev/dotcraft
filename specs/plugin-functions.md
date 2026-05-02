# DotCraft Plugin Function Specification

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Planning |
| **Date** | 2026-05-02 |
| **Related Specs** | [AppServer Protocol](appserver-protocol.md), [External Channel Adapter](external-channel-adapter.md) |

Purpose: Define the long-term Plugin Function model that unifies built-in plugin functions, external-channel tools, and future manifest-discovered plugins under one DotCraft-owned tool pipeline.

---

## Table of Contents

- [1. Current Baseline](#1-current-baseline)
- [2. M3 Goals](#2-m3-goals)
- [3. Plugin Directory and Manifest Discovery](#3-plugin-directory-and-manifest-discovery)
- [4. Loader and Registration Model](#4-loader-and-registration-model)
- [5. Execution Backends](#5-execution-backends)
- [6. Configuration and Diagnostics](#6-configuration-and-diagnostics)
- [7. M3 Acceptance Criteria](#7-m3-acceptance-criteria)
- [8. Deferred Work](#8-deferred-work)

---

## 1. Current Baseline

DotCraft currently exposes Plugin Functions through internal C# providers and thread-scoped providers:

- Built-in plugin functions, such as Desktop Node REPL, are registered by C# providers.
- External-channel tools are declared by channel adapters during `initialize`, converted to Plugin Function descriptors, and executed through the existing `ext/channel/toolCall` backend.
- Runtime conversation projection uses `pluginFunctionCall` items. Plugin-backed tools do not emit companion `toolCall` or `toolResult` items.
- Plugin enablement is controlled by `Plugins.EnabledPlugins` and `Plugins.DisabledPlugins`.

M2 is expected to reduce this baseline before M3 by removing model-visible `NodeReplReset` and deleting the old `externalChannelToolCall` item type.

---

## 2. M3 Goals

M3 should introduce the first formal plugin discovery layer without turning DotCraft into a full plugin marketplace.

Required M3 outcomes:

- Discover local plugin directories from configured and default roots.
- Parse a stable plugin manifest format.
- Attach manifest metadata to Plugin Function registration and diagnostics.
- Support built-in C# plugin providers through the same discovered plugin identity model.
- Keep external-channel runtime tool declaration compatible with the current adapter contract.
- Define extension points for later executable plugin backends without loading arbitrary third-party code in M3.

M3 must preserve the model-facing `AIFunction.Name = descriptor.name` behavior from M1/M2. If multiple enabled plugins provide the same model-visible function name in one agent tool set, the later function is rejected and a diagnostic is recorded.

---

## 3. Plugin Directory and Manifest Discovery

### 3.1 Plugin Root Layout

The preferred DotCraft plugin manifest path is:

```text
<plugin-root>/.craft-plugin/plugin.json
```

The manifest path is DotCraft-specific. Compatibility with Codex's `.codex-plugin/plugin.json` may be considered later, but M3 should not treat it as the primary DotCraft carrier unless explicitly added as a compatibility mode.

### 3.2 Discovery Roots

M3 should discover plugin roots from:

- Workspace-local root: `<workspace>/.craft/plugins`
- User-global root: `<craft-home>/plugins`
- Explicit config roots under `Plugins.PluginRoots`

Explicit roots are additive. Relative explicit roots are resolved against the workspace root. Missing roots are ignored with diagnostics rather than treated as fatal startup errors.

### 3.3 Manifest Shape

The M3 manifest should include, at minimum:

- `schemaVersion`
- `id`
- `version`
- `displayName`
- `description`
- `capabilities`
- `functions`

Function entries should describe Plugin Function metadata but not require M3 to execute arbitrary code:

- `namespace`
- `name`
- `description`
- `inputSchema`
- `outputSchema`
- `display`
- `approval`
- `requiresChatContext`
- `deferLoading`
- `backend`

All manifest-relative paths must start with `./`, must stay inside the plugin root, and must not contain `..`.

---

## 4. Loader and Registration Model

M3 should split plugin loading into three layers:

1. **Manifest parser**: reads and validates `.craft-plugin/plugin.json`, normalizes paths, and returns structured metadata plus diagnostics.
2. **Plugin discovery service**: scans roots, resolves duplicate plugin IDs, applies enable/disable config, and produces enabled plugin records.
3. **Plugin function loader**: binds enabled plugin records to available providers/backends and returns `PluginFunctionRegistration` objects.

Built-in providers remain C# implementations, but their plugin identity should be resolved through the same plugin ID system. For example, `node-repl` remains a built-in provider while being eligible for manifest metadata and config-driven enablement.

External-channel tools remain thread-scoped and runtime-declared. They should keep using plugin IDs such as `external-channel:<channelName>` and should not require a static manifest in M3.

---

## 5. Execution Backends

M3 should implement only safe, already-owned backends:

- `builtin`: binds a manifest function declaration to a known C# provider function.
- `external-channel`: remains runtime-declared by adapter handshake and is not loaded from static manifests.

M3 should not execute plugin-provided binaries, scripts, DLLs, browser code, or arbitrary MCP servers from a manifest. The manifest may reserve backend fields for future backends, but unsupported backend kinds must be skipped with diagnostics.

Future executable backends must define a separate security model before implementation, including process isolation, approval boundaries, secret access, lifecycle ownership, and update/install trust.

---

## 6. Configuration and Diagnostics

M3 should extend `Plugins` config with:

- `PluginRoots`: optional list of additional plugin root directories.
- Existing `EnabledPlugins`: explicit plugin IDs to enable.
- Existing `DisabledPlugins`: explicit plugin IDs to disable; disabled entries override enabled/default entries.

Diagnostics should be available to logs and future UI surfaces. They should cover:

- Invalid manifest JSON.
- Missing required fields.
- Invalid plugin ID or function name.
- Invalid JSON schema.
- Unsupported backend kind.
- Duplicate plugin ID.
- Duplicate model-visible function name.
- Disabled plugin skipped.

Discovery failures for one plugin must not prevent other plugins from loading.

---

## 7. M3 Acceptance Criteria

M3 is complete when:

- DotCraft can discover valid local plugin manifests from workspace, user-global, and explicit roots.
- Invalid manifests produce diagnostics without aborting startup.
- Built-in C# plugin functions can be associated with manifest metadata and controlled by plugin enable/disable config.
- External-channel tools continue to work without static manifests.
- Duplicate model-visible function names are rejected deterministically with diagnostics.
- Plugin Function runtime still emits only `pluginFunctionCall` conversation items for plugin-backed tools.

Required tests:

- Manifest parser accepts valid manifests and rejects invalid path escapes.
- Discovery applies root precedence and reports duplicate IDs.
- Config enable/disable precedence matches existing `Plugins` semantics.
- Built-in provider binding registers expected functions only when the plugin is enabled and runtime capabilities are available.
- Unsupported backend kinds are skipped with diagnostics.
- Existing external-channel Plugin Function tests continue to pass.

---

## 8. Deferred Work

The following are intentionally outside M3:

- Plugin marketplace discovery.
- Plugin installation, update, or uninstall flows.
- Remote plugin download.
- Dynamic assembly loading.
- Script or process execution backends.
- Static manifests for external-channel adapter runtime tools.
- UI plugin marketplace or plugin management screens.
- Deferred/lazy Plugin Function loading behavior beyond preserving the descriptor field.
