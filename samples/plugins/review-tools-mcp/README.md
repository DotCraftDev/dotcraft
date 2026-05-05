# Review Tools MCP Plugin Sample

This sample shows the current DotCraft plugin shape:

- plugin-contained skills under `skills/`
- plugin-bundled MCP configuration in `.mcp.json`
- optional interface metadata in `.craft-plugin/plugin.json`

Plugin manifests no longer declare native `tools`, `functions`, or `processes`. Reusable executable capabilities should be exposed through MCP. Thread-scoped client callbacks should use AppServer Runtime Dynamic Tools.

## Try it

Copy this folder into a workspace plugin root:

```powershell
Copy-Item -Recurse samples/plugins/review-tools-mcp .craft/plugins/review-tools-mcp
```

Then edit `.mcp.json` so the `review` server points at a real MCP server command or HTTPS endpoint.
