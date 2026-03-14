# DotCraft Configuration Guide

This document describes DotCraft's configuration system, including global configuration, workspace configuration, and security settings.

## Configuration File Locations

DotCraft supports two levels of configuration: **Global configuration** and **Workspace configuration**.

| Config File | Path | Purpose |
|-------------|------|---------|
| Global config | `~/.craft/config.json` | Default API Key, model, and other global settings |
| Workspace config | `<workspace>/.craft/config.json` | Workspace-specific override configuration |

### Configuration Merge Rules

- **Global config as baseline**: Provides default values
- **Workspace config overrides global config**: Values set in workspace take higher priority
- Items not set in workspace retain global config values

This design allows sensitive information like API Keys to be placed in global config, avoiding leaks to the workspace (e.g., Git repositories).

### Usage Example

**Global config** (`~/.craft/config.json`): Store default API Key and model

```json
{
    "ApiKey": "sk-your-default-api-key",
    "Model": "gpt-4o-mini",
    "EndPoint": "https://api.openai.com/v1"
}
```

**Workspace config** (`<workspace>/.craft/config.json`): Override model and feature settings without repeating API Key

```json
{
    "Model": "deepseek-chat",
    "EndPoint": "https://api.deepseek.com/v1",
    "QQBot": {
        "Enabled": true,
        "Port": 6700,
        "AdminUsers": [123456789]
    }
}
```

In this case, DotCraft will use the `ApiKey` from global config, but the `Model`, `EndPoint`, and `QQBot` settings from workspace config.

---

## Visual Configuration via Dashboard

In addition to editing JSON files directly, DotCraft's built-in Dashboard provides a **visual Settings page** for a safer and more convenient configuration experience.

The Dashboard starts automatically at `http://127.0.0.1:8080/dashboard`. Open that URL and click **Settings** in the left navigation bar.

### Page Layout

- **Left panel (Global config)**: Read-only view of `~/.craft/config.json`; sensitive fields (ApiKey, passwords, etc.) are always masked
- **Right panel (Workspace config)**: Editable form for `.craft/config.json`, overriding global values per field; leave a field empty to inherit the global value
- **Bottom preview**: Live merged config view with source annotations (global or workspace) for each field
- **Save button**: Writes the workspace config to disk; restart DotCraft to apply the changes

The Dashboard's Settings form structure (field types, descriptions, allowed values, etc.) is **automatically generated** from C# config class metadata on the server side, so it always stays in sync with the code without any manual maintenance.

### Security Notes

- Sensitive fields such as ApiKey and passwords are always displayed as `***` and are never transmitted in plain text
- If a sensitive field is left unchanged (still shown as `***`), the save operation will preserve the existing value from disk rather than overwriting it
- A field is only updated when you explicitly enter a new value

> After saving, you must **restart DotCraft manually** for the changes to take effect.

---

## Basic Configuration

| Config Item | Description | Default |
|-------------|-------------|---------|
| `ApiKey` | LLM API Key (OpenAI-compatible format) | empty |
| `Model` | Model name to use | `gpt-4o-mini` |
| `EndPoint` | API endpoint address | `https://api.openai.com/v1` |
| `Language` | UI language (`Chinese` / `English`) | `Chinese` |
| `MaxToolCallRounds` | Main Agent max tool call rounds | `100` |
| `SubagentMaxToolCallRounds` | SubAgent max tool call rounds | `50` |
| `SubagentMaxConcurrency` | Maximum concurrent SubAgents (excess requests are queued) | `3` |
| `MaxSessionQueueSize` | Maximum queued requests per session. Oldest waiting request is evicted with a notification when exceeded. `0` = unlimited | `3` |
| `CompactSessions` | Whether to compress sessions when saving (remove tool call messages) | `true` |
| `MaxContextTokens` | Cumulative input token threshold for triggering automatic context compaction. `0` disables compaction | `160000` |
| `MemoryWindow` | Message count threshold that triggers memory consolidation. `0` disables | `50` |
| `ConsolidationModel` | Dedicated model for memory consolidation. Uses the main `Model` when empty. If the main model doesn't support `tool_choice` in thinking mode, specify a non-thinking model here | empty |
| `DebugMode` | Debug mode: tool call arguments are not truncated in console output | `false` |
| `EnabledTools` | Global list of enabled tool names. Enables all tools when empty | `[]` |

DotCraft's base identity is built in. If you want to customize the system prompt, prefer `.craft/AGENTS.md`, `.craft/SOUL.md`, and other bootstrap files instead of maintaining a separate field in `config.json`.

### Reasoning Configuration

Controls the Reasoning/Thinking behavior of the LLM provider. Providers or models that do not support these settings will ignore them.

| Config Item | Description | Default |
|-------------|-------------|---------|
| `Reasoning.Enabled` | Whether to request reasoning support from the provider | `true` |
| `Reasoning.Effort` | Reasoning depth: `None` / `Low` / `Medium` / `High` / `ExtraHigh` | `Medium` |
| `Reasoning.Output` | Whether reasoning content is exposed in the response: `None` / `Summary` / `Full` | `Full` |

---

## Security Configuration

### File Access Blacklist

Configure forbidden paths via `Security.BlacklistedPaths`. The blacklist is **globally effective**, blocking access in both CLI mode and QQ Bot mode.

```json
{
    "Security": {
        "BlacklistedPaths": [
            "~/.ssh",
            "/etc/shadow",
            "/etc/passwd",
            "C:\\Windows\\System32"
        ]
    }
}
```

#### Blacklist Behavior

- **File operations**: `ReadFile`, `WriteFile`, `EditFile`, `GrepFiles`,`FindFiles` operations on blacklisted paths are directly rejected
- **Shell commands**: Shell commands referencing blacklisted paths are rejected
- **Priority**: Blacklist check takes priority over workspace boundary check; even paths within the workspace will be blocked if blacklisted
- **Path matching**: Supports absolute paths and `~` expansion; checks whether a path is a sub-path of a blacklisted path

#### Recommended Blacklist Configuration

```json
{
    "Security": {
        "BlacklistedPaths": [
            "~/.ssh",
            "~/.gnupg",
            "~/.aws",
            "/etc/shadow",
            "/etc/sudoers"
        ]
    }
}
```

### Shell Command Path Detection & Workspace Boundary

