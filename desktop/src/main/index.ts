import { app, BrowserWindow, session, Menu, ipcMain, shell } from 'electron'
import type { MenuItemConstructorOptions } from 'electron'
import { join, basename } from 'path'
import { existsSync } from 'fs'
import { AppServerManager } from './AppServerManager'
import { WireProtocolClient } from './WireProtocolClient'
import {
  registerIpcHandlers,
  unregisterIpcHandlers,
  broadcastConnectionStatus,
  broadcastNotification,
  broadcastServerRequest,
  createServerRequestBridge,
  type ConnectionStatusPayload,
  type IpcHandlerCallbacks
} from './ipcBridge'
import {
  loadSettings,
  saveSettings,
  addRecentWorkspace,
  getRecentWorkspaces,
  type AppSettings
} from './settings'

// ─── Per-window state ─────────────────────────────────────────────────────────

interface WindowContext {
  win: BrowserWindow
  workspacePath: string
  manager: AppServerManager | null
  wireClient: WireProtocolClient | null
  /** Tracks the number of AppServer crash restarts to cap retry attempts */
  crashRetries: number
}

const windowContexts = new Map<number, WindowContext>()

/** Must match `titleBarOverlay.height` (Windows / Linux) and CustomMenuBar height in renderer. */
const TITLE_BAR_OVERLAY_HEIGHT = 36

// ─── Shared (mutable) settings ────────────────────────────────────────────────

let sharedSettings: AppSettings = {}

// ─── Workspace resolution ─────────────────────────────────────────────────────

function resolveWorkspacePath(settings: AppSettings): string | null {
  // 1. Command-line argument: --workspace /path
  const argIdx = process.argv.indexOf('--workspace')
  if (argIdx !== -1 && process.argv[argIdx + 1]) {
    return process.argv[argIdx + 1]
  }

  // 2. Last-used workspace from settings (if it still exists)
  if (settings.lastWorkspacePath && existsSync(settings.lastWorkspacePath)) {
    return settings.lastWorkspacePath
  }

  // 3. No configured workspace → show welcome screen
  return null
}

// ─── Window creation ──────────────────────────────────────────────────────────

function createWindow(workspacePath: string | null): BrowserWindow {
  const isMac = process.platform === 'darwin'
  const isDev = import.meta.env.DEV
  const win = new BrowserWindow({
    width: 1400,
    height: 800,
    minWidth: 900,
    minHeight: 600,
    backgroundColor: '#1a1a1a',
    // In dev, show immediately so the window is visible even if `ready-to-show` is late
    // or never fires (e.g. Vite not ready yet). Otherwise DevTools can be the only thing
    // the user sees while the main window stays hidden.
    show: isDev,
    titleBarStyle: isMac ? 'hiddenInset' : 'hidden',
    ...(isMac
      ? {}
      : {
          titleBarOverlay: {
            color: '#1a1a1a',
            symbolColor: '#e5e5e5',
            height: TITLE_BAR_OVERLAY_HEIGHT
          }
        }),
    autoHideMenuBar: !isMac,
    webPreferences: {
      preload: join(__dirname, '../preload/index.js'),
      sandbox: false,
      contextIsolation: true,
      nodeIntegration: false
    }
  })

  const workspaceName = workspacePath ? basename(workspacePath) : 'DotCraft'
  win.setTitle(`DotCraft \u2014 ${workspaceName}`)

  if (!isDev) {
    win.once('ready-to-show', () => {
      win.show()
    })
  }

  win.on('close', () => {
    const ctx = windowContexts.get(win.id)
    if (ctx) {
      ctx.manager?.shutdown()
      ctx.wireClient?.dispose()
      windowContexts.delete(win.id)
    }
    // Remove the static title handler only when all windows are gone
    // (it is registered once per window in connectToAppServer via registerIpcHandlers)
  })

  return win
}

// ─── WebSocket remote connection ─────────────────────────────────────────────

