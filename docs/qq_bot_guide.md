# DotCraft QQ 外部渠道使用指南

QQ 已从内置 C# 模块迁移为 TypeScript 外部渠道 `@dotcraft/channel-qq`。新的接入方式通过 AppServer WebSocket 连接 DotCraft，再由 QQ 适配器提供 OneBot v11 反向 WebSocket 服务给 NapCat 等 OneBot 实现。

旧的 `QQBot` 配置段不再生效，请改用 `ExternalChannels.qq` 和 `.craft/qq.json`。

## 前置条件

| 需求 | 说明 |
|------|------|
| QQ 账号 | 用作机器人的 QQ 号，建议使用小号 |
| OneBot v11 实现 | 推荐 NapCat，负责登录 QQ 并连接反向 WebSocket |
| DotCraft AppServer | 需要启用 WebSocket 模式 |
| QQ 外部渠道包 | 发布包中位于 `resources/modules/channel-qq`，开发环境中位于 `sdk/typescript/packages/channel-qq` |

> 使用第三方 QQ 协议框架存在账号风险，请自行评估。

## DotCraft 配置

在工作区 `.craft/config.json` 中启用 AppServer WebSocket，并注册 QQ 外部渠道：

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
  "ExternalChannels": [
    {
      "Name": "qq",
      "Enabled": true,
      "Transport": "Websocket"
    }
  ]
}
```

## QQ 渠道配置

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
    "adminUsers": [123456789],
    "whitelistedUsers": [],
    "whitelistedGroups": [],
    "approvalTimeoutMs": 60000,
    "requireMentionInGroups": true
  }
}
```

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `dotcraft.wsUrl` | DotCraft AppServer WebSocket 地址 | `ws://127.0.0.1:9100/ws` |
| `dotcraft.token` | AppServer WebSocket Token | 空 |
| `qq.host` | OneBot 反向 WebSocket 监听地址 | `127.0.0.1` |
| `qq.port` | OneBot 反向 WebSocket 监听端口 | `6700` |
| `qq.accessToken` | OneBot 鉴权 Token，需与 NapCat 一致 | 空 |
| `qq.adminUsers` | 管理员 QQ 号列表 | `[]` |
| `qq.whitelistedUsers` | 白名单用户 QQ 号列表 | `[]` |
| `qq.whitelistedGroups` | 白名单群号列表 | `[]` |
| `qq.approvalTimeoutMs` | 操作审批超时，毫秒 | `60000` |
| `qq.requireMentionInGroups` | 群聊是否必须 @ 机器人 | `true` |

如果 `adminUsers`、`whitelistedUsers`、`whitelistedGroups` 都为空，适配器不会响应任何 QQ 用户。请至少配置一个管理员。

## 配置 NapCat

在 NapCat WebUI 中创建 WebSocket 客户端（反向 WS）：

1. URL 填写 `ws://127.0.0.1:6700/`。
2. Token 填写与 `qq.accessToken` 相同的值。
3. 消息格式选择 `array`。

如果 NapCat 运行在 Docker 或另一台机器上，请把 `127.0.0.1` 替换为 QQ 适配器所在机器的可访问地址。

## 启动方式

桌面版会从打包资源中识别 `qq-standard` 外部渠道，可在渠道管理界面启动。

开发环境可以直接运行：

```bash
cd sdk/typescript
npm run build --workspace @dotcraft/channel-qq
dotcraft-channel-qq --workspace E:\dotcraft
```

也可以在包目录下运行：

```bash
cd sdk/typescript/packages/channel-qq
npm run build
npm start -- --workspace E:\dotcraft
```

## 使用说明

- 群聊默认只响应 @ 机器人的消息。
- 私聊会直接进入对应用户的独立会话。
- 每个 QQ 群或私聊用户映射到独立的 DotCraft thread。
- 管理员触发需要审批的操作时，适配器会在 QQ 会话里发送审批提示，回复 `同意`、`允许`、`yes`、`approve` 会通过，回复 `拒绝`、`no`、`reject`、`deny` 会拒绝。

## QQ Runtime 工具

迁移后仍保留原有 QQ 专用工具名，供 Agent 执行显式跨目标投递：

| 工具 | 目标 | 来源 |
|------|------|------|
| `QQSendGroupVoice` | QQ 群 | 本地路径、HTTP URL、`base64://...` |
| `QQSendPrivateVoice` | QQ 用户 | 本地路径、HTTP URL、`base64://...` |
| `QQSendGroupVideo` | QQ 群 | 本地路径、HTTP URL |
| `QQSendPrivateVideo` | QQ 用户 | 本地路径、HTTP URL |
| `QQUploadGroupFile` | QQ 群 | 本地绝对路径 |
| `QQUploadPrivateFile` | QQ 用户 | 本地绝对路径 |

这些工具由 TypeScript 适配器通过 external channel capabilities 暴露，不再依赖旧的内置 QQ 程序集。
