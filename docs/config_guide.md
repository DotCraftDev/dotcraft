# DotCraft 配置指南

本文档介绍 DotCraft 的配置体系，包括全局配置、工作区配置、安全设置等。

## 配置文件位置

DotCraft 支持两级配置：**全局配置**和**工作区配置**。

| 配置文件 | 路径 | 用途 |
|----------|------|------|
| 全局配置 | `~/.craft/config.json` | 默认 API Key、模型等全局设置 |
| 工作区配置 | `<workspace>/.craft/config.json` | 工作区特定的覆盖配置 |

### 配置合并规则

- **全局配置作为基础**：提供默认值
- **工作区配置覆盖全局配置**：工作区中设置的值优先级更高
- 未在工作区中设置的项会保留全局配置的值

这种设计可以将 API Key 等敏感信息放在全局配置中，避免泄露到工作区（例如 Git 仓库）。

### 使用示例

**全局配置** (`~/.craft/config.json`)：存放默认 API Key 和模型

```json
{
    "ApiKey": "sk-your-default-api-key",
    "Model": "gpt-4o-mini",
    "EndPoint": "https://api.openai.com/v1"
}
```

**工作区配置** (`<workspace>/.craft/config.json`)：覆盖模型和功能设置，无需重复 API Key

```json
{
    "Model": "deepseek-chat",
    "EndPoint": "https://api.deepseek.com/v1",
    "AppServer": {
        "Mode": "WebSocket"
    },
    "ExternalChannels": {
        "qq": {
            "enabled": true,
            "transport": "websocket"
        }
    }
}
```

此时 DotCraft 会使用全局配置中的 `ApiKey`，但使用工作区中的 `Model`、`EndPoint` 和外部渠道设置。

---

> 💡 **提示**：除了直接编辑 JSON 文件，也可以使用 Dashboard 可视化配置页面。详见 [Dashboard 使用指南](./dash_board_guide.md)。

## 基础配置项

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `ApiKey` | LLM API Key（OpenAI 兼容格式） | 空 |
| `Model` | 使用的模型名称 | `gpt-4o-mini` |
| `EndPoint` | API 端点地址 | `https://api.openai.com/v1` |
| `Language` | 界面语言（`Chinese` / `English`） | `Chinese` |
| `MaxToolCallRounds` | 主 Agent 最大工具调用轮数 | `100` |
| `SubagentMaxToolCallRounds` | 子 Agent 最大工具调用轮数 | `50` |
| `SubagentMaxConcurrency` | 最大并发子 Agent 数量（超出时排队等待） | `3` |
| `MaxSessionQueueSize` | 每个 Session 最大排队请求数，超出后最老的等待请求被驱逐并通知用户。`0` 表示无限制 | `3` |
| `Compaction` | 上下文压缩流水线配置（阈值、微压缩、局部摘要、反应式重试）。详见下文 | 见下 |
| `ConsolidationModel` | 记忆整合专用模型。为空时使用主 `Model`；如果主模型在思考模式下不支持 `tool_choice`，建议指定非思考模型 | 空 |
| `DebugMode` | 调试模式：控制台不截断工具调用参数输出 | `false` |
| `EnabledTools` | 全局启用的工具名称列表，为空时启用所有工具 | `[]` |

DotCraft 的基础身份由系统内置；如果你想定制系统提示词，建议使用 `.craft/AGENTS.md`、`.craft/SOUL.md` 等 bootstrap 文件，而不是在 `config.json` 中维护单独字段。

### Compaction（上下文压缩）配置

