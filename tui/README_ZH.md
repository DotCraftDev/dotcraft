# DotCraft TUI

**中文 | [English](./README.md)**

DotCraft 的 Rust 原生终端界面，基于 [Ratatui](https://ratatui.rs/) 构建，通过 Wire Protocol（JSON-RPC）连接 DotCraft AppServer，在终端中提供完整的 AI Agent 交互体验。

## 功能特性

| 功能 | 说明 |
|------|------|
| **流式输出** | Agent 消息逐字流式渲染，支持 Markdown（代码高亮、表格、标题等） |
| **工具调用展示** | `• Called ReadFile("src/main.rs") (0.3s)` 格式，含耗时和结果摘要 |
| **StatusIndicator** | 任务运行时显示 `⠋ Working (Ns · esc to interrupt)`，带文字闪烁动画 |
| **FooterLine** | 底部单行情境提示（快捷键、模式、Token 用量、连接状态） |
| **WelcomeScreen** | 启动画面，含 ASCII Logo（大终端）、工作区路径与连接状态 |
| **内联 SubAgent 进度** | SubAgent 运行状态内联展示，全部完成后折叠为摘要行 |
| **内联 Plan 视图** | Agent 任务计划（待办清单）内联展示 |
| **会话管理** | `/sessions` 打开会话选择器，支持恢复/归档/删除 |
| **审批流** | 工具调用需要审批时弹出 `ApprovalOverlay`，支持多种决策选项 |
| **多语言** | 内置中文 / English 切换（`--lang zh` / `--lang en`） |
| **主题定制** | 通过 TOML 文件自定义配色 |
| **剪贴板** | `y` 键复制最后一条 Agent 消息（需 `clipboard` feature） |
| **WebSocket 模式** | 可连接远程 AppServer（需 `websocket` feature） |

## 构建

**前提条件**：Rust 工具链（stable channel），推荐通过 [rustup](https://rustup.rs/) 安装。

```bash
# 进入 tui 目录
cd tui

# 标准构建（包含 WebSocket 支持）
cargo build --release

# 不含 WebSocket（仅 stdio 模式）
cargo build --release --no-default-features

# 包含系统剪贴板支持
cargo build --release --features clipboard
```

构建产物位于 `target/release/dotcraft-tui`（Windows 下为 `dotcraft-tui.exe`）。

## 启动方式

### 方式一：子进程模式（默认）

TUI 会自动将 `dotcraft`（或 `--server-bin` 指定的二进制）作为 AppServer 子进程启动，通过 stdio 通信。

```bash
# 在项目目录下直接启动（dotcraft 需在 PATH 中）
dotcraft-tui

# 指定工作区路径
dotcraft-tui --workspace /path/to/project

# 指定 dotcraft 二进制路径
dotcraft-tui --server-bin /usr/local/bin/dotcraft

# 通过环境变量指定二进制路径
DOTCRAFT_BIN=/usr/local/bin/dotcraft dotcraft-tui
```

### 方式二：远程 WebSocket 模式

连接到已在运行的 AppServer（需 `websocket` feature）。

```bash
# 连接本地 AppServer
dotcraft-tui --remote ws://localhost:3000/ws

# 连接带认证的远程 AppServer
dotcraft-tui --remote "ws://host:3000/ws?token=your-secret"

# 配合 --workspace 指定工作区
dotcraft-tui --remote ws://host:3000/ws --workspace /path/to/project
```

AppServer 启动方式参考：

```bash
# 启动 AppServer（WebSocket 模式）
dotcraft app-server --listen ws://0.0.0.0:3000
```

### 语言与主题

```bash
# 使用中文界面（默认）
dotcraft-tui --lang zh

# 使用英文界面
dotcraft-tui --lang en

# 使用自定义主题
dotcraft-tui --theme /path/to/theme.toml
```

### 命令行参数速查

| 参数 | 说明 | 默认值 |
|------|------|--------|
| `--remote <URL>` | 连接远程 AppServer（WebSocket URL） | — |
| `--server-bin <PATH>` | AppServer 二进制路径 | `dotcraft` |
| `--workspace <PATH>` | 工作区路径 | 当前目录 |
| `--theme <PATH>` | 自定义主题 TOML 路径 | 内置深色主题 |
| `--lang <LANG>` | 界面语言（`zh` / `en`） | `zh` |

## 快捷键

| 按键 | 作用 |
|------|------|
| `Enter` | 发送消息 |
| `Shift+Enter` | 在输入框内插入换行 |
| `Tab` | 任务运行中：将消息加入队列；空闲时：斜杠命令补全 |
| `Ctrl+C` | 任务运行中：中断当前 Agent；空闲时：第一次标记退出意图，再次按下退出 |
| `Shift+Tab` | 切换 Agent / Plan 模式 |
| `↑` / `↓` | 输入框内容为空时：历史消息导航；有内容时：滚动对话区域 |
| `PageUp` / `PageDown` | 对话区域翻页 |
| `Home` / `End` | 对话区域跳到顶部 / 底部 |
| `?` 或 `/help` | 打开快捷键帮助 |
| `y` | 复制最后一条 Agent 消息到剪贴板（需 `clipboard` feature） |
| `s` | SubAgent 全部完成后：展开 / 折叠详情 |
| `Ctrl+L` | 强制刷新终端 |

## 斜杠命令

| 命令 | 说明 |
|------|------|
| `/help` | 显示快捷键帮助 |
| `/sessions` | 打开会话管理器 |
| `/new` | 开启新会话 |
| `/clear` | 清空当前对话历史 |
| `/load <thread-id>` | 加载指定会话 |
| `/agent` | 切换到 Agent 模式 |
| `/plan` | 切换到 Plan 模式 |
| `/cron` | 列出 Cron 任务 |
| `/quit` | 退出 TUI |

## 主题配置

在 `--theme` 指定的 TOML 文件中自定义颜色（颜色支持 Ratatui 颜色名或 `#RRGGBB`）：

```toml
[colors]
brand = "#7C3AED"           # 品牌色（Logo、模式指示器）
user_message = "white"
agent_message = "white"
reasoning = "cyan"
tool_active = "yellow"
tool_completed = "gray"
error = "red"
success = "green"
dim = "dark_gray"
mode_agent = "green"
mode_plan = "blue"
status_indicator = "yellow" # "Working" 状态文字颜色

[footer]
foreground = "dark_gray"    # 底栏提示文字
context_color = "dark_gray" # Token 用量与连接状态

[code]
syntect_theme = "base16-ocean.dark"  # 代码高亮主题
```

## 日志

设置 `DOTCRAFT_TUI_LOG` 环境变量启用日志输出（日志写入 stderr）：

```bash
DOTCRAFT_TUI_LOG=debug dotcraft-tui 2>tui.log
```
