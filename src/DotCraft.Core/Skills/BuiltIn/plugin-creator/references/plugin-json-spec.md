# DotCraft Plugin JSON Samples

## Skill-only plugin

```json
{
  "schemaVersion": 1,
  "id": "my-plugin",
  "version": "0.1.0",
  "displayName": "My Plugin",
  "description": "Describe what this plugin contributes.",
  "capabilities": ["skill"],
  "skills": "./skills/",
  "interface": {
    "displayName": "My Plugin",
    "shortDescription": "One-line user-facing summary.",
    "longDescription": "A concise description for plugin detail views.",
    "developerName": "DotCraft",
    "category": "Coding",
    "capabilities": ["Skill"],
    "defaultPrompt": "Use my plugin.",
    "brandColor": "#2563EB"
  }
}
```

## Plugin with an external process dynamic tool

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
      "namespace": "external_process_echo",
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
  },
  "interface": {
    "displayName": "External Process Echo",
    "shortDescription": "Run an echo tool in a plugin process.",
    "longDescription": "A sample plugin that contributes a skill and a process-backed dynamic tool.",
    "developerName": "DotCraft",
    "category": "Coding",
    "capabilities": ["Skill", "Tool"],
    "defaultPrompt": "Echo text through the external process plugin.",
    "brandColor": "#2563EB"
  }
}
```

## Approval examples

```json
{
  "approval": {
    "kind": "file",
    "targetArgument": "path",
    "operation": "write",
    "operationArgument": "operation"
  }
}
```

```json
{
  "approval": {
    "kind": "shell",
    "targetArgument": "workingDirectory",
    "operationArgument": "command"
  }
}
```

```json
{
  "approval": {
    "kind": "remoteResource",
    "targetArgument": "url",
    "operation": "fetch"
  }
}
```

## Rules

- `schemaVersion` must be `1`.
- `id` must contain only ASCII letters, digits, `.`, `_`, `-`, or `:`.
- `displayName` is required.
- At least one supported contribution is required: `skills` or one valid `tools` entry.
- `tools` may be omitted for skill-only plugins.
- Existing manifests that use `functions` are still accepted for compatibility, but new manifests should use `tools`.
- Process-backed tools use `backend.kind = "process"` and must reference a top-level `processes` entry.
- MCP servers are configured separately through `McpServers`, not plugin manifests.
- Manifest paths must start with `./`, must not contain `..`, and must stay inside the plugin root.