DotCraft performs **cross-platform path static analysis** (`ShellCommandInspector`) on Shell command strings before execution, covering the following path forms:

**Unix Paths**
- Absolute paths: `/etc/passwd`, `/var/log/syslog`
- Home directory paths: `~/.ssh/config`
- Environment variable home directory: `$HOME/.config`, `${HOME}/.gitconfig`
- Safe device whitelist (does not trigger detection): `/dev/null`, `/dev/stdout`, etc.

**Windows Paths**
- Drive letter absolute paths: `C:\`, `D:\Users\Aki\file.txt`
- Environment variable paths: `%USERPROFILE%\Documents`, `%APPDATA%\config`
- UNC paths: `\\server\share\file`
- Safe device whitelist: `NUL`, `CON`, `PRN`, `AUX`

**File Tool Path Resolution**

`FileTools` also expands `~`, `$HOME`, `${HOME}`, and `%ENV%` variables to actual paths when resolving file paths, ensuring workspace boundary checks work for all path forms.

**Trigger Rules**

If any path referenced in a command resolves to outside the workspace:
- When `Tools.Shell.RequireApprovalOutsideWorkspace = false`: Execution is directly rejected, with the detected path list provided
- When `Tools.Shell.RequireApprovalOutsideWorkspace = true`: An approval request is sent to the current interaction source (console/QQ), execution proceeds only after approval

Note: Even if the working directory (cwd) is within the workspace, if the command string contains paths outside the workspace, the above rules still apply.

Examples:
- `ls /etc` -> Triggers (Unix absolute path, outside workspace)
- `dir C:\` -> Triggers (Windows drive path, outside workspace)
- `cat ~/.ssh/id_rsa` -> Triggers (home directory path + recommended to add to blacklist)
- `type %USERPROFILE%\Desktop\secret.txt` -> Triggers (Windows environment variable path)
- `grep foo ${HOME}/.bashrc` -> Triggers (Unix environment variable path)
- `ls ./src` -> Normal execution within workspace
- `echo test > /dev/null` -> Safe device whitelist, does not trigger

### Tool Security Configuration

| Config Item | Description | Default |
|-------------|-------------|---------|
| `Tools.File.RequireApprovalOutsideWorkspace` | Whether file operations outside workspace require approval (`false` = direct reject) | `true` |
| `Tools.File.MaxFileSize` | Maximum readable file size (bytes) | `10485760` (10MB) |
| `Tools.Shell.RequireApprovalOutsideWorkspace` | Whether Shell commands outside workspace require approval (`false` = direct reject) | `true` |
| `Tools.Shell.Timeout` | Shell command timeout (seconds) | `300` |
| `Tools.Shell.MaxOutputLength` | Shell command max output length (characters) | `10000` |
| `Tools.Web.MaxChars` | Web scraping max characters | `50000` |
| `Tools.Web.Timeout` | Web request timeout (seconds) | `300` |
| `Tools.Web.SearchMaxResults` | Web search default result count (1-10) | `5` |
| `Tools.Web.SearchProvider` | Search engine provider: `Bing` (default, globally available), `Exa` (AI-optimized, free MCP interface) | `Exa` |

### Sandbox Mode (OpenSandbox)

DotCraft supports using [OpenSandbox](https://github.com/alibaba/OpenSandbox) to migrate Shell and File tool execution from the host machine to isolated Docker containers. When enabled, all Agent command execution and file operations happen inside containers with zero risk to the host.

**Prerequisites**:
1. Docker running on the host or a remote server
2. Install and start OpenSandbox Server: `pip install opensandbox-server && opensandbox-server`
3. Set `Tools.Sandbox.Enabled = true` in config and specify the server address

#### Sandbox Configuration

| Config Item | Description | Default |
|-------------|-------------|---------|
| `Tools.Sandbox.Enabled` | Enable sandbox mode | `false` |
| `Tools.Sandbox.Domain` | OpenSandbox server address (host:port) | `localhost:5880` |
| `Tools.Sandbox.ApiKey` | OpenSandbox API Key (optional, depends on server config) | empty |
| `Tools.Sandbox.UseHttps` | Use HTTPS connection | `false` |
| `Tools.Sandbox.Image` | Docker image for sandbox containers | `ubuntu:latest` |
| `Tools.Sandbox.TimeoutSeconds` | Sandbox TTL in seconds (server-side auto-termination) | `600` |
| `Tools.Sandbox.Cpu` | Container CPU limit | `1` |
| `Tools.Sandbox.Memory` | Container memory limit | `512Mi` |
| `Tools.Sandbox.NetworkPolicy` | Network policy: `deny` (block all egress), `allow` (no restrictions), `custom` (custom rules) | `allow` |
| `Tools.Sandbox.AllowedEgressDomains` | Domains allowed for outbound access (only when `NetworkPolicy = custom`) | `[]` |
| `Tools.Sandbox.IdleTimeoutSeconds` | Idle timeout in seconds before auto-destroying unused sandbox. Set to 0 to disable | `300` |
| `Tools.Sandbox.SyncWorkspace` | Sync host workspace into sandbox `/workspace` on creation | `true` |
| `Tools.Sandbox.SyncExclude` | Relative paths to exclude from workspace sync (prefix matching, see below) | see defaults below |

#### Sandbox Configuration Examples

**Minimal config** (local OpenSandbox server, defaults):

```json
{
    "Tools": {
        "Sandbox": {
            "Enabled": true,
            "Domain": "localhost:8080"
        }
    }
}
```

**Production config** (remote server + custom network policy + resource limits):

```json
{
    "Tools": {
        "Sandbox": {
            "Enabled": true,
            "Domain": "sandbox.example.com:443",
            "ApiKey": "your-sandbox-api-key",
            "UseHttps": true,
            "Image": "node:20-slim",
            "TimeoutSeconds": 1800,
            "Cpu": "2",
            "Memory": "1Gi",
            "NetworkPolicy": "custom",
            "AllowedEgressDomains": ["pypi.org", "registry.npmjs.org", "github.com"],
            "IdleTimeoutSeconds": 600,
            "SyncWorkspace": true,
            "SyncExclude": [
                ".craft/config.json",
                ".craft/sessions",
                ".craft/memory",
                ".craft/dashboard",
                ".craft/security",
                ".craft/logs",
                ".craft/plans"
            ]
        }
    }
}
```

#### SyncExclude Matching Rules

Each entry in `SyncExclude` is a forward-slash relative path prefix from the workspace root:

- Exact file path: `.craft/config.json` → skips only that file
- Directory prefix: `.craft/sessions` → skips the entire directory and all its contents
- Matching is case-insensitive (Windows compatible)

> **Security note**: The defaults cover all sensitive DotCraft runtime directories. If you need to customize, extend the list rather than replacing it entirely.

#### How It Works

When sandbox is enabled:

1. **Tool replacement**: `CoreToolProvider` no longer provides local `ShellTools` / `FileTools`; `SandboxToolProvider` provides containerized equivalents. Tool names remain identical (`Exec`, `ReadFile`, `WriteFile`, `EditFile`, `GrepFiles`, `FindFiles`), so system prompts require no changes
2. **Lifecycle management**: Each Agent session automatically creates and reuses a sandbox container, destroyed after idle timeout
3. **Workspace sync**: When `SyncWorkspace = true`, host workspace files are pushed to the container's `/workspace` directory on creation. Paths listed in `SyncExclude` are skipped; the defaults exclude all sensitive `.craft/` runtime data (API keys, conversation history, memory, traces, approval records, logs, and plans) to prevent host secrets from leaking into the container
4. **SubAgent isolation**: SubAgents also execute inside the sandbox, sharing the same container instance

#### Security Model Comparison

| Dimension | Local Mode | Sandbox Mode |
|-----------|-----------|-------------|
| Command execution | Host machine + regex filtering | Isolated container |
| File access | Workspace boundary check | Container filesystem isolation |
| Network access | Unrestricted | NetworkPolicy controlled |
| Resource usage | Unrestricted | CPU/Memory hard limits |
| Bypass risk | Regex may be bypassed | Container escape (extremely low probability) |
| Human approval | Every out-of-workspace operation | Only when writing back to host |
| Performance overhead | None | Container startup ~2-5s |

#### Backward Compatibility

When `Tools.Sandbox.Enabled = false` (the default), behavior is identical to before — all tools execute locally on the host machine.

---

## QQ Bot Configuration

For detailed QQ Bot configuration, see [QQ Bot Guide](./qq_bot_guide.md).

Quick reference:

| Config Item | Description | Default |
|-------------|-------------|---------|
| `QQBot.Enabled` | Enable QQ bot mode | `false` |
| `QQBot.Host` | WebSocket listen address | `127.0.0.1` |
| `QQBot.Port` | WebSocket listen port | `6700` |
| `QQBot.AccessToken` | Auth token (must match NapCat) | empty |
| `QQBot.AdminUsers` | Admin QQ number list | `[]` |
| `QQBot.WhitelistedUsers` | Whitelisted user QQ number list | `[]` |
| `QQBot.WhitelistedGroups` | Whitelisted group number list | `[]` |
| `QQBot.ApprovalTimeoutSeconds` | Operation approval timeout (seconds) | `60` |

---

## WeCom Bot Configuration

For detailed WeCom Bot configuration, see [WeCom Guide](./wecom_guide.md).

Quick reference:

| Config Item | Description | Default |
|-------------|-------------|---------|
| `WeComBot.Enabled` | Enable WeCom bot mode | `false` |
| `WeComBot.Host` | HTTP service listen address | `0.0.0.0` |
| `WeComBot.Port` | HTTP service listen port | `9000` |
| `WeComBot.AdminUsers` | Admin user ID list (WeCom UserId) | `[]` |
| `WeComBot.WhitelistedUsers` | Whitelisted user ID list (WeCom UserId) | `[]` |
| `WeComBot.WhitelistedChats` | Whitelisted chat ID list (WeCom ChatId) | `[]` |
| `WeComBot.ApprovalTimeoutSeconds` | Operation approval timeout (seconds) | `60` |
| `WeComBot.Robots` | Bot configuration list (Path/Token/AesKey) | `[]` |

**Note**: By default (`Gateway.Enabled = false`), QQ Bot, WeCom Bot, and API mode cannot be enabled simultaneously. Priority order: QQ Bot > WeCom Bot > API > CLI. To run multiple channels at the same time, enable [Gateway mode](#gateway-multi-channel-concurrent-mode).

**Permission Notes**:
- `AdminUsers`: Has all permissions; workspace write operations require approval
- `WhitelistedUsers`: Can only perform read operations (file reading, Web search, etc.)
- `WhitelistedChats`: All users in that chat automatically get whitelisted permissions
- Users not in any of the above lists cannot use Agent features

**Approval Mechanism**:
- When admins perform workspace write operations, they receive an approval request in the WeCom chat
- Reply "approve" or "allow" to approve the operation; reply "approve all" to skip future approval for similar operations in the session
- Reply "reject" or don't reply (timeout) to reject the operation
- Approval timeout can be configured via `ApprovalTimeoutSeconds`

---

## Session Compaction

`CompactSessions` controls whether tool call-related messages are automatically removed when saving sessions, to reduce session file size and avoid redundant data on load.

**Compaction behavior**:
- Removes all `role: tool` messages (tool return results)
- Removes `FunctionCallContent` from assistant messages (tool call instructions)
- If an assistant message has no other content after removing FunctionCallContent, the entire message is removed
- Preserves user messages and assistant text replies

**When to disable**: If you need to retain the complete tool call history in session files (e.g., for debugging), set `"CompactSessions": false`.

---

## Token Usage Statistics

DotCraft automatically extracts token usage information from LLM responses and displays it.

- **CLI mode**: Shows `Tokens: X in / Y out / Z total` after each reply in the console
- **QQ mode**: Appends `[Tokens: X in / Y out / Z total]` at the end of each reply

No additional configuration needed. Token statistics depend on the LLM provider returning `UsageContent` in streaming responses; some providers may not support this.

---

## Heartbeat Service

Heartbeat periodically reads the `HEARTBEAT.md` file from the .craft directory. If there is executable content, it is automatically submitted to the Agent for processing. Suitable for periodic inspections, status monitoring, etc.

| Config Item | Description | Default |
|-------------|-------------|---------|
| `Heartbeat.Enabled` | Enable heartbeat service | `false` |
| `Heartbeat.IntervalSeconds` | Check interval (seconds) | `1800` (30 minutes) |
| `Heartbeat.NotifyAdmin` | In QQ mode, whether to privately notify admins with results | `false` |

### Heartbeat Configuration Example

```json
{
    "Heartbeat": {
        "Enabled": true,
        "IntervalSeconds": 1800,
        "NotifyAdmin": false
    }
}
```

**Console output**: During Heartbeat execution, tool calls and results are output to the console in the format `[Heartbeat] tool_name args` / `[Heartbeat] Result: ...`, convenient for debugging.

**Admin notification**: In QQ mode with `"NotifyAdmin": true`, Heartbeat results are automatically sent as private messages to all `AdminUsers`.

### Heartbeat Usage

1. Create a `HEARTBEAT.md` file in the .craft directory
2. Write tasks to be executed periodically:

```markdown
# Heartbeat Tasks

