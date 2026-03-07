# DotCraft Hooks Guide

Hooks is DotCraft's lifecycle event system that allows you to automatically run external Shell commands at key points during Agent execution. With Hooks, you can implement security guards, auto-formatting, logging, notifications, and other custom extensions without modifying DotCraft source code.

This design is inspired by the Hooks implementations in Claude Code, Cursor, and other mainstream Agent tools.

---

## Quick Start

Create a `hooks.json` file in your workspace's `.craft` directory:

```json
{
    "hooks": {
        "PostToolUse": [
            {
                "matcher": "WriteFile|EditFile",
                "hooks": [
                    {
                        "type": "command",
                        "command": "echo 'File modified' >> /tmp/dotcraft-hooks.log"
                    }
                ]
            }
        ]
    }
}
```

This configuration will log to a file every time the `WriteFile` or `EditFile` tool completes successfully.

---

## Configuration Files

Hooks supports two-level configuration, consistent with DotCraft's main configuration system:

| Config File | Path | Purpose |
|-------------|------|---------|
| Global config | `~/.craft/hooks.json` | Shared Hooks across all workspaces |
| Workspace config | `<workspace>/.craft/hooks.json` | Hooks specific to the current workspace |

### Configuration Merge Rules

- Global Hooks are loaded first
- Workspace Hooks are **appended** after global Hooks (for the same event)
- Workspace config does not override or remove global config — they are additive

### Enable/Disable

Hooks are enabled by default. You can disable them in `config.json`:

```json
{
    "Hooks": {
        "Enabled": false
    }
}
```

---

## Configuration Format

```json
{
    "hooks": {
        "<EventName>": [
            {
                "matcher": "<regex>",
                "hooks": [
                    {
                        "type": "command",
                        "command": "<shell command>",
                        "timeout": 30
                    }
                ]
            }
        ]
    }
}
```

### Field Reference

| Field | Type | Description |
|-------|------|-------------|
| `hooks` | object | Top-level field, grouped by event name |
| `<EventName>` | array | List of matcher groups for this event |
| `matcher` | string | Regex pattern to match tool names. Empty string matches all tools. Only effective for tool-related events |
| `hooks` | array | List of commands to execute when matched |
| `type` | string | Currently only `"command"` (shell command) is supported |
| `command` | string | Shell command to execute. Uses `/bin/bash -c` on Linux/macOS or `powershell.exe` on Windows |
| `timeout` | number | Command timeout in seconds, default `30` |

---

## Lifecycle Events

DotCraft provides 6 lifecycle events:

| Event | Trigger | Can Block? | stdin JSON Fields | Typical Use Case |
|-------|---------|-----------|-------------------|-----------------|
| `SessionStart` | When a session is created or resumed | No | `sessionId` | Load context, initialize environment |
| `PreToolUse` | Before a tool executes | **Yes** (exit 2) | `sessionId`, `toolName`, `toolArgs` | Security review, access control |
| `PostToolUse` | After a tool executes successfully | No | `sessionId`, `toolName`, `toolArgs`, `toolResult` | Auto-formatting, logging |
| `PostToolUseFailure` | After a tool execution fails | No | `sessionId`, `toolName`, `toolArgs`, `error` | Error monitoring, alerting |
| `PrePrompt` | Before user prompt is sent to Agent | **Yes** (exit 2) | `sessionId`, `prompt` | Input validation, content filtering |
| `Stop` | After Agent finishes responding | No | `sessionId` | Test verification, result notification |

---

## Execution Model

### Shell Process

Each Hook command runs as an independent shell process:

- **Linux/macOS**: `/bin/bash -c '<command>'`
- **Windows**: `powershell.exe -NoLogo -NoProfile -NonInteractive -Command <command>`
- **Working directory**: Workspace root

### stdin Input

Hook processes receive JSON context data via **stdin**. JSON fields vary by event type; unused fields are omitted from the JSON.

**PreToolUse example**:

```json
{
    "sessionId": "acp_abc123",
    "toolName": "WriteFile",
    "toolArgs": {
        "filePath": "src/main.cs",
        "content": "// new code"
    }
}
```

**PrePrompt example**:

```json
{
    "sessionId": "acp_abc123",
    "prompt": "Please refactor this function"
}
```

**PostToolUse example**:

```json
{
    "sessionId": "acp_abc123",
    "toolName": "WriteFile",
    "toolArgs": {
        "filePath": "src/main.cs",
        "content": "// new code"
    },
    "toolResult": "Successfully wrote to src/main.cs"
}
```