分层的上下文压缩流水线：预估 Token → 微压缩（清理陈旧工具结果）→ 局部摘要（仅总结前缀，保留尾部原文）→ 反应式重试（遇到 `prompt_too_long` 时自动压缩并重试一次）。

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `Compaction.AutoCompactEnabled` | 启用基于阈值的自动压缩 | `true` |
| `Compaction.ReactiveCompactEnabled` | 启用对 `prompt_too_long` 错误的反应式压缩 | `true` |
| `Compaction.ContextWindow` | 模型上下文窗口（Token） | `200000` |
| `Compaction.SummaryReserveTokens` | 为摘要输出预留的 Token | `20000` |
| `Compaction.AutoCompactBufferTokens` | 低于 `ContextWindow - SummaryReserve` 多少 Token 时触发自动压缩 | `13000` |
| `Compaction.WarningBufferTokens` | 在到达自动阈值前多少 Token 开始发出 `compactWarning` 事件 | `20000` |
| `Compaction.ErrorBufferTokens` | 在到达自动阈值前多少 Token 开始发出 `compactError` 事件 | `10000` |
| `Compaction.ManualCompactBufferTokens` | 低于硬上限多少 Token 时仍允许手动 `/compact` | `3000` |
| `Compaction.KeepRecentMinTokens` | 局部摘要后尾部至少保留的 Token 数 | `10000` |
| `Compaction.KeepRecentMinGroups` | 局部摘要后尾部至少保留的 API 轮次数 | `3` |
| `Compaction.KeepRecentMaxTokens` | 局部摘要后尾部最多保留的 Token 数 | `40000` |
| `Compaction.MicrocompactEnabled` | 启用微压缩（清理陈旧工具结果） | `true` |
| `Compaction.MicrocompactTriggerCount` | 可压缩工具结果数量达到该值时触发微压缩 | `30` |
| `Compaction.MicrocompactKeepRecent` | 微压缩时保留的最近工具结果数 | `8` |
| `Compaction.MicrocompactGapMinutes` | 距离上次助理消息超过该分钟数也触发微压缩，`0` 表示禁用 | `20` |
| `Compaction.MemoryConsolidationPrefixTokens` | 当前缀超过此 Token 数时触发 `MEMORY.md` / `HISTORY.md` 记忆整合 | `20000` |
| `Compaction.MaxConsecutiveFailures` | 连续失败次数达到该值时熔断（本会话剩余回合不再尝试） | `3` |

### Reasoning（推理）配置

控制 LLM Provider 的 Reasoning/Thinking 行为。不支持的 Provider 或模型会忽略这些设置。

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `Reasoning.Enabled` | 是否请求 Provider 的推理支持 | `false` |
| `Reasoning.Effort` | 推理深度：`None` / `Low` / `Medium` / `High` / `ExtraHigh` | `Medium` |
| `Reasoning.Output` | 推理内容是否暴露在响应中：`None` / `Summary` / `Full` | `Full` |

---

## 安全配置

### 文件访问黑名单

通过 `Security.BlacklistedPaths` 配置禁止访问的路径列表。黑名单是**全局生效**的，无论 CLI、Desktop 还是外部渠道都会拦截。

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

#### 黑名单行为

- **文件操作**：`ReadFile`、`WriteFile`、`EditFile`、`GrepFiles`、`FindFiles` 对黑名单路径的操作会被直接拒绝
- **Shell 命令**：引用黑名单路径的 Shell 命令会被拒绝
- **优先级**：黑名单检查优先于工作区边界检查，即使路径在工作区内也会被拦截
- **路径匹配**：支持绝对路径和 `~` 展开，匹配时会检查路径是否为黑名单路径的子路径

#### 推荐黑名单配置

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

### Shell 命令路径检测与工作区边界

DotCraft 会在执行 Shell 命令前对命令字符串进行**跨平台路径静态分析**（`ShellCommandInspector`），覆盖以下路径形式：

**Unix 路径**
- 绝对路径：`/etc/passwd`、`/var/log/syslog`
- 家目录路径：`~/.ssh/config`
- 环境变量家目录：`$HOME/.config`、`${HOME}/.gitconfig`
- 安全设备白名单（不触发检测）：`/dev/null`、`/dev/stdout` 等

