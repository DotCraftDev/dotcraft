# DotCraft Weixin external channel example

**[中文](./README_ZH.md) | English**

Connects [Tencent iLink](https://ilinkai.weixin.qq.com) (WeChat bot API) to DotCraft via the **WebSocket** external channel transport.

## Prerequisites

1. **DotCraft workspace config**: merge `config.example.json` into your `.craft/config.json` (or equivalent) so `ExternalChannels.weixin` is enabled with `transport: "websocket"`. This mirrors how the Telegram example ships a **workspace** snippet, not the adapter runtime config.
2. **GatewayModule** services: `ExternalChannelRegistry` is registered when the Gateway module is enabled (e.g. enable `Automations` or any channel, or enable `ExternalChannels`).
3. **AppServerHost** bootstraps WebSocket external channels when `app-server` is the primary module (fixed in this repo).

## Setup

```bash
cd sdk/typescript
npm install && npm run build
cd packages/channel-weixin
npm install && npm run build
# Edit adapter_config.json: wsUrl, apiBaseUrl, dataDir
```

Start DotCraft (`app-server` / `stdioAndWebSocket`), then:

```bash
npm start
# or: node dist/index.js
```

(If you omit the path, the CLI defaults to `adapter_config.json` in the current working directory.)

First run opens a **QR code** in the terminal; scan with WeChat. Credentials are saved under `dataDir`.

## Configuration files

| File | Role |
|------|------|
| `config.example.json` | Snippet for **DotCraft** `.craft/config.json`: enables `ExternalChannels.weixin` with WebSocket transport. |
| `adapter_config.json` | **Weixin adapter** runtime config (checked in; edit in place). |

### Adapter config (`adapter_config.json`)

| Field | Description |
|-------|-------------|
| `dotcraft.wsUrl` | e.g. `ws://127.0.0.1:9100/ws` |
| `dotcraft.token` | Optional `?token=` if `AppServer.WebSocket.Token` is set |
| `weixin.apiBaseUrl` | Default `https://ilinkai.weixin.qq.com` |
| `weixin.dataDir` | Directory for credentials and sync cursor |
| `weixin.approvalTimeoutMs` | Approval prompt timeout (default 120000) |

Threads created by this adapter do not send a workspace path in `thread/start`; the DotCraft AppServer substitutes the host process workspace root so Desktop and other clients list them under the same project.

## Commands

Send **`/new`** in the chat (exact message, case-insensitive) to archive the current DotCraft thread and start a new conversation on the next message—same idea as the Telegram example’s `/new`.

## Approval flow

WeChat does not support inline buttons like Telegram. Approvals use **plain-text keywords** (same idea as QQ/WeCom): `同意` / `yes`, `同意全部` / `yes all`, `拒绝` / `no`, etc. The prompt text sent to the user is **Chinese**, aligned with the QQ/WeCom session-protocol wording.

## Credits

[@tencent-weixin/openclaw-weixin](https://www.npmjs.com/package/@tencent-weixin/openclaw-weixin)
