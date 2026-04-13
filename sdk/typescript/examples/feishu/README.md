# DotCraft Feishu External Channel Example

**[中文](./README_ZH.md) | English**

This example connects a Feishu/Lark bot to DotCraft through the external channel adapter protocol over **WebSocket**.

It is built on:

- `dotcraft-wire` for the DotCraft AppServer JSON-RPC protocol
- `@larksuiteoapi/node-sdk` for Feishu bot APIs and event WebSocket

## What This Example Supports

- Feishu **WebSocket** event subscription
- Startup bot probe with `appId` + `appSecret`
- DotCraft thread reuse via external channel identity
- `/new` to start a fresh DotCraft thread
- Group chats that only respond when the bot is **@mentioned**
- Add an immediate reaction to handled inbound messages so users can see the bot has seen them
- Interactive approval cards with buttons
- Static reply cards after `turn/completed`
- Image input forwarding to DotCraft as `localImage`

## What This Example Does Not Cover

- Multi-account Feishu configuration
- Feishu webhook mode
- Streaming card updates
- User-level OAuth / Open Platform authorization flows

## Prerequisites

1. Node.js `>= 18`
2. A running DotCraft AppServer with WebSocket enabled
3. A Feishu self-built app with bot capability enabled

## 1. Enable DotCraft External Channel

`config.example.json` in this directory is the **DotCraft workspace config snippet**.

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

1. Create a **self-built app**
2. Enable the **Bot** capability
3. Enable event subscription over **long connection / WebSocket**
4. Add the bot/message related permissions you need

Recommended minimum bot-side permissions for this example:

- `im:message`
- `im:message:send`
- message reaction permission required by `im/v1/messages/:message_id/reactions`
- `im:resource`
- `im:chat`

Then collect:

- `appId`
- `appSecret`

## 3. Configure the Adapter

`adapter_config.json` is the **adapter runtime config**. Edit that file directly and fill in the real values:

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
    "downloadDir": "./tmp"
  }
}
```

Notes:

- `config.example.json` is for DotCraft workspace config, not adapter runtime config.
- `adapter_config.json` is the adapter runtime config in this example directory.
- `ackReactionEmoji` must be a Feishu official `emoji_type`, such as `GLANCE`, `SMILE`, or `OnIt`. Default: `GLANCE`.
- Supported values are documented in the Feishu emoji reference: `message-reaction/emojis-introduce`.
- `downloadDir` is used to store temporary image files before they are forwarded to DotCraft.

## 4. Install and Build

```bash
cd sdk/typescript
npm install
npm run build

cd examples/feishu
npm install
npm run build
```

## 5. Run

```bash
npm start
```

Or:

```bash
node dist/index.js adapter_config.json
```

If you do not pass a path, the process looks for `adapter_config.json` in the current directory.

## Behavior Notes

- **DM**: always handled
- **Group**: handled only when the bot is mentioned, unless `groupMentionRequired` is set to `false`
- **Inbound ack**: once a message passes filtering and parsing, the adapter immediately adds the configured reaction to that user message
- **Commands**: `/new` archives the current thread and starts a new one
- **Approvals**: rendered as interactive cards with `Approve`, `Approve Session`, `Decline`, and `Cancel`
- **Replies**: sent as static interactive cards after the turn finishes

## Auth / Login Model

This adapter does **not** use a QR login flow like the WeChat example.

Feishu bots use a static app credential model:

- `appId`
- `appSecret`

The Lark SDK handles token acquisition internally. On startup the adapter validates the credentials by calling the bot info API before it starts listening for events.

## Credits

[larksuite/openclaw-lark](https://github.com/larksuite/openclaw-lark)
