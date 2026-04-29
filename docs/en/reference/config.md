# DotCraft Full Configuration Reference

This page collects configuration fields in one place. For first-time setup, read [Configuration Guide](../config_guide.md). For file, shell, sandbox, and network boundaries, read [Security Configuration](../config/security.md).

## Basic Configuration

| Field | Description | Default |
|-------|-------------|---------|
| `ApiKey` | OpenAI-compatible API key | Empty |
| `Model` | Default model name | `gpt-4o-mini` |
| `EndPoint` | API endpoint URL | `https://api.openai.com/v1` |
| `Language` | UI language: `Chinese` / `English` | `Chinese` |
| `MaxToolCallRounds` | Maximum tool-call rounds for the main Agent | `100` |
| `SubagentMaxToolCallRounds` | Maximum tool-call rounds for subagents | `50` |
| `SubagentMaxConcurrency` | Maximum concurrent subagents | `3` |
| `MaxSessionQueueSize` | Maximum queued requests per session; `0` means unlimited | `3` |
| `ConsolidationModel` | Memory consolidation model. Empty uses the main model | Empty |
| `DebugMode` | Prints untruncated tool arguments in the console | `false` |
| `EnabledTools` | Globally enabled tool names. Empty enables all tools | `[]` |

## Memory

| Field | Description | Default |
|-------|-------------|---------|
| `Memory.AutoConsolidateEnabled` | Enables automatic long-term memory consolidation | `true` |
| `Memory.ConsolidateEveryNTurns` | Successful turns per thread between long-term memory consolidation attempts | `5` |

## Compaction

| Field | Description | Default |
|-------|-------------|---------|
| `Compaction.AutoCompactEnabled` | Enables threshold-based auto compaction | `true` |
| `Compaction.ReactiveCompactEnabled` | Enables reactive compaction for `prompt_too_long` errors | `true` |
| `Compaction.ContextWindow` | Model context window in tokens | `200000` |
| `Compaction.SummaryReserveTokens` | Tokens reserved for summary output | `20000` |
| `Compaction.AutoCompactBufferTokens` | Token buffer below the hard limit that triggers auto compaction | `13000` |
| `Compaction.WarningBufferTokens` | Token buffer before auto threshold that emits warning | `20000` |
| `Compaction.ErrorBufferTokens` | Token buffer before auto threshold that emits error | `10000` |
| `Compaction.ManualCompactBufferTokens` | Token buffer below the hard limit where manual `/compact` remains allowed | `3000` |
| `Compaction.KeepRecentMinTokens` | Minimum recent tail tokens after partial summary | `10000` |
| `Compaction.KeepRecentMinGroups` | Minimum recent API groups after partial summary | `3` |
| `Compaction.KeepRecentMaxTokens` | Maximum recent tail tokens after partial summary | `40000` |
| `Compaction.MicrocompactEnabled` | Enables micro-compaction | `true` |
| `Compaction.MicrocompactTriggerCount` | Compressible tool-result count that triggers micro-compaction | `30` |
| `Compaction.MicrocompactKeepRecent` | Recent tool results kept during micro-compaction | `8` |
| `Compaction.MicrocompactGapMinutes` | Also triggers after this many minutes since last assistant message; `0` disables it | `20` |
| `Compaction.MaxConsecutiveFailures` | Consecutive failures before circuit breaking compaction | `3` |

## Reasoning

| Field | Description | Default |
|-------|-------------|---------|
| `Reasoning.Enabled` | Requests provider reasoning support | `false` |
| `Reasoning.Effort` | Reasoning depth: `None` / `Low` / `Medium` / `High` / `ExtraHigh` | `Medium` |
| `Reasoning.Output` | Reasoning visibility: `None` / `Summary` / `Full` | `Full` |

## Entry Points and Services