async function connectViaWebSocket(
  ctx: WindowContext,
  workspacePath: string,
  wsUrl: string
): Promise<void> {
  const callbacks = buildCallbacks(ctx)
  broadcastConnectionStatus(ctx.win, { status: 'connecting' })
  unregisterIpcHandlers()
  registerIpcHandlers(null, () => ctx.wireClient, workspacePath, callbacks)

  const client = WireProtocolClient.fromWebSocket(wsUrl)
  ctx.wireClient = client

  client.onNotification((method, params) => {
    broadcastNotification(ctx.win, method, params)
  })

  client.onServerRequest(async (method, params) => {
    const { bridgeId, promise } = createServerRequestBridge()
    broadcastServerRequest(ctx.win, { bridgeId, method, params })
    return promise
  })

  try {
    const result = await client.initialize()
    broadcastConnectionStatus(ctx.win, {
      status: 'connected',
      serverInfo: result.serverInfo,
      capabilities: result.capabilities as Record<string, unknown>
    })
  } catch (err) {
    const message = err instanceof Error ? err.message : String(err)
    broadcastConnectionStatus(ctx.win, { status: 'error', errorMessage: message })
  }
}

// ─── AppServer connection ─────────────────────────────────────────────────────

function buildCallbacks(ctx: WindowContext): IpcHandlerCallbacks {
  return {
    onSwitchWorkspace: async (newPath: string) => {
      addRecentWorkspace(sharedSettings, newPath)
      saveSettings(sharedSettings)
      await connectToAppServer(ctx, newPath)
      if (!ctx.win.isDestroyed()) {
        ctx.win.setTitle(`DotCraft \u2014 ${basename(newPath)}`)
      }
    },
    onOpenNewWindow: () => {
      openNewWindow(sharedSettings.lastWorkspacePath ?? null)
    },
    getSettings: () => sharedSettings,
    updateSettings: (partial) => {
      Object.assign(sharedSettings, partial)
      saveSettings(sharedSettings)
    },
    getRecentWorkspaces: () => getRecentWorkspaces(sharedSettings)
  }
}

async function connectToAppServer(
  ctx: WindowContext,
  workspacePath: string
): Promise<void> {
  // Tear down previous connection for this window if any
  ctx.manager?.shutdown()
  ctx.wireClient?.dispose()
  ctx.wireClient = null
  ctx.manager = null

  ctx.workspacePath = workspacePath

  // --remote ws://host:port/ws?token=xxx  → skip AppServerManager, connect via WebSocket
  const remoteIdx = process.argv.indexOf('--remote')
  if (remoteIdx !== -1 && process.argv[remoteIdx + 1]) {
    await connectViaWebSocket(ctx, workspacePath, process.argv[remoteIdx + 1])
    return
  }

  const callbacks = buildCallbacks(ctx)

  broadcastConnectionStatus(ctx.win, { status: 'connecting' })

  const manager = new AppServerManager({
    workspacePath,
    binaryPath: sharedSettings.appServerBinaryPath
  })
  ctx.manager = manager

  // Re-register IPC for this window with the new workspace path
  unregisterIpcHandlers()
  registerIpcHandlers(null, () => ctx.wireClient, workspacePath, callbacks)

  manager.on('error', (err: Error) => {
    const isBinaryError =
      err.message.includes('not found') || err.message.includes('ENOENT')
    const payload: ConnectionStatusPayload = {
      status: 'error',
      errorMessage: err.message,
      ...(isBinaryError ? { errorType: 'binary-not-found' } : {})
    }
    broadcastConnectionStatus(ctx.win, payload as ConnectionStatusPayload)
  })

  manager.on('crash', () => {
    ctx.wireClient?.dispose()
    ctx.wireClient = null
    broadcastConnectionStatus(ctx.win, {
      status: 'disconnected',
      errorMessage: 'Connection lost. Reconnecting...'
    })

    // Auto-restart on crash, up to 3 attempts
    if (ctx.crashRetries < 3) {
      ctx.crashRetries++
      setTimeout(() => {
        if (!ctx.win.isDestroyed()) {
          void connectToAppServer(ctx, ctx.workspacePath)
        }
      }, 2000)
    }
  })

  manager.on('started', async () => {
    // Reset retry counter on successful start
    ctx.crashRetries = 0

    const { stdin, stdout } = manager
    if (!stdin || !stdout) {
      broadcastConnectionStatus(ctx.win, {
        status: 'error',
        errorMessage: 'AppServer process streams unavailable'
      })
      return
    }

    const client = new WireProtocolClient(stdout, stdin)

    ctx.wireClient = client

    client.onNotification((method, params) => {
      broadcastNotification(ctx.win, method, params)
    })

    client.onServerRequest(async (method, params) => {
      const { bridgeId, promise } = createServerRequestBridge()
      broadcastServerRequest(ctx.win, { bridgeId, method, params })
      return promise
    })

    try {
      const result = await client.initialize()
      broadcastConnectionStatus(ctx.win, {
        status: 'connected',
        serverInfo: result.serverInfo,
        capabilities: result.capabilities as Record<string, unknown>
      })
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err)
      const isTimeout = message.includes('timed out')
      broadcastConnectionStatus(ctx.win, {
        status: 'error',
        errorMessage: isTimeout
          ? 'AppServer is not responding. Restart?'
          : message,
        ...(isTimeout ? { errorType: 'handshake-timeout' } : {})
      } as ConnectionStatusPayload)
    }
  })

  manager.spawn()
}