**PostToolUseFailure example**:

```json
{
    "sessionId": "acp_abc123",
    "toolName": "WriteFile",
    "toolArgs": {
        "filePath": "/etc/passwd",
        "content": "test"
    },
    "error": "Access denied: path is blacklisted"
}
```

### Exit Code Semantics

| Exit Code | Meaning | Behavior |
|-----------|---------|----------|
| `0` | Success | Continue execution |
| `2` | Block | Block the current action (only `PreToolUse` and `PrePrompt`). stderr content becomes the block reason |
| Other | Error | **Fail-Open**: Warning logged, but execution is not blocked |

> **Fail-Open Design**: All non-zero exit codes except 2 will not block the Agent workflow. This ensures that unexpected errors in Hook scripts do not disrupt normal usage.

### Matcher Regex

The `matcher` field uses regular expressions to match tool names:

| Matcher | Effect |
|---------|--------|
| `""` (empty string) | Matches all tools |
| `"WriteFile"` | Matches WriteFile only |
| `"WriteFile\|EditFile"` | Matches WriteFile or EditFile |
| `".*File"` | Matches all tools ending in File |
| `"Exec"` | Matches Exec (shell command execution) |

Regex matching is case-insensitive.

---

## Usage Examples

### Example 1: Auto-format After File Write

Automatically run `dotnet format` after every C# file write or edit:

```json
{
    "hooks": {
        "PostToolUse": [
            {
                "matcher": "WriteFile|EditFile",
                "hooks": [
                    {
                        "type": "command",
                        "command": "dotnet format --include $(cat /dev/stdin | jq -r '.toolArgs.filePath // empty') 2>/dev/null || true",
                        "timeout": 60
                    }
                ]
            }
        ]
    }
}
```

### Example 2: Shell Command Security Guard

Check for dangerous operations before executing shell commands:

```json
{
    "hooks": {
        "PreToolUse": [
            {
                "matcher": "Exec",
                "hooks": [
                    {
                        "type": "command",
                        "command": ".craft/hooks/guard-shell.sh"
                    }
                ]
            }
        ]
    }
}
```

**`.craft/hooks/guard-shell.sh`**:

```bash
#!/bin/bash
# Read JSON input from stdin
INPUT=$(cat)
COMMAND=$(echo "$INPUT" | jq -r '.toolArgs.command // empty')

# Check for dangerous commands
if echo "$COMMAND" | grep -qiE '(rm\s+-rf\s+/|mkfs|dd\s+if=|:(){ :|fork)'; then
    echo "Dangerous command detected: $COMMAND" >&2
    exit 2  # Block execution
fi

exit 0  # Allow execution
```

### Example 3: Tool Call Logging

Log all tool calls to a file:

```json
{
    "hooks": {
        "PreToolUse": [
            {
                "matcher": "",
                "hooks": [
                    {
                        "type": "command",
                        "command": "INPUT=$(cat); echo \"[$(date -Iseconds)] CALL: $(echo $INPUT | jq -r '.toolName') args=$(echo $INPUT | jq -c '.toolArgs')\" >> .craft/tool-calls.log"
                    }
                ]
            }
        ],
        "PostToolUseFailure": [
            {
                "matcher": "",
                "hooks": [
                    {
                        "type": "command",
                        "command": "INPUT=$(cat); echo \"[$(date -Iseconds)] FAIL: $(echo $INPUT | jq -r '.toolName') error=$(echo $INPUT | jq -r '.error')\" >> .craft/tool-calls.log"
                    }
                ]
            }
        ]
    }
}
```

### Example 4: Load Project Context on Session Start

```json
{
    "hooks": {
        "SessionStart": [
            {
                "matcher": "",
                "hooks": [
                    {
                        "type": "command",
                        "command": "echo \"Project: $(basename $(pwd)), Git branch: $(git branch --show-current 2>/dev/null || echo 'N/A')\""
                    }
                ]
            }
        ]
    }
}
```

### Example 5: Send Notification After Agent Completes

```json
{
    "hooks": {
        "Stop": [
            {
                "matcher": "",
                "hooks": [
                    {
                        "type": "command",
                        "command": "curl -s -X POST 'https://hooks.slack.com/services/YOUR/WEBHOOK/URL' -H 'Content-Type: application/json' -d '{\"text\": \"DotCraft Agent has completed the task\"}' > /dev/null 2>&1 || true",
                        "timeout": 10
                    }
                ]
            }
        ]
    }
}
```

