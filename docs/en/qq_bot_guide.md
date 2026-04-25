# DotCraft QQ External Channel Guide

QQ has been migrated from the built-in C# module to the TypeScript external channel `@dotcraft/channel-qq`. The adapter connects to DotCraft through AppServer WebSocket, then exposes a OneBot v11 reverse WebSocket endpoint for NapCat or another OneBot implementation.

The old `QQBot` config section is no longer used. Configure `ExternalChannels.qq` and `.craft/qq.json` instead.

## Prerequisites

| Requirement | Description |
|-------------|-------------|
| QQ account | QQ number for the bot, preferably an alternate account |
| OneBot v11 implementation | NapCat is recommended for logging into QQ and connecting to reverse WebSocket |
| DotCraft AppServer | WebSocket mode must be enabled |
| QQ external channel package | Packaged under `resources/modules/channel-qq`; in development under `sdk/typescript/packages/channel-qq` |

> Third-party QQ protocol frameworks carry account risk. Evaluate that risk before use.

## DotCraft Config

Enable AppServer WebSocket and register the QQ external channel in `.craft/config.json`:

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
  "ExternalChannels": [
    {
      "Name": "qq",
      "Enabled": true,
      "Transport": "Websocket"
    }
  ]
}
```

## QQ Channel Config

Create `.craft/qq.json`:

```json
{
  "dotcraft": {
    "wsUrl": "ws://127.0.0.1:9100/ws",
    "token": ""
  },
  "qq": {
    "host": "127.0.0.1",
    "port": 6700,
    "accessToken": "",
    "adminUsers": [123456789],
    "whitelistedUsers": [],
    "whitelistedGroups": [],
    "approvalTimeoutMs": 60000,
    "requireMentionInGroups": true
  }
}
```

| Config | Description | Default |
|--------|-------------|---------|
| `dotcraft.wsUrl` | DotCraft AppServer WebSocket URL | `ws://127.0.0.1:9100/ws` |
| `dotcraft.token` | AppServer WebSocket token | empty |
| `qq.host` | OneBot reverse WebSocket listen host | `127.0.0.1` |
| `qq.port` | OneBot reverse WebSocket listen port | `6700` |
| `qq.accessToken` | OneBot auth token, must match NapCat | empty |
| `qq.adminUsers` | Admin QQ number list | `[]` |
| `qq.whitelistedUsers` | Whitelisted QQ user list | `[]` |
| `qq.whitelistedGroups` | Whitelisted QQ group list | `[]` |
| `qq.approvalTimeoutMs` | Approval timeout in milliseconds | `60000` |
| `qq.requireMentionInGroups` | Require @mention in group chats | `true` |

If `adminUsers`, `whitelistedUsers`, and `whitelistedGroups` are all empty, the adapter will not respond to any QQ user. Configure at least one admin.

## Configure NapCat

Create a WebSocket client (reverse WS) in the NapCat WebUI:

1. Set URL to `ws://127.0.0.1:6700/`.
2. Set Token to the same value as `qq.accessToken`.
3. Select `array` as the message format.

If NapCat runs in Docker or on another machine, replace `127.0.0.1` with an address that can reach the QQ adapter.

## Start

The desktop app discovers the packaged `qq-standard` external channel from module resources and can start it from channel management.

In development:

```bash
cd sdk/typescript
npm run build --workspace @dotcraft/channel-qq
dotcraft-channel-qq --workspace E:\dotcraft
```

Or from the package directory:

```bash
cd sdk/typescript/packages/channel-qq
npm run build
npm start -- --workspace E:\dotcraft
```

## Usage

- Group chats only respond to @mentions by default.
- Private messages go directly to a per-user thread.
- Each QQ group or private user maps to an independent DotCraft thread.
- When an admin triggers an operation that needs approval, the adapter sends an approval prompt in QQ. Reply with `approve`, `yes`, `y`, `同意`, or `允许` to approve; reply with `reject`, `deny`, `no`, `n`, or `拒绝` to reject.

## QQ Runtime Tools

The migrated channel keeps the legacy QQ tool names for explicit cross-target delivery:

| Tool | Target | Sources |
|------|--------|---------|
| `QQSendGroupVoice` | QQ group | Local path, HTTP URL, `base64://...` |
| `QQSendPrivateVoice` | QQ user | Local path, HTTP URL, `base64://...` |
| `QQSendGroupVideo` | QQ group | Local path, HTTP URL |
| `QQSendPrivateVideo` | QQ user | Local path, HTTP URL |
| `QQUploadGroupFile` | QQ group | Local absolute path |
| `QQUploadPrivateFile` | QQ user | Local absolute path |

These tools are exposed by the TypeScript adapter through external channel capabilities and no longer depend on the old built-in QQ assembly.
