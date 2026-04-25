# DotCraft QQ Channel

基于 OneBot v11 反向 WebSocket 的 DotCraft QQ 外部渠道适配器。

## 配置

创建 `.craft/qq.json`：

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

在 `ExternalChannels.qq` 中注册该适配器，启用 AppServer WebSocket，然后将 OneBot 实现连接到 `ws://127.0.0.1:6700/`。
