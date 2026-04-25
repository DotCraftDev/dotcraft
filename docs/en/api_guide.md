# DotCraft API Mode Guide

DotCraft API mode exposes Agent capabilities via an **OpenAI-compatible HTTP API**. External applications can directly use standard OpenAI SDKs (Python, JavaScript, .NET, etc.) to call DotCraft for inference and tool calling, with no custom SDK required.

## Quick Start

### 1. Configuration

Enable API mode in `config.json`:

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

### 2. Start

```bash
cd /your/workspace
dotcraft
```

Console output on successful start:

```
DotCraft API listening on http://127.0.0.1:8080
Endpoints (OpenAI-compatible):
  POST /v1/chat/completions
Additional endpoints:
  GET  /v1/health
All tools enabled (9 tools)
Press Ctrl+C to stop...
```

### 3. Call

Use any OpenAI-compatible SDK to call:

**Python**

Python sample code is available in [API Samples](./samples/api.md).

```python

from openai import OpenAI

client = OpenAI(
base_url="http://localhost:8080/v1",

api_key="your-api-access-key"

)

response = client.chat.completions.create(
model="dotcraft",

messages=[
{"role": "user", "content": "Search for the latest AI news"}

]

)

print(response.choices[0].message.content)

```

**Desktop Application**

DotCraft's API mode can act as a model provider, chatting in popular AI desktop applications such as Chatbox.

![Chatbox](https://github.com/DotHarness/resources/raw/master/dotcraft/api-proxy.png)

---

## Configuration

| Config Item | Type | Default | Description |
|-------------|------|---------|-------------|
| `Api.Enabled` | bool | `false` | Enable API mode |
| `Api.Host` | string | `"127.0.0.1"` | HTTP service listen address |
| `Api.Port` | int | `8080` | HTTP service listen port |
| `Api.ApiKey` | string | empty | API access key (Bearer Token), no verification when empty |
| `Api.AutoApprove` | bool | `true` | Auto-approve all file/Shell operations when true; auto-reject when false |
| `Api.EnabledTools` | array | `[]` | Enabled tools list, enables all when empty |

---

## Authentication

### Bearer Token Authentication

When `Api.ApiKey` is configured with a non-empty value, all requests to `/v1/` paths (except `/v1/health`) must carry a Bearer Token:

```
Authorization: Bearer your-api-access-key
```

Unauthenticated requests will return `401 Unauthorized`.

### Disabling Authentication

Set `Api.ApiKey` to an empty string or leave it unset to disable authentication. Suitable for local development or intranet deployments.

> **Security Note**: Always configure `ApiKey` in production environments to prevent unauthorized access.

---

## Operation Approval

API mode handles operation approval via `ApiApprovalService`, supporting two modes:

| Mode | Configuration | Behavior |
|------|--------------|----------|
| **auto** | `"AutoApprove": true` (default) | All file operations and Shell commands auto-approved |
| **reject** | `"AutoApprove": false` | All file operations and Shell commands auto-rejected |

> **Security Note**: If DotCraft is deployed on a public network, set `"AutoApprove": false` and enable only safe tools (e.g., `WebSearch`).

---

## Python Examples

For complete Python usage examples, see [API Samples](./samples/api.md):

| Example | Description |
|---------|-------------|
| [basic_chat.py](https://github.com/DotHarness/dotcraft/blob/master/samples/api/basic_chat.py) | Basic chat (non-streaming) |
| [streaming_chat.py](https://github.com/DotHarness/dotcraft/blob/master/samples/api/streaming_chat.py) | Streaming output |
| [multi_turn_chat.py](https://github.com/DotHarness/dotcraft/blob/master/samples/api/multi_turn_chat.py) | Multi-turn conversation (interactive REPL) |

## Usage Examples

| Scenario | Approach |
|----------|----------|
| Debug local HTTP calls | Enable `Api.Enabled`, keep `Host = 127.0.0.1` |
| Expose to internal tools | Set `Api.ApiKey` and use Bearer token authentication |
| Restrict dangerous actions | Set `Api.AutoApprove = false` and narrow tools with `EnabledTools` |
| Share a port with Dashboard | In Gateway, set `Api.Port` and `DashBoard.Port` to the same value |

## Troubleshooting

### `/v1/chat/completions` returns 401

Check that the request includes `Authorization: Bearer <Api.ApiKey>`. `/v1/health` does not require auth and is useful for service checks.

### Tool calls are all rejected

When `Api.AutoApprove = false`, file and shell operations are automatically rejected. For public deployments, keep this setting and enable only low-risk tools.

### Clients cannot connect

Confirm the API host, port, and path. When Gateway shares ports, OpenAI-compatible API routes still live under `/v1/...`.

---

## Related Documentation

- [Configuration Guide](./config_guide.md) - Complete configuration reference
- [QQ Channel Adapter](./sdk/typescript-qq.md) - QQ external channel
- [WeCom Channel Adapter](./sdk/typescript-wecom.md) - WeCom external channel
- [Documentation Index](./reference.md) - Full documentation navigation
