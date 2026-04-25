# DotCraft WeCom Channel

DotCraft 企业微信外部渠道适配器。它通过 DotCraft AppServer WebSocket 连接工作区，并在本地启动企业微信机器人 HTTP(S) 回调服务。

## 配置

在 `.craft/config.json` 中启用 AppServer WebSocket 和外部渠道：

```json
{
  "AppServer": {
    "Mode": "WebSocket",
    "WebSocket": {
      "Host": "127.0.0.1",
      "Port": 9100,
      "Token": ""
    }
  },
  "ExternalChannels": {
    "wecom": {
      "Enabled": true,
      "Transport": "Websocket"
    }
  }
}
```

创建 `.craft/wecom.json`：

```json
{
  "dotcraft": {
    "wsUrl": "ws://127.0.0.1:9100/ws",
    "token": ""
  },
  "wecom": {
    "host": "0.0.0.0",
    "port": 9000,
    "scheme": "http",
    "adminUsers": ["zhangsan"],
    "whitelistedUsers": [],
    "whitelistedChats": [],
    "approvalTimeoutMs": 60000,
    "robots": [
      {
        "path": "/dotcraft",
        "token": "your_token_here",
        "aesKey": "your_43_char_aeskey"
      }
    ]
  }
}
```

企业微信后台的回调 URL 指向 `http://your-server:9000/dotcraft`。生产环境通常在前面放置反向代理和 HTTPS 证书。

