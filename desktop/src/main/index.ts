import { app, BrowserWindow, session, Menu, ipcMain, shell, nativeImage } from 'electron'
import type { MenuItemConstructorOptions } from 'electron'
import { join, basename } from 'path'
import { existsSync } from 'fs'
import { spawn } from 'child_process'
import { AppServerManager } from './AppServerManager'
import { WireProtocolClient } from './WireProtocolClient'
import {
  registerIpcHandlers,
  unregisterIpcHandlers,
  broadcastConnectionStatus,
  broadcastNotification,
  broadcastServerRequest,
  createServerRequestBridge,
  sanitizeHttpOrHttpsUrl,
  openExternalHttpUrl,
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
import { acquireWorkspaceLock, releaseWorkspaceLock } from './workspaceLock'
import {
  TITLE_BAR_OVERLAY_BY_THEME,
  TITLE_BAR_OVERLAY_HEIGHT
} from '../shared/titleBarOverlay'
import { WORKSPACE_LOCKED_IPC_PREFIX } from '../shared/workspaceSwitchErrors'
import {
  normalizeLocale,
  translate,
  type AppLocale,
  type TopLevelMenuId
} from '../shared/locales'

// ─── Single-process state ─────────────────────────────────────────────────────
// Each Electron process owns exactly one window and one AppServer connection.
// "New Window" spawns a separate OS process instead of creating another
// BrowserWindow, avoiding the global-IPC-handler conflict that the previous
// multi-window-in-one-process design had.

let mainWindow: BrowserWindow | null = null
let appServerManager: AppServerManager | null = null
let wireClient: WireProtocolClient | null = null
let currentWorkspacePath = ''
let crashRetries = 0
/** Last DashBoard URL from a successful initialize (for View menu). */
let lastDashboardUrl: string | null = null

/** PNG shipped via `build.extraResources` (prod) or repo `resources/` (dev). macOS uses bundle icon. */
function resolveWindowIconPath(): string | null {
  if (process.platform === 'darwin') {
    return null
  }
  const packaged = join(process.resourcesPath, 'icon.png')
  const dev = join(__dirname, '../../resources/icon.png')
  const path = app.isPackaged ? packaged : dev
  return existsSync(path) ? path : null
}

// ─── Shared (mutable) settings ────────────────────────────────────────────────

let sharedSettings: AppSettings = {}

// ─── Workspace resolution ─────────────────────────────────────────────────────

function resolveWorkspacePath(settings: AppSettings): string | null {
  const argIdx = process.argv.indexOf('--workspace')
  if (argIdx !== -1 && process.argv[argIdx + 1]) {
    return process.argv[argIdx + 1]
  }

  if (settings.lastWorkspacePath && existsSync(settings.lastWorkspacePath)) {
    return settings.lastWorkspacePath
  }

  return null
}

// ─── Window creation ──────────────────────────────────────────────────────────

function createWindow(workspacePath: string | null): BrowserWindow {
  const isMac = process.platform === 'darwin'
  const isDev = import.meta.env.DEV
  const iconPath = resolveWindowIconPath()
  const win = new BrowserWindow({
    width: 1400,
    height: 800,
    minWidth: 900,
    minHeight: 600,
    backgroundColor: '#1a1a1a',
    ...(iconPath
      ? {
          icon: nativeImage.createFromPath(iconPath)
        }
      : {}),
    show: isDev,
    titleBarStyle: isMac ? 'hiddenInset' : 'hidden',
    ...(isMac
      ? {}
      : {
          titleBarOverlay: {
            ...TITLE_BAR_OVERLAY_BY_THEME.dark,
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
  const loc = normalizeLocale(sharedSettings.locale)
  win.setTitle(translate(loc, 'app.titleWithWorkspace', { name: workspaceName }))

  if (!isDev) {
    win.once('ready-to-show', () => {
      win.show()
    })
  }

  win.on('close', () => {
    releaseWorkspaceLock(currentWorkspacePath)
    appServerManager?.shutdown()
    wireClient?.dispose()
    appServerManager = null
    wireClient = null
    mainWindow = null
  })

  return win
}

// ─── Spawn a new process for "New Window" ─────────────────────────────────────
// Always spawns without a --workspace argument so the new process shows the
// welcome screen. This prevents two processes from accidentally opening the
// same workspace simultaneously.

function openNewProcess(): void {
  const filteredArgs = stripWorkspaceArgs(process.argv.slice(1))
  const child = spawn(process.execPath, filteredArgs, {
    detached: true,
    stdio: 'ignore'
  })
  child.unref()
}

/** Remove any existing --workspace <path> pair from argv so the new process can set its own. */
function stripWorkspaceArgs(argv: string[]): string[] {
  const result: string[] = []
  for (let i = 0; i < argv.length; i++) {
    if (argv[i] === '--workspace') {
      i++ // skip the value too
    } else {
      result.push(argv[i])
    }
  }
  return result
}

// ─── WebSocket remote connection ─────────────────────────────────────────────

async function connectViaWebSocket(
  workspacePath: string,
  wsUrl: string
): Promise<void> {
  const win = mainWindow!
  emitConnectionStatus(win, { status: 'connecting' })
  reregisterIpcForWorkspace(workspacePath)

  const client = WireProtocolClient.fromWebSocket(wsUrl)
  wireClient = client

  client.onNotification((method, params) => {
    if (mainWindow && !mainWindow.isDestroyed()) {
      broadcastNotification(mainWindow, method, params)
    }
  })

  client.onServerRequest(async (method, params) => {
    if (!mainWindow || mainWindow.isDestroyed()) {
      return Promise.reject(
        new Error('Window is not available to handle server request')
      )
    }
    const { bridgeId, promise } = createServerRequestBridge()
    broadcastServerRequest(mainWindow, { bridgeId, method, params })
    return promise
  })

  try {
    const result = await client.initialize()
    emitConnectionStatus(win, {
      status: 'connected',
      serverInfo: result.serverInfo,
      capabilities: result.capabilities as Record<string, unknown>,
      dashboardUrl: result.dashboardUrl
    })
  } catch (err) {
    const message = err instanceof Error ? err.message : String(err)
    if (mainWindow && !mainWindow.isDestroyed()) {
      emitConnectionStatus(mainWindow, { status: 'error', errorMessage: message })
    }
  }
}

// ─── AppServer connection ─────────────────────────────────────────────────────

function buildCallbacks(): IpcHandlerCallbacks {
  return {
    onSwitchWorkspace: async (newPath: string) => {
      addRecentWorkspace(sharedSettings, newPath)
      saveSettings(sharedSettings)
      await connectToAppServer(newPath)
      if (mainWindow && !mainWindow.isDestroyed()) {
        const loc = normalizeLocale(sharedSettings.locale)
        mainWindow.setTitle(
          translate(loc, 'app.titleWithWorkspace', { name: basename(newPath) })
        )
      }
    },
    onOpenNewWindow: () => {
      openNewProcess()
    },
    getSettings: () => sharedSettings,
    updateSettings: (partial) => {
      const prevLocale = normalizeLocale(sharedSettings.locale)
      const next: Partial<typeof sharedSettings> = { ...partial }
      if (partial.locale !== undefined) {
        next.locale = normalizeLocale(partial.locale)
      }
      Object.assign(sharedSettings, next)
      saveSettings(sharedSettings)
      if (partial.locale !== undefined && normalizeLocale(sharedSettings.locale) !== prevLocale) {
        refreshAppMenu()
      }
    },
    getRecentWorkspaces: () => getRecentWorkspaces(sharedSettings)
  }
}

/** Re-register IPC handlers with the current workspace path (used on workspace switch). */
function reregisterIpcForWorkspace(workspacePath: string): void {
  unregisterIpcHandlers()
  registerIpcHandlers(null, () => wireClient, workspacePath, buildCallbacks())
}

async function connectToAppServer(workspacePath: string): Promise<void> {
  // Acquire the lock BEFORE tearing anything down so a failure leaves the
  // current connection intact and propagates as an exception to the caller
  // (e.g. the renderer's workspace:switch IPC).
  const lockResult = acquireWorkspaceLock(workspacePath)
  if (!lockResult.ok) {
    const loc = normalizeLocale(sharedSettings.locale)
    throw new Error(
      WORKSPACE_LOCKED_IPC_PREFIX +
        translate(loc, 'main.error.workspaceLocked', { pid: lockResult.pid ?? 0 })
    )
  }

  // Release lock on previous workspace after the new lock is secured
  if (currentWorkspacePath && currentWorkspacePath !== workspacePath) {
    releaseWorkspaceLock(currentWorkspacePath)
  }

  // Tear down previous connection
  appServerManager?.shutdown()
  wireClient?.dispose()
  wireClient = null
  appServerManager = null

  currentWorkspacePath = workspacePath

  // --remote ws://host:port/ws?token=xxx  → skip AppServerManager, connect via WebSocket
  const remoteIdx = process.argv.indexOf('--remote')
  if (remoteIdx !== -1 && process.argv[remoteIdx + 1]) {
    await connectViaWebSocket(workspacePath, process.argv[remoteIdx + 1])
    return
  }

  const win = mainWindow!
  emitConnectionStatus(win, { status: 'connecting' })

  const manager = new AppServerManager({
    workspacePath,
    binaryPath: sharedSettings.appServerBinaryPath
  })
  appServerManager = manager

  reregisterIpcForWorkspace(workspacePath)

  manager.on('error', (err: Error) => {
    const isBinaryError =
      err.message.includes('not found') || err.message.includes('ENOENT')
    const payload: ConnectionStatusPayload = {
      status: 'error',
      errorMessage: err.message,
      ...(isBinaryError ? { errorType: 'binary-not-found' } : {})
    }
    if (mainWindow && !mainWindow.isDestroyed()) {
      emitConnectionStatus(mainWindow, payload as ConnectionStatusPayload)
    }
  })

  manager.on('crash', () => {
    wireClient?.dispose()
    wireClient = null
    if (mainWindow && !mainWindow.isDestroyed()) {
      const loc = normalizeLocale(sharedSettings.locale)
      emitConnectionStatus(mainWindow, {
        status: 'disconnected',
        errorMessage: translate(loc, 'main.status.reconnecting')
      })
    }

    if (crashRetries < 3) {
      crashRetries++
      setTimeout(() => {
        if (mainWindow && !mainWindow.isDestroyed()) {
          void connectToAppServer(currentWorkspacePath)
        }
      }, 2000)
    }
  })

  manager.on('started', async () => {
    crashRetries = 0

    const { stdin, stdout } = manager
    if (!stdin || !stdout) {
      if (mainWindow && !mainWindow.isDestroyed()) {
        const loc = normalizeLocale(sharedSettings.locale)
        emitConnectionStatus(mainWindow, {
          status: 'error',
          errorMessage: translate(loc, 'main.error.streamsUnavailable')
        })
      }
      return
    }

    const client = new WireProtocolClient(stdout, stdin)
    wireClient = client

    client.onNotification((method, params) => {
      if (mainWindow && !mainWindow.isDestroyed()) {
        broadcastNotification(mainWindow, method, params)
      }
    })

    client.onServerRequest(async (method, params) => {
      if (!mainWindow || mainWindow.isDestroyed()) {
        return Promise.reject(
          new Error('Window is not available to handle server request')
        )
      }
      const { bridgeId, promise } = createServerRequestBridge()
      broadcastServerRequest(mainWindow, { bridgeId, method, params })
      return promise
    })

    try {
      const result = await client.initialize()
      if (mainWindow && !mainWindow.isDestroyed()) {
        emitConnectionStatus(mainWindow, {
          status: 'connected',
          serverInfo: result.serverInfo,
          capabilities: result.capabilities as Record<string, unknown>,
          dashboardUrl: result.dashboardUrl
        })
      }
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err)
      const isTimeout = message.includes('timed out')
      if (mainWindow && !mainWindow.isDestroyed()) {
        const loc = normalizeLocale(sharedSettings.locale)
        emitConnectionStatus(mainWindow, {
          status: 'error',
          errorMessage: isTimeout
            ? translate(loc, 'main.error.handshakeTimeout')
            : message,
          ...(isTimeout ? { errorType: 'handshake-timeout' } : {})
        } as ConnectionStatusPayload)
      }
    }
  })

  manager.spawn()
}

// ─── App menu ─────────────────────────────────────────────────────────────────

function buildAppMenu(locale: AppLocale): Menu {
  const isMac = process.platform === 'darwin'
  const L = (key: string) => translate(locale, key)
  const template: MenuItemConstructorOptions[] = [
    ...(isMac ? ([{ role: 'appMenu' }] as MenuItemConstructorOptions[]) : []),
    {
      id: 'file',
      label: L('menu.file'),
      submenu: [
        {
          label: L('menu.newWindow'),
          accelerator: 'CmdOrCtrl+Shift+N',
          click: () => {
            openNewProcess()
          }
        },
        { type: 'separator' },
        isMac ? { role: 'close' } : { role: 'quit' }
      ]
    },
    {
      id: 'edit',
      label: L('menu.edit'),
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
      id: 'view',
      label: L('menu.view'),
      submenu: [
        { role: 'reload' },
        { role: 'forceReload' },
        { role: 'toggleDevTools' },
        { type: 'separator' },
        { role: 'resetZoom' },
        { role: 'zoomIn' },
        { role: 'zoomOut' },
        { type: 'separator' },
        {
          label: L('menu.openDashboard'),
          accelerator: 'CmdOrCtrl+Shift+D',
          enabled: Boolean(lastDashboardUrl),
          click: async () => {
            if (lastDashboardUrl) await openExternalHttpUrl(lastDashboardUrl)
          }
        },
        { type: 'separator' },
        { role: 'togglefullscreen' }
      ]
    },
    {
      id: 'window',
      label: L('menu.window'),
      submenu: [
        { role: 'minimize' },
        { role: 'zoom' },
        ...(isMac
          ? ([{ type: 'separator' }, { role: 'front' }] as MenuItemConstructorOptions[])
          : ([{ role: 'close' }] as MenuItemConstructorOptions[]))
      ]
    },
    {
      id: 'help',
      label: L('menu.help'),
      submenu: [
        {
          label: L('menu.documentation'),
          click: async () => {
            await shell.openExternal('https://github.com/DotCraftDev/dotcraft')
          }
        }
      ]
    }
  ]
  return Menu.buildFromTemplate(template)
}

function refreshAppMenu(): void {
  Menu.setApplicationMenu(buildAppMenu(normalizeLocale(sharedSettings.locale)))
}

function emitConnectionStatus(win: BrowserWindow, payload: ConnectionStatusPayload): void {
  if (payload.status === 'connected') {
    const sanitized = sanitizeHttpOrHttpsUrl(payload.dashboardUrl)
    lastDashboardUrl = sanitized
    broadcastConnectionStatus(win, {
      ...payload,
      dashboardUrl: sanitized ?? undefined
    })
  } else {
    lastDashboardUrl = null
    broadcastConnectionStatus(win, payload)
  }
  refreshAppMenu()
}

function registerMenuPopupIpc(): void {
  ipcMain.removeHandler('menu:popup-top-level')
  ipcMain.handle(
    'menu:popup-top-level',
    (event, payload: { menuId: TopLevelMenuId; x: number; y: number }) => {
      const win = BrowserWindow.fromWebContents(event.sender)
      if (!win || win.isDestroyed()) return
      const appMenu = Menu.getApplicationMenu()
      if (!appMenu) return
      const item = appMenu.items.find((i) => i.id === payload.menuId)
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
  sharedSettings = loadSettings()
  refreshAppMenu()

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

  let workspacePath = resolveWorkspacePath(sharedSettings)

  // If another process is already using this workspace, start without one
  // so the user sees the welcome screen and can pick a different workspace.
  if (workspacePath) {
    const lockCheck = acquireWorkspaceLock(workspacePath)
    if (!lockCheck.ok) {
      workspacePath = null
    } else {
      addRecentWorkspace(sharedSettings, workspacePath)
      saveSettings(sharedSettings)
    }
  }

  const win = createWindow(workspacePath)
  mainWindow = win
  currentWorkspacePath = workspacePath ?? ''

  if (workspacePath) {
    registerIpcHandlers(null, () => wireClient, workspacePath, buildCallbacks())
  } else {
    registerIpcHandlers(null, () => null, '', buildCallbacks())
  }

  if (import.meta.env.DEV) {
    win.loadURL('http://localhost:5173')
    win.webContents.once('did-finish-load', () => {
      win.webContents.openDevTools()
    })
  } else {
    win.loadFile(join(__dirname, '../renderer/index.html'))
  }

  win.webContents.once('did-finish-load', () => {
    if (workspacePath) {
      void connectToAppServer(workspacePath)
    } else {
      emitConnectionStatus(win, { status: 'disconnected' })
    }
  })

  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) {
      sharedSettings = loadSettings()
      const wsPath = resolveWorkspacePath(sharedSettings)
      const newWin = createWindow(wsPath)
      mainWindow = newWin
      currentWorkspacePath = wsPath ?? ''

      if (wsPath) {
        reregisterIpcForWorkspace(wsPath)
      } else {
        unregisterIpcHandlers()
        registerIpcHandlers(null, () => null, '', buildCallbacks())
      }

      if (import.meta.env.DEV) {
        newWin.loadURL('http://localhost:5173')
      } else {
        newWin.loadFile(join(__dirname, '../renderer/index.html'))
      }

      newWin.webContents.once('did-finish-load', () => {
        if (wsPath) {
          void connectToAppServer(wsPath)
        } else {
          emitConnectionStatus(newWin, { status: 'disconnected' })
        }
      })
    }
  })
})

app.on('window-all-closed', () => {
  releaseWorkspaceLock(currentWorkspacePath)
  appServerManager?.shutdown()
  wireClient?.dispose()
  appServerManager = null
  wireClient = null
  mainWindow = null
  if (process.platform !== 'darwin') {
    app.quit()
  }
})
