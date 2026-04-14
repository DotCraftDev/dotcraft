# dotcraft-wire (TypeScript)

**中文 | [English](./README.md)**

用于 DotCraft AppServer Wire Protocol（JSON-RPC 2.0，通过 stdio JSONL 或 WebSocket 文本帧）的 TypeScript SDK。

与 `sdk/python/` 下的 Python 包 `dotcraft_wire` 相对应。

## 安装

```bash
cd sdk/typescript && npm install && npm run build
```

在其他包中引用：

```json
"dependencies": {
  "dotcraft-wire": "file:../typescript"
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

- `examples/weixin/`：微信适配器
- `examples/feishu/`：飞书 / Lark 适配器

## 调试

- 在 `ChannelAdapter` 构造选项中传入 `debugStream: true`（流式事件，前缀 `[dotcraft-wire:adapter-stream]`）。
- 调用 `configureTextMergeDebug(true)`（合并分支，前缀 `[dotcraft-wire:text-merge]`）。

飞书示例在 `adapter_config.json` 的 `feishu.debug.adapterStream` / `feishu.debug.textMerge` 中配置。

## 许可证

MIT
