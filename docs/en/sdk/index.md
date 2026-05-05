# DotCraft SDK

DotCraft SDKs are for external clients, social channel adapters, and automation integrations. They connect to the same AppServer Wire Protocol and reuse DotCraft's thread, streaming event, approval, and delivery capabilities.

## Quick Start

1. Read the [AppServer Mode Guide](../appserver_guide.md) first and decide between stdio and WebSocket.
2. Choose [Python SDK](./python.md) for standalone adapters.
3. Choose [TypeScript SDK](./typescript.md) for social channel modules.
4. Start from the closest platform example, then replace tokens, callback URLs, and permission allowlists.

## Configuration

| Goal | Docs |
|------|------|
| Build external channel adapters in Python | [Python SDK](./python.md) |
| Reference the Telegram Python adapter | [Python Telegram Adapter](./python-telegram.md) |
| Build external channel modules in TypeScript | [TypeScript SDK](./typescript.md) |
| Connect Feishu / Lark | [Feishu Adapter](./typescript-feishu.md) |
| Connect Telegram | [Telegram Adapter](./typescript-telegram.md) |
| Connect Weixin | [Weixin Adapter](./typescript-weixin.md) |
| Connect QQ | [QQ Adapter](./typescript-qq.md) |
| Connect WeCom | [WeCom Adapter](./typescript-wecom.md) |

## Usage Examples

| Scenario | Recommended entry |
|----------|-------------------|
| Build a Python bot | [Python SDK](./python.md) + [Telegram Adapter](./python-telegram.md) |
| Build a TypeScript social channel | [TypeScript SDK](./typescript.md) |
| Connect to an existing AppServer | Use WebSocket transport |
| Let DotCraft host the adapter subprocess | Use stdio / subprocess mode |

## Advanced Topics

- [TypeScript Channel Module Host Integration](../typescript-module-integration.md)
- [External CLI Subagents Guide](../external_cli_subagents_guide.md)
- [AppServer Protocol Spec](https://github.com/DotHarness/dotcraft/blob/master/specs/appserver-protocol.md)

## Troubleshooting

### Adapter cannot connect to DotCraft

Confirm AppServer is running in WebSocket mode, the URL includes `/ws`, and the token matches the client config.

### Messages arrive but results cannot be delivered

Check whether the adapter declares delivery capabilities and channel tools during initialization. Platform tokens, callback URLs, and permission allowlists must also match.

### You are not sure whether to choose Python or TypeScript

Choose Python for quick standalone adapters. Choose TypeScript when reusing existing channel modules, interactive setup, or platform packages.
