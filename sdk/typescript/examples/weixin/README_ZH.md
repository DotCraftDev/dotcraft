# DotCraft 微信外部渠道示例

通过 **WebSocket** 外部渠道将 [腾讯iLink](https://ilinkai.weixin.qq.com)（微信机器人API）连接到 DotCraft。

## 前置条件

1. **DotCraft 工作区配置**：将本目录下的 `config.example.json` 片段合并到 `.craft/config.json`（或等价配置）中，启用 `ExternalChannels.weixin` 且 `transport` 为 `websocket`。这与 Telegram 示例一致：`config.example.json` 表示**工作区**配置，而非适配器自身配置。
2. 需启用 Gateway 相关 DI（例如开启 `Automations` 或任意会启用 `GatewayModule` 的模块），以便注册 `ExternalChannelRegistry`。
3. 本仓库已在 `AppServerHost` 中于 **app-server 为主模块** 时启动 WebSocket 外部渠道宿主。

## 使用

```bash
cd sdk/typescript && npm install && npm run build
cd examples/weixin && npm install && npm run build
# 编辑 adapter_config.json（仓库已提供，可直接改）
```

启动 DotCraft 后执行：

```bash
npm start
# 或: node dist/index.js
```

不传参数时，CLI 默认读取当前目录下的 `adapter_config.json`。

首次运行需 **扫码登录**；凭据保存在 `dataDir`。

## 配置文件说明

| 文件 | 用途 |
|------|------|
| `config.example.json` | **DotCraft** 工作区配置片段：启用 `ExternalChannels.weixin`（WebSocket）。 |
| `adapter_config.json` | **微信适配器**运行时配置（随仓库提供，直接编辑）。 |

本适配器在 `thread/start` 中不传工作区路径；DotCraft AppServer 会将空身份中的工作区替换为宿主进程的工作区根目录，因此 Desktop 等客户端可在同一项目下列出这些线程。

## 命令

在对话中发送 **`/new`**（整段消息仅为此命令，不区分大小写）可结束当前会话线程并开启新对话，下一条消息会进入新线程；与 Telegram 示例中的 `/new` 一致。若当时有待处理的审批，会先按取消处理。

## 审批

微信侧无 Telegram 那样的按钮，审批通过 **文本关键词**（与 QQ/企业微信类似）：`同意` / `yes`、`同意全部` / `yes all`、`拒绝` / `no` 等。发给用户的审批说明为**中文**，与企业微信 / QQ 会话协议侧文案风格一致。

## 致谢

[@tencent-weixin/openclaw-weixin](https://www.npmjs.com/package/@tencent-weixin/openclaw-weixin)