import { ipcMain, BrowserWindow, dialog, Notification, shell } from 'electron'
import { promises as fs } from 'fs'
import { execFile } from 'child_process'
import * as path from 'path'
import type { WireProtocolClient } from './WireProtocolClient'
import type { AppSettings, RecentWorkspace } from './settings'
import { checkWorkspaceLock } from './workspaceLock'
import {
  TITLE_BAR_OVERLAY_BY_THEME,
  TITLE_BAR_OVERLAY_HEIGHT
} from '../shared/titleBarOverlay'
import {
  invalidateFileIndex,
  saveImageDataUrlToTemp,
  searchWorkspaceFiles,
  warmFileSearchIndex
} from './workspaceComposerIpc'
import { translate, normalizeLocale, DEFAULT_LOCALE, type AppLocale } from '../shared/locales'

export type ConnectionStatus = 'connecting' | 'connected' | 'disconnected' | 'error'

export type ConnectionErrorType = 'binary-not-found' | 'handshake-timeout' | 'crash'

export interface ConnectionStatusPayload {
  status: ConnectionStatus
  serverInfo?: {
    name: string
    version: string
    protocolVersion?: string
  }
  capabilities?: Record<string, unknown>
  /** DashBoard URL when the server hosts it (initialize). */
  dashboardUrl?: string
  errorMessage?: string
  errorType?: ConnectionErrorType
}

export interface ServerRequestPayload {
  bridgeId: string
  method: string
  params: unknown
}

/**
 * Returns normalized http(s) URL string, or null if empty / malformed / wrong protocol.
 * Used for DashBoard URLs from initialize and for `shell:open-external` validation.
 */
export function sanitizeHttpOrHttpsUrl(url: string | undefined): string | null {
  if (url === undefined || typeof url !== 'string') return null
  const trimmed = url.trim()
  if (trimmed === '') return null
  let parsed: URL
  try {
    parsed = new URL(trimmed)
  } catch {
    return null
  }
  if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') return null
  return parsed.href
}

/**
 * Opens an http(s) URL in the system browser. Throws the same errors as the legacy
 * `shell:open-external` IPC handler for invalid input.
 */
export async function openExternalHttpUrl(url: string): Promise<void> {
  if (typeof url !== 'string' || url.trim() === '') {
    throw new Error('Invalid URL')
  }
  const safe = sanitizeHttpOrHttpsUrl(url)
  if (safe === null) {
    try {
      new URL(url.trim())
    } catch {
      throw new Error('Invalid URL')
    }
    throw new Error('Only http(s) URLs are allowed')
  }
  await shell.openExternal(safe)
}

// ---------------------------------------------------------------------------
// Pending server-request bridge
//
// When AppServer sends a server-initiated request (e.g. item/approval/request),
// Main forwards it to Renderer and waits for a response. A "bridge ID" links
// the forward to the matching renderer reply.
// ---------------------------------------------------------------------------

let nextBridgeId = 1
const pendingServerRequests = new Map<string, (result: unknown) => void>()

/**
 * Creates a pending entry and returns a Promise that resolves when the Renderer
 * calls `appserver:server-response` with the matching bridgeId.
 */
export function createServerRequestBridge(): { bridgeId: string; promise: Promise<unknown> } {
  const bridgeId = String(nextBridgeId++)
  const promise = new Promise<unknown>((resolve) => {
    pendingServerRequests.set(bridgeId, resolve)
  })
  return { bridgeId, promise }
}

export interface IpcHandlerCallbacks {
  /** Called when the renderer requests a workspace switch. */
  onSwitchWorkspace: (newPath: string) => Promise<void>
  /** Called when the renderer requests a new window. */
  onOpenNewWindow: () => void
  /** Restarts the Desktop-managed AppServer subprocess for the current workspace. */
  onRestartManagedAppServer: () => Promise<void>
  /** Returns the current settings object. */
  getSettings: () => AppSettings
  /** Updates and persists partial settings. */
  updateSettings: (partial: Partial<AppSettings>) => void
  /** Returns the recent workspaces list. */
  getRecentWorkspaces: () => RecentWorkspace[]
  /** Returns the latest known connection status snapshot. */
  getConnectionStatus: () => ConnectionStatusPayload
}

