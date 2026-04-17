# DotCraft 飞书渠道适配器

**中文 | [English](./README.md)**

`@dotcraft/channel-feishu` 通过 WebSocket 外部渠道协议，把飞书 / Lark 机器人接入 DotCraft。

它基于：

- `dotcraft-wire`：DotCraft AppServer 的 JSON-RPC 协议封装
- `@larksuiteoapi/node-sdk`：飞书 Bot API 与事件长连接

## 已支持能力

- 飞书 WebSocket 事件订阅
- 基于显式 tenant token 鉴权启动并探测 Bot 信息
- 基于外部渠道身份复用 DotCraft 线程
- `/new` 开启新会话
- 群聊仅在 @机器人 时响应
- 对会处理的入站消息立即添加表情回复
- 按钮式审批卡片
- `turn/completed` 后发送静态回复卡片
- 图片消息下载后以 `localImage` 形式转发给 DotCraft
- 公共 `FeishuClient.sendTextMessage(...)` 与 `replyToMessage(...)`

## 当前不覆盖

- 飞书多账号
- 飞书 webhook 模式
- 流式更新卡片
- 用户级 OAuth / Open Platform 授权流程

## 前置条件

1. Node.js `>= 20`
2. 已启动并启用 WebSocket 的 DotCraft AppServer
3. 一个已开启 Bot 能力的飞书自建应用

## 1. 启用 DotCraft 外部渠道

本目录下的 `config.example.json` 是 DotCraft 工作区配置片段。

将它合并到工作区 `.craft/config.json`：

```json
{
  "AppServer": {
    "Mode": "stdioAndWebSocket",
    "WebSocket": {
      "Host": "127.0.0.1",
      "Port": 9100,
      "Token": ""
    }
  },
  "ExternalChannels": {
    "feishu": {
      "enabled": true,
      "transport": "websocket"
    }
  }
}
```

## 2. 创建飞书应用

在飞书开放平台中：

1. 创建自建应用
2. 开启机器人（Bot）能力
3. 开启长连接 / WebSocket 事件订阅
4. 配置本适配器需要的权限

建议至少具备以下机器人权限：

- `im:message`
- `im:message:send`
- 调用 `im/v1/messages/:message_id/reactions` 所需的消息表情权限
- `im:resource`
- `im:chat`

然后获取：

- `appId`
- `appSecret`

## 3. 配置适配器

在目标工作区中创建 `.craft/feishu.json`：

```json
{
  "dotcraft": {
    "wsUrl": "ws://127.0.0.1:9100/ws",
    "token": ""
  },
  "feishu": {
    "appId": "cli_your_app_id",
    "appSecret": "your_app_secret",
    "brand": "feishu",
    "approvalTimeoutMs": 120000,
    "groupMentionRequired": true,
    "ackReactionEmoji": "GLANCE",
    "downloadDir": "./tmp",
    "debug": {
      "adapterStream": false,
      "textMerge": false
    }
  }
}
```

说明：

- `feishu.debug.adapterStream`：打印 `ChannelAdapter` 流式事件调试日志（stderr，前缀 `[dotcraft-wire:adapter-stream]`），仅 `true` 启用
- `feishu.debug.textMerge`：打印文本合并分支日志，仅 `true` 启用
- `ackReactionEmoji` 必须为飞书官方 `emoji_type`，如 `GLANCE`、`SMILE`、`OnIt`
- `downloadDir` 用于暂存图片文件，再转发给 DotCraft

## 4. 安装并构建

```bash
cd sdk/typescript
npm install
npm run build:all
```

## 5. 运行

推荐方式：

```bash
npx dotcraft-channel-feishu --workspace /path/to/workspace
```

可选配置覆盖：

```bash
npx dotcraft-channel-feishu --workspace /path/to/workspace --config /custom/feishu.json
```

## 行为说明

- 私聊：默认处理
- 群聊：默认仅被 @ 时处理；`groupMentionRequired=false` 时放开群消息
- 入站提示：消息通过过滤与解析后，会先添加配置的表情回复
- 命令：`/new` 归档当前线程并创建新会话
- 审批：通过交互式卡片处理
- 回复：在回合结束后发送静态交互卡片

## 能力-权限矩阵

| 能力 | OpenAPI / 接口面 | 典型权限范围 | 是否依赖 Bot 能力 |
|---|---|---|---|
| 实时入站事件 | 长连接事件订阅 | 接收入站消息事件所需的订阅权限 | 是 |
| 历史消息读取 `listChatMessages` | `GET /open-apis/im/v1/messages` | 历史消息读取权限，如 `im:message:readonly` | 通常需要 |
| 文本发送 `sendTextMessage` | `POST /open-apis/im/v1/messages` | 发送消息权限，如 `im:message:send` | 是 |
| 消息回复 `replyToMessage` | `POST /open-apis/im/v1/messages/{message_id}/reply` | 发送 / 回复消息权限，如 `im:message:send` | 是 |
| 交互式卡片 | `im/v1/messages` 创建与更新 | 交互消息发送 / 更新权限 | 是 |
| 文件上传 / 发送 | `im/v1/files`、`im/v1/messages` | 文件/媒体上传权限与消息发送权限 | 是 |
| 图片下载 | `im/v1/messages/{message_id}/resources` | 消息资源读取权限，如 `im:resource` | 通常需要 |
| 表情 reaction | `im/v1/messages/{message_id}/reactions` | reaction 相关权限 | 是 |

说明：

- 上表描述的是公共适配层依赖，不代表租户一定已经开通了这些权限。
- 即使公共 API 已封装，租户策略、应用发布状态或 Bot 能力状态仍可能阻塞能力调用。
- 历史消息读取是否可用，最终取决于租户是否授予对应读取权限；本包只负责 API 封装。

## 历史消息 API 的非目标

- 不负责定时轮询或调度编排
- 不负责 checkpoint 持久化
- 不负责冷却或审计策略
- 不保证租户已开通所需权限

## 认证 / 登录模型

本适配器不使用微信示例那种二维码登录流程。

飞书 Bot 使用静态应用凭据模型：

- `appId`
- `appSecret`

适配器会基于 `appId` + `appSecret` 显式获取 tenant access token，并用它访问 bot probe 与消息 API，然后再开始监听事件。

## 群聊 @ 提及说明（多机器人/多应用）

飞书 `open_id` 是 app-scoped（按应用隔离）。在多机器人群聊里，WebSocket 事件中的 mention 身份有时会和机器人自身份不一致。

本适配器采用轻量缓解策略：

- 先匹配 `mention.id.open_id === botOpenId`
- 当 `mention.name` 和 `botName` 都可用时，再要求名称一致

这能改善多数场景，但并不是跨应用身份问题的绝对解法。

## 致谢

[larksuite/openclaw-lark](https://github.com/larksuite/openclaw-lark)
