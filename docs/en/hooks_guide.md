# DotCraft Hooks Guide

Hooks let DotCraft run external commands during lifecycle events. They are useful for formatting, auditing, notifications, environment checks, and security guards. Start with an observing Hook, then add blocking Hooks when the behavior is clear.

## Quick Start

Add this to `.craft/config.json`:

```json
{
  "Hooks": {
    "Enabled": true,
    "Events": {
      "AfterToolCall": [
        {
          "name": "log-tool-call",
          "type": "command",
          "command": "node .craft/hooks/log-tool-call.js",
          "matcher": ".*",
          "timeoutSeconds": 10
        }
      ]
    }
  }
}
```

Example script that reads JSON from stdin and records the tool call:

```js
import fs from 'node:fs'

let input = ''
process.stdin.on('data', chunk => (input += chunk))
process.stdin.on('end', () => {
  fs.appendFileSync('.craft/hooks.log', input + '\n')
})
```

## Configuration

Hooks support global and workspace configuration. Workspace config overrides or extends global config, which makes it the right place for project-specific formatting, audit, and notification rules.

| Field | Description |
|-------|-------------|
| `Hooks.Enabled` | Enables Hooks |
| `Hooks.Events` | Hook lists grouped by event name |
| `name` | Hook name for logs and troubleshooting |
| `type` | Supports `"command"` |
| `command` | Shell command to run |
| `matcher` | Regex for matching tool names. Empty matches all tool-related events |
| `timeoutSeconds` | Hook timeout |

For all events, stdin payloads, exit-code semantics, and examples, see [Hooks Reference](./hooks/reference.md).

## Usage Examples

| Scenario | Event | Behavior |
|----------|-------|----------|
| Format after file writes | `AfterToolCall` | Match `WriteFile` / `EditFile` and run a formatter |
| Shell command security guard | `BeforeToolCall` | Inspect dangerous commands and return non-zero to block |
| Notify after Agent completion | `AfterTurn` | Send a summary to WeCom, Feishu, or an internal system |
| Audit automation tasks | Automation-related events | Write external audit logs |

## Advanced Topics

### Execution Model

Hook processes receive JSON context through stdin. DotCraft waits for the command to exit and uses the exit code to decide whether the current operation continues:

| Exit code | Meaning |
|-----------|---------|
| `0` | Hook succeeded, continue |
| Non-zero | Hook failed; blocking events interrupt the current operation |

### Matcher

`matcher` is a regular expression and only applies to tool-related events. Common examples:

| matcher | Matches |
|---------|---------|
| `WriteFile|EditFile` | File writes and edits |
| `Exec` | Shell commands |
| `.*` | All tools |

### Best Practices

- Keep Hook scripts small; put complex logic in project scripts.
- For observing Hooks, append `|| true` when failures should not affect the main flow.
- Blocking Hooks should print clear error messages so users know how to fix the issue.
- Do not commit sensitive tokens. Prefer environment variables or global config.

## Troubleshooting

### Hook does not run

Confirm `Hooks.Enabled = true`, the event name is correct, `matcher` matches the current tool name, and the command path works from the current workspace.

### Hook times out

Increase `timeoutSeconds` or move slow work to a background queue. Hooks are best for short tasks, not long-running Agent blockers.

### Can Hooks modify tool arguments?

Hooks cannot modify tool arguments or tool results. They can only observe or block the current operation.
