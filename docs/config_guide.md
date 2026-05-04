# DotCraft 配置指南

本文面向第一次配置 DotCraft 的用户，说明配置文件放在哪里、如何合并，以及最常用的字段。完整字段表见 [完整配置参考](./reference/config.md)，安全细节见 [安全配置](./config/security.md)。

## 快速开始

最小可用配置只需要模型提供商信息：

```json
{
  "ApiKey": "sk-your-api-key",
  "Model": "gpt-4o-mini",
  "EndPoint": "https://api.openai.com/v1"
}
```

推荐做法：

| 配置 | 放在哪里 | 原因 |
|------|----------|------|
| API Key | `~/.craft/config.json` | 避免敏感信息进入项目仓库 |
| 项目模型覆盖 | `<workspace>/.craft/config.json` | 不同项目可使用不同模型和工具 |
| Dashboard / Automations / Gateway | `<workspace>/.craft/config.json` | 这些通常跟项目工作流绑定 |

如果你不确定放在哪里：API Key 放全局，其余先放工作区。

## 配置

DotCraft 支持两级配置：

| 配置文件 | 路径 | 用途 |
|----------|------|------|
| 全局配置 | `~/.craft/config.json` | 默认 API Key、模型和语言等个人设置 |
| 工作区配置 | `<workspace>/.craft/config.json` | 当前项目的模型覆盖、工具开关、入口和自动化设置 |

### 合并规则

- 全局配置作为基础。
- 工作区配置覆盖全局配置。
- 工作区没有设置的字段继续使用全局值。

### 示例

全局配置保存默认 API Key 和模型：

```json
{
  "ApiKey": "sk-your-default-api-key",
  "Model": "gpt-4o-mini",
  "EndPoint": "https://api.openai.com/v1"
}
```

工作区配置覆盖当前项目需要的模型和入口：

```json
{
  "Model": "deepseek-chat",
  "EndPoint": "https://api.deepseek.com/v1",
  "DashBoard": {
    "Enabled": true
  },
  "Automations": {
    "Enabled": true
  }
}
```

此时 DotCraft 会使用全局配置中的 `ApiKey`，并使用工作区中的 `Model`、`EndPoint`、Dashboard 和 Automations 设置。

### 常用字段

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `ApiKey` | OpenAI-compatible API Key | 空 |
| `Model` | 默认模型名称 | `gpt-4o-mini` |
| `EndPoint` | API 端点地址 | `https://api.openai.com/v1` |
| `Language` | 界面语言：`Chinese` / `English` | `Chinese` |
| `EnabledTools` | 全局启用的工具名称列表，为空时启用所有工具 | `[]` |
| `DebugMode` | 控制台不截断工具调用参数输出 | `false` |

DotCraft 的基础身份由系统内置。定制项目说明、工作规范或长期记忆时，优先使用 `.craft/AGENTS.md`、`.craft/SOUL.md`、`.craft/MEMORY.md` 等工作区文件。

## 使用示例

| 我想做什么 | 下一步 |
|------------|--------|
| 配置 API Key 和默认模型 | 编辑全局 `~/.craft/config.json` |
| 给某个项目换模型 | 编辑该项目的 `.craft/config.json` |
| 限制文件、Shell 或网络能力 | 阅读 [安全配置](./config/security.md) |
| 配置 SubAgent role、profile 和递归深度 | 阅读 [SubAgent 配置指南](./subagents_guide.md) |
| 查看所有配置字段 | 阅读 [完整配置参考](./reference/config.md) |
| 在界面中检查配置合并结果 | 打开 [Dashboard](./dash_board_guide.md) 的 Settings 页面 |

## 进阶

### 启动级配置

AppServer、Gateway、API、AG-UI、外部渠道、端口和 Dashboard 监听地址等字段在 Host 启动时读取。修改这些字段后，请重启对应 Host 或 Desktop 后台进程。

### 运行时配置

模型、工具开关、语言和部分 Agent 行为通常会影响新会话。已有会话是否立即改变，取决于入口和客户端是否重新加载配置。

### 安全边界

文件访问、Shell 执行、沙箱、黑名单和工作区外审批属于安全边界。建议先理解 [安全配置](./config/security.md)，再把 DotCraft 接入外部渠道、自动化或公网可访问服务。

## 故障排查

### 配置文件写了但没有生效

确认配置写在正确层级：个人默认值放全局，项目行为放工作区。启动级字段需要重启对应 Host。

### API Key 不想提交进仓库

把 `ApiKey` 放在全局 `~/.craft/config.json`，工作区配置只放模型、Endpoint 和项目功能。

### Dashboard 或 API 端口冲突

修改 `DashBoard.Port`、`Api.Port`、`AgUi.Port` 或使用 Gateway 共享端口。端口字段属于启动级配置，修改后需要重启。

### 工具访问被拒绝

检查 `Security.BlacklistedPaths`、`Tools.File.RequireApprovalOutsideWorkspace`、`Tools.Shell.RequireApprovalOutsideWorkspace` 和沙箱配置。
