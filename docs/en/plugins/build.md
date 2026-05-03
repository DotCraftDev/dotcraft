# Build Plugins

DotCraft plugins distribute reusable capabilities. The fastest path is to ask the built-in `$plugin-creator` to scaffold the plugin first, then tailor the generated descriptions, tool behavior, and verification notes for your use case.

If you are only adding a personal workflow for the current project, create a regular skill first. Build a plugin when you want to distribute skills, dynamic tools, icons, and install-page metadata together.

## Start With Plugin Creator

Describe the plugin you want directly in chat:

```text
$plugin-creator create a plugin named External Process Echo with one skill and one external process tool.
```

You can include the runtime, language, and verification goal:

```text
$plugin-creator create a local plugin that provides an EchoText dynamic tool through a Python process and includes install verification notes.
```

`plugin-creator` generates the DotCraft plugin directory, `.craft-plugin/plugin.json`, a plugin-contained skill, and optional process-backed dynamic tool scaffolding. After generation, you usually only need to:

1. Replace TODOs and sample copy.
2. Implement or adjust the tool process behavior.
3. Install the plugin locally and refresh the Plugins page to verify it.

## DotCraft Plugin Structure

DotCraft uses `.craft-plugin/plugin.json` as the plugin entry point:

```text
my-plugin/
  .craft-plugin/
    plugin.json
  skills/
    my-skill/
      SKILL.md
  tools/
    tool_process.py
```

DotCraft v1 supports two contribution types:

| Content | Description |
|---------|-------------|
| `skills` | DotCraft/Codex-compatible skills distributed with the plugin. |
| `tools` | DotCraft dynamic tools the Agent can call, optionally executed by a local stdio process. |

## Understand the Minimal Manifest

You usually do not need to write the full manifest by hand. For troubleshooting or advanced customization, know the basic shape:

```json
{
  "schemaVersion": 1,
  "id": "my-plugin",
  "version": "0.1.0",
  "displayName": "My Plugin",
  "description": "Package reusable DotCraft capabilities.",
  "skills": "./skills/",
  "interface": {
    "displayName": "My Plugin",
    "shortDescription": "Reusable workflow for DotCraft",
    "developerName": "Your team",
    "category": "Coding"
  }
}
```

Plugins with external-process dynamic tools also include `tools` and `processes`. Let `plugin-creator` generate that structure, then edit it for your tool.

## Verify Locally

1. Put the plugin under `.craft/plugins/<plugin-id>/`, or add the plugin directory to `Plugins.PluginRoots`.
2. Open the Desktop Plugins page and click **Refresh**.
3. Open the plugin detail page and confirm the tools and plugin-contained skills.
4. Describe the capability in chat, or click **Try in chat**.
5. If the plugin does not appear, check the diagnostics banner on the Plugins page.

## When to Read the Reference

Read the full references when you need advanced details such as:

- the JSON-RPC protocol for process-backed dynamic tools
- `approval` metadata for tools
- manifest path rules
- complete fields and schema

References:

- `src/DotCraft.Core/Skills/BuiltIn/plugin-creator/references/plugin-json-spec.md`
- [Plugin Architecture Spec](https://github.com/DotHarness/dotcraft/blob/master/specs/plugin-architecture.md)

## Related Docs

- [Install and Use Plugins](./install.md)
- [Search and Install Skills](../skills/marketplace.md)
- [AppServer Protocol](../reference/appserver-protocol.md)
