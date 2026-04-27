# DotCraft Dashboard API

Dashboard API 面向调试界面和内部工具。普通用户通常只需要使用 Dashboard 页面；需要写集成或排查前端问题时再查本页。

## Trace 事件类型

| 类型 | 说明 |
|------|------|
| `session.started` | 会话开始 |
| `session.completed` | 会话完成 |
| `turn.started` | Agent 回合开始 |
| `turn.completed` | Agent 回合完成 |
| `tool.started` | 工具调用开始 |
| `tool.completed` | 工具调用完成 |
| `tool.failed` | 工具调用失败 |
| `approval.requested` | 需要人工审批 |
| `approval.completed` | 审批完成 |
| `error` | 运行错误 |

## 端点

### `GET /DashBoard`

返回 Dashboard 页面。

### `GET /DashBoard/api/summary`

返回运行摘要，包括会话数量、最近事件和模块状态。

### `GET /DashBoard/api/sessions`

返回 Dashboard 可见的会话列表。

### `GET /DashBoard/api/sessions/{sessionKey}/events`

返回指定会话的 Trace 事件。

### `GET /dashboard/api/orchestrators/automations/state`

返回 Automations 编排器状态，包括任务、Cron 和 GitHub 来源摘要。

### `POST /dashboard/api/orchestrators/automations/refresh`

请求刷新 Automations 状态。

### `GET /dashboard/api/config/schema`

返回 Dashboard Settings 页面使用的配置 schema。

### `DELETE /api/sessions/{sessionKey}`

删除指定 Dashboard 会话记录。

### `DELETE /api/sessions`

清空 Dashboard 会话记录。

### `GET /api/events/stream`

返回 Dashboard 使用的事件流。

## 使用建议

- API 路径大小写沿用现有 Dashboard 路由。
- 调试本地页面时优先绑定 `127.0.0.1`。
- 生产或共享网络环境中不要暴露未加保护的 Dashboard。
