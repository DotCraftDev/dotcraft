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
    "QQBot": {
        "Enabled": true,
        "Port": 6700,
        "AdminUsers": [123456789]
    }
}
```

此时 DotCraft 会使用全局配置中的 `ApiKey`，但使用工作区中的 `Model`、`EndPoint` 和 `QQBot` 设置。

---

## 使用 Dashboard 可视化配置

除了直接编辑 JSON 文件外，DotCraft 内置的 Dashboard 提供了**可视化配置页面**，让配置更加直观、安全。

Dashboard 默认在 `http://127.0.0.1:8080/dashboard` 启动（随 DotCraft 自动开启），打开后点击左侧导航栏的 **Settings** 即可进入配置页面。

### 页面功能

- **左栏（全局配置）**：只读展示 `~/.craft/config.json` 的当前内容，敏感字段（ApiKey、密码等）自动屏蔽显示
- **右栏（工作区配置）**：可编辑 `.craft/config.json`，覆盖全局配置的各项参数；留空则继承全局值
- **底部预览**：实时展示合并后的生效配置，并标注每个字段的来源（全局或工作区）
- **Save 按钮**：保存工作区配置，重启 DotCraft 后生效

Dashboard 的配置表单结构（字段类型、说明、可选值等）由服务端从 C# 配置类的元数据中**自动生成**，无需手动维护与代码的同步。

### 安全说明

- ApiKey、密码等敏感字段在界面上始终以 `***` 显示，不会明文传输
- 如果某个敏感字段未修改（仍显示为 `***`），保存时会自动保留磁盘上的原始值，不会覆盖
- 只有实际填写了新值，才会更新该字段

> 配置保存后需要**手动重启 DotCraft** 才能生效。

---

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
| `CompactSessions` | 保存会话时是否压缩（移除工具调用消息） | `true` |
| `MaxContextTokens` | 触发自动上下文压缩的累积输入 Token 阈值。`0` 表示禁用压缩 | `160000` |
| `MemoryWindow` | 触发记忆整合的消息数量阈值。`0` 表示禁用 | `50` |
| `ConsolidationModel` | 记忆整合专用模型。为空时使用主 `Model`；如果主模型在思考模式下不支持 `tool_choice`，建议指定非思考模型 | 空 |
| `DebugMode` | 调试模式：控制台不截断工具调用参数输出 | `false` |
| `EnabledTools` | 全局启用的工具名称列表，为空时启用所有工具 | `[]` |

DotCraft 的基础身份由系统内置；如果你想定制系统提示词，建议使用 `.craft/AGENTS.md`、`.craft/SOUL.md` 等 bootstrap 文件，而不是在 `config.json` 中维护单独字段。

### Reasoning（推理）配置

控制 LLM Provider 的 Reasoning/Thinking 行为。不支持的 Provider 或模型会忽略这些设置。

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `Reasoning.Enabled` | 是否请求 Provider 的推理支持 | `true` |
| `Reasoning.Effort` | 推理深度：`None` / `Low` / `Medium` / `High` / `ExtraHigh` | `Medium` |
| `Reasoning.Output` | 推理内容是否暴露在响应中：`None` / `Summary` / `Full` | `Full` |

---

## 安全配置

### 文件访问黑名单

通过 `Security.BlacklistedPaths` 配置禁止访问的路径列表。黑名单是**全局生效**的，无论 CLI 模式还是 QQ Bot 模式都会拦截。

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

### 沙箱模式（OpenSandbox）

