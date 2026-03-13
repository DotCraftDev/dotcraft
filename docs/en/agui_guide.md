# DotCraft AG-UI Mode Guide

DotCraft AG-UI mode exposes Agent capabilities via the **[AG-UI Protocol](https://github.com/ag-ui-protocol/ag-ui)** as an SSE streaming endpoint, compatible with any AG-UI client such as [CopilotKit](https://www.copilotkit.ai/).

## Quick Start

### 1. Configuration

Enable AG-UI mode in `config.json`:

```json
{
    "AgUi": {
        "Enabled": true,
        "Port": 5100,
        "Path": "/ag-ui"
    }
}
```

### 2. Start

```bash
cd /your/workspace
dotcraft
```

### 3. Connect

Point any AG-UI client to `http://127.0.0.1:5100/ag-ui` to start chatting.

DotCraft includes a minimal Next.js + CopilotKit sample client — see [`samples/ag-ui-client/`](../../samples/ag-ui-client/).

---

## Configuration

| Config Item | Type | Default | Description |
|-------------|------|---------|-------------|
| `AgUi.Enabled` | bool | `false` | Enable AG-UI server |
| `AgUi.Path` | string | `"/ag-ui"` | Endpoint path |
| `AgUi.Host` | string | `"127.0.0.1"` | Bind address |
| `AgUi.Port` | int | `5100` | Bind port |
| `AgUi.RequireAuth` | bool | `false` | Require Bearer token authentication |
| `AgUi.ApiKey` | string | empty | Bearer token value (required when `RequireAuth` is `true`) |

---

## Authentication

When `AgUi.RequireAuth` is `true`, all requests must carry a Bearer token:

```
Authorization: Bearer your-api-key
```

Unauthenticated requests return `401 Unauthorized`.

Leave `RequireAuth` as `false` to disable authentication for local development or intranet deployments.

---

## Sample Client

[`samples/ag-ui-client/`](../../samples/ag-ui-client/) provides a minimal Next.js client that connects to DotCraft's AG-UI server via CopilotKit.

**Quick setup**:

```bash
cd samples/ag-ui-client
pnpm install
cp .env.example .env.local  # edit DOTCRAFT_AGUI_URL if needed
pnpm run dev
```

Open http://localhost:3000 to chat with DotCraft in the browser.

If DotCraft has `RequireAuth` enabled, also set `DOTCRAFT_AGUI_API_KEY` in `.env.local`.

---

## Gateway Mode and Port Sharing

AG-UI is fully supported in [Gateway multi-channel concurrent mode](./config_guide.md#gateway-multi-channel-concurrent-mode), running alongside other channels in the same process.

**Automatic port merging**: If `AgUi.Host:AgUi.Port` is configured to the same address as another service (e.g. `Api` or `DashBoard`), Gateway's **WebHostPool** automatically merges them into a single Kestrel server instance — no manual port coordination needed. AG-UI routes are distinguished by `AgUi.Path` (default `/ag-ui`).

**Example: API and AG-UI sharing a port**:

```json
{
    "Gateway": { "Enabled": true },
    "Api": { "Enabled": true, "Port": 8080 },
    "AgUi": { "Enabled": true, "Port": 8080, "Path": "/ag-ui" }
}
```

With this configuration, `http://127.0.0.1:8080/v1/chat/completions` serves the OpenAI API and `http://127.0.0.1:8080/ag-ui` serves the AG-UI SSE endpoint — both from the same server process.

---

## Related Documentation

- [Configuration Guide](./config_guide.md) - Complete configuration reference
- [API Mode Guide](./api_guide.md) - OpenAI-compatible API mode
- [DashBoard Guide](./dash_board_guide.md) - Built-in Web debugging UI
- [Documentation Index](./index.md) - Full documentation navigation
