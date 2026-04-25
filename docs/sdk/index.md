# DotCraft SDK

DotCraft SDK 面向外部客户端、社交渠道适配器和自动化集成。它们都连接同一个 AppServer Wire Protocol，并复用 DotCraft 的线程、流式事件、审批和投递能力。

## Quick Start

1. 先阅读 [AppServer 模式指南](../appserver_guide.md)，确认你要使用 stdio 还是 WebSocket。
2. 如果用 Python 构建独立适配器，选择 [Python SDK](./python.md)。
3. 如果用 TypeScript 构建社交渠道模块，选择 [TypeScript SDK](./typescript.md)。
4. 从最接近的平台示例开始复制配置，再替换 token、回调地址和权限白名单。

## Configuration

| 目标 | 文档 |
|------|------|
| 使用 Python 构建外部渠道适配器 | [Python SDK](./python.md) |
| 参考 Telegram Python 适配器 | [Python Telegram Adapter](./python-telegram.md) |
| 使用 TypeScript 构建外部频道模块 | [TypeScript SDK](./typescript.md) |
| 接入飞书 / Lark | [Feishu Adapter](./typescript-feishu.md) |
| 接入 Telegram | [Telegram Adapter](./typescript-telegram.md) |
| 接入微信 | [Weixin Adapter](./typescript-weixin.md) |
| 接入 QQ | [QQ Adapter](./typescript-qq.md) |
| 接入企业微信 | [WeCom Adapter](./typescript-wecom.md) |

## Usage Examples

| 场景 | 推荐入口 |
|------|----------|
| 写一个 Python Bot | [Python SDK](./python.md) + [Telegram Adapter](./python-telegram.md) |
| 写一个 TypeScript 社交渠道 | [TypeScript SDK](./typescript.md) |
| 接入已有 AppServer | 使用 WebSocket transport |
| 让 DotCraft 托管适配器子进程 | 使用 stdio / subprocess 模式 |

## Advanced Topics

- [外部频道模块宿主集成](../typescript-channel-module-host-integration.md)
- [外部 CLI 子代理指南](../external_cli_subagents_guide.md)
- [AppServer Protocol Spec](https://github.com/DotHarness/dotcraft/blob/master/specs/appserver-protocol.md)

## Troubleshooting

### 适配器连不上 DotCraft

确认 AppServer 已以 WebSocket 模式启动，URL 包含 `/ws`，token 与客户端配置一致。

### 消息能收到但无法投递结果

检查适配器是否在初始化握手中声明了投递能力和 channel tools；平台 token、回调地址和权限白名单也需要匹配。

### 不知道选 Python 还是 TypeScript

需要快速写独立适配器时选 Python；需要复用现有 TypeScript 渠道模块、交互式初始化或平台包时选 TypeScript。

## 相关协议

- [AppServer 模式指南](../appserver_guide.md)
- [外部频道模块宿主集成](../typescript-channel-module-host-integration.md)
- [外部 CLI 子代理指南](../external_cli_subagents_guide.md)
