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
dotcraft
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

![Chatbox](https://github.com/DotHarness/resources/raw/master/dotcraft/api-proxy.png)

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

---

## 相关文档

- [配置指南](./config_guide.md) - 完整配置项说明
- [QQ 渠道适配器](./sdk/typescript-qq.md) - QQ 外部渠道
- [企业微信渠道适配器](./sdk/typescript-wecom.md) - 企业微信外部渠道
- [文档索引](./reference.md) - 完整文档导航
