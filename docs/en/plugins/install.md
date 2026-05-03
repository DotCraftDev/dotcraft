# Install and Use Plugins

DotCraft plugins package reusable workspace capabilities. A plugin can provide:

| Content | Description |
|---------|-------------|
| Dynamic tool | A tool the Agent can call. Plugins can execute tools through plugin-owned local stdio processes. |
| Skill | Workflow instructions distributed with the plugin. Installed and enabled plugins expose these skills in DotCraft's skill list. |
| Metadata | Name, description, developer, category, icon, default prompt, and related links. |

Plugin-contained skills follow the plugin lifecycle. They are available while the plugin is enabled and leave the Agent context when the plugin is disabled or removed.

## Install in Desktop

1. Open DotCraft Desktop.
2. Go to **Plugins**.
3. Search or browse for a plugin, then open its details.
4. Click **Install**.
5. After installation, click **Try in chat**, or start a new conversation and describe the task directly.

If you do not name the plugin, DotCraft can still choose an installed and enabled plugin when it fits the task.

## Enable, Disable, and Remove

DotCraft tracks plugin state in three layers:

| Operation | Meaning |
|-----------|---------|
| Install | Add the plugin to the current workspace capability set. |
| Enable / disable | Keep the plugin files, but control whether the plugin enters the Agent context. |
| Remove | Remove the plugin from the current workspace. For local plugins under `.craft/plugins/<plugin-id>`, this deletes that plugin directory. |

Use the plugin manage page to enable or disable installed plugins in bulk. When a plugin is disabled, its tools and plugin-contained skills are not added to the Agent context.

## Install Local Plugins

For local development and testing, use one of two install paths:

```text
.craft/plugins/<plugin-id>/.craft-plugin/plugin.json
```

Copy the plugin root into `.craft/plugins/<plugin-id>/`, then open the Plugins page and click **Refresh**. Plugins installed this way can be removed from the Desktop plugin detail page; removal deletes that plugin directory.

You can also add a plugin root or plugin container to `Plugins.PluginRoots`:

```json
{
  "Plugins": {
    "PluginRoots": ["./samples/plugins"]
  }
}
```

`Plugins.PluginRoots` is best when DotCraft should read an external plugin directory that you maintain yourself. Desktop does not delete those external directories; remove the root from configuration or manage the files yourself when you no longer want to use it.

## Verify a Plugin

After installing or changing a plugin:

1. Click **Refresh** on the Plugins page.
2. Search for the plugin name or ID.
3. Open the detail page and confirm the tools, skills, and links.
4. If the plugin does not appear, check the diagnostics banner for the manifest path and error details.

## Security and Trust

Installing a plugin adds new tools and skills to the workspace capability set. When a plugin uses the `process` backend, DotCraft can start the local stdio process declared in the plugin manifest to execute dynamic tools. Install and enable only plugins whose source, code, and dependencies you trust.

Plugin tool calls still flow through DotCraft's session, approval, and tool-call records. Website, privacy policy, and terms links in plugin details help you verify the plugin source and behavior boundaries.

## Related Docs

- [Build Plugins](./build.md)
- [Search and Install Skills](../skills/marketplace.md)
- [AppServer Protocol](../reference/appserver-protocol.md)
- [Plugin Architecture Spec](https://github.com/DotHarness/dotcraft/blob/master/specs/plugin-architecture.md)
