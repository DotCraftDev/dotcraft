# DotCraft WeCom External Channel Guide

WeCom has moved from the built-in C# `WeComBot` module to the TypeScript external channel `@dotcraft/channel-wecom`. The adapter connects to DotCraft through AppServer WebSocket and hosts the WeCom bot HTTP(S) callback endpoint locally.

The old `WeComBot` config section is no longer the runtime entrypoint. Use `ExternalChannels.wecom` and `.craft/wecom.json` instead. Personal Weixin (`@dotcraft/channel-weixin`) and enterprise WeCom (`@dotcraft/channel-wecom`) are separate channels.

## Requirements

| Requirement | Notes |
|-------------|-------|
| WeCom bot | Self-built app or smart bot with callback URL, Token, and EncodingAESKey |
| DotCraft AppServer | WebSocket mode must be enabled |
| WeCom package | In releases: `resources/modules/channel-wecom`; in development: `sdk/typescript/packages/channel-wecom` |
| Public callback URL | WeCom must reach the adapter callback URL; production deployments usually terminate HTTPS in a reverse proxy |

## DotCraft Configuration

Enable AppServer WebSocket and register the external channel in `.craft/config.json`:

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
  "ExternalChannels": {
    "wecom": {
      "Enabled": true,
      "Transport": "Websocket"
    }
  }
}
```

## WeCom Channel Configuration

Create `.craft/wecom.json`:

```json
{
  "dotcraft": {
    "wsUrl": "ws://127.0.0.1:9100/ws",
    "token": ""
  },
  "wecom": {
    "host": "0.0.0.0",
    "port": 9000,
    "scheme": "http",
    "adminUsers": ["zhangsan"],
    "whitelistedUsers": [],
    "whitelistedChats": [],
    "approvalTimeoutMs": 60000,
    "robots": [
      {
        "path": "/dotcraft",
        "token": "your_token_here",
        "aesKey": "your_43_char_aeskey"
      }
    ]
  }
}
```

| Field | Description | Default |
|-------|-------------|---------|
| `dotcraft.wsUrl` | DotCraft AppServer WebSocket URL | `ws://127.0.0.1:9100/ws` |
| `dotcraft.token` | AppServer WebSocket token | empty |
| `wecom.host` | Callback listen host | `0.0.0.0` |
| `wecom.port` | Callback listen port | `9000` |
| `wecom.scheme` | Local callback scheme, `http` or `https` | `http` |
| `wecom.tls.certPath` / `keyPath` | Certificate and key paths for `https` mode | empty |
| `wecom.adminUsers` | WeCom admin UserIds | `[]` |
| `wecom.whitelistedUsers` | Whitelisted WeCom UserIds | `[]` |
| `wecom.whitelistedChats` | Whitelisted ChatIds | `[]` |
| `wecom.approvalTimeoutMs` | Approval timeout in milliseconds | `60000` |
| `wecom.robots` | Bot callback credential list | `[]` |

Robot fields:

| Field | Required | Description |
|-------|----------|-------------|
| `path` | Yes | Callback path, for example `/dotcraft` |
| `token` | Yes | Token configured in WeCom |
| `aesKey` | Yes | EncodingAESKey, usually 43 chars, without trailing equals signs |

## WeCom Console Setup

Configure message receiving in the WeCom admin console:

- URL: `http://your-server:9000/dotcraft`
- Token: same as `robots[].token` in `.craft/wecom.json`
- EncodingAESKey: same as `robots[].aesKey` in `.craft/wecom.json`

If HTTPS is provided by Nginx, Caddy, or another reverse proxy, the adapter can still listen on local HTTP.

## Startup

Desktop discovers the bundled `wecom-standard` module and can start it from channel management.

Development mode:

```bash
cd sdk/typescript
npm run build --workspace @dotcraft/channel-wecom
dotcraft-channel-wecom --workspace E:\dotcraft
```

Package-local mode:

```bash
cd sdk/typescript/packages/channel-wecom
npm run build
npm start -- --workspace E:\dotcraft
```

## Preserved Behavior

- Supports XML message push callbacks and JSON smart bot callbacks.
- Supports text, image, voice transcription, file, attachment, mixed, and event messages.
- Images and mixed-message images are downloaded to temporary local files and submitted as multimodal input.
- File and attachment messages preserve the old default behavior: reply with diagnostic information rather than submitting the file to the Agent.
- Sessions are isolated by `userId + chatId`; `channelContext` is `chat:<ChatId>`.
- Common commands such as `/new`, `/help`, Heartbeat, and Cron remain available.
- Approval keywords remain: `同意`, `同意全部`, `拒绝`, `yes`, `yes all`, `no`, and related aliases.

## Runtime Tools

The original WeCom-specific tool names remain available:

| Tool | Purpose | Notes |
|------|---------|-------|
| `WeComSendVoice(filePath)` | Send voice to the current WeCom chat | AMR only, local absolute path, uploads temporary media first |
| `WeComSendFile(filePath)` | Send file to the current WeCom chat | Local absolute path, uploads temporary media first |

These tools are exposed by the TypeScript adapter through external channel capabilities and no longer depend on the old built-in WeCom assembly.

## Cron and Heartbeat

| `channel` | `to` | Target | Prerequisite |
|-----------|------|--------|--------------|
| `"wecom"` | `"chat:<ChatId>"` or `"<ChatId>"` | WeCom chat | The adapter has received a message for that ChatId and cached its webhook |

Create Cron tasks from inside a WeCom chat whenever possible so the current ChatId is captured automatically. If no webhook has been cached for the target chatId, delivery fails with `No WeCom webhook is available for target ...`.

## Old Config Migration

| Old `WeComBot` | New `.craft/wecom.json` |
|----------------|--------------------------|
| `Host` | `wecom.host` |
| `Port` | `wecom.port` |
| `AdminUsers` | `wecom.adminUsers` |
| `WhitelistedUsers` | `wecom.whitelistedUsers` |
| `WhitelistedChats` | `wecom.whitelistedChats` |
| `ApprovalTimeoutSeconds` | `wecom.approvalTimeoutMs`, converted from seconds to milliseconds |
| `Robots[].Path` | `wecom.robots[].path` |
| `Robots[].Token` | `wecom.robots[].token` |
| `Robots[].AesKey` | `wecom.robots[].aesKey` |

## References

- [Enterprise WeChat Group Robot Configuration](https://developer.work.weixin.qq.com/document/path/99110)
- [Enterprise WeChat Smart Robot API](https://developer.work.weixin.qq.com/document/path/100719)
- [DotCraft Configuration Guide](../config_guide.md)
