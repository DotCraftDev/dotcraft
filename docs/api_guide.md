# DotCraft API 模式指南

DotCraft API 模式将 Agent 能力通过 **OpenAI 兼容的 HTTP API** 暴露，外部应用可直接使用标准 OpenAI SDK（Python、JavaScript、.NET 等）调用 DotCraft 进行推理和工具调用，无需自定义 SDK。

## 快速开始

### 1. 配置

在 `config.json` 中启用 API 模式：

```json
{
    "ApiKey": "sk-your-llm-api-key",
    "Model": "gpt-4o-mini",
    "EndPoint": "https://api.openai.com/v1",
    "Api": {
        "Enabled": true,
        "Host": "127.0.0.1",
        "Port": 8080,
        "ApiKey": "your-api-access-key",
        "AutoApprove": true
    }
}
```

### 2. 启动

```bash
cd /your/workspace
dotcraft gateway
```

启动成功后控制台输出：

```
DotCraft API listening on http://127.0.0.1:8080
Endpoints (OpenAI-compatible):
  POST /v1/chat/completions
Additional endpoints:
  GET  /v1/health
All tools enabled (9 tools)
Press Ctrl+C to stop...
```

### 3. 调用

使用任何 OpenAI 兼容的 SDK 即可调用：

**Python**

Python 示例代码可见 [API Samples](./samples/api.md)。

```python
from openai import OpenAI

client = OpenAI(
    base_url="http://localhost:8080/v1",
    api_key="your-api-access-key"
)

response = client.chat.completions.create(
    model="dotcraft",
    messages=[
        {"role": "user", "content": "搜索最新的 AI 新闻"}
    ]
)

print(response.choices[0].message.content)
```

**Desktop Application**

DotCraft 的 API 模式可以作为模型提供方，在流行的 AI 桌面应用（例如Chatbox）中聊天。

---

## 配置项

| 配置项 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| `Api.Enabled` | bool | `false` | 是否启用 API 模式 |
| `Api.Host` | string | `"127.0.0.1"` | HTTP 服务监听地址 |
| `Api.Port` | int | `8080` | HTTP 服务监听端口 |
| `Api.ApiKey` | string | 空 | API 访问密钥（Bearer Token），为空时不验证 |
| `Api.AutoApprove` | bool | `true` | 是否自动批准所有文件/Shell 操作；`false` 时自动拒绝 |
| `Api.EnabledTools` | array | `[]` | 启用的工具列表，为空时启用所有工具 |

---

## 认证

### Bearer Token 认证

当 `Api.ApiKey` 配置为非空值时，所有对 `/v1/` 路径的请求（`/v1/health` 除外）都需要携带 Bearer Token：

```
Authorization: Bearer your-api-access-key
```

未通过认证的请求会返回 `401 Unauthorized`。

### 禁用认证

将 `Api.ApiKey` 设置为空字符串或不设置，即可禁用认证。适用于本地开发或内网部署。

> **安全建议**：生产环境务必配置 `ApiKey`，避免未授权访问。

---

## 操作审批

API 模式通过 `ApiApprovalService` 处理操作审批，支持两种模式：

| 模式 | 配置 | 行为 |
|------|------|------|
| **auto** | `"AutoApprove": true`（默认） | 所有文件操作和 Shell 命令自动批准 |
| **reject** | `"AutoApprove": false` | 所有文件操作和 Shell 命令自动拒绝 |

> **安全建议**：如果 DotCraft 部署在公网，建议设置 `"AutoApprove": false` 并仅启用安全的工具（如 `WebSearch`）。

---

## Python 示例

完整的 Python 使用示例见 [API Samples](./samples/api.md)：

| 示例 | 说明 |
|------|------|
| [basic_chat.py](https://github.com/DotHarness/dotcraft/blob/master/samples/api/basic_chat.py) | 基本对话（非流式） |
| [streaming_chat.py](https://github.com/DotHarness/dotcraft/blob/master/samples/api/streaming_chat.py) | 流式输出 |
| [multi_turn_chat.py](https://github.com/DotHarness/dotcraft/blob/master/samples/api/multi_turn_chat.py) | 多轮对话（交互式 REPL） |

## 使用示例

| 场景 | 做法 |
|------|------|
| 本地调试 HTTP 调用 | 启用 `Api.Enabled`，保持 `Host = 127.0.0.1` |
| 提供给内部工具调用 | 配置 `Api.ApiKey`，使用 Bearer Token 访问 |
| 限制危险操作 | 设置 `Api.AutoApprove = false` 并通过 `EnabledTools` 缩小工具范围 |
| 与 Dashboard 共用端口 | 在 Gateway 中让 `Api.Port` 与 `DashBoard.Port` 相同 |

## 故障排查

### `/v1/chat/completions` 返回 401

检查请求头是否包含 `Authorization: Bearer <Api.ApiKey>`。`/v1/health` 不需要认证，可先用它确认服务是否在线。

### 工具调用全部被拒绝

如果 `Api.AutoApprove = false`，文件和 Shell 操作会自动拒绝。公网部署建议保留该设置，并只启用低风险工具。

### 客户端无法连接

确认 API Host、Port 和路径正确。Gateway 共享端口时，OpenAI-compatible API 路径仍然是 `/v1/...`。
