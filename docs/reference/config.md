# DotCraft 完整配置参考

本页集中列出配置字段。第一次配置请先读 [配置指南](../config_guide.md)，安全策略请读 [安全配置](../config/security.md)。

## 基础配置

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `ApiKey` | OpenAI-compatible API Key | 空 |
| `Model` | 默认模型名称 | `gpt-4o-mini` |
| `EndPoint` | API 端点地址 | `https://api.openai.com/v1` |
| `Language` | 界面语言：`Chinese` / `English` | `Chinese` |
| `MaxToolCallRounds` | 主 Agent 最大工具调用轮数 | `100` |
| `SubagentMaxToolCallRounds` | 子 Agent 最大工具调用轮数 | `50` |
| `SubagentMaxConcurrency` | 最大并发子 Agent 数量 | `3` |
| `MaxSessionQueueSize` | 每个 Session 最大排队请求数；`0` 表示无限制 | `3` |
| `ConsolidationModel` | 记忆整合专用模型，空值使用主模型 | 空 |
| `DebugMode` | 控制台不截断工具调用参数输出 | `false` |
| `EnabledTools` | 全局启用的工具名称列表，为空时启用所有工具 | `[]` |

## Compaction

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `Compaction.AutoCompactEnabled` | 启用基于阈值的自动压缩 | `true` |
| `Compaction.ReactiveCompactEnabled` | 启用对 `prompt_too_long` 错误的反应式压缩 | `true` |
| `Compaction.ContextWindow` | 模型上下文窗口（Token） | `200000` |
| `Compaction.SummaryReserveTokens` | 为摘要输出预留的 Token | `20000` |
| `Compaction.AutoCompactBufferTokens` | 低于硬上限多少 Token 时触发自动压缩 | `13000` |
| `Compaction.WarningBufferTokens` | 到达自动阈值前多少 Token 发出 warning | `20000` |
| `Compaction.ErrorBufferTokens` | 到达自动阈值前多少 Token 发出 error | `10000` |
| `Compaction.ManualCompactBufferTokens` | 低于硬上限多少 Token 时仍允许手动 `/compact` | `3000` |
| `Compaction.KeepRecentMinTokens` | 局部摘要后尾部至少保留的 Token 数 | `10000` |
| `Compaction.KeepRecentMinGroups` | 局部摘要后尾部至少保留的 API 轮次数 | `3` |
| `Compaction.KeepRecentMaxTokens` | 局部摘要后尾部最多保留的 Token 数 | `40000` |
| `Compaction.MicrocompactEnabled` | 启用微压缩 | `true` |
| `Compaction.MicrocompactTriggerCount` | 可压缩工具结果数量达到该值时触发微压缩 | `30` |
| `Compaction.MicrocompactKeepRecent` | 微压缩时保留的最近工具结果数 | `8` |
| `Compaction.MicrocompactGapMinutes` | 距离上次助理消息超过该分钟数也触发微压缩；`0` 表示禁用 | `20` |
| `Compaction.MemoryConsolidationPrefixTokens` | 当前缀超过此 Token 数时触发记忆整合 | `20000` |
| `Compaction.MaxConsecutiveFailures` | 连续失败次数达到该值时熔断 | `3` |

## Reasoning

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `Reasoning.Enabled` | 是否请求 Provider 的推理支持 | `false` |
| `Reasoning.Effort` | 推理深度：`None` / `Low` / `Medium` / `High` / `ExtraHigh` | `Medium` |
| `Reasoning.Output` | 推理内容是否暴露在响应中：`None` / `Summary` / `Full` | `Full` |

