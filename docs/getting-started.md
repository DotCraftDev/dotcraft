# DotCraft 快速开始

这条路径面向第一次使用 DotCraft 的用户：先安装 Desktop，选择项目目录初始化工作区，配置模型提供商，然后发起第一次对话。完成后再按需要进入 TUI、AppServer、API、SDK 或 Automations。

## Quick Start

### 1. 下载 Desktop

前往 [GitHub Releases](https://github.com/DotHarness/dotcraft/releases) 下载桌面应用：

| 平台 | 推荐文件 |
|------|----------|
| Windows | `DotCraft-Desktop-win-x64-Setup.exe` |
| macOS | `DotCraft-Desktop-macos-x64.dmg` |

Desktop 是推荐的第一入口，因为它把工作区选择、模型配置、会话、Diff、计划、自动化审核和运行状态放在同一个界面里。

### 2. 初始化工作区

首次打开 Desktop 后选择一个真实项目目录作为工作区。DotCraft 会把这个项目的配置、会话、任务、技能和附件跟随项目保存，之后从 Desktop、终端或自动化入口进入时都能继续使用同一份上下文。

建议从真实项目目录开始，而不是空目录。这样 Agent 可以直接读取仓库结构、现有文档和构建脚本。

### 3. 配置模型

DotCraft 支持两种常用方式：

| 方式 | 适合场景 |
|------|----------|
| OpenAI-compatible API Key | 直接使用 OpenAI API、OpenRouter、DeepSeek 等兼容接口 |
| CLIProxyAPI | 复用本机 coding agent CLI，通过反向代理暴露 OpenAI-compatible 接口 |

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

如果你更喜欢终端入口，可以在项目目录直接运行：

```bash
dotcraft
```

如果你想使用终端富界面，请继续阅读 [TUI 指南](./tui_guide.md)。

## Configuration

第一次使用只需要关心这些配置：

| 配置 | 用途 | 推荐位置 |
|------|------|----------|
| `ApiKey` | 模型 API Key | 全局配置 |
| `Model` | 默认模型名称 | 全局或工作区配置 |
| `EndPoint` | OpenAI-compatible API 地址 | 全局或工作区配置 |
| `Language` | 界面语言：`Chinese` / `English` | 全局配置 |
| `DashBoard.Enabled` | 启用 Web 调试与可视化配置 | 工作区配置 |

如果你不确定应该把配置放在哪里：API Key 放全局，其余先放工作区。

## Usage Examples

| 我想做什么 | 下一步 |
|------------|--------|
| 使用图形界面持续协作 | [Desktop 指南](./desktop_guide.md) |
| 在终端里使用完整界面 | [TUI 指南](./tui_guide.md) |
| 远程或多客户端共享工作区 | [AppServer 模式指南](./appserver_guide.md) |
| 开放 OpenAI-compatible HTTP API | [API 模式指南](./api_guide.md) |
| 接入 IDE 或编辑器 | [ACP 模式指南](./acp_guide.md) |
| 运行本地或 GitHub 自动化任务 | [Automations 指南](./automations_guide.md) |
| 构建机器人或外部适配器 | [SDK 总览](./sdk/index.md) |

## Advanced Topics

- 使用 [Dashboard](./dash_board_guide.md) 查看 Trace、工具调用和配置合并结果。
- 使用 [Hooks](./hooks_guide.md) 在生命周期事件中执行脚本。
- 使用 [沙箱和安全配置](./config_guide.md#安全配置) 限制文件、Shell 和网络能力。
- 使用 [Workspace Sample](./samples/workspace.md) 验证完整工作区模板。

## Troubleshooting

### Desktop 找不到 `dotcraft`

确认 DotCraft CLI 已在 `PATH` 中，或在 Desktop 设置中手动指定 AppServer / `dotcraft` 二进制路径。源码构建用户可先运行仓库根目录的 `build.bat`。

### 模型请求失败

检查 `ApiKey`、`Model` 和 `EndPoint` 是否匹配同一个提供商。OpenAI-compatible 地址通常以 `/v1` 结尾。

### 工作区配置没有生效

确认配置写在当前工作区的 `.craft/config.json`，并重启 Desktop 或相关 Host。部分 AppServer 和入口配置只在启动时读取。

### 不确定下一篇该看什么

回到 [文档索引](./reference.md)，按“我想做什么”选择路径。
