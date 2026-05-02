# DotCraft Plugin Architecture Specification

| Field | Value |
|-------|-------|
| **Version** | 1.0.0 |
| **Status** | Living |
| **Date** | 2026-05-02 |
| **Related Specs** | [AppServer Protocol](appserver-protocol.md), [Session Core](session-core.md), [External Channel Adapter](external-channel-adapter.md), [Desktop Client](desktop-client.md) |

Purpose: define the durable architecture for DotCraft plugins, including Plugin Functions, local plugin manifests, built-in backends, and the TypeScript external channel module contract.

---

## 1. Architecture Overview

DotCraft plugins are host-integrated capabilities that can contribute model-callable functions, external channel integrations, configuration surfaces, or runtime adapter behavior without requiring the agent pipeline to know each integration's implementation details.

The architecture has three layers:

1. **Plugin metadata** describes identity, display information, capabilities, configuration, and function declarations.
2. **Plugin loading and binding** discovers enabled plugins, validates metadata, binds declarations to owned backends, and produces runtime registrations.
3. **Runtime execution** invokes a bound backend and projects results into Session Core and AppServer events.

Plugin-backed model tools are exposed as Microsoft.Extensions.AI `AIFunction` instances, but DotCraft owns the descriptor, execution lifecycle, result mapping, diagnostics, and conversation projection.

---

## 2. Plugin Function Model

Plugin Functions are model-callable functions provided by a plugin.

Each Plugin Function has:

- `pluginId`: stable plugin identity, such as `node-repl` or `external-channel:telegram`.
- `namespace`: optional DotCraft-internal namespace.
- `name`: flat model-visible function name exported as `AIFunction.Name`.
- `description`: model-facing function description.
- `inputSchema`: JSON Schema for arguments.
- `outputSchema`: optional JSON Schema for structured output.
- `display`: optional client-facing display metadata.
- `approval`: optional server-owned approval target metadata.
- `requiresChatContext`: whether execution requires originating channel chat context.
- `deferLoading`: reserved for future lazy-loading behavior.

The model-facing name remains flat for Microsoft.Extensions.AI compatibility. If multiple enabled functions in one agent tool set use the same model-visible name, DotCraft keeps the first registration and rejects later registrations with diagnostics.

Plugin Function invocations are represented by Session Core `pluginFunctionCall` items. Plugin-backed tools do not emit companion `toolCall` or `toolResult` items. AppServer serializes this payload with camelCase fields such as `pluginId`, `namespace`, `functionName`, `callId`, `arguments`, `contentItems`, `structuredResult`, `success`, `errorCode`, and `errorMessage`.

---

## 3. Local Plugin Manifest

Local plugins use this manifest path:

```text
<plugin-root>/.craft-plugin/plugin.json
```

The supported manifest schema version is `1`.

Manifest metadata includes:

- `schemaVersion`
- `id`
- `version`
- `displayName`
- `description`
- `capabilities`
- `functions`

Function entries describe Plugin Function metadata and a backend binding. Manifest-relative paths must start with `./`, must remain inside the plugin root, and must not contain `..`.

DotCraft discovers plugin roots from:

1. Workspace-local root: `<workspace>/.craft/plugins`
2. Explicit roots in `Plugins.PluginRoots` order
3. User-global root: `<craft-home>/plugins`

Explicit roots may point either to one plugin root containing `.craft-plugin/plugin.json` or to a container directory containing multiple plugin roots. Missing roots are skipped with diagnostics. Local manifest plugins are enabled by default; `Plugins.DisabledPlugins` disables a plugin even when it is discovered from a default or explicit root.

When multiple roots contain the same plugin id, higher-priority roots win and lower-priority duplicates are skipped with diagnostics.

---

## 4. Loading, Binding, and Diagnostics

Plugin loading has three responsibilities:

1. The manifest parser reads `.craft-plugin/plugin.json`, validates fields, normalizes paths, and returns metadata plus diagnostics.
2. The discovery service scans roots, resolves duplicate plugin ids, applies plugin enablement config, and produces enabled plugin records.
3. The function loader binds enabled plugin function declarations to available backends and returns `PluginFunctionRegistration` objects.

