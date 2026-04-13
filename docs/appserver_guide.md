# AppServer 模式指南

## 概述

AppServer 是 DotCraft 的 wire protocol 服务器，它将 Agent 能力（会话管理、工具调用、审批流）以 JSON-RPC 协议暴露给外部客户端。CLI 默认通过 AppServer 与 Agent 通信，你也可以直接启动 AppServer 来构建自定义集成。

**适用场景**：

- 🔌 自定义 IDE / 编辑器集成
- 🌐 远程开发（CLI 连接远端 AppServer）
- 👥 多客户端共享同一个工作区
- 🔧 构建非 C# 客户端（任何支持 WebSocket / stdio 的语言）

## 快速开始

### 启动 AppServer

```bash
# stdio 模式（默认，适用于子进程通信）
dotcraft app-server

# 纯 WebSocket 模式（适用于远程连接、多客户端）
dotcraft app-server --listen ws://127.0.0.1:9100

# stdio + WebSocket 双模式（同时支持两种传输方式）
dotcraft app-server --listen ws+stdio://127.0.0.1:9100
```

### 从 CLI 连接远程 AppServer

```bash
# 连接已运行的 AppServer
dotcraft --remote ws://127.0.0.1:9100/ws

# 带 Token 认证
dotcraft --remote ws://server:9100/ws --token my-secret
```

### 带认证的 WebSocket 服务

```bash
# 服务端：监听所有网络接口，要求 Token
dotcraft app-server --listen ws://0.0.0.0:9100 --token my-secret

# 客户端：连接时携带 Token
dotcraft --remote ws://server:9100/ws --token my-secret
```

## 命令行参数参考

### 子命令与全局参数

| 命令 / 参数 | 说明 |
|-------------|------|
| `dotcraft` | 交互式 CLI（默认模式） |
| `dotcraft app-server` | 启动 AppServer（默认 stdio 模式） |
| `--listen <URL>` | AppServer 传输方式，搭配 `app-server` 使用 |
| `--remote <URL>` | CLI 连接远程 AppServer，搭配默认模式使用 |
| `--token <VALUE>` | WebSocket 认证 Token，可搭配 `--listen` 或 `--remote` |

### `--listen` URL Scheme

| Scheme | 传输模式 | stdout 行为 | 示例 |
|--------|---------|------------|------|
| `stdio://` | 纯 stdio（默认） | 保留给 JSON-RPC | `--listen stdio://` |
| `ws://host:port` | 纯 WebSocket | 正常控制台输出 | `--listen ws://127.0.0.1:9100` |
| `wss://host:port` | 纯 WebSocket (TLS) | 正常控制台输出 | `--listen wss://0.0.0.0:9100` |
| `ws+stdio://host:port` | stdio + WebSocket | 保留给 JSON-RPC | `--listen ws+stdio://127.0.0.1:9100` |

## Transport 模式

### stdio（默认）

AppServer 通过 stdin/stdout 以换行分隔的 JSON（JSONL）格式通信。这是标准的子进程通信方式——CLI 默认自动以子进程方式启动 AppServer。

```
Client (stdin) → JSON-RPC Request → AppServer
AppServer → JSON-RPC Response/Notification → Client (stdout)
AppServer → 诊断日志 → stderr
```

**特点**：
- 1:1 通信（一个客户端对应一个服务进程）
- stdout 被 wire protocol 占用，控制台日志输出到 stderr
- 无需网络配置，适合本地开发

### WebSocket

AppServer 在指定地址上启动 WebSocket 监听，每个 WebSocket 文本帧携带一条完整的 JSON-RPC 消息。

```bash
dotcraft app-server --listen ws://127.0.0.1:9100
```

**特点**：
- 多客户端并发连接（每个连接独立维护初始化状态和线程订阅）
- stdout 不被占用，控制台正常输出
- 支持远程连接和网络认证

### stdio + WebSocket 双模式

同时启动两种传输方式，适用于需要同时支持子进程通信和远程连接的场景。

```bash
dotcraft app-server --listen ws+stdio://127.0.0.1:9100
```

## 安全认证

当 AppServer 监听非回环地址（非 `127.0.0.1` / `::1`）时，**强烈建议**设置 Token 认证。

### 服务端设置 Token

