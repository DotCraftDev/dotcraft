# DotCraft ACP 模式指南

[Agent Client Protocol（ACP）](https://agentclientprotocol.com/) 是一个开放协议，专门用于标准化编码代理与编辑器/IDE 之间的通信方式——与 LSP（Language Server Protocol）解决编辑器与语言服务器耦合问题的思路如出一辙，但作用对象是 AI 代理。任何实现了 ACP 的编辑器都可以接入任意 ACP 兼容代理。DotCraft 原生支持 ACP，这意味着它可以作为编辑器内的一等公民编码助手运行，无需云订阅、无需专有插件、无需任何厂商特定的配置。

通信方式基于 **stdio（标准输入/输出）与 JSON-RPC 2.0**：编辑器将 DotCraft 作为子进程启动，通过标准流双向交换消息。整个过程没有网络端口、无需管理认证 Token，数据也不会离开本机（除非你配置的 LLM 端点位于远端）。

## 支持的编辑器

ACP 是开放标准，生态持续扩展。目前已验证 DotCraft 可运行于以下环境：

| 编辑器 | 插件 / 集成方式 |
|--------|----------------|
| **JetBrains Rider**（及其他 JetBrains IDE） | AI Assistant 内置 Agent 支持 |
| **Obsidian** | [obsidian-agent-client](https://github.com/RAIT-09/obsidian-agent-client) |

任何其他支持 ACP 的编辑器或工具，均可使用相同的配置模式接入 DotCraft。

## 快速开始

### 1. 初始化 DotCraft 工作区

在接入编辑器之前，先在终端中进入项目目录并运行一次 DotCraft。这一步会创建 `.craft/` 文件夹，并生成默认配置文件和内置命令：

```bash
cd <你的项目目录>
dotcraft
```

DotCraft 初始化完成后会进入 CLI 模式，可以直接退出——工作区已准备就绪。模型配置等详细设置请参考[配置指南](./config_guide.md)。

### 2. 在编辑器中配置 ACP

在编辑器的 Agent 配置中，将**命令**设置为 `dotcraft`，并在**参数**中填入 `-acp`。DotCraft 以 `-acp` 标志启动时会自动激活 ACP 模式，无需修改任何配置文件。

**工作目录**应设置为第 1 步中完成初始化的项目根目录。

---

## JetBrains Rider（及 JetBrains IDE）

安装了 AI Assistant 插件的 JetBrains IDE 可以直接添加 ACP 代理。打开 **AIChat - Add Custom Agents**，新增一条代理配置：

```json
{
    "agent_servers": {
        "DotCraft": {
            "command": "dotcraft",
            "args": ["-acp"]
        }
    }
}
```

保存后，在 AI 聊天面板的代理选择器中选中 DotCraft 即可。IDE 负责进程的生命周期管理——打开会话时 DotCraft 自动启动，关闭时自动退出。

---

## Obsidian

安装 [obsidian-agent-client](https://github.com/RAIT-09/obsidian-agent-client) 插件（通过 BRAT 或手动安装），然后在插件设置中添加Custom agents：

| 字段 | 值 |
|------|----|
| **AgentID** | DotCraft |
| **Display name** | DotCraft |
| **Path** | `dotcraft.exe` |
| **Arguments** | `-acp` |

配置完成后，DotCraft 会出现在插件的聊天界面中。它既能回答问题，也能直接读写笔记——同一个代理，既是编码助手，也是知识库助理。

---

## 工作原理

编辑器以 ACP 模式启动 DotCraft 后，交互流程如下：

1. **初始化** — 双方交换协议版本和能力声明（`initialize`）
2. **创建会话** — 编辑器创建新会话（`session/new`）；DotCraft 将可用的斜杠命令和配置选项广播到编辑器 UI
3. **提示交互** — 编辑器发送用户消息（`session/prompt`）；DotCraft 通过 `session/update` 流式返回回复、工具调用状态和执行结果
4. **权限请求** — 执行文件写入或 Shell 命令前，DotCraft 发送 `requestPermission` 消息；编辑器向用户展示审批/拒绝提示
5. **文件与终端访问** — DotCraft 可通过编辑器读取文件（`fs/readTextFile`，含未保存内容）、以 diff 预览方式写入文件（`fs/writeTextFile`），以及创建/管理终端（`terminal/*`），所有操作均通过编辑器自身的 API 路由

这意味着 DotCraft 能够读取编辑器缓冲区中尚未保存的内容、在应用变更前展示内联 diff、并在编辑器管理的终端中执行命令——这些能力超出了普通 CLI 代理所能提供的范畴。

## 支持的协议功能

| 功能 | 说明 |
|------|------|
| `initialize` | 协议版本协商和能力交换 |
| `session/new` | 创建新会话 |
| `session/load` | 加载已有会话并回放历史 |
| `session/list` | 列出所有 ACP 会话 |
| `session/prompt` | 发送提示并流式接收回复 |
| `session/update` | DotCraft 向编辑器推送消息块和工具调用状态 |
| `session/cancel` | 取消正在进行的操作 |
| `requestPermission` | DotCraft 就敏感操作向编辑器请求执行权限 |
| `fs/readTextFile` | 通过编辑器读取文件（含未保存内容） |
| `fs/writeTextFile` | 通过编辑器写入文件（可预览 diff） |
| `terminal/*` | 通过编辑器创建和管理终端 |
| Slash Commands | `.craft/commands/` 中的自定义命令自动广播到编辑器命令选择器 |
| Config Options | 将可选配置（模式、模型等）暴露到编辑器 UI |

## 会话与工作区行为

ACP 会话遵循与所有其他 Channel 相同的隔离模型：

- **会话 ID 格式**：`acp_{sessionId}`（会话 ID 由编辑器分配后传给 DotCraft）
- **会话存储**：存储于 `<workspace>/.craft/sessions/`，与其他 Channel 的会话并列存放
- **共享记忆**：`memory/MEMORY.md` 和 `memory/HISTORY.md` 在同一工作区的所有 Channel 之间共享——在 ACP 会话中获取的知识，在 CLI 或 QQ 机器人会话中同样可以访问，反之亦然

在 [Gateway 模式](./config_guide.md#gateway-多-channel-并发模式)下，ACP 可与 QQ Bot、WeCom Bot、API 服务并发运行，共享同一套 HeartbeatService、CronService 和工作区。

## 延伸阅读

- [配置指南 — ACP 模式配置](./config_guide.md#acp-模式配置) — 完整配置参考
- [ACP 协议规范](https://agentclientprotocol.com/get-started/introduction) — 官方协议文档
- [Gateway 模式](./config_guide.md#gateway-多-channel-并发模式) — 将 ACP 与其他 Channel 并发运行