**Windows 路径**
- 盘符绝对路径：`C:\`、`D:\Users\Aki\file.txt`
- 环境变量路径：`%USERPROFILE%\Documents`、`%APPDATA%\config`
- UNC 路径：`\\server\share\file`
- 安全设备白名单：`NUL`、`CON`、`PRN`、`AUX`

**文件工具路径解析**

`FileTools` 在解析文件路径时也会展开 `~`、`$HOME`、`${HOME}` 和 `%ENV%` 变量为实际路径，确保工作区边界检查对所有路径形式生效。

**触发规则**

若命令中引用的任意路径解析后位于工作区之外：
- 当 `Tools.Shell.RequireApprovalOutsideWorkspace = false` 时：直接拒绝执行，并给出被检测到的路径列表
- 当 `Tools.Shell.RequireApprovalOutsideWorkspace = true` 时：向当前交互源（控制台/QQ）发起审批，审批通过后才执行

注意：即便工作目录（cwd）仍在工作区内，只要命令字符串中包含工作区外的路径，也会触发上述规则。

示例：
- `ls /etc` → 触发（Unix 绝对路径，工作区外）
- `dir C:\` → 触发（Windows 盘符路径，工作区外）
- `cat ~/.ssh/id_rsa` → 触发（家目录路径 + 建议加入黑名单）
- `type %USERPROFILE%\Desktop\secret.txt` → 触发（Windows 环境变量路径）
- `grep foo ${HOME}/.bashrc` → 触发（Unix 环境变量路径）
- `ls ./src` → 工作区内正常执行
- `echo test > /dev/null` → 安全设备白名单，不触发

### 工具安全配置

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `Tools.File.RequireApprovalOutsideWorkspace` | 工作区外文件操作是否需要审批（`false` 则直接拒绝） | `true` |
| `Tools.File.MaxFileSize` | 最大可读取文件大小（字节） | `10485760` (10MB) |
| `Tools.Shell.RequireApprovalOutsideWorkspace` | 工作区外 Shell 命令是否需要审批（`false` 则直接拒绝） | `true` |
| `Tools.Shell.Timeout` | Shell 命令超时时间（秒） | `300` |
| `Tools.Shell.MaxOutputLength` | Shell 命令最大输出长度（字符） | `10000` |
| `Tools.Web.MaxChars` | Web 抓取最大字符数 | `50000` |
| `Tools.Web.Timeout` | Web 请求超时时间（秒） | `300` |
| `Tools.Web.SearchMaxResults` | 联网搜索默认返回结果数（1-10） | `5` |
| `Tools.Web.SearchProvider` | 搜索引擎提供商：`Bing`（全球可用）、`Exa`（AI 优化，免费 MCP 接口）| `Exa` |
| `Tools.Lsp.Enabled` | 是否启用内置 LSP 工具（工具名 `LSP`） | `false` |
| `Tools.Lsp.MaxFileSize` | LSP 打开/同步文件时允许的最大文件大小（字节） | `10485760` (10MB) |

### 沙箱模式（OpenSandbox）

通过 [OpenSandbox](https://github.com/alibaba/OpenSandbox) 将 Shell 和 File 工具执行环境迁移到隔离的 Docker 容器。

**前置条件**：Docker 运行 + 安装 OpenSandbox Server：`pip install opensandbox-server && opensandbox-server`

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `Tools.Sandbox.Enabled` | 是否启用沙箱模式 | `false` |
| `Tools.Sandbox.Domain` | OpenSandbox 服务地址 | `localhost:5880` |
| `Tools.Sandbox.ApiKey` | OpenSandbox API Key（可选） | 空 |
| `Tools.Sandbox.UseHttps` | 是否使用 HTTPS | `false` |
| `Tools.Sandbox.Image` | 沙箱容器 Docker 镜像 | `ubuntu:latest` |
| `Tools.Sandbox.TimeoutSeconds` | 沙箱超时时间（秒） | `600` |
| `Tools.Sandbox.Cpu` | 容器 CPU 限制 | `1` |
| `Tools.Sandbox.Memory` | 容器内存限制 | `512Mi` |
| `Tools.Sandbox.NetworkPolicy` | 网络策略：`deny`/`allow`/`custom` | `allow` |
| `Tools.Sandbox.AllowedEgressDomains` | 自定义允许出站的域名列表 | `[]` |
| `Tools.Sandbox.IdleTimeoutSeconds` | 空闲超时（秒） | `300` |
| `Tools.Sandbox.SyncWorkspace` | 是否同步 workspace 到容器 | `true` |
| `Tools.Sandbox.SyncExclude` | 同步时排除的路径列表 | 见默认值 |

启用后，每个 Agent Session 自动创建并复用一个沙箱容器，SubAgent 也在同一容器内执行。

---

## QQ / WeCom 外部渠道配置

QQ 和企业微信由 TypeScript 外部渠道提供。工作区配置通过 `ExternalChannels` 注册渠道，平台连接、权限白名单和审批超时等渠道专属设置分别放在 `.craft/qq.json` 和 `.craft/wecom.json`。

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

详细说明请参考 [QQ 渠道适配器](./sdk/typescript-qq.md) 和 [企业微信渠道适配器](./sdk/typescript-wecom.md)。

**注意**：`dotcraft` 现在始终默认启动 CLI。若需同时运行多个 Channel，请显式使用 `dotcraft gateway` 启动 [Gateway 模式](#gateway-多-channel-并发模式)。

---

## Heartbeat 心跳服务

Heartbeat 定时读取 `.craft/HEARTBEAT.md` 文件，自动执行其中定义的定期任务。

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `Heartbeat.Enabled` | 是否启用心跳服务 | `false` |
| `Heartbeat.IntervalSeconds` | 检查间隔（秒） | `1800` |
| `Heartbeat.NotifyAdmin` | 社交渠道下是否将结果通知管理员 | `true` |

---

## Automations 配置

Automations 提供统一的自动化任务管线，支持本地任务和 GitHub Issue/PR 编排。详见 [Automations 指南](./automations_guide.md)。

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `Automations.Enabled` | 是否启用 Automations 编排器 | `true` |
| `Automations.LocalTasksRoot` | 本地任务根目录，留空使用 `.craft/tasks/` | 空 |
| `Automations.WorkspaceRoot` | 任务工作区根目录，留空使用系统临时目录 | 空 |
| `Automations.PollingInterval` | 轮询间隔 | `00:00:30` |
| `Automations.MaxConcurrentTasks` | 所有来源合计最大并发任务数 | `3` |
| `Automations.TurnTimeout` | 单轮对话超时时间 | `00:30:00` |
| `Automations.StallTimeout` | 停顿超时时间（无响应） | `00:10:00` |
| `Automations.MaxRetries` | 最大重试次数 | `3` |
| `Automations.RetryInitialDelay` | 重试初始延迟 | `00:00:30` |
| `Automations.RetryMaxDelay` | 重试最大延迟 | `00:10:00` |
| `GitHubTracker.Enabled` | 是否启用 GitHub 来源 | `false` |
| `GitHubTracker.IssuesWorkflowPath` | Issue `WORKFLOW.md` 路径 | `WORKFLOW.md` |
| `GitHubTracker.Tracker.Repository` | GitHub 仓库，格式 `owner/repo` | 空 |
| `GitHubTracker.Tracker.ApiKey` | GitHub Token，支持 `$ENV_VAR` | 空 |

---

## API 模式配置

API 模式将 DotCraft 作为 OpenAI 兼容的 HTTP 服务暴露。详见 [API 模式指南](./api_guide.md)。

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `Api.Enabled` | 是否启用 API 模式 | `false` |
| `Api.Host` | HTTP 服务监听地址 | `127.0.0.1` |
| `Api.Port` | HTTP 服务监听端口 | `8080` |
| `Api.ApiKey` | API 访问密钥（Bearer Token），为空时不验证 | 空 |
| `Api.AutoApprove` | 是否自动批准所有文件/Shell 操作；`false` 时自动拒绝 | `true` |

根级别 `EnabledTools` 字段控制全局可用工具（为空时启用所有工具）：`SpawnSubagent`、`ReadFile`、`WriteFile`、`EditFile`、`GrepFiles`、`FindFiles`、`Exec`、`WebSearch`、`WebFetch`、`LSP`、`Cron`、`WeComNotify`。

---

## AG-UI 模式配置

AG-UI 模式通过 [AG-UI 协议](https://github.com/ag-ui-protocol/ag-ui) 将 Agent 能力以 SSE 流式推送的方式对外暴露。详见 [AG-UI 模式指南](./agui_guide.md)。

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `AgUi.Enabled` | 是否启用 AG-UI 服务 | `false` |
| `AgUi.Host` | HTTP 服务监听地址 | `127.0.0.1` |
| `AgUi.Port` | HTTP 服务监听端口 | `5100` |
| `AgUi.Path` | SSE 端点路径 | `/ag-ui` |
| `AgUi.RequireAuth` | 是否启用 Bearer Token 认证 | `false` |
| `AgUi.ApiKey` | Bearer Token 值（`RequireAuth` 为 `true` 时必填） | 空 |
| `AgUi.ApprovalMode` | 工具操作审批模式：`interactive`（前端审批）/ `auto`（自动批准） | `interactive` |

---

## ACP 模式配置

ACP（[Agent Client Protocol](https://agentclientprotocol.com/)）模式允许 DotCraft 作为 AI 编码代理与代码编辑器/IDE 集成。详见 [ACP 模式指南](./acp_guide.md)。

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `Acp.Enabled` | 是否启用 ACP 模式 | `false` |

ACP 模式通过 stdin/stdout 以 JSON-RPC 2.0 协议通信，使用 `dotcraft acp` 显式启动。在 Gateway 模式下，外部渠道、API、AG-UI、Automations 等服务可以并发运行。

---

## Dashboard 配置

Dashboard 提供 Web 可视化配置界面和会话管理功能。详见 [Dashboard 使用指南](./dash_board_guide.md)。

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `DashBoard.Enabled` | 是否启用 Dashboard | `false` |
| `DashBoard.Host` | HTTP 服务监听地址 | `127.0.0.1` |
| `DashBoard.Port` | HTTP 服务监听端口 | `8080` |
| `DashBoard.Username` | 登录用户名，与 Password 同时设置时启用认证 | 空 |
| `DashBoard.Password` | 登录密码 | 空 |

---

## Logging 日志配置

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `Logging.Enabled` | 是否启用文件日志 | `true` |
| `Logging.Console` | 是否同时输出到控制台（stdout） | `false` |
| `Logging.MinLevel` | 最低日志级别：`Trace`/`Debug`/`Information`/`Warning`/`Error`/`Critical` | `Information` |
| `Logging.Directory` | 日志目录（相对于 `.craft/`） | `logs` |
| `Logging.RetentionDays` | 日志保留天数，`0` 表示永久保留 | `7` |

---

## Hooks 钩子配置

Hooks 允许在特定事件发生时执行自定义脚本。钩子配置文件默认位于 `.craft/hooks.json`，详情见 [Hooks 指南](./hooks_guide.md)。

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `Hooks.Enabled` | 是否启用 Hooks 系统 | `true` |

---

## Cron 定时任务服务

Cron 提供定时任务调度，支持一次性（`at`）和周期性（`every`）任务。

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `Cron.Enabled` | 是否启用定时任务 | `true` |
| `Cron.StorePath` | 任务存储文件路径（相对于 `.craft/`） | `cron/jobs.json` |

Agent 可通过 `Cron` 工具创建任务，支持投递到 QQ 群/私聊或企业微信。

---

## MCP 服务接入

DotCraft 支持通过 [Model Context Protocol (MCP)](https://modelcontextprotocol.io/) 接入外部工具服务。

`McpServers` 配置项为数组，每个元素定义一个 MCP 服务器：

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `Name` | 服务器名称 | 空 |
| `Enabled` | 是否启用该服务器 | `true` |
| `Transport` | 传输方式：`stdio`（本地进程）或 `http`（HTTP/SSE） | `stdio` |
| `Command` | 启动命令（仅 stdio） | 空 |
| `Arguments` | 命令参数列表（仅 stdio） | `[]` |
| `EnvironmentVariables` | 环境变量（仅 stdio） | `{}` |
| `Url` | 服务器地址（仅 http） | 空 |
| `Headers` | 附加 HTTP 请求头（仅 http） | `{}` |

**HTTP 示例**：
```json
{ "Name": "exa", "Transport": "http", "Url": "https://mcp.exa.ai/mcp" }
```

**Stdio 示例**：
```json
{ "Name": "filesystem", "Transport": "stdio", "Command": "npx", "Arguments": ["-y", "@modelcontextprotocol/server-filesystem", "/path"] }
```

### 延迟加载

当 MCP 工具数量较多时，可启用延迟加载以减少 Token 开销：

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `Tools.DeferredLoading.Enabled` | 是否启用延迟加载 | `false` |
| `Tools.DeferredLoading.AlwaysLoadedTools` | 始终预加载的 MCP 工具名称列表 | `[]` |
| `Tools.DeferredLoading.MaxSearchResults` | `SearchTools` 每次最多返回的工具数量 | `5` |
| `Tools.DeferredLoading.DeferThreshold` | MCP 工具总数低于此值时不触发延迟加载 | `10` |

---

## LSP 服务接入

DotCraft 支持通过 `LSP` 工具接入本地 Language Server（如 `typescript-language-server`、`csharp-ls`），用于定义跳转、引用查找、悬停信息、文档符号、调用层次等代码智能能力。

### 启用步骤

1. 打开 `Tools.Lsp.Enabled = true`
2. 在 `LspServers` 中配置一个或多个 LSP Server
3. （可选）通过 `EnabledTools` 控制是否允许 `LSP` 工具

> 说明：当前 `LspServers` 仅支持 `stdio` 传输方式。DotCraft 会按文件扩展名路由到对应 server，并在 `WriteFile` / `EditFile` 后自动发送 `didChange + didSave` 做状态同步。

### `LspServers` 字段说明

`LspServers` 建议使用 object-map 形式（键为 server 名）：

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

字段速查：

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `Name` | 服务器名称（object-map 形式下由键名提供） | 空 |
| `Enabled` | 是否启用该 LSP server | `true` |
| `Command` | 启动命令 | 空 |
| `Arguments` | 启动参数列表（兼容别名 `Args`） | `[]` |
| `ExtensionToLanguage` | 扩展名到语言 ID 的映射（如 `.cs -> csharp`） | `{}` |
| `Transport` | 传输方式（当前仅支持 `stdio`） | `stdio` |
| `EnvironmentVariables` | 子进程环境变量（兼容别名 `Env`） | `{}` |
| `WorkspaceFolder` | server 工作目录（可选，支持相对 workspace 路径） | 空 |
| `InitializationOptions` | `initialize` 请求附加 JSON | 空 |
| `Settings` | `workspace/didChangeConfiguration` 的 settings JSON | 空 |
| `StartupTimeoutMs` | 启动与初始化超时（毫秒） | `30000` |
| `MaxRestarts` | 崩溃后最大重启次数 | `3` |

### 综合示例

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

## 自定义命令（Custom Commands）

将常用提示词模板保存为 Markdown 文件，通过 `/命令名` 快速调用。

| 级别 | 路径 | 优先级 |
|------|------|--------|
| 工作区级 | `<workspace>/.craft/commands/` | 高 |
| 用户级 | `~/.craft/commands/` | 低 |

每个 `.md` 文件对应一个命令（如 `code-review.md` → `/code-review`）。支持 `$ARGUMENTS` 占位符接收用户参数。

内置命令：`/code-review`、`/explain`、`/summarize`。

---

## Gateway 多 Channel 并发模式

Gateway 模式允许外部渠道、API、AG-UI、Automations 等多个服务在同一进程中并发运行。

**显式启动**：Gateway 不再自动接管启动流程。需要并发 Channel / Automations 宿主时，请使用 `dotcraft gateway`。

**默认入口**：即使 `Automations.Enabled = true`，直接运行 `dotcraft` 仍然进入 CLI。

**后台能力默认值**：

- `Automations.Enabled` 默认为 `true`
- `GitHubTracker.Enabled` 默认为 `false`

**启用示例**：
```json
{
    "ExternalChannels": {
        "qq": { "enabled": true, "transport": "websocket" },
        "wecom": { "enabled": true, "transport": "websocket" }
    },
    "Api": { "Enabled": true, "Port": 8080 },
    "DashBoard": { "Enabled": true, "Port": 8080 }
}
```

所有 HTTP 服务通过 WebHostPool 统一管理，相同 `Host:Port` 的服务自动共享端口。
