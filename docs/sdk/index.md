# DotCraft SDK

DotCraft SDK 面向外部客户端、社交渠道适配器和自动化集成。它们都连接同一个 AppServer Wire Protocol，并复用 DotCraft 的线程、流式事件、审批和投递能力。

## 选择 SDK

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

## 相关协议

- [AppServer 模式指南](../appserver_guide.md)
- [外部频道模块宿主集成](../typescript-channel-module-host-integration.md)
- [外部 CLI 子代理指南](../external_cli_subagents_guide.md)
