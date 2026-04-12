# DotCraft 飞书外部渠道示例

**中文 | [English](./README.md)**

这个示例通过 **WebSocket 外部渠道适配器**，把飞书 / Lark 机器人接到 DotCraft。

它基于：

- `dotcraft-wire`：DotCraft AppServer 的 JSON-RPC Wire Protocol
- `@larksuiteoapi/node-sdk`：飞书 Bot API 和事件长连接

## 已支持能力

- 飞书 **WebSocket** 事件订阅
- 使用 `appId` + `appSecret` 启动并探测 bot 信息
- 基于外部渠道身份复用 DotCraft 线程
- `/new` 开启新会话
- 群聊仅在 **@机器人** 时响应
- 按钮式审批卡片
- `turn/completed` 后发送静态回复卡片
- 图片消息下载后以 `localImage` 形式转发给 DotCraft

## 当前不覆盖

- 飞书多账号
- 飞书 webhook 模式
- 流式更新卡片
- 用户级 OAuth / Open Platform 授权流程

## 前置条件

1. Node.js `>= 18`
2. 已启动并启用 WebSocket 的 DotCraft AppServer
3. 一个已开启 Bot 能力的飞书自建应用

## 1. 启用 DotCraft 外部渠道

本目录下的 `config.example.json` 是 **DotCraft 工作区配置片段**。

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

1. 创建一个 **自建应用**
2. 开启 **机器人（Bot）** 能力
3. 开启 **长连接 / WebSocket** 事件订阅
4. 配置本示例需要的权限

建议至少具备以下机器人侧权限：

- `im:message`
- `im:message:send`
- `im:resource`
- `im:chat`

然后拿到：

- `appId`
- `appSecret`

## 3. 配置适配器

`adapter_config.json` 是 **适配器运行时配置**，直接编辑这个文件并填入真实配置：

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
    "downloadDir": "./tmp"
  }
}
```

说明：

- `config.example.json` 用于 DotCraft 工作区配置，不是适配器运行时配置。
- `adapter_config.json` 是本示例目录下的适配器运行时配置。
- `downloadDir` 用于暂存图片文件，再把它们转发给 DotCraft。

## 4. 安装并构建

```bash
cd sdk/typescript
npm install
npm run build

cd examples/feishu
npm install
npm run build
```

## 5. 运行

```bash
npm start
```

或者：

```bash
node dist/index.js adapter_config.json
```

如果不传参数，默认读取当前目录下的 `adapter_config.json`。

## 行为说明

- **私聊**：默认都会处理
- **群聊**：默认只有被 @ 时才处理；如果把 `groupMentionRequired` 设为 `false`，则群聊所有消息都可触发
- **命令**：`/new` 会归档当前线程并创建新会话
- **审批**：通过交互式卡片按钮处理
- **回复**：在回合结束后以静态交互卡片发送

## 认证 / 登录模型

这个飞书适配器 **不使用** 微信示例那种二维码登录流程。

飞书 Bot 使用的是静态应用凭据模型：

- `appId`
- `appSecret`

Lark SDK 会在内部处理访问令牌获取。适配器启动时会先调用 bot info API 做一次探测校验，确认凭据可用后再开始监听事件。

## 群聊 @ 提及说明（多机器人/多应用）

飞书 `open_id` 是 app-scoped（按应用隔离）。在多机器人群聊里，WebSocket 事件中的 mention 身份有时会和机器人自身份不一致。

本示例采用轻量缓解策略：

- 先匹配 `mention.id.open_id === botOpenId`
- 当 `mention.name` 和 `botName` 都可用时，再要求名称一致

这能改善多数场景，但并不是跨应用身份问题的绝对解法。

## 致谢

[larksuite/openclaw-lark](https://github.com/larksuite/openclaw-lark)