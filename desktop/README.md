# DotCraft Desktop

**[中文](./README_ZH.md) | English**

Electron client for [DotCraft](../README.md). Open a workspace, chat with the agent, review file changes, and run automation tasks when the server exposes that capability.

---

## Prerequisites

- **Node.js 18+** and **npm**
- **DotCraft AppServer** (`dotcraft` / `dotcraft.exe`) on `PATH` or set in app settings — [Releases](https://github.com/DotHarness/dotcraft/releases) or [build from source](../README.md#build-from-source).

---

## Quick start

```bash
cd desktop
npm install
npm run dev
```

The window uses the current workspace folder (or the path you pass with `--workspace`).

**Language:** UI strings support English (default) and Simplified Chinese (`zh-Hans`). Change under **Settings** (Ctrl+,).

---

## npm scripts

| Command | Description |
|---------|-------------|
| `npm run dev` | Dev mode with hot reload |
| `npm run build` | Production build |
| `npm run preview` | Preview built renderer in browser |
| `npm test` | Unit tests (Vitest) |
| `npm run e2e` | Smoke E2E |
| `npm run pack` / `npm run dist` | Package / installers (see below) |

---

## Installers

`npm run dist` outputs under `desktop/dist/` (NSIS/portable on Windows, DMG on macOS, AppImage/deb on Linux). Portable Windows build is a single `.exe` without install.

```bash
npx electron-builder --win   # or --mac / --linux
```

---

## Using the app

**Workspace** — Pick or switch folder from the menu / welcome flow. One window is one workspace.

**Chat** — Sidebar lists threads; create with **New thread** (`Ctrl+N`). Type in the composer; the agent streams replies and tool use in the main column.

**Image attachments** — Pasted/dropped images are saved under `.craft/attachments/images/` and user message metadata stores attachment path + MIME/name, so switching threads or restarting the app can rehydrate thumbnails from disk.

**Detail panel** (`Ctrl+Shift+B`) — **Changes**: diffs for edits; revert/re-apply where supported. **Plan** / **Terminal** when available.

**Git** — The app can **stage selected changed files and commit** with a message from the Changes flow (`window.api.git.commit`). It does **not** replace a full Git client (no clone, pull, or branch UI here).

**Automations** (sidebar **Automations**, only if the server reports this capability):

1. **New task** — Title, description, **Agent workspace** (`Project` = repo folder, `Isolated` = separate sandbox), and **Tool policy** (workspace-scoped tools vs full auto). Submit creates a task; the server-side orchestrator runs it according to server rules.
2. Filter tabs **All / Local / GitHub** limit the task list by source; GitHub tasks appear in the same Automations view as local tasks.
3. Select a task and use **Review** or **View** depending on its status to open the review panel: live or historical agent activity for that task; approve or reject when the task waits for review.

**Shortcuts** — `Ctrl+B` sidebar, `Ctrl+Shift+B` detail panel (may vary by platform).

---

## Settings

AppServer binary path is stored in `settings.json` under the app user data directory; first launch searches `PATH` for `dotcraft`.

```bash
DotCraft Desktop --app-server /path/to/dotcraft
DotCraft Desktop --workspace /path/to/project
```
