# DotCraft Desktop

**中文 | [English](./README.md)**

基于 Electron 的 [DotCraft](../README_ZH.md) 桌面客户端：打开工作区、与 Agent 对话、查看文件变更，并在服务端启用时编排自动化任务。

---

## 前提条件

- **Node.js 18+** 与 **npm**
- **DotCraft AppServer**（`dotcraft` / `dotcraft.exe`）需在 `PATH` 中或通过应用设置指定 —— 见 [GitHub Releases](https://github.com/DotCraftDev/DotCraft/releases) 或[从源码构建](../README_ZH.md#从源码构建)。

---

## 快速开始

```bash
cd desktop
npm install
npm run dev
```

窗口会使用当前工作区目录（或通过 `--workspace` 传入的路径）。

---

## npm 命令

| 命令 | 说明 |
|------|------|
| `npm run dev` | 开发模式（热重载） |
| `npm run build` | 生产构建 |
| `npm run preview` | 浏览器预览已构建的 renderer |
| `npm test` | 单元测试（Vitest） |
| `npm run e2e` | 冒烟 E2E |
| `npm run pack` / `npm run dist` | 打包 / 安装包（见下文） |

---

## 安装包

`npm run dist` 在 `desktop/dist/` 下生成各平台产物（Windows 含 NSIS 与便携 exe，macOS DMG，Linux AppImage/deb）。Windows 便携版为单个 `.exe`，无需安装。

```bash
npx electron-builder --win   # 或 --mac / --linux
```

---

## 界面使用说明

**工作区** — 通过菜单或欢迎流程选择/切换文件夹；一个窗口对应一个工作区。

**对话** — 侧栏为会话列表；**新建会话**（`Ctrl+N`）。在输入框发送消息，主区域显示流式回复与工具调用。

**详情面板**（`Ctrl+Shift+B`）— **变更**：文件差异；在支持处可撤销/重新应用。**计划** / **终端** 等视服务端能力而定。

**Git** — 应用支持在「变更」流程中**将所选已修改文件暂存并填写说明后提交**（底层为 `git add` + `git commit`）。**不包含**完整 Git 客户端能力（无克隆、拉取、分支等界面）。

**自动化**（侧栏 **Automations**，仅当服务端声明支持该能力时可用）：

1. **新建任务** — 填写标题与说明；**Agent workspace** 选「项目目录」或「隔离工作区」；**工具策略** 选工作区内工具或全自动。提交后由服务端编排器执行。
2. 顶部 **全部 / Local / GitHub** 可按来源筛选任务列表；其中 GitHub 任务会和本地任务一起显示在同一个 Automations 视图中。
3. 选中任务后根据状态点击 **Review** 或 **View** 打开审查侧栏，查看该任务的实时或历史 Agent 活动；若任务处于待审状态，可在此批准或拒绝。

**快捷键** — `Ctrl+B` 侧栏、`Ctrl+Shift+B` 详情面板（具体以当前平台为准）。

---

## 配置

AppServer 二进制路径保存在应用用户数据目录下的 `settings.json`；首次启动会在 `PATH` 中查找 `dotcraft`。

```bash
DotCraft Desktop --app-server /path/to/dotcraft
DotCraft Desktop --workspace /path/to/project
```

---

## 许可证

Apache License 2.0 —— 见 [../LICENSE](../LICENSE)。