## Active Tasks
- Check if the project has new GitHub issues and summarize
- Check log files for anomalies
```

3. Start DotCraft (auto-runs in QQ mode; manually trigger in CLI mode via `/heartbeat trigger`)
4. The Agent will automatically execute tasks based on HEARTBEAT.md content. If the file is empty or contains only titles/comments, it will be skipped
5. Heartbeat has independent Session management but shares long-term memory with the main Agent.

### Manual Trigger

- **CLI mode**: Enter `/heartbeat trigger`
- **QQ mode**: Send `/heartbeat trigger`

---

## WeCom Integration

DotCraft provides two WeCom integration capabilities:

| Capability | Config Section | Description |
|-----------|----------------|-------------|
| WeCom Push | `WeCom` | Send notifications to WeCom groups via group bot Webhook |
| WeCom Bot | `WeComBot` | Run as an independent mode, receive and respond to WeCom messages |

### Quick Configuration

**WeCom Push** (Webhook notifications):

```json
{
    "WeCom": {
        "Enabled": true,
        "WebhookUrl": "https://qyapi.weixin.qq.com/cgi-bin/webhook/send?key=YOUR_KEY"
    }
}
```

**WeCom Bot mode** (bidirectional interaction):

```json
{
    "WeComBot": {
        "Enabled": true,
        "Port": 9000,
        "Host": "0.0.0.0",
        "AdminUsers": ["zhangsan", "lisi"],
        "WhitelistedUsers": ["wangwu"],
        "WhitelistedChats": ["wrxxxxxxxx"],
        "ApprovalTimeoutSeconds": 60,
        "Robots": [
            {
                "Path": "/dotcraft",
                "Token": "your_token",
                "AesKey": "your_aeskey"
            }
        ]
    }
}
```

For detailed configuration, usage, deployment guide, and troubleshooting, see [WeCom Guide](./wecom_guide.md).

---

## GitHubTracker Configuration

The GitHubTracker module automatically polls GitHub issues, creates an isolated workspace for each issue, dispatches an agent to complete the coding task, and converges the flow by calling `CompleteIssue` when the work is done.

For the complete usage flow, see the [GitHubTracker Guide](./github_tracker_guide.md).

### Quick Configuration

```json
{
    "GitHubTracker": {
        "Enabled": true,
        "IssuesWorkflowPath": "WORKFLOW.md",
        "Tracker": {
            "Repository": "your-org/your-repo",
            "ApiKey": "$GITHUB_TOKEN",
            "GitHubStateLabelPrefix": "status:",
            "AssigneeFilter": ""
        }
    }
}
```

### Key Fields

| Config Item | Description | Default |
|-------------|-------------|---------|
| `GitHubTracker.Enabled` | Enable the GitHubTracker module | `false` |
| `GitHubTracker.IssuesWorkflowPath` | Path to the issue `WORKFLOW.md`, resolved relative to the workspace root | `WORKFLOW.md` |
| `GitHubTracker.Tracker.Repository` | GitHub repository in `owner/repo` format | empty |
| `GitHubTracker.Tracker.ApiKey` | GitHub token, supports `$ENV_VAR` indirection | empty |
| `GitHubTracker.Tracker.GitHubStateLabelPrefix` | Label prefix used to infer issue state | `status:` |
| `GitHubTracker.Tracker.AssigneeFilter` | Only process issues assigned to a specific user | empty |

## API Mode Configuration

API mode exposes DotCraft as an OpenAI-compatible HTTP service, allowing external applications to call it directly using standard OpenAI SDKs. Based on the [Microsoft.Agents.AI.Hosting.OpenAI](https://github.com/microsoft/agent-framework) official framework.

For detailed usage guide, see [API Mode Guide](./api_guide.md).

Quick reference:

| Config Item | Description | Default |
|-------------|-------------|---------|
| `Api.Enabled` | Enable API mode | `false` |
| `Api.Host` | HTTP service listen address | `127.0.0.1` |
| `Api.Port` | HTTP service listen port | `8080` |
| `Api.ApiKey` | API access key (Bearer Token), no verification when empty | empty |
| `Api.AutoApprove` | Whether to auto-approve all file/Shell operations (overridden by ApprovalMode) | `true` |
| `Api.ApprovalMode` | Approval mode: `auto`/`reject`/`interactive` | empty |
| `Api.ApprovalTimeoutSeconds` | Interactive mode approval timeout (seconds) | `120` |

### API Mode Configuration Example

**Basic config** (all tools enabled, no authentication):

```json
{
    "Api": {
        "Enabled": true,
        "Port": 8080,
        "AutoApprove": true
    }
}
```

**Search tools only** (for search service scenarios):

```json
{
    "EnabledTools": ["WebSearch", "WebFetch"],
    "Api": {
        "Enabled": true,
        "Port": 8080,
        "ApiKey": "your-api-access-key",
        "AutoApprove": false
    }
}
```

### Tool Filtering

The root-level `EnabledTools` field controls globally available tools. Enables all tools when set to an empty array or not set. This applies to all runtime modes (CLI, API, QQ Bot, etc.).

Available built-in tool names: `spawn_subagent`, `ReadFile`, `WriteFile`, `EditFile`, `GrepFiles`, `FindFiles`, `Exec`, `WebSearch`, `WebFetch`, `Cron`, `WeComNotify`.

### Authentication

When `Api.ApiKey` is configured with a non-empty value, all API requests must carry a Bearer Token:

```
Authorization: Bearer your-api-access-key
```

### Approval Mechanism

API mode supports three approval modes via `ApprovalMode` (overrides `AutoApprove` when set):

- **`auto`**: All file operations and Shell commands auto-approved (equivalent to `AutoApprove: true`)
- **`reject`**: All file operations and Shell commands auto-rejected (equivalent to `AutoApprove: false`)
- **`interactive`**: Human-in-the-Loop mode, sensitive operations pause waiting for API client approval via `/v1/approvals` endpoint

`ApprovalTimeoutSeconds` controls the approval timeout in interactive mode (default 120 seconds); auto-rejected if not approved within the timeout.

For detailed explanation and Python examples, see [API Mode Guide](./api_guide.md#human-in-the-loop-interactive-approval).

---

## AG-UI Mode Configuration

AG-UI mode exposes Agent capabilities via the [AG-UI Protocol](https://github.com/ag-ui-protocol/ag-ui) as an SSE streaming endpoint, compatible with CopilotKit and other AG-UI clients.

For detailed usage, see the [AG-UI Mode Guide](./agui_guide.md).

Quick reference:

| Config Item | Description | Default |
|-------------|-------------|---------|
| `AgUi.Enabled` | Enable AG-UI server | `false` |
| `AgUi.Host` | HTTP service listen address | `127.0.0.1` |
| `AgUi.Port` | HTTP service listen port | `5100` |
| `AgUi.Path` | SSE endpoint path | `/ag-ui` |
| `AgUi.RequireAuth` | Require Bearer token authentication | `false` |
| `AgUi.ApiKey` | Bearer token value (required when `RequireAuth` is `true`) | empty |
| `AgUi.ApprovalMode` | Tool approval mode: `interactive` (frontend approval) / `auto` (auto-approve) | `interactive` |

---

## ACP Mode Configuration

ACP ([Agent Client Protocol](https://agentclientprotocol.com/)) mode allows DotCraft to integrate directly with code editors/IDEs (such as Cursor, VS Code, and other compatible editors) as an AI coding agent. The editor communicates with DotCraft via stdio (standard input/output) using the JSON-RPC 2.0 protocol, similar to how LSP (Language Server Protocol) works.

| Config Item | Description | Default |
|-------------|-------------|---------|
| `Acp.Enabled` | Enable ACP mode | `false` |

### Enabling ACP Mode

```json
{
    "Acp": {
        "Enabled": true
    }
}
```

### How It Works

When enabled, DotCraft communicates via stdin/stdout JSON-RPC messages:

1. **Editor launches DotCraft process**: The editor starts DotCraft as a subprocess and communicates via stdio
2. **Initialization handshake**: Both sides exchange protocol versions and capability declarations
3. **Session management**: The editor creates a session; DotCraft broadcasts available slash commands and config options
4. **Prompt interaction**: The editor sends user messages; DotCraft streams back replies, tool call statuses, and results
5. **Permission requests**: Before executing sensitive operations, DotCraft requests user authorization through the protocol

### Supported ACP Protocol Features

| Feature | Description |
|---------|-------------|
| `initialize` | Protocol version negotiation and capability exchange |
| `session/new` | Create a new session |
| `session/load` | Load an existing session and replay history |
| `session/list` | List all ACP sessions |
| `session/prompt` | Send a prompt and receive streaming replies |
| `session/update` | Agent pushes message chunks, tool call statuses, etc. to the editor |
| `session/cancel` | Cancel an ongoing operation |
| `requestPermission` | Agent requests execution permission from the editor |
| `fs/readTextFile` | Read files through the editor (including unsaved changes) |
| `fs/writeTextFile` | Write files through the editor (with diff preview) |
| `terminal/*` | Create and manage terminals through the editor |
| Slash Commands | Automatically broadcast custom commands to the editor |
| Config Options | Expose selectable configuration (mode, model, etc.) to the editor |

### Relationship with Other Modes

- ACP mode can run as a standalone mode (higher priority than API mode)
- In Gateway mode, ACP can run concurrently with QQ Bot, WeCom Bot, and API
- ACP mode session IDs are prefixed with `acp:`, isolated from other channels

---

## Logging Configuration

| Config Item | Description | Default |
|-------------|-------------|---------|
| `Logging.Enabled` | Enable file logging | `true` |
| `Logging.Console` | Also output to the console (stdout) | `false` |
| `Logging.MinLevel` | Minimum log level: `Trace`/`Debug`/`Information`/`Warning`/`Error`/`Critical` | `Information` |
| `Logging.Directory` | Log directory (relative to `.craft/`) | `logs` |
| `Logging.RetentionDays` | Number of days to retain log files. `0` = keep forever | `7` |

---

## Hooks Configuration

Hooks allow you to run custom scripts when specific events occur. The hooks config file defaults to `.craft/hooks.json`. See the [Hooks Guide](./hooks_guide.md) for details.

| Config Item | Description | Default |
|-------------|-------------|---------|
| `Hooks.Enabled` | Enable the Hooks system | `true` |

---

## Cron Scheduled Task Service

Cron is a scheduled task scheduling system supporting one-time and recurring tasks. Tasks are persisted to JSON files and automatically restored after restart.

| Config Item | Description | Default |
|-------------|-------------|---------|
| `Cron.Enabled` | Enable scheduled tasks | `false` |
| `Cron.StorePath` | Task storage file path (relative to `.craft/`) | `cron/jobs.json` |

### Cron Configuration Example

```json
{
    "Cron": {
        "Enabled": true,
        "StorePath": "cron/jobs.json"
    }
}
```

### Schedule Types

| Type | Description | Parameter |
|------|-------------|-----------|
| `at` | One-time task, auto-deleted after execution at specified time | `AtMs` (Unix millisecond timestamp) |
| `every` | Recurring task, repeats at fixed intervals | `EveryMs` (interval in milliseconds) |

### Delivery Channels

Cron tasks support multiple delivery channels (set `deliver: true` when creating the task):

| `channel` | `to` | Description | Prerequisite |
|-----------|------|-------------|--------------|
| `"qq"` | `"group:<groupId>"` | Deliver to specified QQ group | QQ Bot mode |
| `"qq"` | `"<qqUserId>"` | Deliver to specified QQ private chat | QQ Bot mode |
| `"wecom"` | `"<ChatId>"` | Deliver to specific WeCom group | WeCom Bot mode |
| `"wecom"` | (omit) | Deliver to WeCom (global Webhook) | `WeCom` config enabled |

### Agent Self-Service Task Creation

When Cron is enabled, the Agent can create scheduled tasks via the `Cron` tool. For example, saying "remind me to drink water every hour" in conversation will trigger:

```
Cron(action: "add", message: "Remind user to drink water", everySeconds: 3600, name: "Drink water reminder")
```

### Command Line Management

**CLI mode**:
- `/cron list` - View all tasks
- `/cron remove <jobId>` - Delete a task
- `/cron enable <jobId>` - Enable a task
- `/cron disable <jobId>` - Disable a task

**QQ mode**:
- `/cron list` - View all tasks
- `/cron remove <jobId>` - Delete a task

---

## MCP Service Integration

DotCraft supports connecting external tool services via [Model Context Protocol (MCP)](https://modelcontextprotocol.io/). MCP is an open protocol for standardizing the integration between AI applications and external tools/data sources.

Once configured, tools provided by MCP servers are automatically registered with the Agent and used alongside built-in tools (files, Shell, Web, etc.).

### Configuration

`McpServers` is an array where each element defines an MCP server connection:

| Config Item | Description | Default |
|-------------|-------------|---------|
| `Name` | Server name (for logging and tool tracing) | empty |
| `Enabled` | Whether to enable this server | `true` |
| `Transport` | Transport method: `stdio` (local process) or `http` (HTTP/SSE) | `stdio` |
| `Command` | Start command (stdio only) | empty |
| `Arguments` | Command arguments list (stdio only) | `[]` |
| `EnvironmentVariables` | Environment variables (stdio only) | `{}` |
| `Url` | Server address (http only), e.g., `https://mcp.exa.ai/mcp` | empty |
| `Headers` | Additional HTTP request headers (http only) | `{}` |

### Transport Methods

**HTTP/SSE Transport**: Connect to remote MCP servers, suitable for cloud MCP services (e.g., Exa).

```json
{
    "McpServers": [
        {
            "Name": "exa",
            "Transport": "http",
            "Url": "https://mcp.exa.ai/mcp"
        }
    ]
}
```

**Stdio Transport**: Start a local process and communicate via stdin/stdout, suitable for local MCP servers.

```json
{
    "McpServers": [
        {
            "Name": "filesystem",
            "Transport": "stdio",
            "Command": "npx",
            "Arguments": ["-y", "@modelcontextprotocol/server-filesystem", "/path/to/dir"]
        }
    ]
}
```

### MCP Servers with Authentication

Some MCP servers require API Key authentication, which can be passed via `Headers` (HTTP) or `EnvironmentVariables` (stdio):

```json
{
    "McpServers": [
        {
            "Name": "my-service",
            "Transport": "http",
            "Url": "https://example.com/mcp",
            "Headers": {
                "Authorization": "Bearer your-api-key"
            }
        },
        {
            "Name": "local-tool",
            "Transport": "stdio",
            "Command": "my-mcp-server",
            "EnvironmentVariables": {
                "API_KEY": "your-api-key"
            }
        }
    ]
}
```

### Multi-Server Configuration

Multiple MCP servers can be connected simultaneously; all server tools are merged and registered with the Agent:

```json
{
    "McpServers": [
        {
            "Name": "exa",
            "Transport": "http",
            "Url": "https://mcp.exa.ai/mcp"
        },
        {
            "Name": "github",
            "Transport": "stdio",
            "Command": "npx",
            "Arguments": ["-y", "@modelcontextprotocol/server-github"],
            "EnvironmentVariables": {
                "GITHUB_PERSONAL_ACCESS_TOKEN": "ghp_xxxxx"
            }
        }
    ]
}
```

### Disabling a Single Server

Set `Enabled: false` to temporarily disable an MCP server without deleting the configuration:

```json
{
    "McpServers": [
        {
            "Name": "exa",
            "Enabled": false,
            "Transport": "http",
            "Url": "https://mcp.exa.ai/mcp"
        }
    ]
}
```

### Startup Behavior

- MCP servers connect automatically on application startup; after successful connection, the console outputs the number of discovered tools
- Connection failures do not prevent application startup; failed servers output error logs
- All MCP connections are automatically disconnected on application exit

### Exa Search Migration Note

DotCraft's built-in `Tools.Web.SearchProvider: "Exa"` uses a manual MCP call approach. This can now be replaced via MCP configuration:

1. Add Exa server configuration in `McpServers`
2. Switch `Tools.Web.SearchProvider` to `Bing` or another provider
3. The Agent will get all Exa tools via MCP (not just search), including `WebSearch_exa`, `research_exa`, etc.

### Browser Automation (Playwright MCP)

Microsoft provides an official [`@playwright/mcp`](https://github.com/microsoft/playwright-mcp) MCP server that enables the Agent to control a real browser (Chromium/Firefox/WebKit) for web interaction tasks. It operates on the accessibility tree rather than screenshots — fast, deterministic, and no vision model required.

**Prerequisites**: Node.js 18+, plus install the browser:

```bash
npx playwright install chromium
```

**Configuration** (add to `config.json`):

```json
{
    "McpServers": [
        {
            "Name": "playwright",
            "Transport": "stdio",
            "Command": "npx",
            "Arguments": ["-y", "@playwright/mcp@latest"]
        }
    ]
}
```

Once configured, the Agent automatically gains 22 browser control tools, including: `browser_navigate`, `browser_snapshot`, `browser_click`, `browser_type`, `browser_fill_form`, `browser_take_screenshot`, and more.

### MCP Tool Deferred Loading

> **Experimental**: This feature is still under evaluation and behavior may change in future releases.

When many MCP servers are connected and the total tool count is high, injecting all tool definitions upfront into every LLM call carries significant token overhead and can degrade tool selection accuracy. **Deferred Loading** lets the Agent discover and activate MCP tools on demand rather than loading them all at session start.

When enabled, MCP tools are not sent to the LLM upfront. Instead, the Agent receives a `SearchTools` function it can call to look up tools by keyword. Once found, those tools are immediately activated and available for direct invocation in subsequent calls.

#### How It Works

1. When the Agent needs an external capability, it calls `SearchTools(query: "keyword")`
2. The system performs a fuzzy search across all deferred tools and returns the best matches
3. Matched tools are immediately injected into the next LLM call's tool list
4. The tool list grows monotonically within a session, keeping the prompt prefix stable for cache reuse after the initial discovery

#### Configuration

| Config Item | Description | Default |
|-------------|-------------|---------|
| `Tools.DeferredLoading.Enabled` | Enable deferred loading | `false` |
| `Tools.DeferredLoading.AlwaysLoadedTools` | MCP tool names to always load upfront (high-frequency tools) | `[]` |
| `Tools.DeferredLoading.MaxSearchResults` | Maximum results returned per `SearchTools` call (1–20) | `5` |
| `Tools.DeferredLoading.DeferThreshold` | Skip deferred loading if total MCP tool count is below this value | `10` |

#### Example

```json
{
    "Tools": {
        "DeferredLoading": {
            "Enabled": true,
            "AlwaysLoadedTools": ["github_create_issue", "slack_send_message"],
            "MaxSearchResults": 5,
            "DeferThreshold": 10
        }
    }
}
```

#### Guiding the Agent with a Skill

When deferred loading is active, the system prompt automatically includes the names of connected MCP services and basic `SearchTools` usage instructions. However, for the Agent to reliably know which keywords to search in specific scenarios, it is recommended to write a dedicated Skill under `.craft/skills/` that describes the capabilities and typical usage of each MCP service.

**Example Skill** (`.craft/skills/github-mcp/SKILL.md`):

```markdown
# GitHub MCP Tools

A GitHub MCP server is connected, providing tools of the following types:
- Issue management: search with "github issue"
- Pull Requests: search with "github pull request" or "github pr"
- Repository operations: search with "github repo" or "github repository"
- Code search: search with "github search code"

Always call SearchTools to activate the required tools before using them.
```

A Skill like this, combined with the workspace `AGENTS.md` or the on-demand skill loading mechanism, significantly improves the Agent's accuracy in choosing the right search keywords.

---

## Custom Commands

Custom Commands let you save frequently used prompt templates as Markdown files and invoke them via `/command-name`. DotCraft expands the command file content into a full prompt and passes it to the Agent for processing.

### Command File Locations

| Level | Path | Priority |
|-------|------|----------|
| Workspace | `<workspace>/.craft/commands/` | High (overrides same-name user-level commands) |
| User | `~/.craft/commands/` | Low |

### Command File Format

Each `.md` file corresponds to a command; the filename is the command name (e.g. `code-review.md` → `/code-review`).

Files support YAML frontmatter for metadata:

```markdown
---
description: Review code changes for bugs, security issues, and style
---

Run `git diff` on the current branch, then review all changes for:
1. Correctness and logic errors
2. Security vulnerabilities
3. Edge cases and error handling
4. Code style and readability

$ARGUMENTS
```

### Placeholders

| Placeholder | Description |
|-------------|-------------|
| `$ARGUMENTS` | The full argument string from user input (`/cmd foo bar` → `foo bar`) |
| `$1`, `$2`, ... | Positional arguments split by spaces |

### Subdirectory Namespaces

Commands can be organized in subdirectories, with directory separators mapped to `:`:

```
commands/
├── code-review.md      → /code-review
├── explain.md          → /explain
└── frontend/
    └── component.md    → /frontend:component
```

### Built-in Commands

DotCraft ships with the following built-in commands, automatically deployed to the workspace `commands/` directory on first run:

| Command | Description |
|---------|-------------|
| `/code-review` | Review code changes |
| `/explain` | Explain code in detail |
| `/summarize` | Summarize content concisely |

You can edit these files to customize their behavior, or add new `.md` files to create your own commands.

### CLI Commands

- `/commands` — List all available custom commands

---

## QQ Bot Commands

QQ Bot mode supports the following slash commands (send directly in chat):

| Command | Description |
|---------|-------------|
| `/new` or `/clear` | Clear current session, start new conversation |
| `/help` | Show available commands |
| `/heartbeat trigger` | Manually trigger a heartbeat check |
| `/cron list` | View all scheduled tasks |
| `/cron remove <id>` | Delete a scheduled task |

---

## Gateway Multi-Channel Concurrent Mode

By default, DotCraft runs only one channel module at a time (highest-priority wins). **Gateway mode** removes this restriction, allowing QQ Bot, WeCom Bot, and the API service to run concurrently **within the same process**, sharing a single HeartbeatService, CronService, and DashBoard.

### How to Enable

Set `Gateway.Enabled = true` together with all the channel modules you want to run:

```json
{
    "Gateway": { "Enabled": true },
    "QQBot": {
        "Enabled": true,
        "Port": 6700,
        "AdminUsers": [123456789]
    },
    "WeComBot": {
        "Enabled": true,
        "Port": 9000,
        "Robots": [{ "Path": "/dotcraft", "Token": "your_token", "AesKey": "your_aeskey" }]
    },
    "Api": {
        "Enabled": true,
        "Port": 8080
    },
    "AgUi": {
        "Enabled": true,
        "Port": 5100,
        "Path": "/ag-ui"
    },
    "DashBoard": {
        "Enabled": true,
        "Port": 8080
    }
}
```

### Configuration

| Config Item | Description | Default |
|-------------|-------------|---------|
| `Gateway.Enabled` | Enable Gateway multi-channel concurrent mode | `false` |

### How It Works

When enabled, DotCraft will:

1. Select GatewayModule as the primary module (highest priority: 100)
2. Create an independent AgentFactory, ChannelAdapter, and network listener for each enabled channel (QQ / WeCom / API / AG-UI)
3. Use **WebHostPool** to group all HTTP services by `(scheme, host, port)`: services configured to the same address automatically share a single Kestrel server — no manual port coordination required
4. Start all channels concurrently — each channel has its own streaming pipeline and approval workflow
5. Share a single HeartbeatService and CronService, routing results to the correct channel via MessageRouter

```
GatewayHost
├── HeartbeatService (shared — routes notifications per channel)
├── CronService (shared — routes delivery via Payload.Channel)
├── WebHostPool (groups services by host:port, merges same-address services)
│   ├── http://127.0.0.1:8080 ← ApiChannelService + Dashboard routes
│   └── http://127.0.0.1:5100 ← AGUIChannelService routes
├── QQChannelService    → QQChannelAdapter → Agent (independent)
├── WeComChannelService → WeComChannelAdapter → Agent (independent)
├── ApiChannelService   → OpenAI API endpoints → Agent (independent)
└── AGUIChannelService  → AG-UI SSE endpoints → Agent (independent)
```

### Port Sharing

In Gateway mode, all HTTP services (API, AG-UI, Dashboard) are managed by **WebHostPool**. Whenever two services share the same `Host` and `Port`, they are automatically merged into a single Kestrel listener — routes are dispatched by path prefix with no manual configuration needed.

**Default port assignments**:

| Service | Default Host | Default Port |
|---------|-------------|--------------|
| `Api` | `127.0.0.1` | `8080` |
| `DashBoard` | `127.0.0.1` | `8080` |
| `AgUi` | `127.0.0.1` | `5100` |
| `WeComBot` | `0.0.0.0` | `9000` (HTTPS) |

Because `Api` and `DashBoard` share the same default port, they are served from the same Kestrel instance in Gateway mode by default. To serve them on separate ports, set `DashBoard.Port` to a different value.

**Example scenarios**:

- `Api.Port = 8080`, `DashBoard.Port = 8080` (default): API and Dashboard routes are merged on `127.0.0.1:8080`, single external port
- `Api.Port = 8080`, `AgUi.Port = 8080`: API and AG-UI share the same port; AG-UI is distinguished by its `/ag-ui` path
- `Api.Port = 8080`, `AgUi.Port = 5100` (default): Both listen on independent ports with no overlap

### Cron Cross-Channel Delivery

In Gateway mode, the Cron task `deliver` feature routes to the correct channel via the `channel` field:

| `channel` value | Delivery target | Example `to` |
|---|---|---|
| `"qq"` | QQ private chat (`to` = QQ number) or group (`to` = group number with `group:` prefix) | `"123456789"` / `"group:98765432"` |
| `"wecom"` | Specific WeCom group (`to` = ChatId); omit `to` to fall back to global `WeCom.WebhookUrl` | `"wrxxxxxxxx"` |
| `"api"` | API channel has no proactive delivery; ignored | — |
| `"ag-ui"` | AG-UI channel has no proactive delivery; ignored | — |

### Heartbeat Cross-Channel Notifications

Heartbeat results are broadcast to all channels that have admin configuration:
- QQ: Private message sent to all `QQBot.AdminUsers`
- WeCom: Sent to the WeCom group via `WeCom.WebhookUrl` (requires `WeCom.WebhookUrl` to be configured)

### Backward Compatibility

When `Gateway.Enabled = false` (the default), behavior is identical to before — the highest-priority enabled module runs alone.

---

## Full Configuration Example

```json
{
    "ApiKey": "sk-your-api-key",
    "Model": "gpt-4o-mini",
    "EndPoint": "https://api.openai.com/v1",
    "MaxToolCallRounds": 100,
    "SubagentMaxToolCallRounds": 50,
    "SubagentMaxConcurrency": 3,
    "MaxSessionQueueSize": 3,
    "CompactSessions": true,
    "MaxContextTokens": 160000,
    "MemoryWindow": 50,
    "ConsolidationModel": "",
    "EnabledTools": [],
    "Reasoning": {
        "Enabled": true,
        "Effort": "Medium",
        "Output": "Full"
    },
    "Logging": {
        "Enabled": true,
        "Console": false,
        "MinLevel": "Information",
        "Directory": "logs",
        "RetentionDays": 7
    },
    "Hooks": {
        "Enabled": true
    },
    "Tools": {
        "File": {
            "RequireApprovalOutsideWorkspace": true,
            "MaxFileSize": 10485760
        },
        "Shell": {
            "RequireApprovalOutsideWorkspace": true,
            "Timeout": 300,
            "MaxOutputLength": 10000
        },
        "Web": {
            "MaxChars": 50000,
            "Timeout": 300,
            "SearchMaxResults": 5,
            "SearchProvider": "Bing"
        },
        "Sandbox": {
            "Enabled": false,
            "Domain": "localhost:5880",
            "ApiKey": "",
            "UseHttps": false,
            "Image": "ubuntu:latest",
            "TimeoutSeconds": 600,
            "Cpu": "1",
            "Memory": "512Mi",
            "NetworkPolicy": "allow",
            "AllowedEgressDomains": [],
            "IdleTimeoutSeconds": 300,
            "SyncWorkspace": true,
            "SyncExclude": [
                ".craft/config.json",
                ".craft/sessions",
                ".craft/memory",
                ".craft/dashboard",
                ".craft/security",
                ".craft/logs",
                ".craft/plans"
            ]
        }
    },
    "Security": {
        "BlacklistedPaths": [
            "~/.ssh",
            "~/.gnupg",
            "/etc/shadow"
        ]
    },
    "QQBot": {
        "Enabled": false,
        "Host": "127.0.0.1",
        "Port": 6700,
        "AccessToken": "",
        "AdminUsers": [],
        "WhitelistedUsers": [],
        "WhitelistedGroups": [],
        "ApprovalTimeoutSeconds": 60
    },
    "WeComBot": {
        "Enabled": false,
        "Host": "0.0.0.0",
        "Port": 9000,
        "AdminUsers": [],
        "WhitelistedUsers": [],
        "WhitelistedChats": [],
        "ApprovalTimeoutSeconds": 60,
        "Robots": []
    },
    "Api": {
        "Enabled": false,
        "Host": "127.0.0.1",
        "Port": 8080,
        "ApiKey": "",
        "AutoApprove": true,
        "ApprovalMode": "",
        "ApprovalTimeoutSeconds": 120
    },
    "AgUi": {
        "Enabled": false,
        "Host": "127.0.0.1",
        "Port": 5100,
        "Path": "/ag-ui",
        "RequireAuth": false,
        "ApiKey": "",
        "ApprovalMode": "interactive"
    },
    "Heartbeat": {
        "Enabled": false,
        "IntervalSeconds": 1800,
        "NotifyAdmin": false
    },
    "WeCom": {
        "Enabled": false,
        "WebhookUrl": ""
    },
    "Cron": {
        "Enabled": false,
        "StorePath": "cron/jobs.json"
    },
    "Acp": {
        "Enabled": false
    },
    "DashBoard": {
        "Enabled": false,
        "Host": "127.0.0.1",
        "Port": 8080
    },
    "McpServers": [],
    "Gateway": {
        "Enabled": false
    }
}
```