| Field | Description | Default |
|-------|-------------|---------|
| `Api.Enabled` | Enables API mode | `false` |
| `Api.Host` | HTTP listen address | `127.0.0.1` |
| `Api.Port` | HTTP listen port | `8080` |
| `Api.ApiKey` | API bearer token. Empty disables auth | Empty |
| `Api.AutoApprove` | Automatically approves all file/shell operations | `true` |
| `AgUi.Enabled` | Enables AG-UI service | `false` |
| `AgUi.Host` | HTTP listen address | `127.0.0.1` |
| `AgUi.Port` | HTTP listen port | `5100` |
| `AgUi.Path` | SSE endpoint path | `/ag-ui` |
| `AgUi.RequireAuth` | Enables bearer token authentication | `false` |
| `AgUi.ApiKey` | Bearer token value | Empty |
| `AgUi.ApprovalMode` | Tool approval mode: `interactive` / `auto` | `interactive` |
| `Acp.Enabled` | Enables ACP mode | `false` |
| `DashBoard.Enabled` | Enables Dashboard | `false` |
| `DashBoard.Host` | Dashboard listen address | `127.0.0.1` |
| `DashBoard.Port` | Dashboard listen port | `8080` |
| `Gateway.Enabled` | Enables Gateway Host | `false` |

## Automation and Workflows

| Field | Description | Default |
|-------|-------------|---------|
| `Automations.Enabled` | Enables the Automations orchestrator | `true` |
| `Automations.LocalTasksRoot` | Local task root. Empty uses `.craft/tasks/` | Empty |
| `Automations.WorkspaceRoot` | Task workspace root. Empty uses system temp | Empty |
| `Automations.PollingInterval` | Polling interval | `00:00:30` |
| `Automations.MaxConcurrentTasks` | Maximum concurrent tasks across sources | `3` |
| `Automations.TurnTimeout` | Single-turn timeout | `00:30:00` |
| `Automations.StallTimeout` | Stall timeout without response | `00:10:00` |
| `Automations.MaxRetries` | Maximum retry count | `3` |
| `Automations.RetryInitialDelay` | Initial retry delay | `00:00:30` |
| `Automations.RetryMaxDelay` | Maximum retry delay | `00:10:00` |
| `GitHubTracker.Enabled` | Enables GitHub source | `false` |
| `GitHubTracker.IssuesWorkflowPath` | Issue `WORKFLOW.md` path | `WORKFLOW.md` |
| `GitHubTracker.PullRequestWorkflowPath` | PR workflow path | Empty |
| `GitHubTracker.Tracker.Repository` | GitHub repository as `owner/repo` | Empty |
| `GitHubTracker.Tracker.ApiKey` | GitHub token, supports `$ENV_VAR` | Empty |
| `Hooks.Enabled` | Enables Hooks | `true` |
| `Hooks.Events` | Hook event configuration list | `[]` |
| `Cron.Enabled` | Enables Cron scheduled tasks | `true` |
| `Heartbeat.Enabled` | Enables heartbeat service | `false` |
| `Heartbeat.IntervalSeconds` | Check interval in seconds | `1800` |
| `Heartbeat.NotifyAdmin` | Sends results to admin in social channels | `true` |

## MCP and LSP

| Field | Description | Default |
|-------|-------------|---------|
| `McpServers` | MCP server configuration map | `{}` |
| `Tools.DeferredLoading.Enabled` | Enables deferred tool loading | `false` |
| `Tools.DeferredLoading.AlwaysLoadedTools` | Tool names that are always loaded | `[]` |
| `LspServers` | LSP server configuration map | `{}` |
| `Tools.Lsp.Enabled` | Enables the built-in LSP tool | `false` |

## External Channels

TypeScript external channels such as QQ and WeCom are registered with `ExternalChannels`:

```json
{
  "AppServer": {
    "Mode": "WebSocket",
    "WebSocket": {
      "Host": "127.0.0.1",
      "Port": 9100,
      "Token": ""
    }
  },
  "ExternalChannels": {
    "qq": {
      "enabled": true,
      "transport": "websocket"
    },
    "wecom": {
      "enabled": true,
      "transport": "websocket"
    }
  }
}
```

Platform connections, allowlists, and approval timeouts live in adapter-specific files such as `.craft/qq.json` and `.craft/wecom.json`.

## Custom Commands

`CustomCommands` stores reusable prompts or workflows as commands. Command content usually lives under workspace `.craft/commands/` or the corresponding configuration entry, then can be reused from CLI, Desktop, and other entry points.
