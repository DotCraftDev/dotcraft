# DotCraft Plugin Architecture Specification

| Field | Value |
|-------|-------|
| **Version** | 1.1.0 |
| **Status** | Living |
| **Date** | 2026-05-03 |
| **Related Specs** | [AppServer Protocol](appserver-protocol.md), [Session Core](session-core.md), [External Channel Adapter](external-channel-adapter.md), [Desktop Client](desktop-client.md) |

Purpose: define the durable architecture for DotCraft plugins, including plugin-contained skills, plugin dynamic tools, local plugin manifests, process-backed tool execution, built-in tool bindings, and the TypeScript external channel module contract.

---

## 1. Architecture Overview

DotCraft plugins are host-integrated capabilities that can contribute skills and model-callable dynamic tools without requiring the agent pipeline to know each integration's implementation details.

The v1 plugin contribution model is:

1. **Skills**: plugin-contained DotCraft/Codex-compatible `SKILL.md` directories.
2. **Tools**: static manifest-declared DotCraft dynamic tools. They are conceptually aligned with Codex dynamic tools and may execute through a plugin-owned local stdio process.

The architecture has three layers:

1. **Plugin metadata** describes identity, display information, capabilities, configuration, skill directories, tool declarations, and process declarations.
2. **Plugin loading and binding** discovers enabled plugins, validates metadata, binds tool declarations to owned runtimes, and produces runtime registrations.
3. **Runtime execution** validates arguments, applies approval routing, invokes the bound runtime, and projects results into Session Core and AppServer events.

Plugin-backed model tools are exposed as Microsoft.Extensions.AI `AIFunction` instances, but DotCraft owns the descriptor, execution lifecycle, result mapping, diagnostics, and conversation projection.

MCP is intentionally outside plugin manifests. MCP servers continue to use the separate `McpServers` configuration source and runtime.

---

## 2. Dynamic Tool Model

Plugin tools are model-callable dynamic tools provided by a plugin.

Each tool has:

- `pluginId`: stable plugin identity, such as `external-process-echo`.
- `namespace`: optional namespace.
- `name`: flat model-visible tool name exported as `AIFunction.Name`.
- `description`: model-facing tool description.
- `inputSchema`: JSON Schema for arguments.
- `outputSchema`: optional JSON Schema for structured output.
- `display`: optional client-facing display metadata.
- `approval`: optional server-owned approval target metadata.
- `requiresChatContext`: whether execution requires originating channel chat context.
- `deferLoading`: reserved for lazy-loading behavior.
- `backend`: execution binding for the tool.

The model-facing name remains flat for Microsoft.Extensions.AI compatibility. If multiple enabled tools in one agent tool set use the same model-visible name, DotCraft keeps the first registration and rejects later registrations with diagnostics.

Dynamic tool invocations continue to be represented by Session Core `pluginFunctionCall` items for protocol compatibility. Plugin-backed tools do not emit companion `toolCall` or `toolResult` items. AppServer serializes this payload with camelCase fields such as `pluginId`, `namespace`, `functionName`, `callId`, `arguments`, `contentItems`, `structuredResult`, `success`, `errorCode`, and `errorMessage`.

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
- `interface`
- `skills`
- `tools`
- `processes`

Plugins must declare at least one supported contribution: a plugin-contained `skills` path, one or more `tools`, or both. Skill-only plugins are valid and do not register model-callable tools.

`tools` is the user-facing manifest field for plugin dynamic tools. DotCraft also accepts the legacy `functions` field for existing manifests and normalizes it into the same internal descriptor model.

Example process-backed dynamic tool:

```json
{
  "schemaVersion": 1,
  "id": "external-process-echo",
  "version": "0.1.0",
  "displayName": "External Process Echo",
  "description": "Echo text through a plugin-owned local process.",
  "capabilities": ["skill", "tool"],
  "skills": "./skills/",
  "tools": [
    {
      "namespace": "demo",
      "name": "EchoText",
      "description": "Echo text through an external plugin process.",
      "inputSchema": {
        "type": "object",
        "properties": {
          "text": { "type": "string" }
        },
        "required": ["text"]
      },
      "backend": {
        "kind": "process",
        "processId": "demo",
        "toolName": "EchoText"
      }
    }
  ],
  "processes": {
    "demo": {
      "command": "python",
      "args": ["./tools/demo_tool.py"],
      "workingDirectory": "./",
      "toolTimeoutSeconds": 20
    }
  }
}
```