DotCraft 支持通过 [OpenSandbox](https://github.com/alibaba/OpenSandbox) 将 Shell 和 File 工具的执行环境从宿主机迁移到隔离的 Docker 容器中。启用后，Agent 的所有命令执行和文件操作都在容器内完成，对宿主机零风险。

**前置条件**：
1. Docker 运行在宿主机或远程服务器
2. 安装并启动 OpenSandbox Server：`pip install opensandbox-server && opensandbox-server`
3. 在配置中设置 `Tools.Sandbox.Enabled = true` 并指定服务器地址

#### 沙箱配置项

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `Tools.Sandbox.Enabled` | 是否启用沙箱模式 | `false` |
| `Tools.Sandbox.Domain` | OpenSandbox 服务地址（host:port） | `localhost:5880` |
| `Tools.Sandbox.ApiKey` | OpenSandbox API Key（可选，取决于服务端配置） | 空 |
| `Tools.Sandbox.UseHttps` | 是否使用 HTTPS 连接 | `false` |
| `Tools.Sandbox.Image` | 沙箱容器使用的 Docker 镜像 | `ubuntu:latest` |
| `Tools.Sandbox.TimeoutSeconds` | 沙箱超时时间（秒），到期后服务端自动销毁 | `600` |
| `Tools.Sandbox.Cpu` | 容器 CPU 限制 | `1` |
| `Tools.Sandbox.Memory` | 容器内存限制 | `512Mi` |
| `Tools.Sandbox.NetworkPolicy` | 网络策略：`deny`（禁止所有出站）、`allow`（无限制）、`custom`（自定义规则） | `allow` |
| `Tools.Sandbox.AllowedEgressDomains` | 自定义允许出站的域名列表（仅 `NetworkPolicy = custom` 时生效） | `[]` |
| `Tools.Sandbox.IdleTimeoutSeconds` | 空闲超时（秒），超过此时间未使用自动销毁沙箱。设为 0 禁用 | `300` |
| `Tools.Sandbox.SyncWorkspace` | 创建沙箱时是否将宿主机 workspace 同步到容器 `/workspace` | `true` |
| `Tools.Sandbox.SyncExclude` | 同步时排除的相对路径列表（支持路径前缀匹配，详见下方说明） | 见下方默认值 |

#### 沙箱配置示例

**最小配置**（本地 OpenSandbox 服务，默认设置）：

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

**生产配置**（远程服务 + 自定义网络策略 + 资源限制）：

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

#### SyncExclude 匹配规则

`SyncExclude` 中的每条记录均为相对于 workspace 根目录的路径前缀（使用正斜杠）：

- 精确文件路径：`.craft/config.json` → 仅跳过该文件
- 目录前缀：`.craft/sessions` → 跳过 `.craft/sessions` 目录及其所有子内容
- 匹配不区分大小写（Windows 兼容）

> **安全提示**：默认值已覆盖 DotCraft 的敏感运行时目录。如需自定义，建议保留这些默认项，在其基础上追加，而非整体替换。

#### 工作原理

启用沙箱后：

1. **工具替换**：`CoreToolProvider` 不再提供本地的 `ShellTools` / `FileTools`，由 `SandboxToolProvider` 提供容器化的等价工具。工具名称保持一致（`Exec`、`ReadFile`、`WriteFile`、`EditFile`、`GrepFiles`、`FindFiles`），系统提示词无需修改
2. **生命周期管理**：每个 Agent Session 自动创建并复用一个沙箱容器，空闲超时后自动销毁
3. **Workspace 同步**：`SyncWorkspace = true` 时，沙箱创建后自动将宿主机 workspace 的文件推送到容器 `/workspace` 目录；`SyncExclude` 中列出的路径会被跳过，默认排除 `.craft/` 下所有敏感运行时数据（API Key、会话历史、记忆、Trace、审批记录、日志、计划），防止宿主机敏感数据泄露到容器
4. **SubAgent 隔离**：SubAgent 也在沙箱内执行，共享同一个容器实例

#### 安全模型对比

| 维度 | 本地模式 | 沙箱模式 |
|------|---------|----------|
| 命令执行 | 宿主机 + 正则拦截 | 隔离容器 |
| 文件访问 | workspace 边界检查 | 容器文件系统隔离 |
| 网络访问 | 无限制 | NetworkPolicy 控制 |
| 资源消耗 | 无限制 | CPU/Memory 硬限制 |
| 绕过风险 | 正则可能被绕过 | 容器逃逸（极低概率） |
| 人工审批 | 每次 workspace 外操作 | 仅回写宿主机时需要 |
| 性能开销 | 无 | 容器启动 ~2-5s |

#### 向后兼容

`Tools.Sandbox.Enabled = false`（默认值）时，行为与之前完全一致，所有工具在宿主机本地执行。

---

## QQ Bot 配置

QQ Bot 的详细配置说明请参考 [QQ 机器人使用指南](./qq_bot_guide.md)。

以下是 QQ Bot 配置项速查：

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `QQBot.Enabled` | 是否启用 QQ 机器人模式 | `false` |
| `QQBot.Host` | WebSocket 监听地址 | `127.0.0.1` |
| `QQBot.Port` | WebSocket 监听端口 | `6700` |
| `QQBot.AccessToken` | 鉴权 Token（需与 NapCat 一致） | 空 |
| `QQBot.AdminUsers` | 管理员 QQ 号列表 | `[]` |
| `QQBot.WhitelistedUsers` | 白名单用户 QQ 号列表 | `[]` |
| `QQBot.WhitelistedGroups` | 白名单群号列表 | `[]` |
| `QQBot.ApprovalTimeoutSeconds` | 操作审批超时（秒） | `60` |

---

## WeCom Bot 配置

WeCom Bot 的详细配置说明请参考 [企业微信指南](./wecom_guide.md)。

以下是 WeCom Bot 配置项速查：

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `WeComBot.Enabled` | 是否启用企业微信机器人模式 | `false` |
| `WeComBot.Host` | HTTP 服务监听地址 | `0.0.0.0` |
| `WeComBot.Port` | HTTP 服务监听端口 | `9000` |
| `WeComBot.AdminUsers` | 管理员用户 ID 列表（企业微信 UserId） | `[]` |
| `WeComBot.WhitelistedUsers` | 白名单用户 ID 列表（企业微信 UserId） | `[]` |
| `WeComBot.WhitelistedChats` | 白名单会话 ID 列表（企业微信 ChatId） | `[]` |
| `WeComBot.ApprovalTimeoutSeconds` | 操作审批超时（秒） | `60` |
| `WeComBot.Robots` | 机器人配置列表（Path/Token/AesKey） | `[]` |

**注意**：默认情况下（`Gateway.Enabled = false`），QQ Bot、WeCom Bot 和 API 模式不能同时启用，按 QQ Bot > WeCom Bot > API > CLI 的优先级选择。若需同时运行多个 Channel，请启用 [Gateway 模式](#gateway-多-channel-并发模式)。

**权限说明**：
- `AdminUsers`：拥有所有权限，工作区内写入操作需要审批
- `WhitelistedUsers`：只能执行读取操作（文件读取、Web 搜索等）
- `WhitelistedChats`：该会话中的所有用户自动获得白名单权限
- 未在上述列表中的用户无法使用 Agent 功能

**审批机制**：
- 管理员执行工作区内/外写入操作时，会在企业微信会话中收到审批请求
- 回复 "同意" 或 "允许" 批准操作，回复 "同意全部" 则本会话中不再询问同类操作
- 回复 "拒绝" 或不回复（超时）则拒绝操作
- 审批超时时间可通过 `ApprovalTimeoutSeconds` 配置

---

## 会话压缩

`CompactSessions` 控制是否在保存会话时自动移除工具调用相关的消息，以减少会话文件体积并避免加载时的冗余数据。

**压缩行为**：
- 移除所有 `role: tool` 的消息（工具返回结果）
- 移除 assistant 消息中的 `FunctionCallContent`（工具调用指令）
- 如果 assistant 消息在移除 FunctionCallContent 后没有其他内容，则整条消息被移除
- 保留 user 消息和 assistant 的文本回复

**何时关闭**：如果你需要在会话文件中保留完整的工具调用历史（例如调试用途），可以设置 `"CompactSessions": false`。

---

## Token 用量统计

DotCraft 会自动从 LLM 响应中提取 token 用量信息并显示。

- **CLI 模式**：每次回复后在控制台显示 `Tokens: X in / Y out / Z total`
- **QQ 模式**：每次回复末尾附加 `[Tokens: X in / Y out / Z total]`

无需额外配置。token 统计依赖 LLM 提供商在 streaming 响应中返回 `UsageContent`，部分提供商可能不支持。

---

## Heartbeat 心跳服务

Heartbeat 定时读取 .craft 目录的 `HEARTBEAT.md` 文件，如有可执行内容则自动交给 Agent 处理。适用于定期巡检、状态监控等场景。

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `Heartbeat.Enabled` | 是否启用心跳服务 | `false` |
| `Heartbeat.IntervalSeconds` | 检查间隔（秒） | `1800`（30 分钟） |
| `Heartbeat.NotifyAdmin` | QQ 模式下是否将结果私信通知管理员 | `false` |

### Heartbeat 配置示例

```json
{
    "Heartbeat": {
        "Enabled": true,
        "IntervalSeconds": 1800,
        "NotifyAdmin": false
    }
}
```

**控制台输出**：Heartbeat 执行时会在控制台输出工具调用和结果，格式为 `[Heartbeat] tool_name args` / `[Heartbeat] Result: ...`，方便调试。

**管理员通知**：QQ 模式下设置 `"NotifyAdmin": true`，Heartbeat 自动执行后会将结果私信发送给所有 `AdminUsers`。

### Heartbeat 使用方法

1. 在 .craft 目录创建 `HEARTBEAT.md` 文件
2. 写入需要定期执行的任务：

```markdown
# Heartbeat Tasks

## Active Tasks
- 检查项目是否有新的 GitHub issue 并汇总
- 检查日志文件是否有异常
```

3. 启动 DotCraft（QQ 模式下自动运行，CLI 模式下可通过 `/heartbeat trigger` 手动触发）
4. Agent 会根据 HEARTBEAT.md 的内容自动执行任务。如果文件为空或仅包含标题/注释，则跳过
5. Heartbeat 拥有独立的 Session 管理，但和主 Agent 共享长期记忆。

### 手动触发

- **CLI 模式**：输入 `/heartbeat trigger`
- **QQ 模式**：发送 `/heartbeat trigger`

---

## 企业微信集成

DotCraft 提供两种企业微信集成能力：

| 能力 | 配置节 | 说明 |
|------|--------|------|
| 企业微信推送 | `WeCom` | 通过群机器人 Webhook 向企业微信群发送通知 |
| 企业微信机器人 | `WeComBot` | 作为独立运行模式，接收并响应企业微信消息 |

### 快速配置

**企业微信推送**（Webhook 通知）：

```json
{
    "WeCom": {
        "Enabled": true,
        "WebhookUrl": "https://qyapi.weixin.qq.com/cgi-bin/webhook/send?key=YOUR_KEY"
    }
}
```

**企业微信机器人模式**（双向交互）：

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

详细配置、使用方式、部署指南和故障排查见 [企业微信指南](./wecom_guide.md)。

---

## GitHubTracker 配置

GitHubTracker 模块用于自动轮询 GitHub Issue、为每个 Issue 创建独立工作区、派发 Agent 完成代码任务，并在完成后通过 `CompleteIssue` 收敛流程。

完整使用说明见 [GitHubTracker 指南](./github_tracker_guide.md)。

### 快速配置

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

### 关键配置项

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `GitHubTracker.Enabled` | 是否启用 GitHubTracker 模块 | `false` |
| `GitHubTracker.IssuesWorkflowPath` | Issue `WORKFLOW.md` 路径，相对工作区根目录解析 | `WORKFLOW.md` |
| `GitHubTracker.Tracker.Repository` | GitHub 仓库，格式 `owner/repo` | 空 |
| `GitHubTracker.Tracker.ApiKey` | GitHub Token，支持 `$ENV_VAR` | 空 |
| `GitHubTracker.Tracker.GitHubStateLabelPrefix` | 用于推断 Issue 状态的标签前缀 | `status:` |
| `GitHubTracker.Tracker.AssigneeFilter` | 仅处理分配给指定用户的 Issue | 空 |

## API 模式配置

API 模式将 DotCraft 作为 OpenAI 兼容的 HTTP 服务暴露，外部应用可直接使用标准 OpenAI SDK 调用。基于 [Microsoft.Agents.AI.Hosting.OpenAI](https://github.com/microsoft/agent-framework) 官方框架实现。

详细使用指南见 [API 模式指南](./api_guide.md)。

以下是 API 模式配置项速查：

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `Api.Enabled` | 是否启用 API 模式 | `false` |
| `Api.Host` | HTTP 服务监听地址 | `127.0.0.1` |
| `Api.Port` | HTTP 服务监听端口 | `8080` |
| `Api.ApiKey` | API 访问密钥（Bearer Token），为空时不验证 | 空 |
| `Api.AutoApprove` | 是否自动批准所有文件/Shell 操作（被 ApprovalMode 覆盖） | `true` |
| `Api.ApprovalMode` | 审批模式：`auto`/`reject`/`interactive` | 空 |
| `Api.ApprovalTimeoutSeconds` | interactive 模式下审批超时（秒） | `120` |

### API 模式配置示例

**基本配置**（启用所有工具，无认证）：

```json
{
    "Api": {
        "Enabled": true,
        "Port": 8080,
        "AutoApprove": true
    }
}
```

**仅暴露搜索工具**（适用于搜索服务场景）：

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

### 工具过滤

根级别的 `EnabledTools` 字段控制全局可用工具。为空数组或不设置时启用所有工具，对所有运行模式（CLI、API、QQ Bot 等）生效。

可用的内置工具名称：`spawn_subagent`、`ReadFile`、`WriteFile`、`EditFile`、`GrepFiles`、`FindFiles`、`Exec`、`WebSearch`、`WebFetch`、`Cron`、`WeComNotify`。

### 认证

当 `Api.ApiKey` 配置为非空值时，所有 API 请求需要携带 Bearer Token：

```
Authorization: Bearer your-api-access-key
```

### 审批机制

API 模式支持三种审批模式，通过 `ApprovalMode` 配置（设置后覆盖 `AutoApprove`）：

- **`auto`**：所有文件操作和 Shell 命令自动批准（等价 `AutoApprove: true`）
- **`reject`**：所有文件操作和 Shell 命令自动拒绝（等价 `AutoApprove: false`）
- **`interactive`**：Human-in-the-Loop 模式，敏感操作暂停等待 API 客户端通过 `/v1/approvals` 端点审批

`ApprovalTimeoutSeconds` 控制 interactive 模式下的审批超时时间（默认 120 秒），超时未审批则自动拒绝。

详细说明和 Python 示例见 [API 模式指南](./api_guide.md#human-in-the-loop-交互式审批)。

---

## AG-UI 模式配置

AG-UI 模式通过 [AG-UI 协议](https://github.com/ag-ui-protocol/ag-ui) 将 Agent 能力以 SSE 流式推送的方式对外暴露，兼容 CopilotKit 等 AG-UI 客户端。

详细使用指南见 [AG-UI 模式指南](./agui_guide.md)。

以下是 AG-UI 模式配置项速查：

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

ACP（[Agent Client Protocol](https://agentclientprotocol.com/)）模式允许 DotCraft 作为 AI 编码代理直接与代码编辑器/IDE（如 Cursor、VS Code 等兼容编辑器）集成。编辑器通过 stdio（标准输入/输出）以 JSON-RPC 2.0 协议与 DotCraft 通信，类似 LSP（Language Server Protocol）的工作方式。

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `Acp.Enabled` | 是否启用 ACP 模式 | `false` |

### 启用 ACP 模式

```json
{
    "Acp": {
        "Enabled": true
    }
}
```

### 工作原理

启用后，DotCraft 将通过 stdin/stdout 接收和发送 JSON-RPC 消息：

1. **编辑器启动 DotCraft 进程**：编辑器将 DotCraft 作为子进程启动，并通过 stdio 通信
2. **初始化握手**：双方交换协议版本和能力声明
3. **会话管理**：编辑器创建会话，DotCraft 广播可用的斜杠命令和配置选项
4. **提示交互**：编辑器发送用户消息，DotCraft 流式返回回复、工具调用状态和执行结果
5. **权限请求**：DotCraft 执行敏感操作前通过协议向编辑器请求用户授权

### 支持的 ACP 协议功能

| 功能 | 说明 |
|------|------|
| `initialize` | 协议版本协商和能力交换 |
| `session/new` | 创建新会话 |
| `session/load` | 加载已有会话并回放历史 |
| `session/list` | 列出所有 ACP 会话 |
| `session/prompt` | 发送提示并流式接收回复 |
| `session/update` | Agent 向编辑器推送消息块、工具调用状态等 |
| `session/cancel` | 取消正在进行的操作 |
| `requestPermission` | Agent 向编辑器请求执行权限 |
| `fs/readTextFile` | 通过编辑器读取文件（含未保存内容） |
| `fs/writeTextFile` | 通过编辑器写入文件（可预览 diff） |
| `terminal/*` | 通过编辑器创建和管理终端 |
| Slash Commands | 自动广播自定义命令到编辑器 |
| Config Options | 暴露可选配置（模式、模型等）到编辑器 |

### 与其他模式的关系

- ACP 模式可以作为独立模式运行（优先级高于 API 模式）
- 在 Gateway 模式下，ACP 可以与 QQ Bot、WeCom Bot、API 并发运行
- ACP 模式的会话 ID 以 `acp:` 为前缀，与其他 Channel 隔离

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

Cron 是一个定时任务调度系统，支持一次性和周期性任务。任务持久化到 JSON 文件中，重启后自动恢复。

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `Cron.Enabled` | 是否启用定时任务 | `false` |
| `Cron.StorePath` | 任务存储文件路径（相对于 `.craft/`） | `cron/jobs.json` |

### Cron 配置示例

```json
{
    "Cron": {
        "Enabled": true,
        "StorePath": "cron/jobs.json"
    }
}
```

### 调度类型

| 类型 | 说明 | 参数 |
|------|------|------|
| `at` | 一次性任务，在指定时间执行后自动删除 | `AtMs`（Unix 毫秒时间戳） |
| `every` | 周期性任务，按固定间隔重复执行 | `EveryMs`（间隔毫秒数） |

### 投递渠道

Cron 任务支持多种投递渠道（需在创建任务时设置 `deliver: true`）：

| `channel` | `to` | 说明 | 前置条件 |
|-----------|------|------|----------|
| `"qq"` | `"group:<群号>"` | 投递到指定 QQ 群 | QQ Bot 模式 |
| `"qq"` | `"<QQ号>"` | 投递到指定 QQ 私聊 | QQ Bot 模式 |
| `"wecom"` | `"<ChatId>"` | 投递到企业微信指定群 | WeCom Bot 模式 |
| `"wecom"` | 不填 | 投递到企业微信（全局 Webhook） | 启用 `WeCom` 配置 |

### Agent 自助创建任务

启用 Cron 后，Agent 可通过 `Cron` 工具自行创建定时任务。例如在对话中说"每小时提醒我喝水"，Agent 会调用：

```
Cron(action: "add", message: "提醒用户喝水", everySeconds: 3600, name: "喝水提醒")
```

### 命令行管理

**CLI 模式**：
- `/cron list` — 查看所有任务
- `/cron remove <jobId>` — 删除任务
- `/cron enable <jobId>` — 启用任务
- `/cron disable <jobId>` — 禁用任务

**QQ 模式**：
- `/cron list` — 查看所有任务
- `/cron remove <jobId>` — 删除任务

---

## MCP 服务接入

DotCraft 支持通过 [Model Context Protocol (MCP)](https://modelcontextprotocol.io/) 接入外部工具服务。MCP 是一个开放协议，用于标准化 AI 应用与外部工具/数据源的集成方式。

配置后，MCP 服务器提供的工具会自动注册到 Agent 中，与内置工具（文件、Shell、Web 等）一起使用。

### 配置项

`McpServers` 是一个数组，每个元素定义一个 MCP 服务器连接：

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `Name` | 服务器名称（用于日志和工具追踪） | 空 |
| `Enabled` | 是否启用该服务器 | `true` |
| `Transport` | 传输方式：`stdio`（本地进程）或 `http`（HTTP/SSE） | `stdio` |
| `Command` | 启动命令（仅 stdio） | 空 |
| `Arguments` | 命令参数列表（仅 stdio） | `[]` |
| `EnvironmentVariables` | 环境变量（仅 stdio） | `{}` |
| `Url` | 服务器地址（仅 http），如 `https://mcp.exa.ai/mcp` | 空 |
| `Headers` | 附加 HTTP 请求头（仅 http） | `{}` |

### 传输方式

**HTTP/SSE 传输**：连接远程 MCP 服务器，适用于云端 MCP 服务（如 Exa）。

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

**Stdio 传输**：启动本地进程并通过 stdin/stdout 通信，适用于本地 MCP 服务器。

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

### 带认证的 MCP 服务器

部分 MCP 服务器需要 API Key 认证，可通过 `Headers`（HTTP）或 `EnvironmentVariables`（stdio）传递：

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

### 多服务器配置

可以同时接入多个 MCP 服务器，所有服务器的工具会合并注册到 Agent：

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

### 禁用单个服务器

设置 `Enabled: false` 可临时禁用某个 MCP 服务器，无需删除配置：

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

### 启动行为

- MCP 服务器在应用启动时自动连接，连接成功后会在控制台输出已发现的工具数量
- 连接失败不会阻止应用启动，失败的服务器会输出错误日志
- 应用退出时自动断开所有 MCP 连接

### Exa 搜索迁移说明

DotCraft 内置的 `Tools.Web.SearchProvider: "Exa"` 使用的是手动 MCP 调用方式。现在可以通过 MCP 配置替代：

1. 在 `McpServers` 中添加 Exa 服务器配置
2. 将 `Tools.Web.SearchProvider` 切换为 `Bing` 或其他提供商
3. Agent 将通过 MCP 获得 Exa 的所有工具（不仅是搜索），包括 `WebSearch_exa`、`research_exa` 等

### 浏览器自动化（Playwright MCP）

微软官方提供了 [`@playwright/mcp`](https://github.com/microsoft/playwright-mcp) MCP 服务器，可让 Agent 控制真实浏览器（Chromium/Firefox/WebKit）执行网页交互任务。该方案基于无障碍树（Accessibility Tree）而非截图，速度快、确定性强，无需视觉模型。

**前置条件**：Node.js 18+，以及安装浏览器：

```bash
npx playwright install chromium
```

**配置示例**（添加到 `config.json`）：

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

配置后，Agent 将自动获得 22 个浏览器控制工具，包括：`browser_navigate`、`browser_snapshot`、`browser_click`、`browser_type`、`browser_fill_form`、`browser_take_screenshot` 等。

### MCP 工具延迟加载

> **实验性功能**：此功能仍在测试中，行为可能在后续版本中调整。

当接入的 MCP 服务器较多、工具总数超过一定阈值时，将所有工具定义一次性发送给 LLM 会带来显著的 Token 开销，并可能降低模型的工具选择精度。**延迟加载**（Deferred Loading）允许 Agent 按需发现并激活 MCP 工具，而非在会话开始时全量注入。

启用后，Agent 不再将所有 MCP 工具一次性注入上下文，而是获得一个 `SearchTools` 工具用于按需检索。找到匹配工具后，该工具立即激活，可在后续调用中直接使用。

#### 工作原理

1. Agent 需要某项外部能力时，调用 `SearchTools(query: "关键词")`
2. 系统在所有延迟工具中进行模糊搜索，返回最匹配的结果
3. 匹配的工具立即注入到下一次 LLM 调用的工具列表中
4. 工具列表在会话内单调递增，确保 Prompt Cache 在首次发现后可以稳定复用

#### 配置项

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `Tools.DeferredLoading.Enabled` | 是否启用延迟加载 | `false` |
| `Tools.DeferredLoading.AlwaysLoadedTools` | 始终预加载的 MCP 工具名称列表（高频工具） | `[]` |
| `Tools.DeferredLoading.MaxSearchResults` | `SearchTools` 每次最多返回的工具数量（1-20） | `5` |
| `Tools.DeferredLoading.DeferThreshold` | MCP 工具总数低于此值时不触发延迟加载 | `10` |

#### 配置示例

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

#### 配合 Skill 引导 Agent 搜索

启用延迟加载后，Agent 的系统提示词会自动包含已连接 MCP 服务的名称列表和使用 `SearchTools` 的基本指引。但为了让 Agent 在特定场景下能更准确地知道"应该搜索什么关键词"，推荐在 `.craft/skills/` 中编写专项 Skill，描述每个 MCP 服务的能力和典型用法。

**示例 Skill**（`.craft/skills/github-mcp/SKILL.md`）：

```markdown
# GitHub MCP 工具

连接了 GitHub MCP 服务器，提供以下类型的工具：
- Issue 管理：搜索关键词 "github issue"
- Pull Request：搜索关键词 "github pull request" 或 "github pr"
- 仓库操作：搜索关键词 "github repo" 或 "github repository"
- 代码搜索：搜索关键词 "github search code"

使用前请先调用 SearchTools 激活所需工具。
```

这类 Skill 配合工作区的 `AGENTS.md` 或按需读取机制，可以显著提升 Agent 选择正确关键词的准确率。

---

## 自定义命令（Custom Commands）

自定义命令允许你将常用的提示词模板保存为 Markdown 文件，通过 `/命令名` 快速调用。DotCraft 会将命令文件的内容展开为完整提示词，然后交给 Agent 处理。

### 命令文件位置

| 级别 | 路径 | 优先级 |
|------|------|--------|
| 工作区级 | `<workspace>/.craft/commands/` | 高（覆盖同名用户级命令） |
| 用户级 | `~/.craft/commands/` | 低 |

### 命令文件格式

每个 `.md` 文件对应一个命令，文件名即命令名（如 `code-review.md` → `/code-review`）。

文件支持 YAML Frontmatter 定义元数据：

```markdown
---
description: 审查代码变更，检查 Bug、安全问题和代码风格
---

运行 `git diff` 查看当前分支的所有变更，然后审查：
1. 正确性和逻辑错误
2. 安全漏洞
3. 边界情况和错误处理
4. 代码风格和可读性

$ARGUMENTS
```

### 占位符

| 占位符 | 说明 |
|--------|------|
| `$ARGUMENTS` | 用户输入的完整参数（`/cmd foo bar` → `foo bar`） |
| `$1`, `$2`, ... | 按空格拆分的位置参数 |

### 子目录命名空间

支持子目录组织命令，目录分隔符映射为 `:`：

```
commands/
├── code-review.md      → /code-review
├── explain.md          → /explain
└── frontend/
    └── component.md    → /frontend:component
```

### 内置命令

DotCraft 自带以下内置命令，首次运行时自动部署到工作区 `commands/` 目录：

| 命令 | 说明 |
|------|------|
| `/code-review` | 审查代码变更 |
| `/explain` | 详细解释代码 |
| `/summarize` | 简洁总结内容 |

你可以直接编辑这些文件来自定义行为，也可以添加新的 `.md` 文件来创建自己的命令。

### CLI 命令

- `/commands` — 列出所有可用的自定义命令

---

## QQ Bot 命令

QQ Bot 模式下支持以下斜杠命令（直接在聊天中发送）：

| 命令 | 说明 |
|------|------|
| `/new` 或 `/clear` | 清除当前会话，开始新对话 |
| `/help` | 显示可用命令列表 |
| `/heartbeat trigger` | 手动触发一次心跳检查 |
| `/cron list` | 查看所有定时任务 |
| `/cron remove <id>` | 删除指定定时任务 |

---

## Gateway 多 Channel 并发模式

默认情况下，DotCraft 每次只运行一个 Channel 模块（优先级最高者胜出）。**Gateway 模式**打破这一限制，允许 QQ Bot、WeCom Bot、API 服务在**同一个进程**中并发运行，共享 HeartbeatService、CronService 和 DashBoard。

### 启用方式

在配置中同时设置 `Gateway.Enabled = true` 和所有需要启用的 Channel：

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

### 配置项

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `Gateway.Enabled` | 是否启用 Gateway 多 Channel 并发模式 | `false` |

### 工作原理

启用后，DotCraft 会：

1. 将 GatewayModule 作为主模块（优先级最高，为 100）
2. 为每个 enabled 的 Channel（QQ / WeCom / API / AG-UI）独立创建 AgentFactory、ChannelAdapter 和网络监听
3. 通过 **WebHostPool** 按 `(scheme, host, port)` 分组管理所有 HTTP 服务：配置到同一地址的服务自动共享同一个 Kestrel 服务器，无需手动协调端口
4. 并发启动所有 Channel，每个 Channel 拥有独立的流式交互和审批工作流
5. 共享一套 HeartbeatService 和 CronService，通过 MessageRouter 将结果投递到正确 Channel

```
GatewayHost
├── HeartbeatService (共享，按 Channel 路由通知)
├── CronService (共享，按 Payload.Channel 路由投递)
├── WebHostPool (按 host:port 分组，自动合并同地址服务)
│   ├── http://127.0.0.1:8080 ← ApiChannelService + DashBoard 路由
│   └── http://127.0.0.1:5100 ← AGUIChannelService 路由
├── QQChannelService    → QQChannelAdapter → Agent (独立)
├── WeComChannelService → WeComChannelAdapter → Agent (独立)
├── ApiChannelService   → OpenAI API 端点 → Agent (独立)
└── AGUIChannelService  → AG-UI SSE 端点 → Agent (独立)
```

### 端口共享

Gateway 模式下，所有 HTTP 服务（API、AG-UI、DashBoard）均通过 **WebHostPool** 统一管理。只要 `Host` 和 `Port` 配置相同，相关服务会自动合并到同一个 Kestrel 监听端口，共享同一个 Web 应用实例，路由按各自路径前缀分发。

**默认端口分配**：

| 服务 | 默认 Host | 默认 Port |
|------|-----------|-----------|
| `Api` | `127.0.0.1` | `8080` |
| `DashBoard` | `127.0.0.1` | `8080` |
| `AgUi` | `127.0.0.1` | `5100` |
| `WeComBot` | `0.0.0.0` | `9000` (HTTPS) |

由于 `Api` 和 `DashBoard` 默认端口相同，两者在 Gateway 模式下默认共享同一服务器实例。若希望分开部署，修改 `DashBoard.Port` 为不同值即可。

**示例场景**：

- `Api.Port = 8080`、`DashBoard.Port = 8080`（默认）：API 路由和 Dashboard 路由合并到 `127.0.0.1:8080`，单端口对外
- `Api.Port = 8080`、`AgUi.Port = 8080`：API 和 AG-UI 合并到同一端口，AG-UI 通过 `/ag-ui` 路径区分
- `Api.Port = 8080`、`AgUi.Port = 5100`（默认）：两者各自监听独立端口，互不影响

### Cron 跨 Channel 投递

Gateway 模式下，Cron 任务的 `deliver` 功能通过 `channel` 字段路由到对应 Channel：

| `channel` 值 | 投递目标 | 示例 `to` |
|---|---|---|
| `"qq"` | QQ 私聊（`to` 为 QQ 号）或群聊（`to` 为群号加 `group:` 前缀） | `"123456789"` / `"group:98765432"` |
| `"wecom"` | 企业微信指定群（`to` 为 ChatId）；不填 `to` 则回退到全局 `WeCom.WebhookUrl` | `"wrxxxxxxxx"` |
| `"api"` | API Channel 无主动投递能力，忽略 | — |
| `"ag-ui"` | AG-UI Channel 无主动投递能力，忽略 | — |

### Heartbeat 跨 Channel 通知

Heartbeat 结果会通过 MessageRouter 广播到所有有管理员配置的 Channel：
- QQ：私信所有 `QQBot.AdminUsers`
- WeCom：通过 `WeCom.WebhookUrl` 发送到企业微信群（需同时配置 `WeCom.WebhookUrl`）

### 向后兼容

`Gateway.Enabled = false`（默认值）时，行为与之前完全一致，按优先级选择单一模块运行。

---

## 完整配置示例

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
    "Gateway": {
        "Enabled": false
    },
    "McpServers": []
}
```
