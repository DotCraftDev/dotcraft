# dotcraft-wire (TypeScript)

TypeScript SDK for the DotCraft AppServer Wire Protocol (JSON-RPC 2.0 over stdio JSONL or WebSocket text frames).

Mirrors the Python package `dotcraft_wire` under `sdk/python/`.

## Install

```bash
cd sdk/typescript && npm install && npm run build
```

In another package:

```json
"dependencies": {
  "dotcraft-wire": "file:../typescript"
}
```

## Quick start (WebSocket external channel)

```typescript
import { ChannelAdapter, WebSocketTransport } from "dotcraft-wire";

const transport = new WebSocketTransport({
  url: "ws://127.0.0.1:9100/ws",
  token: "",
});
```

See `examples/weixin/` for a full WeChat adapter.

## License

MIT
