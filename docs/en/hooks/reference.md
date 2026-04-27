# DotCraft Hooks Reference

This page collects Hook events, configuration fields, stdin input, exit-code semantics, and common script examples. For the beginner path, read [Hooks Guide](../hooks_guide.md).

## Configuration Format

```json
{
  "Hooks": {
    "Enabled": true,
    "Events": {
      "BeforeToolCall": [
        {
          "name": "guard-shell",
          "type": "command",
          "command": "node .craft/hooks/guard-shell.js",
          "matcher": "Exec",
          "timeoutSeconds": 10
        }
      ]
    }
  }
}
```

| Field | Description |
|-------|-------------|
| `name` | Hook name |
| `type` | Command type, supports `"command"` |
| `command` | Shell command |
| `matcher` | Tool-name regex. Empty matches all tool-related events |
| `timeoutSeconds` | Timeout |

## Lifecycle Events

| Event | Purpose |
|-------|---------|
| `BeforeToolCall` | Check or block before tool calls |
| `AfterToolCall` | Log, format, or notify after tool calls |
| `BeforeTurn` | Prepare context before an Agent turn |
| `AfterTurn` | Record results or notify after an Agent turn |
| `BeforeAutomationTask` | Check before an automation task runs |
| `AfterAutomationTask` | Sync state after an automation task completes |

## stdin Input

Hook processes receive JSON through stdin. Fields vary by event type, and unused fields are omitted.

Tool-related events usually include:

```json
{
  "event": "BeforeToolCall",
  "workspace": "F:\\project",
  "sessionId": "thread-id",
  "toolName": "Exec",
  "arguments": {
    "command": "dotnet test"
  }
}
```

Turn-related events usually include:

```json
{
  "event": "AfterTurn",
  "workspace": "F:\\project",
  "sessionId": "thread-id",
  "summary": "Agent completed the turn"
}
```

## Exit Code Semantics

| Exit code | Meaning |
|-----------|---------|
| `0` | Success, continue |
| Non-zero | Hook failed; `Before*` events can block the current operation |

Hook stdout is used for logs and error messages. It does not modify tool arguments or tool results.

## Examples

### Auto-format After File Writes

```json
{
  "name": "format-after-edit",
  "type": "command",
  "command": "npm run format -- --write",
  "matcher": "WriteFile|EditFile",
  "timeoutSeconds": 60
}
```

### Shell Command Security Guard

```js
let input = ''
process.stdin.on('data', chunk => (input += chunk))
process.stdin.on('end', () => {
  const payload = JSON.parse(input)
  const command = payload.arguments?.command ?? ''
  if (/rm\s+-rf|Remove-Item\s+-Recurse/i.test(command)) {
    console.error('Dangerous command blocked by workspace Hook.')
    process.exit(1)
  }
})
```

### Tool Call Logging

```js
import fs from 'node:fs'

let input = ''
process.stdin.on('data', chunk => (input += chunk))
process.stdin.on('end', () => {
  const payload = JSON.parse(input)
  fs.appendFileSync('.craft/tool-calls.log', JSON.stringify({
    event: payload.event,
    toolName: payload.toolName,
    at: new Date().toISOString()
  }) + '\n')
})
```

## Debugging

- Start with an observing Hook to inspect the stdin payload.
- Write raw input to `.craft/hooks-debug.log` while debugging.
- Use workspace-relative command paths to avoid cwd differences between entry points.
- Add `|| true` to non-critical Hooks so temporary external-service failures do not block the Agent.
