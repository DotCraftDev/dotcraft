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
    "AppServer": {
        "Mode": "WebSocket"
    },
    "ExternalChannels": [
        {
            "Name": "qq",
            "Enabled": true,
            "Transport": "Websocket"
        }
    ]
}
```

In this case, DotCraft will use the `ApiKey` from global config, but the `Model`, `EndPoint`, and external channel settings from workspace config.

---

> 💡 **Tip**: In addition to editing JSON files directly, you can use the Dashboard visual configuration page. See [Dashboard Guide](./dash_board_guide.md) for details.

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
| `Compaction` | Context compaction pipeline (thresholds, microcompact, partial summary, reactive retry). See below | see below |
| `ConsolidationModel` | Dedicated model for memory consolidation. Uses the main `Model` when empty. If the main model doesn't support `tool_choice` in thinking mode, specify a non-thinking model here | empty |
| `DebugMode` | Debug mode: tool call arguments are not truncated in console output | `false` |
| `EnabledTools` | Global list of enabled tool names. Enables all tools when empty | `[]` |

DotCraft's base identity is built in. If you want to customize the system prompt, prefer `.craft/AGENTS.md`, `.craft/SOUL.md`, and other bootstrap files instead of maintaining a separate field in `config.json`.

### Compaction Configuration

A layered context compaction pipeline: token estimation → microcompact (clears stale tool results) → partial summary (summarizes only the prefix, keeps the tail verbatim) → reactive retry (re-attempts compaction once when the model returns `prompt_too_long`).

| Config Item | Description | Default |
|-------------|-------------|---------|
| `Compaction.AutoCompactEnabled` | Enable threshold-driven automatic compaction | `true` |
| `Compaction.ReactiveCompactEnabled` | Enable reactive compaction when the model returns `prompt_too_long` | `true` |
| `Compaction.ContextWindow` | Model context window (tokens) | `200000` |
| `Compaction.SummaryReserveTokens` | Tokens reserved for the summary output | `20000` |
| `Compaction.AutoCompactBufferTokens` | Fire auto-compact this many tokens below `ContextWindow - SummaryReserve` | `13000` |
| `Compaction.WarningBufferTokens` | Emit `compactWarning` this many tokens before the auto threshold | `20000` |
| `Compaction.ErrorBufferTokens` | Emit `compactError` this many tokens before the auto threshold | `10000` |
| `Compaction.ManualCompactBufferTokens` | Allow manual `/compact` down to this margin below the hard ceiling | `3000` |
| `Compaction.KeepRecentMinTokens` | Minimum tokens kept verbatim after a partial summary | `10000` |
| `Compaction.KeepRecentMinGroups` | Minimum API-round groups kept verbatim | `3` |
| `Compaction.KeepRecentMaxTokens` | Hard cap on tokens kept verbatim | `40000` |
| `Compaction.MicrocompactEnabled` | Enable microcompact (trim stale tool results) | `true` |
| `Compaction.MicrocompactTriggerCount` | Trigger microcompact when compactable tool-result count reaches this value | `30` |
| `Compaction.MicrocompactKeepRecent` | Number of most-recent tool results kept at full fidelity | `8` |
| `Compaction.MicrocompactGapMinutes` | Idle-time trigger for microcompact, `0` disables | `20` |
| `Compaction.MemoryConsolidationPrefixTokens` | Prefix-token threshold that also triggers `MEMORY.md` / `HISTORY.md` consolidation | `20000` |
| `Compaction.MaxConsecutiveFailures` | Circuit breaker: skip future attempts after this many consecutive failures | `3` |

### Reasoning Configuration

Controls the Reasoning/Thinking behavior of the LLM provider. Providers or models that do not support these settings will ignore them.

| Config Item | Description | Default |
|-------------|-------------|---------|
| `Reasoning.Enabled` | Whether to request reasoning support from the provider | `false` |
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
| `Tools.Lsp.Enabled` | Enable built-in LSP tool (`LSP`) | `false` |
| `Tools.Lsp.MaxFileSize` | Maximum file size (bytes) allowed for LSP open/sync | `10485760` (10MB) |

### Sandbox Mode (OpenSandbox)

Migrate Shell and File tool execution to isolated Docker containers via [OpenSandbox](https://github.com/alibaba/OpenSandbox).

**Prerequisites**: Docker running + Install OpenSandbox Server: `pip install opensandbox-server && opensandbox-server`

| Config Item | Description | Default |
|-------------|-------------|---------|
| `Tools.Sandbox.Enabled` | Enable sandbox mode | `false` |
| `Tools.Sandbox.Domain` | OpenSandbox server address | `localhost:5880` |
| `Tools.Sandbox.ApiKey` | OpenSandbox API Key (optional) | empty |
| `Tools.Sandbox.UseHttps` | Use HTTPS | `false` |
| `Tools.Sandbox.Image` | Docker image for sandbox containers | `ubuntu:latest` |
| `Tools.Sandbox.TimeoutSeconds` | Sandbox TTL in seconds | `600` |
| `Tools.Sandbox.Cpu` | Container CPU limit | `1` |
| `Tools.Sandbox.Memory` | Container memory limit | `512Mi` |
| `Tools.Sandbox.NetworkPolicy` | Network policy: `deny`/`allow`/`custom` | `allow` |
| `Tools.Sandbox.AllowedEgressDomains` | Domains allowed for outbound access | `[]` |
| `Tools.Sandbox.IdleTimeoutSeconds` | Idle timeout in seconds | `300` |
| `Tools.Sandbox.SyncWorkspace` | Sync host workspace into sandbox | `true` |
| `Tools.Sandbox.SyncExclude` | Paths to exclude from workspace sync | see defaults |

When enabled, each Agent session automatically creates and reuses a sandbox container. SubAgents also execute within the same container.

---

## QQ External Channel Configuration

QQ is provided by the TypeScript external channel `@dotcraft/channel-qq`. The old `QQBot` config section is no longer used.

For details, see [QQ External Channel Guide](./qq_bot_guide.md). The workspace config registers the channel through `ExternalChannels`, while channel-specific OneBot and permission settings live in `.craft/qq.json`.

```json
{
    "AppServer": { "Mode": "WebSocket" },
    "ExternalChannels": [
        { "Name": "qq", "Enabled": true, "Transport": "Websocket" }
    ]
}
```

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

**Note**: `dotcraft` always starts the CLI. To run multiple channels at the same time, start the dedicated Gateway host with `dotcraft gateway`.

---

## Heartbeat Service

Heartbeat periodically reads the `.craft/HEARTBEAT.md` file and automatically executes the tasks defined within.

| Config Item | Description | Default |
|-------------|-------------|---------|
| `Heartbeat.Enabled` | Enable heartbeat service | `false` |
| `Heartbeat.IntervalSeconds` | Check interval (seconds) | `1800` |
| `Heartbeat.NotifyAdmin` | In QQ mode, whether to privately notify admins with results | `true` |

---

## Automations Configuration

Automations provides a unified task automation pipeline supporting local tasks and GitHub issue/PR orchestration. See [Automations Guide](./automations_guide.md) for details.

| Config Item | Description | Default |
|-------------|-------------|---------|
| `Automations.Enabled` | Enable the Automations orchestrator | `true` |
| `Automations.LocalTasksRoot` | Local task root directory; empty uses `.craft/tasks/` | empty |
| `Automations.WorkspaceRoot` | Task workspace root; empty uses system temp directory | empty |
| `Automations.PollingInterval` | Polling interval | `00:00:30` |
| `Automations.MaxConcurrentTasks` | Max concurrent tasks across all sources | `3` |
| `Automations.TurnTimeout` | Single turn timeout | `00:30:00` |
| `Automations.StallTimeout` | Stall timeout (no response) | `00:10:00` |
| `Automations.MaxRetries` | Maximum retry count | `3` |
| `Automations.RetryInitialDelay` | Retry initial delay | `00:00:30` |
| `Automations.RetryMaxDelay` | Retry maximum delay | `00:10:00` |
| `GitHubTracker.Enabled` | Enable the GitHub source | `false` |
| `GitHubTracker.IssuesWorkflowPath` | Path to the issue `WORKFLOW.md` | `WORKFLOW.md` |
| `GitHubTracker.Tracker.Repository` | GitHub repository in `owner/repo` format | empty |
| `GitHubTracker.Tracker.ApiKey` | GitHub token, supports `$ENV_VAR` | empty |

---

## API Mode Configuration

API mode exposes DotCraft as an OpenAI-compatible HTTP service. See [API Mode Guide](./api_guide.md) for details.

| Config Item | Description | Default |
|-------------|-------------|---------|
| `Api.Enabled` | Enable API mode | `false` |
| `Api.Host` | HTTP service listen address | `127.0.0.1` |
| `Api.Port` | HTTP service listen port | `8080` |
| `Api.ApiKey` | API access key (Bearer Token), no verification when empty | empty |
| `Api.AutoApprove` | Auto-approve all file/Shell operations when true; auto-reject when false | `true` |

Root-level `EnabledTools` field controls globally available tools (enables all when empty): `SpawnSubagent`, `ReadFile`, `WriteFile`, `EditFile`, `GrepFiles`, `FindFiles`, `Exec`, `WebSearch`, `WebFetch`, `LSP`, `Cron`, `WeComNotify`.

---

## AG-UI Mode Configuration

AG-UI mode exposes Agent capabilities via the [AG-UI Protocol](https://github.com/ag-ui-protocol/ag-ui) as an SSE streaming endpoint. See [AG-UI Mode Guide](./agui_guide.md) for details.

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

ACP ([Agent Client Protocol](https://agentclientprotocol.com/)) mode allows DotCraft to integrate with code editors/IDEs. See [ACP Mode Guide](./acp_guide.md) for details.

| Config Item | Description | Default |
|-------------|-------------|---------|
| `Acp.Enabled` | Enable ACP mode | `false` |

ACP mode communicates via stdin/stdout using JSON-RPC 2.0 protocol. Start it explicitly with `dotcraft acp`. In Gateway mode, QQ Bot, WeCom Bot, API, AG-UI, Automations, and other enabled channel services can run concurrently.

---

## Dashboard Configuration

Dashboard provides a web-based visual configuration interface and session management. See [Dashboard Guide](./dash_board_guide.md) for details.

| Config Item | Description | Default |
|-------------|-------------|---------|
| `DashBoard.Enabled` | Enable Dashboard | `false` |
| `DashBoard.Host` | HTTP service listen address | `127.0.0.1` |
| `DashBoard.Port` | HTTP service listen port | `8080` |
| `DashBoard.Username` | Login username; enables auth when set with Password | empty |
| `DashBoard.Password` | Login password | empty |

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

Cron provides scheduled task management supporting one-time (`at`) and recurring (`every`) tasks.

| Config Item | Description | Default |
|-------------|-------------|---------|
| `Cron.Enabled` | Enable scheduled tasks | `true` |
| `Cron.StorePath` | Task storage file path (relative to `.craft/`) | `cron/jobs.json` |

Agent can create tasks via the `Cron` tool, supporting delivery to QQ groups/private chats or WeCom.

---

## MCP Service Integration

DotCraft supports connecting external tool services via [Model Context Protocol (MCP)](https://modelcontextprotocol.io/).

`McpServers` is an array where each element defines an MCP server:

| Config Item | Description | Default |
|-------------|-------------|---------|
| `Name` | Server name | empty |
| `Enabled` | Whether to enable this server | `true` |
| `Transport` | Transport method: `stdio` (local process) or `http` (HTTP/SSE) | `stdio` |
| `Command` | Start command (stdio only) | empty |
| `Arguments` | Command arguments list (stdio only) | `[]` |
| `EnvironmentVariables` | Environment variables (stdio only) | `{}` |
| `Url` | Server address (http only) | empty |
| `Headers` | Additional HTTP request headers (http only) | `{}` |

**HTTP example**:
```json
{ "Name": "exa", "Transport": "http", "Url": "https://mcp.exa.ai/mcp" }
```

**Stdio example**:
```json
{ "Name": "filesystem", "Transport": "stdio", "Command": "npx", "Arguments": ["-y", "@modelcontextprotocol/server-filesystem", "/path"] }
```

### Deferred Loading

When MCP tool count is high, enable deferred loading to reduce token overhead:

| Config Item | Description | Default |
|-------------|-------------|---------|
| `Tools.DeferredLoading.Enabled` | Enable deferred loading | `false` |
| `Tools.DeferredLoading.AlwaysLoadedTools` | MCP tool names to always load upfront | `[]` |
| `Tools.DeferredLoading.MaxSearchResults` | Maximum results per `SearchTools` call | `5` |
| `Tools.DeferredLoading.DeferThreshold` | Skip deferred loading if tool count is below this | `10` |

---

## LSP Service Integration

DotCraft supports local Language Servers (for example `typescript-language-server` and `csharp-ls`) via the `LSP` tool, enabling definition lookup, references, hover, document/workspace symbols, and call hierarchy operations.

### Enable Steps

1. Set `Tools.Lsp.Enabled = true`
2. Configure one or more servers in `LspServers`
3. (Optional) Control access to `LSP` via root-level `EnabledTools`

> Note: `LspServers` currently supports only `stdio` transport. DotCraft routes requests by file extension and automatically sends `didChange + didSave` after `WriteFile` / `EditFile`.

### `LspServers` Fields

`LspServers` is recommended in object-map form (keys are server names):

```json
{
  "LspServers": {
    "ts": {
      "Enabled": true,
      "Command": "typescript-language-server",
      "Arguments": ["--stdio"],
      "ExtensionToLanguage": {
        ".ts": "typescript",
        ".tsx": "typescriptreact",
        ".js": "javascript",
        ".jsx": "javascriptreact"
      }
    }
  }
}
```

Field reference:

| Config Item | Description | Default |
|-------------|-------------|---------|
| `Name` | Server name (provided by key in object-map form) | empty |
| `Enabled` | Whether this LSP server is enabled | `true` |
| `Command` | Server start command | empty |
| `Arguments` | Command arguments list (alias `Args` is also supported) | `[]` |
| `ExtensionToLanguage` | File extension to language-id map (for example `.cs -> csharp`) | `{}` |
| `Transport` | Transport mode (currently only `stdio`) | `stdio` |
| `EnvironmentVariables` | Process environment variables (alias `Env` is also supported) | `{}` |
| `WorkspaceFolder` | Server working folder (optional, supports workspace-relative path) | empty |
| `InitializationOptions` | Extra JSON for the `initialize` request | empty |
| `Settings` | Settings JSON sent through `workspace/didChangeConfiguration` | empty |
| `StartupTimeoutMs` | Startup and initialize timeout in milliseconds | `30000` |
| `MaxRestarts` | Maximum crash-restart attempts | `3` |

### Complete Example

```json
{
  "Tools": {
    "Lsp": {
      "Enabled": true,
      "MaxFileSize": 10485760
    }
  },
  "EnabledTools": ["ReadFile", "WriteFile", "EditFile", "Exec", "LSP"],
  "LspServers": {
    "csharp": {
      "Enabled": true,
      "Command": "csharp-ls",
      "Arguments": [],
      "ExtensionToLanguage": {
        ".cs": "csharp"
      }
    },
    "typescript": {
      "Enabled": true,
      "Command": "typescript-language-server",
      "Arguments": ["--stdio"],
      "ExtensionToLanguage": {
        ".ts": "typescript",
        ".tsx": "typescriptreact"
      }
    }
  }
}
```

---

## Custom Commands

Save frequently used prompt templates as Markdown files and invoke them via `/command-name`.

| Level | Path | Priority |
|-------|------|----------|
| Workspace | `<workspace>/.craft/commands/` | High |
| User | `~/.craft/commands/` | Low |

Each `.md` file corresponds to a command (e.g., `code-review.md` → `/code-review`). Supports `$ARGUMENTS` placeholder for user input.

Built-in commands: `/code-review`, `/explain`, `/summarize`.

---

## Gateway Multi-Channel Concurrent Mode

Gateway mode allows WeCom Bot, API, AG-UI, Automations, external channels, and other services to run concurrently in the same process.

**Explicit start**: Gateway no longer auto-takes over startup. Use `dotcraft gateway` when you want a dedicated host for concurrent channels and automations.

**Default entry point**: `dotcraft` still starts the CLI even when `Automations.Enabled = true`.

**Background defaults**:

- `Automations.Enabled` defaults to `true`
- `GitHubTracker.Enabled` defaults to `false`

**Enable example**:
```json
{
    "ExternalChannels": [{ "Name": "qq", "Enabled": true, "Transport": "Websocket" }],
    "WeComBot": { "Enabled": true, "Port": 9000 },
    "Api": { "Enabled": true, "Port": 8080 },
    "DashBoard": { "Enabled": true, "Port": 8080 }
}
```

All HTTP services are managed by WebHostPool — services with the same `Host:Port` automatically share the port.
