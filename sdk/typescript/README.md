# dotcraft-wire (TypeScript)

**[中文](./README_ZH.md) | English**

TypeScript SDK for the DotCraft AppServer Wire Protocol and external channel module contract (JSON-RPC 2.0 over stdio JSONL or WebSocket text frames).

Mirrors the Python package `dotcraft_wire` under `sdk/python/`.

## TypeScript Package Set

The TypeScript external channel stack is delivered as these packages:

- `dotcraft-wire` (shared SDK: wire client, adapter base, module contract types)
- `@dotcraft/channel-feishu` (first-party Feishu/Lark channel package)
- `@dotcraft/channel-weixin` (first-party Weixin channel package)
- `@dotcraft/channel-telegram` (first-party Telegram channel package)
- `@dotcraft/channel-qq` (first-party QQ channel package)
- `@dotcraft/channel-wecom` (first-party WeCom channel package)

For host-side module loading and lifecycle integration, see:

- `docs/en/typescript-channel-module-host-integration.md`

## Install

```bash
cd sdk/typescript && npm install && npm run build
```

In another package:

```json
"dependencies": {
  "dotcraft-wire": "*"
}
```

## What You Get

- Raw `DotCraftClient` access for the full wire protocol
- `ChannelAdapter` base class for external channel adapters
- Approval handling via `item/approval/request`
- Structured delivery via `ext/channel/send`
- Runtime channel tools via `ext/channel/toolCall`

## Quick Start

### WebSocket external channel

```typescript
import { ChannelAdapter, WebSocketTransport } from "dotcraft-wire";

class MyAdapter extends ChannelAdapter {
  constructor() {
    super(
      new WebSocketTransport({
        url: "ws://127.0.0.1:9100/ws",
        token: "",
      }),
      "my-channel",
      "my-adapter",
      "1.0.0",
    );
  }

  async onDeliver(target: string, content: string): Promise<boolean> {
    console.log(`Deliver to ${target}: ${content}`);
    return true;
  }

  protected override getDeliveryCapabilities(): Record<string, unknown> | null {
    return {
      structuredDelivery: true,
      media: {
        file: {
          supportsHostPath: true,
          supportsUrl: true,
          supportsBase64: true,
          supportsCaption: true,
        },
      },
    };
  }

  protected override async onSend(
    target: string,
    message: Record<string, unknown>,
    metadata: Record<string, unknown>,
  ): Promise<Record<string, unknown>> {
    const kind = String(message.kind ?? "");
    if (kind === "text") {
      return await super.onSend(target, message, metadata);
    }
    if (kind === "file") {
      return { delivered: true };
    }
    return {
      delivered: false,
      errorCode: "UnsupportedDeliveryKind",
      errorMessage: `Unsupported kind: ${kind}`,
    };
  }

  protected override getChannelTools(): Record<string, unknown>[] | null {
    return [
      {
        name: "SendFileToCurrentChat",
        description: "Send a file to the current chat.",
        requiresChatContext: true,
        display: {
          icon: "📎",
          title: "Send file to current chat",
        },
        inputSchema: {
          type: "object",
          properties: {
            fileName: { type: "string" },
          },
          required: ["fileName"],
        },
      },
    ];
  }

  protected override async onToolCall(
    request: Record<string, unknown>,
  ): Promise<Record<string, unknown>> {
    const args = (request.arguments as Record<string, unknown>) ?? {};
    return {
      success: true,
      contentItems: [
        {
          type: "text",
          text: `Sent ${String(args.fileName ?? "file")}.`,
        },
      ],
    };
  }

  async onApprovalRequest(): Promise<string> {
    return "accept";
  }
}
```

## `ChannelAdapter` Hook Mapping

- `getDeliveryCapabilities()` -> `initialize.capabilities.channelAdapter.deliveryCapabilities`
- `getChannelTools()` -> `initialize.capabilities.channelAdapter.channelTools`
- `onDeliver()` -> `ext/channel/deliver`
- `onSend()` -> `ext/channel/send`
- `onToolCall()` -> `ext/channel/toolCall`

`channelTools` are runtime declarations sent by the adapter during `initialize`. They are not configured statically in DotCraft's `ExternalChannels` config.

Use PascalCase for channel tool names. For display metadata, prefer setting `channelTools[].display.icon` to an emoji string; `display.title` and `display.subtitle` are optional UI hints.

## DotCraft Config

```json
{
  "ExternalChannels": {
    "my-channel": {
      "enabled": true,
      "transport": "websocket"
    }
  }
}
```

`ExternalChannels` only tells DotCraft how to start or accept the adapter connection. Structured delivery capabilities and channel tool descriptors come from the adapter's handshake.

## Package Modules

- `packages/channel-weixin/` for the Weixin channel package
- `packages/channel-feishu/` for the Feishu/Lark channel package
- `packages/channel-telegram/` for the Telegram channel package
- `packages/channel-qq/` for the QQ channel package
- `packages/channel-wecom/` for the WeCom channel package

## Debugging

- Pass `debugStream: true` in [`ChannelAdapter`](src/adapter.ts) options (6th constructor argument). Logs use the prefix `[dotcraft-wire:adapter-stream]`.
- Call `configureTextMergeDebug(true)` from [`turnReply.ts`](src/turnReply.ts) for merge traces (`[dotcraft-wire:text-merge]`).

The Feishu adapter package exposes `feishu.debug.adapterStream` and `feishu.debug.textMerge` config fields in its README examples.

## License

MIT
