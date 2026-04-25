# DotCraft AG-UI Client

一个极简的 Next.js 应用，通过 CopilotKit 连接到 DotCraft 的 AG-UI 服务端。无需克隆 CopilotKit monorepo，即可在浏览器中与 DotCraft 对话。

## 前置条件

- **DotCraft** 已启用 AG-UI 模式（默认：`http://localhost:5100/ag-ui`）。在 DotCraft 配置文件（如 `.craft/config.json`）中确保：

  ```json
  {
    "AgUi": {
      "Enabled": true,
      "Port": 5100,
      "Path": "/ag-ui"
    }
  }
  ```

- Node.js 18+

## 安装

1. 安装依赖：

   ```bash
   pnpm install
   ```

   或使用 `npm install` / `yarn install`。

2. （可选）复制环境变量示例文件并根据需要修改：

   ```bash
   cp .env.example .env.local
   ```

   - `DOTCRAFT_AGUI_URL` — DotCraft AG-UI 基础地址（默认：`http://127.0.0.1:5100/ag-ui`）。
   - `DOTCRAFT_AGUI_API_KEY` — 仅当 DotCraft 配置了 `AgUi.RequireAuth: true` 时需要设置，使用与 DotCraft 配置中相同的密钥。

## 运行

- **开发模式：**

  ```bash
  pnpm run dev
  ```

  打开 `http://localhost:3000` 即可开始对话。

- **生产构建：**

  ```bash
  pnpm run build
  pnpm run start
  ```

## 工作原理

- 前端使用 [CopilotKit](https://www.npmjs.com/package/@copilotkitnext/react)，配置 `runtimeUrl="/api/dotcraft"` 和 `useSingleEndpoint`。
- Next.js API 路由 `/api/dotcraft` 将 CopilotKit 的单端点协议代理到 DotCraft 的 AG-UI 端点：处理 `info`、`agent/run` 和 `agent/connect` 请求，将请求体转发到 DotCraft 并流式返回响应。

本示例不依赖 CopilotKit 仓库，仅使用 npm 发布的包。