`interface` contains optional UI metadata for Desktop and other clients: display name, short and long descriptions, developer, category, capability tags, default prompt, brand color, icon/logo paths, and public website/privacy/terms links. Path fields inside `interface` use the same manifest-relative path rules.

`skills` points to a plugin-contained skill directory, for example `"./skills/"`. Each child directory can contain a DotCraft/Codex-compatible `SKILL.md`. Skills contributed by enabled plugins are available in `skills/list` with source `plugin` and include `pluginId` / `pluginDisplayName` attribution. Disabling the plugin removes its contributed skills from agent context and hides compatibility built-in copies owned by that plugin.

DotCraft discovers plugin roots from:

1. Workspace-local root: `<workspace>/.craft/plugins`
2. Explicit roots in `Plugins.PluginRoots` order
3. User-global root: `<craft-home>/plugins`

Explicit roots may point either to one plugin root containing `.craft-plugin/plugin.json` or to a container directory containing multiple plugin roots. Missing roots are skipped with diagnostics. Local manifest plugins are enabled by default; `Plugins.DisabledPlugins` disables a plugin even when it is discovered from a default or explicit root.

When multiple roots contain the same plugin id, higher-priority roots win and lower-priority duplicates are skipped with diagnostics.

---

## 4. Manifest Path Rules

Manifest-relative paths must:

- Start with `./`.
- Not be absolute paths.
- Not contain `..`.
- Resolve to a path that stays inside the plugin root.

These rules apply to `skills`, interface asset paths, process `workingDirectory`, and process command or argument entries that start with `./`.

---

## 5. Loading, Binding, and Diagnostics

Plugin loading has three responsibilities:

1. The manifest parser reads `.craft-plugin/plugin.json`, validates fields, normalizes paths, and returns metadata plus diagnostics.
2. The discovery service scans roots, resolves duplicate plugin ids, applies plugin enablement config, and produces enabled plugin records.
3. The tool loader binds enabled plugin tool declarations to available runtimes and returns runtime registrations.

Diagnostics are non-fatal and available to logs and future UI surfaces. They cover invalid JSON, missing fields, missing supported plugin capabilities, invalid ids or names, invalid schemas, unsupported backends, invalid or missing process declarations, escaping manifest-relative paths, duplicate plugin ids, duplicate model-visible tool names, disabled plugins, and missing roots.

Discovery or binding failures for one plugin must not prevent other plugins from loading.

---

## 6. Backend Model

### Process Backend

The `process` backend binds a manifest tool to a plugin-declared local stdio process:

```json
{
  "kind": "process",
  "processId": "demo",
  "toolName": "EchoText"
}
```

`processId` must refer to a top-level `processes` entry. `toolName` is optional and defaults to the manifest tool `name`.

DotCraft starts one stdio process per enabled plugin process declaration and workspace context. The process is initialized once and can serve multiple tool calls for the tools bound to that process.

Process declarations support:

- `command`: executable name or `./` plugin-relative executable path.
- `args`: command arguments. Entries that start with `./` resolve under the plugin root.
- `workingDirectory`: optional `./` plugin-relative working directory.
- `env`: optional environment variables.
- `startupTimeoutSeconds`: optional initialize timeout.
- `toolTimeoutSeconds`: optional per-tool-call timeout.

v1 supports local stdio processes only. The manifest declares static tool metadata; the process executes those tools but does not dynamically declare additional tools.

### Process Dynamic Tool Protocol

Transport: JSON-RPC 2.0 over stdio.

Startup request:

```json
{
  "jsonrpc": "2.0",
  "id": "1",
  "method": "plugin/initialize",
  "params": {
    "pluginId": "external-process-echo",
    "pluginRoot": "...",
    "workspaceRoot": "...",
    "tools": []
  }
}
```

