# DotCraft Dashboard 指南

Dashboard 是 DotCraft 的 Web 调试与可视化配置界面，用于查看会话、Trace、工具调用、自动化状态和配置合并结果。它适合排查“Agent 做了什么”和“配置为什么这样生效”。

## 快速开始

### 1. 启用 Dashboard

在 `.craft/config.json` 中添加：

```json
{
  "DashBoard": {
    "Enabled": true,
    "Host": "127.0.0.1",
    "Port": 8080
  }
}
```

### 2. 启动 DotCraft

```bash
dotcraft gateway
```

### 3. 打开 Dashboard

默认地址：

```text
http://127.0.0.1:8080/dashboard
```

### 4. 触发一次 Agent 执行

从 CLI、Desktop、TUI 或其他入口发起一次对话。Dashboard 会展示会话、工具调用、错误和配置状态。

## 配置

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `DashBoard.Enabled` | 是否启用 Dashboard | `false` |
| `DashBoard.Host` | 监听地址 | `127.0.0.1` |
| `DashBoard.Port` | 监听端口 | `8080` |

将 `Host` 设为 `0.0.0.0` 会允许外部网络访问 Dashboard。Dashboard 可能展示 prompt、工具参数和工具结果，请确认网络边界安全。

## 使用示例

| 场景 | 使用方式 |
|------|----------|
| 第一次确认模型是否能调用 | 触发一次会话，查看 Trace Timeline |
| 排查工具调用失败 | 打开会话详情，过滤 Tools / Errors 事件 |
| 检查配置合并结果 | 打开 Settings 页面，对比全局配置和工作区配置 |
| 审核自动化状态 | 在 Gateway + Automations 环境中查看 Automations 面板 |

## 进阶

### 运行模式

| 模式 | 说明 |
|------|------|
| 本地 Dashboard | 用于调试当前工作区 |
| Gateway Dashboard | 与 API、AG-UI、Automations 和外部渠道共用后台 |
| 共享端口 | API、AG-UI 和 Dashboard 可由 Gateway 合并到同一 HTTP 服务 |

### 前端界面

| 页面 | 用途 |
|------|------|
| Dashboard | 查看运行摘要和入口状态 |
| Sessions | 查看会话列表与详情 |
| Trace Timeline | 按时间线检查 Agent、工具和错误事件 |
| Settings | 查看配置 schema、全局配置、工作区配置和合并结果 |
| Automations | 查看本地任务、Cron 和活动状态 |

### API 参考

Dashboard 的 Trace 事件类型和 HTTP 端点见 [Dashboard API](./reference/dashboard-api.md)。

## 故障排查

### 浏览器打不开 Dashboard

确认 `DashBoard.Enabled = true`，并使用控制台输出的地址。默认路径是 `http://127.0.0.1:8080/dashboard`。

### Automations 面板不显示

Automations 面板需要 Gateway 加载 Automations 模块。本地 Dashboard 适合调试当前工作区，不负责展示完整自动化编排状态。

### 修改 Settings 后没有变化

模型类字段通常影响新会话；AppServer、端口、Gateway 和外部渠道等启动级字段需要重启 DotCraft。
