# dotcraft-wire (TypeScript)

用于 DotCraft AppServer Wire Protocol 与外部渠道模块契约的 TypeScript SDK（JSON-RPC 2.0，通过 stdio JSONL 或 WebSocket 文本帧）。

与 `sdk/python/` 下的 Python 包 `dotcraft_wire` 相对应。

## TypeScript 包集合

TypeScript 外部渠道能力由以下包构成：

- `dotcraft-wire`（共享 SDK：Wire 客户端、适配器基类、模块契约类型）
- `@dotcraft/channel-feishu`（飞书 / Lark 一方渠道包）
- `@dotcraft/channel-weixin`（微信一方渠道包）
- `@dotcraft/channel-telegram`（Telegram 一方渠道包）
- `@dotcraft/channel-qq`（QQ 一方渠道包）
- `@dotcraft/channel-wecom`（企业微信一方渠道包）

宿主侧模块加载与生命周期集成说明见：

- `docs/typescript-channel-module-host-integration.md`

## 安装

```bash
cd sdk/typescript && npm install && npm run build
```

在其他包中引用：

```json
"dependencies": {
  "dotcraft-wire": "*"
}
```

## 快速开始（WebSocket 外部渠道）

```typescript
import { ChannelAdapter, WebSocketTransport } from "dotcraft-wire";

const transport = new WebSocketTransport({
  url: "ws://127.0.0.1:9100/ws",
  token: "",
});
```

完整示例可参考：

- `packages/channel-weixin/`：微信适配器
- `packages/channel-feishu/`：飞书 / Lark 适配器
- `packages/channel-telegram/`：Telegram 适配器
- `packages/channel-qq/`：QQ 适配器
- `packages/channel-wecom/`：企业微信适配器

## 调试

- 在 `ChannelAdapter` 构造选项中传入 `debugStream: true`（流式事件，前缀 `[dotcraft-wire:adapter-stream]`）。
- 调用 `configureTextMergeDebug(true)`（合并分支，前缀 `[dotcraft-wire:text-merge]`）。

飞书适配器包在其 README 示例中提供 `feishu.debug.adapterStream` / `feishu.debug.textMerge` 调试配置项。

## 许可证

MIT
