# DotCraft AG-UI 模式指南

DotCraft AG-UI 模式通过 **[AG-UI 协议](https://github.com/ag-ui-protocol/ag-ui)** 将 Agent 能力以 SSE 流式推送的方式对外暴露，兼容任意支持 AG-UI 协议的客户端，例如 [CopilotKit](https://www.copilotkit.ai/)。

## 快速开始

### 1. 配置

在 `config.json` 中启用 AG-UI 模式：

```json
{
    "AgUi": {
        "Enabled": true,
        "Port": 5100,
        "Path": "/ag-ui"
    }
}
```

### 2. 启动

```bash
cd /your/workspace
dotcraft gateway
```

### 3. 连接

将 AG-UI 客户端指向 `http://127.0.0.1:5100/ag-ui` 即可开始对话。

DotCraft 提供了一个基于 Next.js + CopilotKit 的示例客户端，详见 [AG-UI Client Sample](./samples/ag-ui-client.md)。

---

## 配置项

| 配置项 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| `AgUi.Enabled` | bool | `false` | 是否启用 AG-UI 服务 |
| `AgUi.Path` | string | `"/ag-ui"` | 端点路径 |
| `AgUi.Host` | string | `"127.0.0.1"` | 绑定地址 |
| `AgUi.Port` | int | `5100` | 绑定端口 |
| `AgUi.RequireAuth` | bool | `false` | 是否启用 Bearer Token 认证 |
| `AgUi.ApiKey` | string | 空 | Bearer Token 值（`RequireAuth` 为 `true` 时必填） |

---

## 认证

当 `AgUi.RequireAuth` 为 `true` 时，所有请求需携带 Bearer Token：

```
Authorization: Bearer your-api-key
```

未通过认证的请求会返回 `401 Unauthorized`。

本地开发或内网部署可将 `RequireAuth` 保持为 `false` 以禁用认证。

---

## 示例客户端

[AG-UI Client Sample](./samples/ag-ui-client.md) 提供了一个最小可运行的 Next.js 客户端示例，通过 CopilotKit 连接 DotCraft AG-UI 服务。

**快速启动**：

```bash
cd samples/ag-ui-client
pnpm install
cp .env.example .env.local  # 按需修改 DOTCRAFT_AGUI_URL
pnpm run dev
```

打开 `http://localhost:3000` 即可在浏览器中与 DotCraft 对话。

若 DotCraft 启用了 `RequireAuth`，在 `.env.local` 中同步设置 `DOTCRAFT_AGUI_API_KEY`。

---

## Gateway 模式与端口共享

AG-UI 支持在 [Gateway 多 Channel 并发模式](./config_guide.md#gateway-多-channel-并发模式) 中与其他服务同时运行。

**端口冲突自动合并**：如果 `AgUi.Host:AgUi.Port` 与其他服务（例如 `Api` 或 `DashBoard`）配置相同，Gateway 的 **WebHostPool** 会自动将它们合并到同一个 Kestrel 服务器实例上，无需手动处理端口冲突。AG-UI 端点通过 `AgUi.Path`（默认 `/ag-ui`）与其他路由区分。

**示例：API 与 AG-UI 共享端口**：

```json
{
    "Gateway": { "Enabled": true },
    "Api": { "Enabled": true, "Port": 8080 },
    "AgUi": { "Enabled": true, "Port": 8080, "Path": "/ag-ui" }
}
```

此配置下，`http://127.0.0.1:8080/v1/chat/completions` 提供 OpenAI API，`http://127.0.0.1:8080/ag-ui` 提供 AG-UI SSE 端点，均来自同一个服务器进程。

## 使用示例

| 场景 | 做法 |
|------|------|
| 本地前端调试 | 启用 `AgUi.Enabled`，保持 `RequireAuth = false` |
| 生产或共享环境 | 启用 `RequireAuth` 并设置 `ApiKey` |
| 与 API 共用端口 | 在 Gateway 中配置相同 Host/Port，不同 Path |
| 验证客户端接入 | 使用 [AG-UI Client Sample](./samples/ag-ui-client.md) |

## 故障排查

### 前端收不到 SSE 事件

确认客户端连接的是 `AgUi.Path` 配置的路径，默认是 `/ag-ui`，并检查浏览器网络面板中的 SSE 状态。

### 认证失败

启用 `RequireAuth` 后，客户端必须提供与 `AgUi.ApiKey` 匹配的 Bearer Token 或示例客户端环境变量。

### 端口冲突

需要共用端口时请使用 Gateway；非 Gateway 模式下为 API、Dashboard 和 AG-UI 配置不同端口。
