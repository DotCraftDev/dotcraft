# DotCraft DashBoard 调试工具指南

DotCraft DashBoard 是一个内置的 Web 调试工具，用于实时查看 Agent 执行过程中的请求/响应数据、工具调用链路、Token 用量统计等 Trace 数据。灵感来源于 [Microsoft Agent Framework DevUI](https://learn.microsoft.com/en-us/agent-framework/user-guide/devui/?pivots=programming-language-python)（Python 版），但不包含对话和 workflow 编排功能。

## 功能概览

| 功能 | 说明 |
|------|------|
| Dashboard | 总览统计：会话数、请求数、工具调用次数、Token 用量（输入/输出/总计） |
| Sessions | 会话列表，按最近活动排序，支持查看详情和删除 |
| Trace Timeline | 每个会话的事件时间线，支持按类型过滤（请求、响应、工具调用、Token、错误） |
| Tools | 注册的工具列表及图标 |
| Config | 当前 Bot 配置查看（模型、端点、工具调用轮数等） |
| 实时更新 | 通过 SSE（Server-Sent Events）实时推送新事件到前端 |

---

## 快速开始

### 1. 启用 DashBoard

在 `config.json` 中添加 `DashBoard` 配置节：

```json
{
    "DashBoard": {
        "Enabled": true,
        "Host": "127.0.0.1",
        "Port": 5880
    }
}
```

### 2. 启动 DotCraft

正常启动 DotCraft（任意模式均支持），控制台会输出 DashBoard 地址：

```
DashBoard started at http://127.0.0.1:5880/dashboard
```

### 3. 打开 DashBoard

在浏览器中访问控制台输出的地址（默认 `http://127.0.0.1:5880/dashboard`），即可看到 DashBoard 界面。

### 4. 触发 Agent 执行

通过 CLI、QQ、WeCom 或 API 向 Agent 发送消息，DashBoard 会实时显示执行过程中的所有事件。

---

## 配置项

| 配置项 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| `DashBoard.Enabled` | bool | `false` | 是否启用 DashBoard 调试工具 |
| `DashBoard.Host` | string | `"127.0.0.1"` | DashBoard Web 服务监听地址 |
| `DashBoard.Port` | int | `5880` | DashBoard Web 服务监听端口 |

> **安全建议**：DashBoard 会暴露 Agent 的执行细节（包括 prompt 内容、工具调用参数和结果），建议仅在开发/调试环境启用，并绑定 `127.0.0.1` 避免外部访问。

---

## 运行模式

DashBoard 在不同运行模式下的行为略有不同：

| 运行模式 | DashBoard 启动方式 | 访问地址 |
|----------|----------------|----------|
| CLI | 独立 Web 服务器 | `http://{DashBoard.Host}:{DashBoard.Port}/dashboard` |
| QQ Bot | 独立 Web 服务器 | `http://{DashBoard.Host}:{DashBoard.Port}/dashboard` |
| WeCom Bot | 独立 Web 服务器 | `http://{DashBoard.Host}:{DashBoard.Port}/dashboard` |
| API | 集成到 API 服务器 | `http://{Api.Host}:{Api.Port}/dashboard` |

> **API 模式说明**：在 API 模式下，DashBoard 路由直接注册到已有的 API 服务器上，不会启动独立服务器，因此使用 API 的地址和端口访问。`DashBoard.Host` 和 `DashBoard.Port` 在 API 模式下不生效。

---

## Trace 事件类型

DashBoard 捕获的事件类型：

| 事件类型 | 说明 | 包含数据 |
|----------|------|----------|
| `Request` | Agent 收到的用户请求 | 请求内容（prompt） |
| `Response` | Agent 的最终回复 | 回复内容 |
| `ToolCallStarted` | 工具调用开始 | 工具名、图标、调用参数 |
| `ToolCallCompleted` | 工具调用完成 | 工具名、返回结果、耗时（ms） |
| `TokenUsage` | Token 用量报告 | 输入 Token 数、输出 Token 数 |
| `ContextCompaction` | 上下文压缩触发 | 无额外数据 |
| `Error` | 执行过程中的错误 | 错误信息 |

---

## API 端点

DashBoard 提供以下 REST API，均以 `/api/` 为前缀：

### GET /DashBoard

返回 DashBoard 前端页面（HTML/CSS/JS 内嵌 SPA）。

### GET /DashBoard/api/summary

获取全局统计摘要。

**响应示例**：

```json
{
    "sessionCount": 3,
    "totalRequests": 12,
    "totalToolCalls": 28,
    "totalInputTokens": 45000,
    "totalOutputTokens": 8500,
    "totalTokens": 53500
}
```

### GET /DashBoard/api/sessions

获取所有会话列表，按最近活动时间降序排列。

**响应示例**：

```json
[
    {
        "sessionKey": "cli:default",
        "startedAt": "2025-01-01T10:00:00.000Z",
        "lastActivityAt": "2025-01-01T10:05:30.000Z",
        "totalInputTokens": 15000,
        "totalOutputTokens": 3200,
        "totalTokens": 18200,
        "requestCount": 4,
        "toolCallCount": 10
    }
]
```

### GET /DashBoard/api/sessions/{sessionKey}/events

获取指定会话的所有事件，按时间升序排列。

**响应示例**：

```json
[
    {
        "id": "a1b2c3d4e5f6",
        "type": "Request",
        "sessionKey": "cli:default",
        "timestamp": "2025-01-01T10:00:00.000Z",
        "content": "搜索最新的 AI 新闻"
    },
    {
        "id": "f6e5d4c3b2a1",
        "type": "ToolCallStarted",
        "sessionKey": "cli:default",
        "timestamp": "2025-01-01T10:00:01.000Z",
        "toolName": "WebSearch",
        "toolIcon": "🔍",
        "toolArguments": "{\"query\":\"latest AI news 2025\"}"
    }
]
```

### DELETE /api/sessions/{sessionKey}

删除指定会话的所有 Trace 数据。

### DELETE /api/sessions

清空所有会话的 Trace 数据。

### GET /api/tools

获取已注册的工具列表。

**响应示例**：

```json
{
    "tools": [
        {"name": "WebSearch", "icon": "🔍"},
        {"name": "ReadFile", "icon": "📄"},
        {"name": "Exec", "icon": "💻"}
    ]
}
```

### GET /api/config

获取当前 Bot 配置信息。

**响应示例**：

```json
{
    "model": "gpt-4o-mini",
    "endPoint": "https://api.openai.com/v1",
    "maxToolCallRounds": 30,
    "subagentMaxToolCallRounds": 15,
    "compactSessions": true,
    "debugMode": false
}
```

### GET /api/events/stream

SSE（Server-Sent Events）端点，实时推送新的 Trace 事件。

**使用方式**：

```javascript
const eventSource = new EventSource('/dashboard/api/events/stream');
eventSource.onmessage = (event) => {
    const traceEvent = JSON.parse(event.data);
    console.log(traceEvent);
};
```

---

## 前端界面

DashBoard 前端为内嵌的单页应用（SPA），无需额外构建步骤，直接由服务端渲染 HTML。

### Dashboard 页面

展示全局统计卡片：

- **Sessions** - 活跃会话数
- **Requests** - 总请求数
- **Tool Calls** - 总工具调用次数
- **Input Tokens** - 总输入 Token 数
- **Output Tokens** - 总输出 Token 数
- **Total Tokens** - 总 Token 数

### Sessions 页面

- 会话列表，显示每个会话的 Key、请求数、工具调用数、Token 统计
- 点击会话进入 Trace Timeline 查看详细事件
- 支持删除单个会话或清空所有会话

### Trace Timeline 页面

- 按时间顺序展示会话中的所有事件
- 支持按事件类型过滤：All / Requests / Responses / Tools / Tokens / Errors
- 每个事件可展开查看详细内容（请求内容、工具参数、返回结果等）
- 工具调用事件显示耗时
- 支持自动滚动到最新事件（可切换）

### Tools 页面

网格展示所有已注册的工具及其图标。

### Config 页面

表格展示当前 Bot 的关键配置参数。

---

## 配置示例

### 基本开发调试

```json
{
    "ApiKey": "sk-your-llm-key",
    "Model": "gpt-4o-mini",
    "DashBoard": {
        "Enabled": true
    }
}
```

使用默认地址 `http://127.0.0.1:5880/dashboard` 访问。

### API 模式 + DashBoard

```json
{
    "ApiKey": "sk-your-llm-key",
    "Model": "gpt-4o-mini",
    "Api": {
        "Enabled": true,
        "Port": 8080,
        "AutoApprove": true
    },
    "DashBoard": {
        "Enabled": true
    }
}
```

DashBoard 通过 `http://localhost:8080/dashboard` 访问（集成到 API 服务器）。

### 自定义端口

```json
{
    "DashBoard": {
        "Enabled": true,
        "Host": "0.0.0.0",
        "Port": 9090
    }
}
```

> **注意**：将 `Host` 设为 `0.0.0.0` 会允许外部网络访问 DashBoard，请确保网络环境安全。

---

## 相关文档

- [配置指南](./config_guide.md) - 完整配置项说明
- [API 模式指南](./api_guide.md) - OpenAI 兼容 API 服务
- [QQ 机器人指南](./qq_bot_guide.md) - QQ 机器人模式
- [企业微信指南](./wecom_guide.md) - 企业微信机器人模式
- [文档索引](./index.md) - 完整文档导航
