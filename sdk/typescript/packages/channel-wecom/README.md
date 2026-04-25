# DotCraft WeCom Channel

DotCraft external channel adapter for WeCom enterprise bots. It connects to DotCraft through AppServer WebSocket and hosts the WeCom bot callback endpoint locally.

## Configuration

Enable AppServer WebSocket and the external channel in `.craft/config.json`:

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
      "enabled": true,
      "transport": "websocket"
    }
  }
}
```

Create `.craft/wecom.json`:

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

Configure the WeCom callback URL as `http://your-server:9000/dotcraft`. Production deployments commonly terminate HTTPS in a reverse proxy.
