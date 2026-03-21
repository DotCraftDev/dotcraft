# DotCraft Desktop

**[中文](./README_ZH.md) | English**

An Electron-based GUI client for [DotCraft](../README.md) — the Agent Harness that crafts a persistent AI workspace around your project.

DotCraft Desktop connects to the DotCraft AppServer via the Wire Protocol (JSON-RPC over stdio) and provides a full-featured desktop interface including conversation threads, real-time streaming, code review (diff viewer, revert/re-apply), approval flows, plan tracking, and git commit integration.

---

## Prerequisites

- **Node.js 18+** and **npm**
- **DotCraft AppServer** binary (`dotcraft` / `dotcraft.exe`) — must be on your `PATH` or configured in the app settings. Download from [GitHub Releases](https://github.com/DotCraftDev/DotCraft/releases) or [build from source](../README.md#build-from-source).

---

## Quick Start

```bash
# 1. Install dependencies
cd desktop
npm install

# 2. Launch in development mode (hot reload)
npm run dev
```

The app launches and connects to the DotCraft AppServer for the current working directory.

---

## All Commands

| Command | Description |
|---------|-------------|
| `npm run dev` | Launch in development mode with hot reload |
| `npm run build` | Compile main / preload / renderer via electron-vite |
| `npm run preview` | Preview the compiled renderer in a browser |
| `npm test` | Run unit tests once (Vitest) |
| `npm run test:watch` | Run unit tests in watch mode |
| `npm run e2e` | Run E2E smoke tests (Playwright) |
| `npm run pack` | Package the app without producing an installer (unpacked dir) |
| `npm run dist` | Build and produce platform installers (see below) |

---

## Building Distributable Packages

`npm run dist` runs `electron-vite build` followed by `electron-builder` and produces the following outputs in `desktop/dist/`:

| Platform | Output |
|----------|--------|
| **Windows** | NSIS installer (`DotCraft Desktop Setup *.exe`), portable executable (`DotCraft Desktop *.exe`), zip |
| **macOS** | DMG (`DotCraft Desktop-*.dmg`), zip |
| **Linux** | AppImage (`DotCraft Desktop-*.AppImage`), Debian package (`*.deb`) |

### Windows — Portable EXE

The portable build produces a single self-contained `.exe` — no installation required. Just download and run.

### Manual per-platform build

```bash
# Windows installer + portable
npx electron-builder --win

# macOS DMG
npx electron-builder --mac

# Linux AppImage + deb
npx electron-builder --linux
```

---

## Project Structure

```
desktop/
├── src/
│   ├── main/           # Electron Main Process (Node.js)
│   │   ├── index.ts    # App entry — window creation, AppServer lifecycle
│   │   ├── ipcBridge.ts # IPC handlers between Main and Renderer
│   │   ├── AppServerManager.ts
│   │   └── WireProtocolClient.ts
│   ├── preload/        # Preload script — contextBridge API exposure
│   │   ├── index.ts
│   │   └── api.d.ts    # window.api type declarations
│   └── renderer/       # React SPA (Renderer Process)
│       ├── App.tsx     # Root component, wire protocol wiring
│       ├── components/ # UI components (layout, conversation, detail)
│       ├── stores/     # Zustand state (conversation, UI, thread)
│       ├── types/      # Shared TypeScript types
│       └── utils/      # Utility functions (diff extraction, etc.)
├── e2e/                # Playwright E2E tests
├── electron.vite.config.ts
├── package.json
└── vitest.config.ts
```

---

## Architecture

```
┌─────────────────────────────────────────────────────┐
│  Electron Main Process (Node.js)                      │
│                                                       │
│  AppServerManager ──stdio──► dotcraft app-server      │
│  WireProtocolClient (JSON-RPC 2.0)                    │
│  ipcBridge (IPC handlers)                             │
└──────────────┬──────────────────────────────────────┘
               │ contextBridge (window.api)
┌──────────────▼──────────────────────────────────────┐
│  Preload Script                                       │
│  Exposes: appServer, window, shell, file, git APIs   │
└──────────────┬──────────────────────────────────────┘
               │ window.api.*
┌──────────────▼──────────────────────────────────────┐
│  Renderer Process (React + Zustand)                   │
│                                                       │
│  App.tsx — wires Wire Protocol notifications          │
│  ├── Sidebar (thread list)                            │
│  ├── ConversationPanel (message stream + input)       │
│  └── DetailPanel (Changes / Plan / Terminal tabs)     │
└─────────────────────────────────────────────────────┘
```

The **Main Process** spawns and manages the DotCraft AppServer subprocess, communicates with it using the Wire Protocol (JSON-RPC 2.0 over stdio), and forwards notifications/requests to the Renderer via Electron IPC.

The **Renderer Process** is a React SPA. All state is managed by Zustand stores (`conversationStore`, `uiStore`, `threadStore`, `connectionStore`). It never accesses Node.js APIs directly — only through the typed `window.api` interface exposed by the preload script.

---

## Features

| Feature | Description |
|---------|-------------|
| **Conversation threads** | Multi-thread sidebar, new thread creation, thread history |
| **Streaming responses** | Real-time agent message and reasoning display |
| **Tool call visualization** | Collapsible tool call cards, file diff inline view |
| **Detail Panel** | Changes tab (file diff viewer, revert/re-apply), Plan tab, Terminal tab |
| **Approval flows** | Agent approval requests rendered as interactive cards |
| **Git integration** | Commit dialog — stages and commits accepted file changes |
| **Global shortcuts** | `Ctrl+N` new thread, `Ctrl+B` sidebar, `Ctrl+Shift+B` detail panel |

---

## Configuration

The app reads the DotCraft AppServer binary path from `settings.json` in the user data directory (`app.getPath('userData')`). On first launch, it searches `PATH` for `dotcraft` / `dotcraft.exe` automatically.

To override, launch with:

```bash
# Point to a specific binary
DotCraft Desktop --app-server /path/to/dotcraft

# Open a specific workspace
DotCraft Desktop --workspace /path/to/project
```

---

## License

Apache License 2.0 — see [../LICENSE](../LICENSE).