// ─── Open a new independent window ───────────────────────────────────────────

function openNewWindow(workspacePath: string | null): void {
  const win = createWindow(workspacePath)
  const ctx: WindowContext = {
    win,
    workspacePath: workspacePath ?? '',
    manager: null,
    wireClient: null,
    crashRetries: 0
  }
  windowContexts.set(win.id, ctx)

  // Register IPC before loadURL so the renderer can invoke (e.g. window:get-workspace-path)
  // as soon as JS runs — Vite dev can execute the module before did-finish-load on main.
  if (workspacePath) {
    unregisterIpcHandlers()
    registerIpcHandlers(null, () => ctx.wireClient, workspacePath, buildCallbacks(ctx))
  } else {
    unregisterIpcHandlers()
    registerIpcHandlers(null, () => null, '', buildCallbacks(ctx))
  }

  if (import.meta.env.DEV) {
    win.loadURL('http://localhost:5173')
  } else {
    win.loadFile(join(__dirname, '../renderer/index.html'))
  }

  win.webContents.once('did-finish-load', () => {
    if (workspacePath) {
      addRecentWorkspace(sharedSettings, workspacePath)
      saveSettings(sharedSettings)
      void connectToAppServer(ctx, workspacePath)
    } else {
      broadcastConnectionStatus(win, { status: 'disconnected' })
    }
  })
}

function buildAppMenu(): Menu {
  const isMac = process.platform === 'darwin'
  const template: MenuItemConstructorOptions[] = [
    ...(isMac ? ([{ role: 'appMenu' }] as MenuItemConstructorOptions[]) : []),
    {
      label: 'File',
      submenu: [
        {
          label: 'New Window',
          accelerator: 'CmdOrCtrl+Shift+N',
          click: () => {
            openNewWindow(sharedSettings.lastWorkspacePath ?? null)
          }
        },
        { type: 'separator' },
        isMac ? { role: 'close' } : { role: 'quit' }
      ]
    },
    {
      label: 'Edit',
      submenu: [
        { role: 'undo' },
        { role: 'redo' },
        { type: 'separator' },
        { role: 'cut' },
        { role: 'copy' },
        { role: 'paste' },
        { role: 'selectAll' }
      ]
    },
    {
      label: 'View',
      submenu: [
        { role: 'reload' },
        { role: 'forceReload' },
        { role: 'toggleDevTools' },
        { type: 'separator' },
        { role: 'resetZoom' },
        { role: 'zoomIn' },
        { role: 'zoomOut' },
        { type: 'separator' },
        { role: 'togglefullscreen' }
      ]
    },
    {
      label: 'Window',
      submenu: [
        { role: 'minimize' },
        { role: 'zoom' },
        ...(isMac
          ? ([{ type: 'separator' }, { role: 'front' }] as MenuItemConstructorOptions[])
          : ([{ role: 'close' }] as MenuItemConstructorOptions[]))
      ]
    },
    {
      label: 'Help',
      submenu: [
        {
          label: 'Documentation',
          click: async () => {
            await shell.openExternal('https://github.com/DotCraftDev/dotcraft')
          }
        }
      ]
    }
  ]
  return Menu.buildFromTemplate(template)
}

