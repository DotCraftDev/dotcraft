# DotCraft Desktop 指南

Desktop 是 DotCraft 推荐的第一入口。它作为 AppServer 客户端工作，用图形界面管理工作区、会话、Diff、计划、模型配置、自动化审核和运行状态。

## 快速开始

### 直接使用 Release

1. 从 [GitHub Releases](https://github.com/DotHarness/dotcraft/releases) 下载安装包。
2. 启动 DotCraft Desktop。
3. 选择项目目录作为工作区。
4. 在配置向导中填入 OpenAI-compatible API Key 或 CLIProxyAPI 地址。
5. 新建会话并发送第一次请求。

### 从源码运行

```bash
cd desktop
npm install
npm run dev
```

从源码运行 Desktop 时，应用会在 `PATH` 中查找 `dotcraft`。如果找不到，请在设置中指定 AppServer / `dotcraft` 二进制路径。

## 配置

Desktop 常见配置入口：

| 配置 | 说明 |
|------|------|
| Workspace | 当前项目目录，`.craft/` 状态会保存在这里 |
| AppServer binary | `dotcraft` 或自定义 AppServer 二进制路径 |
| Model provider | OpenAI-compatible API Key、模型名、Endpoint |
| Dashboard / Automations | 可视化调试和自动化审核入口 |

也可以通过启动参数覆盖：

```bash
DotCraft Desktop --app-server /path/to/dotcraft
DotCraft Desktop --workspace /path/to/project
```

## 使用示例

| 场景 | Desktop 中的路径 |
|------|------------------|
| 第一次使用 | 选择工作区 → 配置模型 → 新建会话 |
| 查看 Agent 做了什么 | 打开会话详情、Diff、Trace 或 Dashboard |
| 审核自动化任务 | 打开 Automations 面板，查看待审核任务 |
| 切换项目 | 选择另一个 workspace，让配置和任务跟随项目隔离 |

## 进阶

- Desktop 消费 AppServer Wire Protocol，因此可与 CLI、TUI、ACP、外部渠道共享同一会话核心。
- 图片附件会保存到工作区 `.craft/attachments/images/`，重启后仍可恢复缩略图。
- 打包安装包可运行 `npm run dist`，产物位于 `desktop/dist/`。

## 故障排查

### Desktop 启动后没有可用会话

确认已经选择工作区，并且 AppServer / `dotcraft` 二进制可执行。

### 设置修改后没有生效

模型和工作区字段通常会立即用于新会话；AppServer、端口、入口模式等启动级配置需要重启 Desktop 或后台 Host。

### 自动化面板为空

确认使用 Gateway 或支持 Automations 的 Host，并在配置中启用对应任务来源。更多细节见 [Automations 指南](./automations_guide.md)。