/**
 * Registers all ipcMain handlers that bridge the Renderer and the WireProtocolClient.
 *
 * IPC channels:
 * - `appserver:send-request`      (renderer -> main, invoke) -> forwards to WireProtocolClient
 * - `appserver:server-response`   (renderer -> main, invoke) -> resolves pending server request
 * - `appserver:notification`      (main -> renderer, send)   -> forwarded from WireProtocolClient
 * - `appserver:server-request`    (main -> renderer, send)   -> server-initiated request
 * - `appserver:connection-status` (main -> renderer, send)   -> connection state changes
 * - `appserver:get-connection-status` (renderer -> main, invoke) -> latest status snapshot
 * - `appserver:restart-managed`   (renderer -> main, invoke) -> restarts Desktop-managed AppServer
 * - `window:set-title`            (renderer -> main, invoke) -> sets window title
 * - `window:get-workspace-path`   (renderer -> main, invoke) -> returns workspace path
 * - `workspace:pick-folder`       (renderer -> main, invoke) -> opens native folder picker
 * - `workspace:switch`            (renderer -> main, invoke) -> triggers workspace switch
 * - `workspace:get-recent`        (renderer -> main, invoke) -> returns recent workspaces
 * - `workspace:open-new-window`   (renderer -> main, invoke) -> opens a new window
 * - `workspace:check-lock`        (renderer -> main, invoke) -> checks if workspace is locked
 * - `settings:get`                (renderer -> main, invoke) -> returns current settings
 * - `settings:set`                (renderer -> main, invoke) -> merges partial settings
 * - `file:write`                  (renderer -> main, invoke) -> writes file within workspace
 * - `file:read`                   (renderer -> main, invoke) -> reads UTF-8 file within workspace
 * - `file:delete`                 (renderer -> main, invoke) -> deletes file within workspace
 * - `file:exists`                 (renderer -> main, invoke) -> checks whether file exists within workspace
 * - `git:commit`                  (renderer -> main, invoke) -> git add + commit
 * - `shell:open-external`         (renderer -> main, invoke) -> opens http(s) URL in system browser
 */
function mainLocale(callbacks?: IpcHandlerCallbacks): AppLocale {
  return normalizeLocale(callbacks?.getSettings()?.locale ?? DEFAULT_LOCALE)
}