Diagnostics are non-fatal and available to logs and future UI surfaces. They cover invalid JSON, missing fields, invalid ids or names, invalid schemas, unsupported backends, duplicate plugin ids, duplicate model-visible function names, disabled plugins, and missing roots.

Discovery or binding failures for one plugin must not prevent other plugins from loading.

---

## 5. Backend Model

DotCraft supports only owned backends unless a future spec defines a separate security model.

### Built-In Backend

The `builtin` backend binds a manifest function declaration to a known C# provider function owned by DotCraft:

```json
{
  "kind": "builtin",
  "providerId": "node-repl",
  "functionName": "NodeReplJs"
}
```

For `builtin` backends, `providerId` must match the manifest `id`. This prevents a local plugin from declaring a different built-in provider identity.

Built-in plugin manifests are embedded resources deployed to workspace `.craft/plugins/<pluginId>`. Deployed built-ins carry a `.builtin` marker:

- Missing directories are created from the embedded built-in manifest.
- Directories with `.builtin` are updated when the marker version is stale.
- Directories without `.builtin` are treated as user-owned and are not overwritten.

If built-in manifest deployment or discovery fails, a built-in C# provider may keep using a fallback descriptor so the runtime capability remains available.

### External Channel Backend

External channel tools are runtime-declared by channel adapters during AppServer `initialize`. DotCraft converts validated channel tool descriptors into thread-scoped Plugin Functions using plugin ids such as `external-channel:<channelName>`.

Static manifests are not required for external-channel runtime tools. Execution continues to use the `ext/channel/toolCall` server-to-client request defined by [External Channel Adapter](external-channel-adapter.md).

### Deferred Backends

Plugin-provided binaries, scripts, DLLs, browser code, arbitrary MCP servers, remote downloads, marketplace installation, and dynamic assembly loading are outside the current architecture until a security model defines isolation, approval boundaries, secret access, lifecycle ownership, and update trust.

---

## 6. TypeScript External Channel Modules

A TypeScript external channel module is the SDK-facing unit that represents one external channel integration variant, such as a first-party Feishu module or an enterprise Feishu module.

Hosts integrate modules through a stable module contract rather than package-internal source layout. A module owns platform protocol integration, platform-specific configuration, lifecycle behavior, runtime tool registration, and tool execution. The host owns discovery, workspace context, configuration storage, launcher lifecycle, and user-visible enablement.

The module contract defines:

- **Module identity**: stable `moduleId`, channel family, display metadata, variant semantics, and capability summary.
- **Manifest carrier**: a module-root SDK export that exposes host-readable module metadata.
- **Entry contract**: a documented startup entry that receives workspace context and returns a structured startup outcome.
- **Workspace context**: workspace path, `.craft` path, config path, state path, temp path, and AppServer connection information.
- **Configuration contract**: workspace-scoped configuration stored under `.craft/<configFileName>`, with module-owned validation and host-visible descriptors.
- **State and temp layout**: module-owned persistent state and temporary runtime files scoped to the active workspace.
- **Lifecycle contract**: structured statuses, errors, diagnostics, interactive setup needs, and restart requirements.
- **Capability and tool registration**: manifest-level capability summaries plus runtime channel tool descriptors declared during AppServer `initialize`.

Desktop may expose discoverable channel modules in the Channels workflow, but listing modules must not require executing module business logic. Bundled and user-installed modules can coexist; user-installed content wins when both provide the same `moduleId`.

---

## 7. Configuration

The `Plugins` config section contains:

- `PluginRoots`: additional local plugin roots or plugin container directories. Relative paths resolve against the workspace root.
- `EnabledPlugins`: plugin ids explicitly enabled for the workspace.
- `DisabledPlugins`: plugin ids explicitly disabled for the workspace. Disabled entries override enabled/default entries.

Built-in plugins can define their own default enablement policy. Local manifest plugins are enabled by default unless disabled.

---

## 8. Protocol Boundaries

- AppServer JSON-RPC methods and capability negotiation are defined in [AppServer Protocol](appserver-protocol.md).
- Session item payloads, including `pluginFunctionCall`, are defined in [Session Core](session-core.md).
- External channel adapter handshake, delivery, and `ext/channel/*` requests are defined in [External Channel Adapter](external-channel-adapter.md).
- Desktop user-facing module workflows are defined in [Desktop Client](desktop-client.md).

