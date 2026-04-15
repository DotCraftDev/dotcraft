# DotCraft Feishu Channel Adapter

**[中文](./README_ZH.md) | English**

`@dotcraft/channel-feishu` connects a Feishu/Lark bot to DotCraft through the external channel adapter protocol over WebSocket.

It is built on:

- `dotcraft-wire` for DotCraft AppServer JSON-RPC protocol
- `@larksuiteoapi/node-sdk` for Feishu bot APIs and WebSocket events

## What This Adapter Supports

- Feishu WebSocket event subscription
- Startup bot probe with `appId` + `appSecret`
- DotCraft thread reuse via external channel identity
- `/new` to start a fresh DotCraft thread
- Group chats that only respond when the bot is @mentioned
- Immediate reaction on handled inbound messages
- Interactive approval cards with buttons
- Static reply cards after `turn/completed`
- Image input forwarding to DotCraft as `localImage`

## What This Adapter Does Not Cover

- Multi-account Feishu configuration
- Feishu webhook mode
- Streaming card updates
- User-level OAuth / Open Platform authorization flows

## Prerequisites

1. Node.js `>= 20`
2. A running DotCraft AppServer with WebSocket enabled
3. A Feishu self-built app with bot capability enabled

## 1. Enable DotCraft External Channel

`config.example.json` in this directory is the DotCraft workspace config snippet.

Merge it into your workspace `.craft/config.json`:

```json
{
  "AppServer": {
    "Mode": "stdioAndWebSocket",
    "WebSocket": {
      "Host": "127.0.0.1",
      "Port": 9100,
      "Token": ""
    }
  },
  "ExternalChannels": {
    "feishu": {
      "enabled": true,
      "transport": "websocket"
    }
  }
}
```

## 2. Create the Feishu App

In the Feishu Developer Console:

1. Create a self-built app
2. Enable the Bot capability
3. Enable event subscription over long connection/WebSocket
4. Add the bot/message related permissions you need

Recommended minimum bot-side permissions:

- `im:message`
- `im:message:send`
- Message reaction permission required by `im/v1/messages/:message_id/reactions`
- `im:resource`
- `im:chat`

Then collect:

- `appId`
- `appSecret`

## 3. Configure the Adapter

Create `.craft/feishu.json` inside your target workspace:

```json
{
  "dotcraft": {
    "wsUrl": "ws://127.0.0.1:9100/ws",
    "token": ""
  },
  "feishu": {
    "appId": "cli_your_app_id",
    "appSecret": "your_app_secret",
    "brand": "feishu",
    "approvalTimeoutMs": 120000,
    "groupMentionRequired": true,
    "ackReactionEmoji": "GLANCE",
    "downloadDir": "./tmp",
    "debug": {
      "adapterStream": false,
      "textMerge": false
    }
  }
}
```

Notes:

- `feishu.debug.adapterStream`: verbose `ChannelAdapter` stream traces to stderr (`[dotcraft-wire:adapter-stream]`), only when `true`
- `feishu.debug.textMerge`: traces merge decisions, only when `true`
- `ackReactionEmoji` must be a Feishu official `emoji_type` such as `GLANCE`, `SMILE`, `OnIt`
- `downloadDir` is used for temporary image files before forwarding to DotCraft

## 4. Install and Build

```bash
cd sdk/typescript
npm install
npm run build:all
```

## 5. Run

Primary mode:

```bash
npx dotcraft-channel-feishu --workspace /path/to/workspace
```

Optional config override:

```bash
npx dotcraft-channel-feishu --workspace /path/to/workspace --config /custom/feishu.json
```

## Behavior Notes

- DM: always handled
- Group: handled only when the bot is mentioned, unless `groupMentionRequired` is `false`
- Inbound ack: after filtering/parsing, the adapter adds the configured reaction first
- Commands: `/new` archives the current thread and starts a new one
- Approvals: rendered as interactive cards
- Replies: sent as static interactive cards after the turn finishes

## Auth / Login Model

This adapter does not use a QR login flow like the WeChat example.

Feishu bots use a static app credential model:

- `appId`
- `appSecret`

The Lark SDK handles token acquisition internally. On startup the adapter validates credentials by calling the bot info API before listening for events.

## Credits

[larksuite/openclaw-lark](https://github.com/larksuite/openclaw-lark)