export function registerIpcHandlers(
  _wireClient: WireProtocolClient | null,
  getWireClient: () => WireProtocolClient | null,
  workspacePath: string,
  callbacks?: IpcHandlerCallbacks
): void {
  invalidateFileIndex()
  const handleSafe = (
    channel: string,
    listener: Parameters<typeof ipcMain.handle>[1]
  ): void => {
    ipcMain.removeHandler(channel)
    ipcMain.handle(channel, listener)
  }

  // Renderer -> Main: send a JSON-RPC request to AppServer
  handleSafe(
    'appserver:send-request',
    async (_event, method: string, params?: unknown, timeoutMs?: number) => {
      const client = getWireClient()
      if (!client) {
        throw new Error(translate(mainLocale(callbacks), 'ipc.appServerNotConnected'))
      }
      return client.sendRequest(method, params, timeoutMs)
    }
  )

  handleSafe('appserver:model-list', async () => {
    const client = getWireClient()
    if (!client) {
      throw new Error(translate(mainLocale(callbacks), 'ipc.appServerNotConnected'))
    }
    return client.sendRequest('model/list', {}, 20_000)
  })

  handleSafe('appserver:get-connection-status', () => {
    return callbacks?.getConnectionStatus() ?? { status: 'disconnected' }
  })

  handleSafe('appserver:restart-managed', async () => {
    await callbacks?.onRestartManagedAppServer()
  })

  // Renderer -> Main: send back the user's decision for a server-initiated request
  handleSafe('appserver:server-response', (_event, bridgeId: string, result: unknown) => {
    const resolve = pendingServerRequests.get(bridgeId)
    if (resolve) {
      pendingServerRequests.delete(bridgeId)
      resolve(result)
    }
  })

  // Renderer -> Main: set window title (targets the sender's own window)
  handleSafe('window:set-title', (event, title: string) => {
    const win = BrowserWindow.fromWebContents(event.sender)
    win?.setTitle(title)
  })

  // Renderer -> Main: sync titleBarOverlay colors with app theme (Windows / Linux only)
  handleSafe('window:set-title-bar-overlay-theme', (event, theme: 'dark' | 'light') => {
    if (process.platform === 'darwin') return
    const win = BrowserWindow.fromWebContents(event.sender)
    if (!win || win.isDestroyed()) return
    const t = theme === 'light' ? 'light' : 'dark'
    const { color, symbolColor } = TITLE_BAR_OVERLAY_BY_THEME[t]
    win.setTitleBarOverlay({
      color,
      symbolColor,
      height: TITLE_BAR_OVERLAY_HEIGHT
    })
  })

  // Renderer -> Main: get workspace path
  handleSafe('window:get-workspace-path', () => workspacePath)

  // Renderer -> Main: open http(s) URL in the system browser (DashBoard, etc.)
  handleSafe('shell:open-external', async (_event, url: string) => {
    await openExternalHttpUrl(url)
  })

  // Renderer -> Main: write a file to disk (used for revert/re-apply)
  handleSafe('file:write', async (_event, absPath: string, content: string) => {
    // Security: ensure path is within workspace
    const resolved = path.resolve(absPath)
    const wsResolved = path.resolve(workspacePath)
    if (!resolved.startsWith(wsResolved + path.sep) && resolved !== wsResolved) {
      throw new Error(
        translate(mainLocale(callbacks), 'ipc.pathOutsideWorkspace', { path: absPath })
      )
    }
    await fs.mkdir(path.dirname(resolved), { recursive: true })
    await fs.writeFile(resolved, content, 'utf-8')
  })

  // Renderer -> Main: read a file from disk (used for cumulative diff computation)
  handleSafe('file:read', async (_event, absPath: string): Promise<string> => {
    const resolved = path.resolve(absPath)
    const wsResolved = path.resolve(workspacePath)
    if (!resolved.startsWith(wsResolved + path.sep) && resolved !== wsResolved) {
      throw new Error(
        translate(mainLocale(callbacks), 'ipc.pathOutsideWorkspace', { path: absPath })
      )
    }
    try {
      return await fs.readFile(resolved, 'utf-8')
    } catch (err: unknown) {
      const code = (err as NodeJS.ErrnoException)?.code
      if (code === 'ENOENT') return ''
      throw err
    }
  })

  // Renderer -> Main: delete a file (used for reverting new files)
  handleSafe('file:delete', async (_event, absPath: string) => {
    const resolved = path.resolve(absPath)
    const wsResolved = path.resolve(workspacePath)
    if (!resolved.startsWith(wsResolved + path.sep) && resolved !== wsResolved) {
      throw new Error(
        translate(mainLocale(callbacks), 'ipc.pathOutsideWorkspace', { path: absPath })
      )
    }
    await fs.unlink(resolved)
  })

  handleSafe('file:exists', async (_event, absPath: string): Promise<boolean> => {
    const resolved = path.resolve(absPath)
    const wsResolved = path.resolve(workspacePath)
    if (!resolved.startsWith(wsResolved + path.sep) && resolved !== wsResolved) {
      throw new Error(
        translate(mainLocale(callbacks), 'ipc.pathOutsideWorkspace', { path: absPath })
      )
    }
    try {
      await fs.access(resolved)
      return true
    } catch {
      return false
    }
  })

  // Renderer -> Main: git add + commit
  handleSafe(
    'git:commit',
    (_event, wsPath: string, files: string[], message: string): Promise<string> => {
      return new Promise((resolve, reject) => {
        execFile(
          'git',
          ['add', '--', ...files],
          { cwd: wsPath },
          (addErr, _addStdout, addStderr) => {
            if (addErr) {
              reject(new Error(addStderr || addErr.message))
              return
            }
            execFile(
              'git',
              ['commit', '-m', message],
              { cwd: wsPath },
              (commitErr, commitStdout, commitStderr) => {
                if (commitErr) {
                  reject(new Error(commitStderr || commitErr.message))
                  return
                }
                resolve(commitStdout.trim())
              }
            )
          }
        )
      })
    }
  )

  // ─── Workspace management ──────────────────────────────────────────────────

  // Renderer -> Main: open native folder picker dialog
  handleSafe('workspace:pick-folder', async (_event) => {
    const focusedWin = BrowserWindow.getFocusedWindow()
    const result = await dialog.showOpenDialog(
      focusedWin ?? BrowserWindow.getAllWindows()[0],
      {
        title: 'Select Workspace Folder',
        properties: ['openDirectory', 'createDirectory']
      }
    )
    if (result.canceled || result.filePaths.length === 0) return null
    return result.filePaths[0]
  })

  // Renderer -> Main: switch to a different workspace
  handleSafe('workspace:switch', async (_event, newPath: string) => {
    if (callbacks?.onSwitchWorkspace) {
      await callbacks.onSwitchWorkspace(newPath)
    }
  })

  // Renderer -> Main: get recent workspaces
  handleSafe('workspace:get-recent', () => {
    return callbacks?.getRecentWorkspaces() ?? []
  })

  // Renderer -> Main: open a new independent window
  handleSafe('workspace:open-new-window', () => {
    callbacks?.onOpenNewWindow()
  })

  // Renderer -> Main: check if a workspace is already locked by another process
  handleSafe('workspace:check-lock', (_event, wsPath: string) => {
    return checkWorkspaceLock(wsPath)
  })

  // Renderer -> Main: save clipboard/drag image bytes to .craft/tmp/images for localImage wire part
  handleSafe(
    'workspace:save-image-to-temp',
    async (_event, params: { dataUrl: string; fileName?: string }) => {
      const ws = workspacePath
      if (!ws) {
        throw new Error(translate(mainLocale(callbacks), 'ipc.noWorkspaceOpen'))
      }
      const loc = mainLocale(callbacks)
      const pathAbs = await saveImageDataUrlToTemp(ws, params.dataUrl, params.fileName, loc)
      return { path: pathAbs }
    }
  )

  // Renderer -> Main: fuzzy file name search for @ mentions
  handleSafe(
    'workspace:search-files',
    async (
      _event,
      params: { query: string; workspacePath: string; limit?: number }
    ) => {
      const ws = path.resolve(workspacePath)
      const req = path.resolve(params.workspacePath)
      if (ws !== req) {
        throw new Error(translate(mainLocale(callbacks), 'ipc.workspacePathMismatch'))
      }
      if (!ws) {
        return { files: [] as { name: string; relativePath: string; dir: string }[] }
      }
      const limit = Math.min(20, Math.max(1, params.limit ?? 10))
      const files = await searchWorkspaceFiles(ws, params.query, limit)
      return { files }
    }
  )

  // ─── Settings ──────────────────────────────────────────────────────────────

  // Renderer -> Main: get current settings
  handleSafe('settings:get', () => {
    return callbacks?.getSettings() ?? {}
  })

  // Renderer -> Main: merge + persist partial settings update
  handleSafe(
    'settings:set',
    (_event, partial: Partial<AppSettings>) => {
      callbacks?.updateSettings(partial)
    }
  )

  if (workspacePath) {
    warmFileSearchIndex(workspacePath)
  }
}

