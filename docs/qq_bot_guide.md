# DotCraft QQ 机器人使用指南

本文档介绍如何配置 DotCraft 接入 QQ 机器人，使其能够在 QQ 群聊和私聊中作为智能助手回答问题。

## 前置准备

| 需求 | 说明 |
|------|------|
| 一个 QQ 账号 | 用作机器人的 QQ 号（建议使用小号） |
| NapCat | QQ 协议框架，用于登录 QQ |
| DotCraft | 编译好的 DotCraft 可执行文件 |
| LLM API Key | OpenAI 兼容的 API Key（如 OpenAI、DeepSeek 等） |

> **注意**：使用第三方 QQ 协议框架存在账号风险（封号等），请自行评估风险。

---

## 第一步：安装 NapCat

NapCat 是目前最活跃的 OneBot V11 协议实现，基于 NTQQ 内核。

### Windows 用户（推荐：一键包）

1. 前往 [NapCat Releases](https://github.com/NapNeko/NapCatQQ/releases) 下载最新版本
2. 下载 **`NapCat.Shell.Windows.OneKey.zip`**（Windows 一键包，内置 QQ，无需额外安装）
3. 解压到任意目录
4. 运行 `launcher.bat` 启动

### Linux 用户

**方式一：一键安装脚本**

```bash
curl -o napcat.sh https://nclatest.znin.net/NapNeko/NapCat-Installer/main/script/install.sh && sudo bash napcat.sh
```

**方式二：Docker 部署**

```bash
docker run -d \
  -e ACCOUNT=你的QQ号 \
  -e WSR_ENABLE=true \
  -e WS_URLS='["ws://127.0.0.1:6700/"]' \
  -e WEBUI_TOKEN='your-webui-password' \
  -p 6099:6099 \
  --name napcat \
  --restart=always \
  mlikiowa/napcat-docker:latest
```

- `ACCOUNT`：机器人 QQ 号
- `WS_URLS`：DotCraft 的 WebSocket 地址（反向 WS）
- `6099`：NapCat WebUI 管理页面端口

### macOS 用户

前往 [NapCat Releases](https://github.com/NapNeko/NapCatQQ/releases) 下载 macOS DMG 版本。

---

## 第二步：配置 DotCraft

在 DotCraft 工作区的 `config.json` 中添加 QQ 机器人配置：

```json
{
    "ApiKey": "your-llm-api-key",
    "Model": "gpt-4o-mini",
    "EndPoint": "https://api.openai.com/v1",
    "SystemInstructions": "你是 DotCraft，一个简洁、可靠的 QQ 智能助手。",
    "QQBot": {
        "Enabled": true,
        "Host": "127.0.0.1",
        "Port": 6700,
        "AccessToken": "",
        "AdminUsers": [123456789],
        "WhitelistedUsers": [111111111, 222222222],
        "WhitelistedGroups": [333333333],
        "ApprovalTimeoutSeconds": 60
    }
}
```

### 基础配置项

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `QQBot.Enabled` | 是否启用 QQ 机器人模式 | `false` |
| `QQBot.Host` | WebSocket 服务器监听地址 | `127.0.0.1` |
| `QQBot.Port` | WebSocket 服务器监听端口 | `6700` |
| `QQBot.AccessToken` | 鉴权 Token（可选，需与 NapCat 一致） | 空 |

> 如果 NapCat 和 DotCraft 在同一台机器上运行，Host 保持 `127.0.0.1` 即可。

### 权限配置项

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `QQBot.AdminUsers` | 管理员 QQ 号列表（拥有最高权限） | `[]` |
| `QQBot.WhitelistedUsers` | 白名单用户 QQ 号列表（基础权限） | `[]` |
| `QQBot.WhitelistedGroups` | 白名单群号列表（群内成员自动获得基础权限） | `[]` |
| `QQBot.ApprovalTimeoutSeconds` | 操作审批超时时间（秒），超时自动拒绝 | `60` |

---

## 第三步：配置 NapCat 连接 DotCraft

NapCat 启动后，通过浏览器访问 WebUI 进行配置：

1. 打开浏览器访问 `http://127.0.0.1:6099`
2. 输入 WebUI Token 登录
3. 使用手机 QQ 扫码登录机器人账号
4. 进入 **网络配置** 页面
5. 点击 **新建** → 选择 **WebSocket 客户端**（反向 WS）
6. 填写配置：
   - **URL**：`ws://127.0.0.1:6700/`（对应 DotCraft 的 Host:Port）
   - **Token**：如果 DotCraft 配置了 AccessToken，这里填一样的值
   - **消息格式**：选择 `array`（**必须**，不要选 `string`）
7. 点击**保存**

> 如果使用 Docker 部署 NapCat，URL 中的 `127.0.0.1` 需要替换为 DotCraft 所在机器的 IP 或 Docker 网络地址。

---

## 第四步：启动 DotCraft

```bash
./DotCraft
```

启动后，你会看到如下输出：

```
QQ Bot mode enabled
OneBot reverse WebSocket server started on ws://127.0.0.1:6700/
QQ Bot listening on ws://127.0.0.1:6700/
Press Ctrl+C to stop...
```

当 NapCat 成功连接后，会看到：

```
[QQ] Client connected: <connection-id> from <ip:port>
[QQ] OneBot lifecycle event: sub_type=connect
```

---

## 使用说明

### 群聊模式

- 机器人**只会回复 @它** 的消息
- 在群聊中 `@机器人 你好` 即可触发回复
- 未授权用户 @机器人 时会被静默忽略

### 私聊模式

- 机器人**只回复已授权用户**的私聊消息
- 未授权用户的私聊消息会被忽略

### 会话管理

- 每个 QQ 会话（群号或私聊用户）有独立的上下文
- 会话数据自动保存在 DotCraft 工作区的 `sessions/` 目录

---

## 权限与安全

### 用户角色

DotCraft 将 QQ 用户分为三种角色：

| 角色 | 来源 | 权限范围 |
|------|------|----------|
| **管理员** | `AdminUsers` 列表中的 QQ 号 | 最高权限，可执行写入操作（需审批） |
| **白名单用户** | `WhitelistedUsers` 列表中的 QQ 号，或 `WhitelistedGroups` 列表中的群成员 | 基础权限，可对话和读取文件 |
| **未授权用户** | 不在以上任何列表中 | 机器人不响应 |

### 操作分级

不同角色可执行的操作级别不同：

| 操作级别 | 操作内容 | 白名单用户 | 管理员 |
|----------|----------|------------|--------|
| Tier 0 | 纯对话（无工具调用） | 允许 | 允许 |
| Tier 1 | 读取工作区内文件、Web 请求 | 允许 | 允许 |
| Tier 2 | 写入工作区内文件、执行 Shell 命令 | 拒绝 | 需审批 |
| Tier 3 | 写入工作区外文件、执行工作区外命令 | 拒绝 | 拒绝 |

### Shell 命令安全规则

机器人会对 Shell 命令进行**跨平台路径静态分析**，检测以下路径形式：
- **Unix**：绝对路径（`/etc`）、家目录（`~/.ssh`）、环境变量（`$HOME`、`${HOME}`）
- **Windows**：盘符路径（`C:\`、`D:\Users`）、环境变量（`%USERPROFILE%`、`%APPDATA%`）、UNC 路径（`\\server\share`）

触发规则：
- 当 `Tools.Shell.RequireApprovalOutsideWorkspace = false`：此类命令直接拒绝执行
- 当 `Tools.Shell.RequireApprovalOutsideWorkspace = true`：仅管理员可在审批通过后执行，其他角色直接拒绝
- 黑名单路径（如 `~/.ssh`、`/etc/shadow`）一律拒绝（无论审批与否）

示例：
- `ls /etc`、`dir C:\`、`cat ~/.ssh/id_rsa`、`type %USERPROFILE%\secret.txt` → 触发检测
- `ls ./src`、`echo test > NUL` → 不触发（工作区内 / 安全设备白名单）

### 操作审批流程

当管理员触发 Tier 2 操作时，DotCraft 会通过 QQ 消息请求审批：

1. 机器人在当前会话中发送审批请求（群聊中会 @管理员）：
   ```
   ⚠️ 操作审批请求
   文件操作: write
   路径: ./src/main.cs

   请回复: 同意 / 拒绝 (超时60秒自动拒绝)
   ```
2. 管理员回复以下任意关键词完成审批：
   - **同意**：`同意`、`允许`、`yes`、`y`、`approve`
   - **拒绝**：`拒绝`、`不同意`、`no`、`n`、`reject`、`deny`
3. 超时未回复自动拒绝（默认 60 秒，可通过 `ApprovalTimeoutSeconds` 调整）

### 配置示例

**最小权限配置**（推荐）：只允许特定管理员操作

```json
{
    "QQBot": {
        "Enabled": true,
        "Port": 6700,
        "AdminUsers": [123456789]
    }
}
```

**群聊公开配置**：允许群内所有成员使用基础功能

```json
{
    "QQBot": {
        "Enabled": true,
        "Port": 6700,
        "AdminUsers": [123456789],
        "WhitelistedGroups": [987654321, 111222333]
    }
}
```

**混合配置**：指定群白名单 + 额外用户白名单

```json
{
    "QQBot": {
        "Enabled": true,
        "Port": 6700,
        "AdminUsers": [123456789],
        "WhitelistedUsers": [111111111, 222222222],
        "WhitelistedGroups": [333333333],
        "ApprovalTimeoutSeconds": 120
    }
}
```

> **注意**：如果 `AdminUsers`、`WhitelistedUsers`、`WhitelistedGroups` 都为空，机器人将不会响应任何人。请至少配置一个管理员。

---

## 完整部署示例

以下是一个在 Linux 服务器上的完整部署流程：

```bash
# 1. 安装 NapCat（Docker 方式）
docker run -d \
  -e ACCOUNT=123456789 \
  -e WSR_ENABLE=true \
  -e WS_URLS='["ws://host.docker.internal:6700/"]' \
  -e WEBUI_TOKEN='my-secret-token' \
  -p 6099:6099 \
  --name napcat \
  --restart=always \
  --add-host=host.docker.internal:host-gateway \
  mlikiowa/napcat-docker:latest

# 2. 访问 http://服务器IP:6099 扫码登录 QQ

# 3. 配置 DotCraft (确保 config.json 中 QQBot.Enabled = true)

# 4. 启动 DotCraft
./DotCraft
```

---

## 配置模板

### 最小化配置

```json
{
    "ApiKey": "sk-xxx",
    "Model": "gpt-4o-mini",
    "EndPoint": "https://api.openai.com/v1",
    "QQBot": {
        "Enabled": true,
        "Port": 6700,
        "AdminUsers": [123456789]
    }
}
```

### 完整配置

```json
{
    "ApiKey": "sk-xxx",
    "Model": "gpt-4o-mini",
    "EndPoint": "https://api.openai.com/v1",
    "SystemInstructions": "你是 DotCraft，一个简洁、可靠的 QQ 智能助手。",
    "QQBot": {
        "Enabled": true,
        "Host": "127.0.0.1",
        "Port": 6700,
        "AccessToken": "your-optional-token",
        "AdminUsers": [123456789],
        "WhitelistedUsers": [111111111, 222222222],
        "WhitelistedGroups": [333333333, 444444444],
        "ApprovalTimeoutSeconds": 60
    },
    "Security": {
        "BlacklistedPaths": [
            "~/.ssh",
            "/etc/shadow"
        ]
    }
}
```
