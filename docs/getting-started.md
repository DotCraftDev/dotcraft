# DotCraft 快速开始

这条路径面向第一次使用 DotCraft 的用户：先安装 Desktop，选择项目目录初始化工作区，配置模型提供商，然后发起第一次对话。完成后再按需要进入 TUI、AppServer、API、SDK 或 Automations。

## 快速开始

### 1. 下载 Desktop

前往 [GitHub Releases](https://github.com/DotHarness/dotcraft/releases) 下载桌面应用：

| 平台 | 推荐文件 |
|------|----------|
| Windows | `DotCraft-Desktop-win-x64-Setup.exe` |
| macOS | `DotCraft-Desktop-macos-x64.dmg` |

Desktop 是推荐的第一入口，因为它把工作区选择、模型配置、会话、Diff、计划、自动化审核和运行状态放在同一个界面里。

![DotCraft Desktop](https://github.com/DotHarness/resources/raw/master/dotcraft/desktop.png)

如果你更喜欢从源码构建，请先安装 [.NET 10 SDK](https://dotnet.microsoft.com/download)、Rust toolchain 和 Node.js，然后在仓库根目录运行：

```bash
build.bat
```

Linux / macOS 可运行：

```bash
bash build_linux.bat
```

### 2. 初始化工作区

首次打开 Desktop 后选择一个真实项目目录作为工作区。DotCraft 会把这个项目的配置、会话、任务、技能和附件跟随项目保存，之后从 Desktop、终端或自动化入口进入时都能继续使用同一份上下文。

如果你想从终端完成首次初始化，也可以在还没有 `.craft/` 的项目目录中运行：

```bash
dotcraft
```

建议从真实项目目录开始，而不是空目录。这样 Agent 可以直接读取仓库结构、现有文档和构建脚本。

![工作区初始化向导](https://github.com/DotHarness/resources/raw/master/dotcraft/setup.png)

### 3. 配置模型

DotCraft 支持两种常用方式：

| 方式 | 适合场景 |
|------|----------|
| OpenAI-compatible API Key | 直接使用 OpenAI API、OpenRouter、DeepSeek 等兼容接口 |
| CLIProxyAPI | 复用本机 coding agent CLI，通过反向代理暴露 OpenAI-compatible 接口 |

![API 代理配置](https://github.com/DotHarness/resources/raw/master/dotcraft/api-proxy.png)

最小配置通常只需要：

```json
{
  "ApiKey": "sk-your-api-key",
  "Model": "gpt-4o-mini",
  "EndPoint": "https://api.openai.com/v1"
}
```

敏感信息建议放在全局配置；项目特定模型、工具和入口配置放在当前工作区配置。需要手动编辑文件时，对应路径是全局 `~/.craft/config.json` 和工作区 `<workspace>/.craft/config.json`。完整字段见 [配置与安全](./config_guide.md)。

### 4. 第一次运行

在 Desktop 中打开工作区后，新建会话并发送一个轻量请求，例如：

```text
请阅读这个仓库的 README 和 docs/index.md，告诉我这个项目怎么启动。
```

如果你更喜欢脚本友好的命令行入口，可以在项目目录运行一次性任务：

```bash
dotcraft exec "请阅读这个仓库的 README 和 docs/index.md，告诉我这个项目怎么启动。"
```

已经初始化的工作区中，`dotcraft` 不会进入交互式聊天；终端里的交互体验请使用 TUI。

如果你想使用终端富界面，请继续阅读 [TUI 指南](./tui_guide.md)。

## 理解入口模型

DotCraft 围绕 **统一会话核心（Unified Session Core）** 组织不同入口：命令行、Desktop、IDE、机器人与自动化并不是各自维护一套 agent 流程，而是复用同一个执行引擎与会话模型。

| 维度 | Gateway | Unified Session Core |
|------|---------|----------------------|
| 客户端定制 | 消息总线丢失难以定制 | 灵活自由的客户端 |
| 审批 / HITL | 无法表达平台原生的审批交互 | 以平台原生 UI 呈现 |
| 跨渠道恢复 | 不支持 | 会话可跨渠道恢复 |
| 工作区持久化 | 不支持 | 围绕工作区设计 |

![统一入口模型](https://github.com/DotHarness/resources/raw/master/dotcraft/entry.png)

DotCraft 将不同入口连接到同一个项目级工作空间，由统一会话核心负责承接执行、状态与编排。

## 配置

第一次使用只需要关心这些配置：

| 配置 | 用途 | 推荐位置 |
|------|------|----------|
| `ApiKey` | 模型 API Key | 全局配置 |
| `Model` | 默认模型名称 | 全局或工作区配置 |
| `EndPoint` | OpenAI-compatible API 地址 | 全局或工作区配置 |
| `Language` | 界面语言：`Chinese` / `English` | 全局配置 |
| `DashBoard.Enabled` | 启用 Web 调试与可视化配置 | 工作区配置 |

如果你不确定应该把配置放在哪里：API Key 放全局，其余先放工作区。

## 下一步按场景选择

| 我想做什么 | 下一步 |
|------------|--------|
| 使用图形界面持续协作 | [Desktop 指南](./desktop_guide.md) |
| 在终端里使用完整界面 | [TUI 指南](./tui_guide.md) |
| 远程或多客户端共享工作区 | [AppServer 模式指南](./appserver_guide.md) |
| 开放 OpenAI-compatible HTTP API | [API 模式指南](./api_guide.md) |
| 接入 IDE 或编辑器 | [ACP 模式指南](./acp_guide.md) |
| 构建机器人或外部适配器 | [SDK 总览](./sdk/index.md) |
| 运行本地自动化任务 | [Automations 指南](./automations_guide.md) |
| 查看 Trace、工具调用和配置合并结果 | [Dashboard 指南](./dash_board_guide.md) |

## 继续探索

### 社交渠道

DotCraft 通过 SDK 扩展集成 Telegram、微信、飞书、QQ、企业微信等社交渠道。先看 [Python SDK](./sdk/python.md) 和 [TypeScript SDK](./sdk/typescript.md)。

| Telegram（Python SDK） | 微信（TypeScript SDK） |
|:---:|:---:|
| ![Telegram 渠道示例](https://github.com/DotHarness/resources/raw/master/dotcraft/telegram.jpg) | ![微信渠道示例](https://github.com/DotHarness/resources/raw/master/dotcraft/wechat.jpg) |

### Automations

Automations 适合运行本地工作区任务。更多调度、线程绑定、模板和重试流程见 [Automations 指南](./automations_guide.md)。

| Desktop 自动化面板 |
|:---:|
| ![Desktop 自动化面板](https://github.com/DotHarness/resources/raw/master/dotcraft/desktop_automations.png) |

### Dashboard

Dashboard 是 DotCraft 的可视化观察与配置入口，用于查看会话、追踪调用和编辑工作区设置。更多页面说明见 [Dashboard 指南](./dash_board_guide.md)。

| 用量与会话概览 | 会话追踪 |
|:---:|:---:|
| ![Dashboard 用量概览](https://github.com/DotHarness/resources/raw/master/dotcraft/dashboard.png) | ![Dashboard 会话追踪](https://github.com/DotHarness/resources/raw/master/dotcraft/trace.png) |

## 进阶

- 使用 [Hooks](./hooks_guide.md) 在生命周期事件中执行脚本。
- 使用 [安全配置](./config/security.md) 限制文件、Shell 和网络能力。
- 使用 [Workspace Sample](./samples/workspace.md) 验证完整工作区模板。
- 回到 [文档索引](./reference.md)，按“我想做什么”选择路径。

## 故障排查

### Desktop 找不到 `dotcraft`

确认 DotCraft CLI 已在 `PATH` 中，或在 Desktop 设置中手动指定 AppServer / `dotcraft` 二进制路径。源码构建用户可先运行仓库根目录的 `build.bat`。

### 模型请求失败

检查 `ApiKey`、`Model` 和 `EndPoint` 是否匹配同一个提供商。OpenAI-compatible 地址通常以 `/v1` 结尾。

### 工作区配置没有生效

确认配置写在当前工作区的 `.craft/config.json`，并重启 Desktop 或相关 Host。部分 AppServer 和入口配置只在启动时读取。
