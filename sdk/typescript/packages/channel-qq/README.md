# DotCraft QQ Channel

External QQ channel adapter for DotCraft using OneBot v11 reverse WebSocket.

## Configuration

Create `.craft/qq.json`:

```json
{
  "dotcraft": {
    "wsUrl": "ws://127.0.0.1:9100/ws",
    "token": ""
  },
  "qq": {
    "host": "127.0.0.1",
    "port": 6700,
    "accessToken": "",
    "adminUsers": [],
    "whitelistedUsers": [],
    "whitelistedGroups": [],
    "approvalTimeoutMs": 60000,
    "requireMentionInGroups": true
  }
}
```

Register the adapter under `ExternalChannels.qq`, enable AppServer WebSocket, then point your OneBot implementation at `ws://127.0.0.1:6700/`.
