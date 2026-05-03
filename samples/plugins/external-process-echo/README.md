# External Process Echo Plugin

This sample plugin contributes:

- A plugin-contained skill: `external-process-echo`
- A process-backed dynamic tool: `EchoText`
- A Python JSON-RPC stdio process: `tools/demo_tool.py`

## Install

Copy the sample plugin root into the current workspace:

```powershell
New-Item -ItemType Directory -Force .craft/plugins
Copy-Item -Recurse samples/plugins/external-process-echo .craft/plugins/external-process-echo
```

The manifest must be at:

```text
.craft/plugins/external-process-echo/.craft-plugin/plugin.json
```

Or keep the sample in place and add its parent to `Plugins.PluginRoots`:

```json
{
  "Plugins": {
    "PluginRoots": ["./samples/plugins"]
  }
}
```

Use either copy-based install or `Plugins.PluginRoots`; you do not need both. Refresh the Plugins page after copying or changing plugin roots.

Plugins copied into `.craft/plugins/<plugin-id>` can be removed from the Desktop plugin detail page. Plugins loaded through `Plugins.PluginRoots` are managed by that external directory and are not deleted by Desktop.

## Verify in Plugins

Open the Plugins page and click **Refresh**. The plugin should appear under **Installed locally**. If it does not appear, search for `external-process-echo` and check the diagnostics banner for manifest errors.

## Try It

Start a chat in the workspace and ask:

```text
Use External Process Echo to echo "hello from plugin tools".
```

DotCraft launches the configured Python process, sends `plugin/initialize`, and then dispatches `EchoText` through `plugin/toolCall`.

## Notes

- Python must be available as `python` on `PATH`.
- MCP servers are configured through `McpServers`, not this plugin manifest.
- The process receives `workspaceRoot` and `pluginRoot` in every request.
