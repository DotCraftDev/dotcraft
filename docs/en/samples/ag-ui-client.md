# DotCraft AG-UI Client

A minimal Next.js app that connects to DotCraft's AG-UI server via CopilotKit. Use it to chat with DotCraft from the browser without cloning the CopilotKit monorepo.

## Prerequisites

- **DotCraft** running with AG-UI enabled (default: `http://localhost:5100/ag-ui`). In your DotCraft config (e.g. `.craft/config.json`), ensure:

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

## Setup

1. Install dependencies:

   ```bash
   pnpm install
   ```

   or `npm install` / `yarn install`.

2. (Optional) Copy env example and edit if needed:

   ```bash
   cp .env.example .env.local
   ```

   - `DOTCRAFT_AGUI_URL` — DotCraft AG-UI base URL (default: `http://127.0.0.1:5100/ag-ui`).
   - `DOTCRAFT_AGUI_API_KEY` — Set only when DotCraft has `AgUi.RequireAuth: true`; use the same key as in DotCraft config.

## Run

- **Development:**

  ```bash
  pnpm run dev
  ```

  Open http://localhost:3000 and use the chat.

- **Production build:**

  ```bash
  pnpm run build
  pnpm run start
  ```

## How it works

- The frontend uses [CopilotKit](https://www.npmjs.com/package/@copilotkitnext/react) with `runtimeUrl="/api/dotcraft"` and `useSingleEndpoint`.
- The Next.js API route `/api/dotcraft` proxies CopilotKit's single-endpoint protocol to DotCraft's AG-UI endpoint: it handles `info`, `agent/run`, and `agent/connect` by forwarding the request body to DotCraft and streaming the response back.

This sample does not depend on the CopilotKit repository; it uses only npm-published packages.
