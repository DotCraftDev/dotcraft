# DotCraft Desktop

**中文 | [English](./README.md)**

基于 Electron 的 [DotCraft](../README_ZH.md) 桌面 GUI 客户端 —— 围绕项目打造持久 AI 工作空间的 Agent Harness。

DotCraft Desktop 通过 Wire Protocol（基于 stdio 的 JSON-RPC）连接到 DotCraft AppServer，提供完整的桌面交互界面，包括会话线程管理、实时流式输出、代码审查（差异查看、撤销/重新应用）、审批流程、计划追踪以及 git 提交集成。

---

## 前提条件

- **Node.js 18+** 和 **npm**
- **DotCraft AppServer** 二进制文件（`dotcraft` / `dotcraft.exe`）——需在 `PATH` 中或通过应用设置配置。可从 [GitHub Releases](https://github.com/DotCraftDev/DotCraft/releases) 下载，或[从源码构建](../README_ZH.md#从源码构建)。

---

## 快速开始

```bash
# 1. 安装依赖
cd desktop
npm install

# 2. 以开发模式启动（支持热重载）
npm run dev
```

应用启动后会连接当前工作目录下的 DotCraft AppServer。

---

## 全部命令

| 命令 | 说明 |
|------|------|
| `npm run dev` | 以开发模式启动（热重载） |
| `npm run build` | 通过 electron-vite 编译 main / preload / renderer |
| `npm run preview` | 在浏览器中预览已编译的 renderer |
| `npm test` | 单次运行单元测试（Vitest） |
| `npm run test:watch` | 以监听模式运行单元测试 |
| `npm run e2e` | 运行 E2E 冒烟测试（Playwright） |
| `npm run pack` | 打包应用但不生成安装包（输出解压目录） |
| `npm run dist` | 构建并生成各平台安装包（详见下文） |

---

## 构建发行包

`npm run dist` 会依次执行 `electron-vite build` 和 `electron-builder`，在 `desktop/dist/` 下生成以下文件：

| 平台 | 输出 |
|------|------|
| **Windows** | NSIS 安装程序（`DotCraft Desktop Setup *.exe`）、便携版可执行文件（`DotCraft Desktop *.exe`）、zip |
| **macOS** | DMG（`DotCraft Desktop-*.dmg`）、zip |
| **Linux** | AppImage（`DotCraft Desktop-*.AppImage`）、Debian 安装包（`*.deb`） |

### Windows — 便携版 EXE

便携版构建产物是单个自包含的 `.exe` 文件，无需安装，下载即可直接运行。

### 手动按平台构建

```bash
# Windows 安装包 + 便携版
npx electron-builder --win

# macOS DMG
npx electron-builder --mac

# Linux AppImage + deb
npx electron-builder --linux
```

---

## 项目结构

```
desktop/
├── src/
│   ├── main/           # Electron 主进程（Node.js）
│   │   ├── index.ts    # 应用入口 —— 窗口创建、AppServer 生命周期
│   │   ├── ipcBridge.ts # 主进程与渲染进程之间的 IPC 处理器
│   │   ├── AppServerManager.ts
│   │   └── WireProtocolClient.ts
│   ├── preload/        # 预加载脚本 —— contextBridge API 暴露
│   │   ├── index.ts
│   │   └── api.d.ts    # window.api 类型声明
│   └── renderer/       # React SPA（渲染进程）
│       ├── App.tsx     # 根组件，Wire Protocol 事件绑定
│       ├── components/ # UI 组件（布局、会话、详情面板）
│       ├── stores/     # Zustand 状态管理（conversation、UI、thread）
│       ├── types/      # 共享 TypeScript 类型
│       └── utils/      # 工具函数（差异提取等）
├── e2e/                # Playwright E2E 测试
├── electron.vite.config.ts
├── package.json
└── vitest.config.ts
```

---

## 架构说明

```
┌─────────────────────────────────────────────────────┐
│  Electron 主进程（Node.js）                           │
│                                                       │
│  AppServerManager ──stdio──► dotcraft app-server      │
│  WireProtocolClient（JSON-RPC 2.0）                   │
│  ipcBridge（IPC 处理器）                              │
└──────────────┬──────────────────────────────────────┘
               │ contextBridge（window.api）
┌──────────────▼──────────────────────────────────────┐
│  预加载脚本                                           │
│  暴露：appServer、window、shell、file、git API        │
└──────────────┬──────────────────────────────────────┘
               │ window.api.*
┌──────────────▼──────────────────────────────────────┐
│  渲染进程（React + Zustand）                          │
│                                                       │
│  App.tsx —— 绑定 Wire Protocol 通知事件               │
│  ├── Sidebar（会话线程列表）                          │
│  ├── ConversationPanel（消息流 + 输入框）             │
│  └── DetailPanel（变更 / 计划 / 终端 标签页）         │
└─────────────────────────────────────────────────────┘
```

**主进程**负责启动和管理 DotCraft AppServer 子进程，通过 Wire Protocol（基于 stdio 的 JSON-RPC 2.0）与其通信，并通过 Electron IPC 将通知/请求转发给渲染进程。

**渲染进程**是一个 React SPA，所有状态由 Zustand store 管理（`conversationStore`、`uiStore`、`threadStore`、`connectionStore`）。渲染进程不直接访问 Node.js API，只通过预加载脚本暴露的 `window.api` 类型化接口进行通信。

---

## 功能特性

| 功能 | 说明 |
|------|------|
| **会话线程** | 多线程侧边栏、新建线程、历史记录 |
| **流式响应** | Agent 消息与推理内容实时显示 |
| **工具调用可视化** | 可折叠工具调用卡片、文件差异内联显示 |
| **详情面板** | 变更标签页（文件差异查看、撤销/重新应用）、计划标签页、终端标签页 |
| **审批流程** | Agent 审批请求以交互卡片形式呈现 |
| **Git 集成** | 提交对话框 —— 暂存并提交已接受的文件变更 |
| **全局快捷键** | `Ctrl+N` 新建线程、`Ctrl+B` 侧边栏、`Ctrl+Shift+B` 详情面板 |

---

## 配置

应用从用户数据目录（`app.getPath('userData')`）下的 `settings.json` 读取 DotCraft AppServer 二进制路径。首次启动时会自动在 `PATH` 中搜索 `dotcraft` / `dotcraft.exe`。

如需手动指定，可在启动时传入参数：

```bash
# 指定二进制路径
DotCraft Desktop --app-server /path/to/dotcraft

# 打开指定工作区
DotCraft Desktop --workspace /path/to/project
```

---

## 许可证

Apache License 2.0 —— 详见 [../LICENSE](../LICENSE)。
