# DotCraft TUI 指南

TUI 是 DotCraft 的 Rust 原生终端界面，适合希望在终端中获得完整交互体验的用户。它通过 AppServer Wire Protocol 连接 DotCraft，并复用同一套工作区、会话和审批能力。

## 快速开始

### 构建

```bash
cd tui
cargo build --release
```

构建产物位于 `target/release/dotcraft-tui`，Windows 下为 `dotcraft-tui.exe`。

### 本地子进程模式

在项目目录中启动：

```bash
dotcraft-tui
```

TUI 会自动启动 `dotcraft` 作为 AppServer 子进程，并通过 stdio 通信。

### 指定工作区或二进制

```bash
dotcraft-tui --workspace /path/to/project
dotcraft-tui --server-bin /usr/local/bin/dotcraft
DOTCRAFT_BIN=/usr/local/bin/dotcraft dotcraft-tui
```

## 配置

| 参数 | 说明 |
|------|------|
| `--workspace` | 指定工作区目录 |
| `--server-bin` | 指定 `dotcraft` / AppServer 二进制 |
| `--remote` | 连接已有 WebSocket AppServer |
| `--lang zh|en` | 指定界面语言 |
| `--theme` | 加载自定义主题 TOML |

远程 WebSocket 示例：

```bash
dotcraft app-server --listen ws://127.0.0.1:9100
dotcraft-tui --remote ws://127.0.0.1:9100/ws
```

## 使用示例

| 场景 | 命令 |
|------|------|
| 当前项目直接使用 | `dotcraft-tui` |
| 指定项目目录 | `dotcraft-tui --workspace /path/to/project` |
| 连接远程服务 | `dotcraft-tui --remote ws://host:9100/ws` |
| 英文界面 | `dotcraft-tui --lang en` |

常用斜杠命令包括 `/new`、`/compact`、`/clear`、`/quit`。完整快捷键和主题说明见仓库中的 `tui/README_ZH.md`。

## 进阶

- 默认模式适合本地使用；远程模式适合共享 AppServer 或多客户端接入。
- 可通过 `DOTCRAFT_TUI_LOG=debug dotcraft-tui 2>tui.log` 打开日志。
- 可用 `cargo build --release --features clipboard` 构建系统剪贴板支持。

## 故障排查

### TUI 找不到 `dotcraft`

把 `dotcraft` 加入 `PATH`，或使用 `--server-bin` / `DOTCRAFT_BIN` 指定二进制路径。

### 远程连接失败

确认 AppServer 使用 WebSocket 模式启动，并且客户端 URL 包含 `/ws` 路径。带认证服务需要同时传入 token。

### 终端显示异常

确认终端尺寸足够，并使用支持 Unicode 和颜色的现代终端。必要时先使用默认主题排查。
