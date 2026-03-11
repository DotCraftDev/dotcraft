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
  POST /dotcraft/v1/chat/completions
Additional endpoints:
  GET  /v1/health
All tools enabled (9 tools)
Press Ctrl+C to stop...
```

### 3. Call

Use any OpenAI-compatible SDK to call:

**Python**

Python sample code is available in `Samples/python`.

```python

from openai import OpenAI

client = OpenAI(
base_url="http://localhost:8080/dotcraft/v1",

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

![Chatbox](../images/config_model_provider.png)

---

## Configuration

| Config Item | Type | Default | Description |
|-------------|------|---------|-------------|
| `Api.Enabled` | bool | `false` | Enable API mode |
| `Api.Host` | string | `"127.0.0.1"` | HTTP service listen address |
| `Api.Port` | int | `8080` | HTTP service listen port |
| `Api.ApiKey` | string | empty | API access key (Bearer Token), no verification when empty |
| `Api.AutoApprove` | bool | `true` | Whether to auto-approve all file/Shell operations (overridden by ApprovalMode) |
| `Api.ApprovalMode` | string | empty | Approval mode: `auto`/`reject`/`interactive`, overrides AutoApprove when set |
| `Api.ApprovalTimeoutSeconds` | int | `120` | Interactive mode approval timeout (seconds) |
| `Api.EnabledTools` | array | `[]` | Enabled tools list, enables all when empty |

---

## Authentication

### Bearer Token Authentication

When `Api.ApiKey` is configured with a non-empty value, all requests to `/dotcraft/` paths must carry a Bearer Token:

```
Authorization: Bearer your-api-access-key
```

Unauthenticated requests will return `401 Unauthorized`.

### Disabling Authentication

Set `Api.ApiKey` to an empty string or leave it unset to disable authentication. Suitable for local development or intranet deployments.

> **Security Note**: Always configure `ApiKey` in production environments to prevent unauthorized access.

---

## Operation Approval

API mode handles operation approval via `ApiApprovalService`, supporting three modes:

| Mode | Configuration | Behavior |
|------|--------------|----------|
| **auto** | `"ApprovalMode": "auto"` or `"AutoApprove": true` | All file operations and Shell commands auto-approved |
| **reject** | `"ApprovalMode": "reject"` or `"AutoApprove": false` | All file operations and Shell commands auto-rejected |
| **interactive** | `"ApprovalMode": "interactive"` | Human-in-the-Loop: Sensitive operations pause waiting for API client approval |

> `ApprovalMode` overrides `AutoApprove` when set.

### Human-in-the-Loop Interactive Approval

When `ApprovalMode` is set to `"interactive"`, Agent execution of sensitive operations (file access outside workspace, Shell commands) will pause and create pending approval requests, waiting for the API client to approve via the approval endpoint.

**Flow**:

```
Client sends chat request
    ↓
Agent executes tool → encounters operation requiring approval → pauses
    ↓
Client polls GET /v1/approvals → gets pending approval list
    ↓
Client sends POST /v1/approvals/{id} → approve/reject
    ↓
Agent resumes execution → returns result
```

**Configuration**:

```json
{
    "Api": {
        "Enabled": true,
        "ApprovalMode": "interactive",
        "ApprovalTimeoutSeconds": 120
    }
}
```

**Approval Endpoints**:

#### GET /v1/approvals

Get all pending approval requests.

```bash
curl -H "Authorization: Bearer your-key" http://localhost:8080/v1/approvals
```

Response:
```json
{
    "approvals": [
        {
            "id": "a1b2c3d4e5f6",
            "type": "file",
            "operation": "write",
            "detail": "/path/to/file.txt",
            "createdAt": "2025-01-01T00:00:00.0000000Z"
        }
    ]
}
```

#### POST /v1/approvals/{id}

Approve or reject a pending approval request.

```bash
# Approve
curl -X POST -H "Authorization: Bearer your-key" \
  -H "Content-Type: application/json" \
  -d '{"approved": true}' \
  http://localhost:8080/v1/approvals/a1b2c3d4e5f6

# Reject
curl -X POST -H "Authorization: Bearer your-key" \
  -H "Content-Type: application/json" \
  -d '{"approved": false}' \
  http://localhost:8080/v1/approvals/a1b2c3d4e5f6
```

**Python Example**:

For a complete Human-in-the-Loop Python example, see [`Samples/python/human_in_the_loop.py`](../../Samples/python/human_in_the_loop.py).

> **Timeout**: If no approval decision is received within `ApprovalTimeoutSeconds` (default 120 seconds), the operation is automatically rejected.

> **Security Note**: If DotCraft is deployed on a public network, use `"interactive"` or `"reject"` mode, and enable only safe tools (e.g., `WebSearch`).

---

## Python Examples

For complete Python usage examples, see the [`Samples/python/`](../../Samples/python/) directory:

| Example | Description |
|---------|-------------|
| [basic_chat.py](../../Samples/python/basic_chat.py) | Basic chat (non-streaming) |
| [streaming_chat.py](../../Samples/python/streaming_chat.py) | Streaming output |
| [multi_turn_chat.py](../../Samples/python/multi_turn_chat.py) | Multi-turn conversation (interactive REPL) |
| [human_in_the_loop.py](../../Samples/python/human_in_the_loop.py) | Human-in-the-Loop approval flow |

---

## Related Documentation

- [Configuration Guide](./config_guide.md) - Complete configuration reference
- [QQ Bot Guide](./qq_bot_guide.md) - QQ Bot mode
- [WeCom Guide](./wecom_guide.md) - WeCom Bot mode
- [Documentation Index](./index.md) - Full documentation navigation
