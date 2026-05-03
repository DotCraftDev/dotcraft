---
name: plugin-creator
description: Create and scaffold DotCraft local plugin directories with `.craft-plugin/plugin.json`, plugin-contained skills, optional assets, and optional process-backed dynamic tools. Use when developing DotCraft plugins or creating a skill-only plugin bundle for `.craft/plugins` or `~/.craft/plugins`.
---

# Plugin Creator

Use this skill when the user wants to create, scaffold, or maintain a DotCraft plugin directory.

## Quick Start

Default to a workspace-local plugin under `<workspace>/.craft/plugins/<plugin-id>`:

```powershell
python .craft/skills/plugin-creator/scripts/create_basic_plugin.py "My Plugin"
```

If reading the skill from the source tree instead of a deployed workspace skill, use the source-tree script path from the repo root:

```powershell
python src/DotCraft.Core/Skills/BuiltIn/plugin-creator/scripts/create_basic_plugin.py "My Plugin"
```

Use `--path` when the user asks for another parent directory, such as a user-global plugin container:

```powershell
python .craft/skills/plugin-creator/scripts/create_basic_plugin.py "My Plugin" --path "$HOME/.craft/plugins"
```

## Defaults

- Normalize plugin ids to lowercase hyphen-case, max 64 characters.
- Create `<parent>/<plugin-id>/.craft-plugin/plugin.json`.
- Create a skill-only plugin by default with `skills: "./skills/"`.
- Create `skills/<skill-name>/SKILL.md`; `--skill-name` defaults to the plugin id.
- Add `--with-assets` to create plugin-level icon/logo placeholders.
- Add `--with-process-tool` to create a process-backed dynamic tool scaffold using `tools` + `processes`.

## Manifest Rules

DotCraft schema version `1` allows a plugin to contribute skills, dynamic tools, or both.

- Skill-only plugins are valid when `skills` points to a plugin-contained skills directory.
- Dynamic tools should be declared with `tools`.
- Process-backed tools must use `backend.kind = "process"` and reference a `processes` entry.
- MCP servers are configured separately through `McpServers`; do not add MCP server declarations to plugin manifests.
- Manifest-relative paths must start with `./`, stay inside the plugin root, and never contain `..`.

For exact examples, read `references/plugin-json-spec.md`.

## External Process Tool Template

Use this when creating a plugin with a local stdio process dynamic tool:

```powershell
python .craft/skills/plugin-creator/scripts/create_basic_plugin.py external-process-echo --with-process-tool --tool-name EchoText
```

After generation, replace placeholder descriptions and edit `tools/demo_tool.py` with the tool behavior. The generated Python process speaks JSON-RPC 2.0 over stdio and handles `plugin/initialize` plus `plugin/toolCall`.

## Validation

After scaffolding:

1. Inspect `.craft-plugin/plugin.json` and replace TODO placeholders.
2. Confirm every manifest-relative path starts with `./`.
3. If the plugin has skills, confirm each child skill has `SKILL.md`.
4. If the plugin has a process-backed tool, run the process script locally or install the plugin and invoke the tool from chat.
5. Run relevant DotCraft tests when changing the runtime, or start DotCraft and confirm `plugin/list` and `skills/list` show the plugin.