function registerMenuPopupIpc(): void {
  ipcMain.removeHandler('menu:popup-top-level')
  ipcMain.handle(
    'menu:popup-top-level',
    (event, payload: { label: string; x: number; y: number }) => {
      const win = BrowserWindow.fromWebContents(event.sender)
      if (!win || win.isDestroyed()) return
      const appMenu = Menu.getApplicationMenu()
      if (!appMenu) return
      const item = appMenu.items.find((i) => i.label === payload.label)
      if (!item?.submenu) return
      item.submenu.popup({
        window: win,
        x: Math.round(payload.x),
        y: Math.round(payload.y)
      })
    }
  )
}

// ─── App lifecycle ────────────────────────────────────────────────────────────

app.whenReady().then(() => {
  registerMenuPopupIpc()
  Menu.setApplicationMenu(buildAppMenu())

  // Apply a strict Content-Security-Policy in production only.
  // In dev, Vite's HMR injects inline scripts and uses eval for sourcemaps,
  // so we leave CSP untouched and accept the dev-only Electron security warning.
  if (!import.meta.env.DEV) {
    session.defaultSession.webRequest.onHeadersReceived((details, callback) => {
      callback({
        responseHeaders: {
          ...details.responseHeaders,
          'Content-Security-Policy': [
            "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self' data:; connect-src 'self'"
          ]
        }
      })
    })
  }

  sharedSettings = loadSettings()
  const workspacePath = resolveWorkspacePath(sharedSettings)

  if (workspacePath) {
    addRecentWorkspace(sharedSettings, workspacePath)
    saveSettings(sharedSettings)
  }

  const win = createWindow(workspacePath)
  const ctx: WindowContext = {
    win,
    workspacePath: workspacePath ?? '',
    manager: null,
    wireClient: null,
    crashRetries: 0
  }
  windowContexts.set(win.id, ctx)

  // Register IPC before loadURL so the renderer can invoke handlers as soon as JS runs
  // (Vite dev may run the module before main's did-finish-load callback).
  if (workspacePath) {
    registerIpcHandlers(null, () => ctx.wireClient, workspacePath, buildCallbacks(ctx))
  } else {
    registerIpcHandlers(null, () => null, '', buildCallbacks(ctx))
  }

  if (import.meta.env.DEV) {
    win.loadURL('http://localhost:5173')
    // Open DevTools after load (not synchronously with loadURL): the main window used to
    // stay `show: false` until `ready-to-show`, so only the DevTools panel appeared first.
    win.webContents.once('did-finish-load', () => {
      win.webContents.openDevTools()
    })
  } else {
    win.loadFile(join(__dirname, '../renderer/index.html'))
  }

  win.webContents.once('did-finish-load', () => {
    if (workspacePath) {
      void connectToAppServer(ctx, workspacePath)
    } else {
      broadcastConnectionStatus(win, { status: 'disconnected' })
    }
  })

  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) {
      openNewWindow(sharedSettings.lastWorkspacePath ?? null)
    }
  })
})

app.on('window-all-closed', () => {
  for (const ctx of windowContexts.values()) {
    ctx.manager?.shutdown()
    ctx.wireClient?.dispose()
  }
  windowContexts.clear()
  if (process.platform !== 'darwin') {
    app.quit()
  }
})
