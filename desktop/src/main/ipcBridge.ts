import { ipcMain, BrowserWindow, dialog } from 'electron'
import { promises as fs } from 'fs'
import { execFile } from 'child_process'
import * as path from 'path'
import type { WireProtocolClient } from './WireProtocolClient'
import type { AppSettings, RecentWorkspace } from './settings'

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
  errorMessage?: string
  errorType?: ConnectionErrorType
}

export interface ServerRequestPayload {
  bridgeId: string
  method: string
  params: unknown
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
  /** Returns the current settings object. */
  getSettings: () => AppSettings
  /** Updates and persists partial settings. */
  updateSettings: (partial: Partial<AppSettings>) => void
  /** Returns the recent workspaces list. */
  getRecentWorkspaces: () => RecentWorkspace[]
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
 * - `window:set-title`            (renderer -> main, invoke) -> sets window title
 * - `window:get-workspace-path`   (renderer -> main, invoke) -> returns workspace path
 * - `workspace:pick-folder`       (renderer -> main, invoke) -> opens native folder picker
 * - `workspace:switch`            (renderer -> main, invoke) -> triggers workspace switch
 * - `workspace:get-recent`        (renderer -> main, invoke) -> returns recent workspaces
 * - `workspace:open-new-window`   (renderer -> main, invoke) -> opens a new window
 * - `settings:get`                (renderer -> main, invoke) -> returns current settings
 * - `settings:set`                (renderer -> main, invoke) -> merges partial settings
 * - `file:write`                  (renderer -> main, invoke) -> writes file within workspace
 * - `file:read`                   (renderer -> main, invoke) -> reads UTF-8 file within workspace
 * - `file:delete`                 (renderer -> main, invoke) -> deletes file within workspace
 * - `git:commit`                  (renderer -> main, invoke) -> git add + commit
 */
export function registerIpcHandlers(
  _wireClient: WireProtocolClient | null,
  getWireClient: () => WireProtocolClient | null,
  workspacePath: string,
  callbacks?: IpcHandlerCallbacks
): void {
  // Renderer -> Main: send a JSON-RPC request to AppServer
  ipcMain.handle('appserver:send-request', async (_event, method: string, params?: unknown) => {
    const client = getWireClient()
    if (!client) {
      throw new Error('AppServer is not connected')
    }
    return client.sendRequest(method, params)
  })

  // Renderer -> Main: send back the user's decision for a server-initiated request
  ipcMain.handle('appserver:server-response', (_event, bridgeId: string, result: unknown) => {
    const resolve = pendingServerRequests.get(bridgeId)
    if (resolve) {
      pendingServerRequests.delete(bridgeId)
      resolve(result)
    }
  })

  // Renderer -> Main: set window title (targets the sender's own window)
  ipcMain.handle('window:set-title', (event, title: string) => {
    const win = BrowserWindow.fromWebContents(event.sender)
    win?.setTitle(title)
  })

  // Renderer -> Main: get workspace path
  ipcMain.handle('window:get-workspace-path', () => workspacePath)

  // Renderer -> Main: write a file to disk (used for revert/re-apply)
  ipcMain.handle('file:write', async (_event, absPath: string, content: string) => {
    // Security: ensure path is within workspace
    const resolved = path.resolve(absPath)
    const wsResolved = path.resolve(workspacePath)
    if (!resolved.startsWith(wsResolved + path.sep) && resolved !== wsResolved) {
      throw new Error(`Access denied: path is outside workspace: ${absPath}`)
    }
    await fs.writeFile(resolved, content, 'utf-8')
  })

  // Renderer -> Main: read a file from disk (used for cumulative diff computation)
  ipcMain.handle('file:read', async (_event, absPath: string): Promise<string> => {
    const resolved = path.resolve(absPath)
    const wsResolved = path.resolve(workspacePath)
    if (!resolved.startsWith(wsResolved + path.sep) && resolved !== wsResolved) {
      throw new Error(`Access denied: path is outside workspace: ${absPath}`)
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
  ipcMain.handle('file:delete', async (_event, absPath: string) => {
    const resolved = path.resolve(absPath)
    const wsResolved = path.resolve(workspacePath)
    if (!resolved.startsWith(wsResolved + path.sep) && resolved !== wsResolved) {
      throw new Error(`Access denied: path is outside workspace: ${absPath}`)
    }
    await fs.unlink(resolved)
  })

  // Renderer -> Main: git add + commit
  ipcMain.handle(
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
  ipcMain.handle('workspace:pick-folder', async (_event) => {
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
  ipcMain.handle('workspace:switch', async (_event, newPath: string) => {
    if (callbacks?.onSwitchWorkspace) {
      await callbacks.onSwitchWorkspace(newPath)
    }
  })

  // Renderer -> Main: get recent workspaces
  ipcMain.handle('workspace:get-recent', () => {
    return callbacks?.getRecentWorkspaces() ?? []
  })

  // Renderer -> Main: open a new independent window
  ipcMain.handle('workspace:open-new-window', () => {
    callbacks?.onOpenNewWindow()
  })

  // ─── Settings ──────────────────────────────────────────────────────────────

  // Renderer -> Main: get current settings
  ipcMain.handle('settings:get', () => {
    return callbacks?.getSettings() ?? {}
  })

  // Renderer -> Main: merge + persist partial settings update
  ipcMain.handle('settings:set', (_event, partial: Partial<AppSettings>) => {
    callbacks?.updateSettings(partial)
  })
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

/**
 * Forwards a Wire Protocol notification to the renderer.
 */
export function broadcastNotification(
  win: BrowserWindow,
  method: string,
  params: unknown
): void {
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
  ipcMain.removeHandler('appserver:server-response')
  ipcMain.removeHandler('window:set-title')
  ipcMain.removeHandler('window:get-workspace-path')
  ipcMain.removeHandler('file:write')
  ipcMain.removeHandler('file:read')
  ipcMain.removeHandler('file:delete')
  ipcMain.removeHandler('git:commit')
  ipcMain.removeHandler('workspace:pick-folder')
  ipcMain.removeHandler('workspace:switch')
  ipcMain.removeHandler('workspace:get-recent')
  ipcMain.removeHandler('workspace:open-new-window')
  ipcMain.removeHandler('settings:get')
  ipcMain.removeHandler('settings:set')
}