/**
 * Broadcasts a connection status change to all renderer windows.
 */
export function broadcastConnectionStatus(
  win: BrowserWindow,
  payload: ConnectionStatusPayload
): void {
  if (!win.isDestroyed()) {
    win.webContents.send('appserver:connection-status', payload)
  }
}

/** Strip common Markdown for OS notification body (plain text). */
function stripMarkdownForNotify(text: string): string {
  return text
    .replace(/\r?\n/g, ' ')
    .replace(/\*\*(.+?)\*\*/g, '$1')
    .replace(/`([^`]+)`/g, '$1')
    .replace(/\s+/g, ' ')
    .trim()
}

/**
 * Forwards a Wire Protocol notification to the renderer.
 * When the window is not focused, shows a native notification for job results (spec §18.6).
 */
export function broadcastNotification(
  win: BrowserWindow,
  method: string,
  params: unknown
): void {
  if (
    method === 'system/jobResult' &&
    !win.isDestroyed() &&
    !win.isFocused()
  ) {
    const p = (params ?? {}) as Record<string, unknown>
    const jobName = String((p.jobName as string) ?? (p.name as string) ?? 'Job')
    const err = (p.error as string) ?? ''
    const result = (p.result as string) ?? (p.text as string) ?? ''
    const bodyRaw = err || result || 'Job completed'
    const body = stripMarkdownForNotify(bodyRaw).slice(0, 240)
    try {
      if (Notification.isSupported()) {
        new Notification({ title: jobName, body }).show()
      }
    } catch {
      /* ignore — notification optional */
    }
  }
  if (!win.isDestroyed()) {
    win.webContents.send('appserver:notification', { method, params })
  }
}

/**
 * Forwards a server-initiated request to the renderer.
 * The renderer must call sendServerResponse(bridgeId, result) to respond.
 */
export function broadcastServerRequest(
  win: BrowserWindow,
  payload: ServerRequestPayload
): void {
  if (!win.isDestroyed()) {
    win.webContents.send('appserver:server-request', payload)
  }
}

/**
 * Removes all registered ipcMain handlers (call before re-registering on workspace switch).
 */
export function unregisterIpcHandlers(): void {
  ipcMain.removeHandler('appserver:send-request')
  ipcMain.removeHandler('appserver:model-list')
  ipcMain.removeHandler('appserver:get-connection-status')
  ipcMain.removeHandler('appserver:restart-managed')
  ipcMain.removeHandler('appserver:server-response')
  ipcMain.removeHandler('window:set-title')
  ipcMain.removeHandler('window:set-title-bar-overlay-theme')
  ipcMain.removeHandler('window:get-workspace-path')
  ipcMain.removeHandler('shell:open-external')
  ipcMain.removeHandler('file:write')
  ipcMain.removeHandler('file:read')
  ipcMain.removeHandler('file:delete')
  ipcMain.removeHandler('file:exists')
  ipcMain.removeHandler('git:commit')
  ipcMain.removeHandler('workspace:pick-folder')
  ipcMain.removeHandler('workspace:switch')
  ipcMain.removeHandler('workspace:get-recent')
  ipcMain.removeHandler('workspace:open-new-window')
  ipcMain.removeHandler('workspace:check-lock')
  ipcMain.removeHandler('workspace:save-image-to-temp')
  ipcMain.removeHandler('workspace:search-files')
  ipcMain.removeHandler('settings:get')
  ipcMain.removeHandler('settings:set')
  invalidateFileIndex()
}