Tool call request:

```json
{
  "jsonrpc": "2.0",
  "id": "2",
  "method": "plugin/toolCall",
  "params": {
    "callId": "...",
    "threadId": "...",
    "turnId": "...",
    "tool": "EchoText",
    "namespace": "demo",
    "arguments": {},
    "workspaceRoot": "...",
    "pluginRoot": "..."
  }
}
```

Tool responses are JSON-RPC results with:

- `success`
- `contentItems`
- `structuredResult`
- `errorCode`
- `errorMessage`

JSON-RPC errors and malformed responses are mapped to failed plugin tool results.

### Built-In Backend

The `builtin` backend binds a manifest declaration to a known C# provider function owned by DotCraft:

```json
{
  "kind": "builtin",
  "providerId": "browser-use",
  "functionName": "NodeReplJs"
}
```

For `builtin` backends, `providerId` must match the manifest `id`. This backend is for DotCraft-owned built-in plugins, not the normal extension path for user plugins.

Built-in plugin manifests are embedded resources exposed through a built-in catalog. Catalog entries are visible to clients before installation, but they are not active until installed into workspace `.craft/plugins/<pluginId>`.

Installed built-ins carry a `.builtin` marker:

- `plugin/install` creates the managed workspace directory from embedded resources and enables the plugin by default.
- Directories with `.builtin` are owned by DotCraft and can be updated or removed by DotCraft lifecycle operations.
- Directories without `.builtin` are treated as user-owned and are not overwritten or removed by DotCraft.

`plugin/remove` deletes only managed built-in directories that still carry the `.builtin` marker. Removing a plugin is distinct from disabling it: removed built-ins are absent from runtime discovery but remain visible in the installable catalog, while disabled installed plugins remain on disk and can be re-enabled.

---

## 7. Built-In and External Tool Sources

### Browser Use Built-In Plugin

DotCraft ships Browser Use as the built-in plugin `browser-use`. It contributes:

- `NodeReplJs`, backed by the Desktop thread-bound Node REPL C# provider.
- The `browser-use` skill, loaded from the plugin's `skills` directory.

The legacy plugin id `node-repl` is accepted only as a configuration alias for disabling Browser Use. New manifests, diagnostics, and configuration writes use `browser-use`.

### External Channel Tools

External channel tools are runtime-declared by channel adapters during AppServer `initialize`. DotCraft converts validated channel tool descriptors into thread-scoped plugin tool registrations using plugin ids such as `external-channel:<channelName>`.

Static plugin manifests are not required for external-channel runtime tools. Execution continues to use the `ext/channel/toolCall` server-to-client request defined by [External Channel Adapter](external-channel-adapter.md).

### MCP Tools

MCP tools are configured through `McpServers`, discovered by the MCP runtime, and injected through the MCP tool path. MCP servers are not declared in plugin manifests and are not plugin tool backends.

---

## 8. TypeScript External Channel Modules

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

## 9. Configuration

The `Plugins` config section contains:

- `PluginRoots`: additional local plugin roots or plugin container directories. Relative paths resolve against the workspace root.
- `EnabledPlugins`: plugin ids explicitly enabled for the workspace.
- `DisabledPlugins`: plugin ids explicitly disabled for the workspace. Disabled entries override enabled/default entries.

Installed built-in plugins and local manifest plugins are enabled by default unless disabled. Built-ins that are visible only through the catalog are installable but not enabled and do not contribute tools or skills to agent context.

`node-repl` in `DisabledPlugins` is treated as a legacy alias for `browser-use`; new writes normalize to `browser-use`.

MCP configuration remains outside `Plugins` and continues to use `McpServers`.

---

## 10. Protocol Boundaries

- AppServer JSON-RPC methods and capability negotiation are defined in [AppServer Protocol](appserver-protocol.md).
- Session item payloads, including `pluginFunctionCall`, are defined in [Session Core](session-core.md).
- External channel adapter handshake, delivery, and `ext/channel/*` requests are defined in [External Channel Adapter](external-channel-adapter.md).
- Desktop user-facing module workflows are defined in [Desktop Client](desktop-client.md).