### Example 6: Prompt Content Filtering

Block prompts containing sensitive keywords:

```json
{
    "hooks": {
        "PrePrompt": [
            {
                "matcher": "",
                "hooks": [
                    {
                        "type": "command",
                        "command": ".craft/hooks/filter-prompt.sh"
                    }
                ]
            }
        ]
    }
}
```

**`.craft/hooks/filter-prompt.sh`**:

```bash
#!/bin/bash
INPUT=$(cat)
PROMPT=$(echo "$INPUT" | jq -r '.prompt // empty')

# Check for sensitive content
if echo "$PROMPT" | grep -qiE '(password|secret|credential|api.?key)'; then
    echo "Prompt contains sensitive keywords and has been blocked" >&2
    exit 2
fi

exit 0
```

### Example 7: Global Config + Workspace Config

**Global config** (`~/.craft/hooks.json`): Universal security guard for all workspaces

```json
{
    "hooks": {
        "PreToolUse": [
            {
                "matcher": "Exec",
                "hooks": [
                    {
                        "type": "command",
                        "command": "INPUT=$(cat); CMD=$(echo $INPUT | jq -r '.toolArgs.command // empty'); if echo $CMD | grep -qiE 'rm\\s+-rf\\s+/'; then echo 'Blocked: dangerous rm -rf /' >&2; exit 2; fi; exit 0"
                    }
                ]
            }
        ]
    }
}
```

**Workspace config** (`<workspace>/.craft/hooks.json`): Project-specific formatting Hook

```json
{
    "hooks": {
        "PostToolUse": [
            {
                "matcher": "WriteFile|EditFile",
                "hooks": [
                    {
                        "type": "command",
                        "command": "dotnet format --verbosity quiet 2>/dev/null || true"
                    }
                ]
            }
        ]
    }
}
```

In this workspace, `PreToolUse` executes the global security guard, and `PostToolUse` executes the project-specific formatter — both are active simultaneously.

---

## Best Practices for Writing Hook Scripts

1. **Always read stdin**: Even if you don't need the input data, read stdin (e.g., `cat > /dev/null`), otherwise the process may exit abnormally due to broken pipe
2. **Use `jq` for JSON parsing**: We recommend installing `jq` to parse JSON data from stdin
3. **Handle errors**: Append `|| true` at the end of commands to ensure non-critical Hooks don't accidentally return non-zero exit codes
4. **Control timeouts**: Set reasonable `timeout` values for time-consuming operations to avoid blocking Agent execution
5. **Distinguish exit codes**: Only use `exit 2` when you truly need to block an action; use `exit 1` for other errors (handled by Fail-Open)
6. **Use stderr for messages**: When blocking with `exit 2`, write the reason to stderr (`echo "reason" >&2`); this information is fed back to the Agent as the block reason
7. **Avoid interactive commands**: Hooks run in the background and cannot receive interactive user input

---

## FAQ

### What is the Hook execution order?

Hooks within the same event are executed in the order they appear in the config file (global Hooks before workspace Hooks). If any `PreToolUse` or `PrePrompt` Hook returns exit code 2, subsequent Hooks are not executed.

### What happens when a Hook times out?

After timeout, the process is forcefully terminated (including the entire process tree). The Hook returns a timeout error but does not block the action (Fail-Open).

### How do I debug Hooks?

1. Write logs to a file in your Hook command: `echo "debug info" >> /tmp/hook-debug.log`
2. Check stderr output (DotCraft logs Hook stderr)
3. Test Hook scripts manually: `echo '{"toolName":"WriteFile","toolArgs":{}}' | bash .craft/hooks/your-hook.sh`

### Can Hooks modify tool arguments?

Not currently. Hook stdout is not used to modify tool arguments or execution results. Hooks can only perform "observe" and "block" operations.

### What are the built-in tool names in DotCraft?

Common tool names (for `matcher` configuration):

| Tool Name | Description |
|-----------|-------------|
| `Exec` | Execute shell commands |
| `ReadFile` | Read a file (lists directory contents when path is a directory) |
| `WriteFile` | Write a file |
| `EditFile` | Edit a file (partial replacement) |
| `GrepFiles` | Search file contents |
| `FindFiles` | Find files |
| `WebFetch` | Fetch web page content |
| `WebSearch` | Search the web |
| `SpawnSubagent` | Spawn a subagent for background tasks |

> Actual available tool names depend on enabled tool providers and modules. You can view the currently registered tool list via the DashBoard.