## 入口与服务

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `Api.Enabled` | 是否启用 API 模式 | `false` |
| `Api.Host` | HTTP 服务监听地址 | `127.0.0.1` |
| `Api.Port` | HTTP 服务监听端口 | `8080` |
| `Api.ApiKey` | API 访问密钥（Bearer Token），为空时不验证 | 空 |
| `Api.AutoApprove` | 是否自动批准所有文件/Shell 操作 | `true` |
| `AgUi.Enabled` | 是否启用 AG-UI 服务 | `false` |
| `AgUi.Host` | HTTP 服务监听地址 | `127.0.0.1` |
| `AgUi.Port` | HTTP 服务监听端口 | `5100` |
| `AgUi.Path` | SSE 端点路径 | `/ag-ui` |
| `AgUi.RequireAuth` | 是否启用 Bearer Token 认证 | `false` |
| `AgUi.ApiKey` | Bearer Token 值 | 空 |
| `AgUi.ApprovalMode` | 工具操作审批模式：`interactive` / `auto` | `interactive` |
| `Acp.Enabled` | 是否启用 ACP 模式 | `false` |
| `DashBoard.Enabled` | 是否启用 Dashboard | `false` |
| `DashBoard.Host` | Dashboard 监听地址 | `127.0.0.1` |
| `DashBoard.Port` | Dashboard 监听端口 | `8080` |
| `Gateway.Enabled` | 是否启用 Gateway Host | `false` |

## 自动化与工作流

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `Automations.Enabled` | 是否启用 Automations 编排器 | `true` |
| `Automations.LocalTasksRoot` | 本地任务根目录，留空使用 `.craft/tasks/` | 空 |
| `Automations.WorkspaceRoot` | 任务工作区根目录，留空使用系统临时目录 | 空 |
| `Automations.PollingInterval` | 轮询间隔 | `00:00:30` |
| `Automations.MaxConcurrentTasks` | 所有来源合计最大并发任务数 | `3` |
| `Automations.TurnTimeout` | 单轮对话超时时间 | `00:30:00` |
| `Automations.StallTimeout` | 停顿超时时间 | `00:10:00` |
| `Automations.MaxRetries` | 最大重试次数 | `3` |
| `Automations.RetryInitialDelay` | 重试初始延迟 | `00:00:30` |
| `Automations.RetryMaxDelay` | 重试最大延迟 | `00:10:00` |
| `GitHubTracker.Enabled` | 是否启用 GitHub 来源 | `false` |
| `GitHubTracker.IssuesWorkflowPath` | Issue `WORKFLOW.md` 路径 | `WORKFLOW.md` |
| `GitHubTracker.PullRequestWorkflowPath` | PR workflow 路径 | 空 |
| `GitHubTracker.Tracker.Repository` | GitHub 仓库，格式 `owner/repo` | 空 |
| `GitHubTracker.Tracker.ApiKey` | GitHub Token，支持 `$ENV_VAR` | 空 |
| `Hooks.Enabled` | 是否启用 Hooks | `true` |
| `Hooks.Events` | Hook 事件配置列表 | `[]` |
| `Cron.Enabled` | 是否启用 Cron 定时任务服务 | `true` |
| `Heartbeat.Enabled` | 是否启用心跳服务 | `false` |
| `Heartbeat.IntervalSeconds` | 检查间隔（秒） | `1800` |
| `Heartbeat.NotifyAdmin` | 社交渠道下是否将结果通知管理员 | `true` |

## MCP 与 LSP

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `McpServers` | MCP 服务配置集合 | `{}` |
| `Tools.DeferredLoading.Enabled` | 是否启用工具延迟加载 | `false` |
| `Tools.DeferredLoading.AlwaysLoadedTools` | 始终加载的工具名列表 | `[]` |
| `LspServers` | LSP 服务配置集合 | `{}` |
| `Tools.Lsp.Enabled` | 是否启用内置 LSP 工具 | `false` |

## 外部渠道

QQ、企业微信等 TypeScript 外部渠道通过 `ExternalChannels` 注册：

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

平台连接、权限白名单和审批超时等渠道专属设置分别放在 `.craft/qq.json`、`.craft/wecom.json` 等适配器配置文件中。

## 自定义命令

`CustomCommands` 可把常用提示词或工作流保存为命令。命令内容通常放在工作区 `.craft/commands/` 或对应配置项中，供 CLI、Desktop 或其他入口复用。