```bash
dotcraft app-server --listen ws://0.0.0.0:9100 --token my-secret
```

### 客户端连接时传 Token

```bash
dotcraft --remote ws://server:9100/ws --token my-secret
```

Token 通过 WebSocket 连接 URL 的查询参数传递：`ws://host:port/ws?token=<value>`

> ⚠️ **安全提示**：绑定到 `0.0.0.0` 时不设置 Token，AppServer 将对所有网络请求无认证开放。

## 配置方式

### 命令行参数（推荐）

命令行参数优先级高于配置文件，推荐直接通过命令行启动：

```bash
dotcraft app-server --listen ws://127.0.0.1:9100 --token my-secret
```

### config.json 配置（替代方案）

也可以通过 `config.json` 配置 AppServer，适合需要固定配置的部署场景：

如果你在接入外部 channel adapter，需要把 adapter 的启动方式写在 `ExternalChannels` 中；但 structured delivery 能力和 `channelTools` 列表并不写在配置文件里，而是由 adapter 在 `initialize` 握手时动态声明。

**AppServer 配置项**：

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `AppServer.Mode` | 传输模式：`Disabled` / `Stdio` / `WebSocket` / `StdioAndWebSocket` | `Disabled` |
| `AppServer.WebSocket.Host` | WebSocket 监听地址 | `127.0.0.1` |
| `AppServer.WebSocket.Port` | WebSocket 监听端口 | `9100` |
| `AppServer.WebSocket.Token` | WebSocket 认证 Token | 空 |

**CLI 客户端配置项**：

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `CLI.AppServerUrl` | 远程 AppServer WebSocket 地址 | 空 |
| `CLI.AppServerToken` | 远程连接认证 Token | 空 |
| `CLI.AppServerBin` | 自定义 AppServer 可执行文件路径 | 空（使用当前进程） |

**配置示例**：

```json
{
    "AppServer": {
        "Mode": "WebSocket",
        "WebSocket": {
            "Host": "0.0.0.0",
            "Port": 9100,
            "Token": "my-secret"
        }
    }
}
```

```json
{
    "CLI": {
        "AppServerUrl": "ws://server:9100/ws",
        "AppServerToken": "my-secret"
    }
}
```

## 工作原理

### 架构

```
┌─────────────────────────────────────────────────┐
│  Client (CLI / IDE / Custom)                    │
│  ┌───────────┐  ┌───────────┐  ┌───────────┐   │
│  │  stdin/out │  │ WebSocket │  │ WebSocket │   │
│  └─────┬─────┘  └─────┬─────┘  └─────┬─────┘   │
└────────┼───────────────┼──────────────┼─────────┘
         │               │              │
    ┌────┴───────────────┴──────────────┴────┐
    │         AppServer (JSON-RPC)           │
    │  ┌──────────────────────────────────┐  │
    │  │       ISessionService            │  │
    │  │  (线程、会话、工具、审批)          │  │
    │  └──────────────────────────────────┘  │
    │  ┌───────┐ ┌────────┐ ┌────────────┐  │
    │  │ Tools │ │ Memory │ │   Skills   │  │
    │  └───────┘ └────────┘ └────────────┘  │
    └────────────────────────────────────────┘
```

### CLI 与 AppServer 的关系

默认情况下，CLI 自动以子进程方式启动 `dotcraft app-server`，通过 stdio 通信。你无需手动启动 AppServer——但以下场景需要手动管理：

| 场景 | 做法 |
|------|------|
| 本地终端直接使用 | 直接 `dotcraft`，自动管理 AppServer |
| 远程开发 | 远端启动 `dotcraft app-server --listen ws://...`，本地 `dotcraft --remote ws://...` |
| 多客户端共享工作区 | 启动 WebSocket 模式，多个客户端各自连接 |
| 自定义客户端集成 | 启动 AppServer，用任意语言通过 JSON-RPC 通信 |

## 延伸阅读

- [配置指南](./config_guide.md)：完整配置参考，包含 AppServer 和 CLI 配置项
- [ACP 模式指南](./acp_guide.md)：编辑器 / IDE 集成（同样基于 wire protocol）
- [AppServer 协议规范](../specs/appserver-protocol.md)：完整的 JSON-RPC 协议规范（§15 为 WebSocket 传输）
