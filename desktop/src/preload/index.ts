import { contextBridge, ipcRenderer, shell } from 'electron'
import {
  TITLE_BAR_OVERLAY_HEIGHT,
  TITLE_BAR_OVERLAY_RIGHT_RESERVE
} from '../shared/titleBarOverlay'
import type { TopLevelMenuId } from '../shared/locales/types'

export type UnsubscribeFn = () => void
export type ConnectionMode = 'stdio' | 'websocket' | 'stdioAndWebSocket' | 'remote'

export interface NotificationPayload {
  method: string
  params: unknown
}

export interface ConnectionStatusPayload {
  status: 'connecting' | 'connected' | 'disconnected' | 'error'
  serverInfo?: {
    name: string
    version: string
    protocolVersion?: string
  }
  capabilities?: Record<string, unknown>
  dashboardUrl?: string
  errorMessage?: string
  errorType?: 'binary-not-found' | 'handshake-timeout' | 'crash'
}

export interface ServerRequestPayload {
  bridgeId: string
  method: string
  params: unknown
}

// ---------------------------------------------------------------------------
// Single-listener dispatcher for notifications and connection status.
//
// Instead of registering one ipcRenderer.on per subscriber (which can
// accumulate stale listeners when React StrictMode mounts/unmounts/remounts
// components), we keep exactly ONE ipcRenderer listener per channel and
// dispatch to a Set of subscriber callbacks. Adding/removing a callback from
// the Set is safe to call multiple times and is always O(1).
// ---------------------------------------------------------------------------

// Single-slot dispatchers using numeric tokens.
//
// contextBridge wraps functions in new Proxy objects on every call, making
// reference equality (=== or Set/Map) unreliable across the bridge boundary.
// We use monotonically-increasing tokens instead: each registration gets a
// unique number, and the cleanup only clears the slot if its token is still
// the current one. This guarantees exactly one active subscriber per channel
// regardless of React StrictMode's mount/unmount/remount cycle.

let notificationToken = 0
let activeNotificationCallback: ((payload: NotificationPayload) => void) | null = null
ipcRenderer.on(
  'appserver:notification',
  (_event: Electron.IpcRendererEvent, payload: NotificationPayload) => {
    activeNotificationCallback?.(payload)
  }
)

let connectionStatusToken = 0
let activeConnectionStatusCallback: ((status: ConnectionStatusPayload) => void) | null = null
ipcRenderer.on(
  'appserver:connection-status',
  (_event: Electron.IpcRendererEvent, status: ConnectionStatusPayload) => {
    activeConnectionStatusCallback?.(status)
  }
)

let serverRequestToken = 0
let activeServerRequestCallback: ((payload: ServerRequestPayload) => void) | null = null
ipcRenderer.on(
  'appserver:server-request',
  (_event: Electron.IpcRendererEvent, payload: ServerRequestPayload) => {
    activeServerRequestCallback?.(payload)
  }
)

/**
 * Typed API exposed to the Renderer via contextBridge.
 * The Renderer accesses this as `window.api`.
 */
const api = {
  platform: process.platform as 'darwin' | 'win32' | 'linux',

  titleBarOverlayHeight: TITLE_BAR_OVERLAY_HEIGHT,

  /** Matches CustomMenuBar / ToastContainer right inset on Windows / Linux. */
  titleBarOverlayRightReserve: TITLE_BAR_OVERLAY_RIGHT_RESERVE,

  menu: {
    popupTopLevel(menuId: TopLevelMenuId, x: number, y: number): Promise<void> {
      return ipcRenderer.invoke('menu:popup-top-level', { menuId, x, y })
    }
  },

  appServer: {
    /**
     * Sends a JSON-RPC request to the AppServer via Main Process.
     * Returns the result or throws on error.
     */
    sendRequest(method: string, params?: unknown, timeoutMs?: number): Promise<unknown> {
      return ipcRenderer.invoke('appserver:send-request', method, params, timeoutMs)
    },

    /**
     * Subscribes to Wire Protocol notifications forwarded from Main.
     * Returns an unsubscribe function.
     */
    onNotification(callback: (payload: NotificationPayload) => void): UnsubscribeFn {
      const token = ++notificationToken
      activeNotificationCallback = callback
      return () => {
        if (notificationToken === token) activeNotificationCallback = null
      }
    },

    /**
     * Subscribes to connection status changes.
     * Returns an unsubscribe function.
     */
    onConnectionStatus(callback: (status: ConnectionStatusPayload) => void): UnsubscribeFn {
      const token = ++connectionStatusToken
      activeConnectionStatusCallback = callback
      return () => {
        if (connectionStatusToken === token) activeConnectionStatusCallback = null
      }
    },

    /**
     * Subscribes to server-initiated requests (e.g. item/approval/request).
     * The callback receives a bridgeId that must be passed to sendServerResponse.
     */
    onServerRequest(callback: (payload: ServerRequestPayload) => void): UnsubscribeFn {
      const token = ++serverRequestToken
      activeServerRequestCallback = callback
      return () => {
        if (serverRequestToken === token) activeServerRequestCallback = null
      }
    },

    /**
     * Sends the user's decision for a server-initiated request back to Main.
     * Main will forward this as the JSON-RPC response to AppServer.
     */
    sendServerResponse(bridgeId: string, result: unknown): void {
      ipcRenderer.invoke('appserver:server-response', bridgeId, result).catch(() => {})
    }
  },

  window: {
    /**
     * Sets the window title (rendered in the OS title bar).
     */
    setTitle(title: string): void {
      ipcRenderer.invoke('window:set-title', title)
    },

    /**
     * Updates native title bar overlay colors to match app theme (no-op on macOS).
     */
    setTitleBarOverlayTheme(theme: 'dark' | 'light'): Promise<void> {
      return ipcRenderer.invoke('window:set-title-bar-overlay-theme', theme)
    },

    /**
     * Returns the workspace path for this window.
     */
    getWorkspacePath(): Promise<string> {
      return ipcRenderer.invoke('window:get-workspace-path')
    }
  },

  shell: {
    /**
     * Opens the given path in the system file explorer.
     */
    openPath(path: string): Promise<string> {
      return shell.openPath(path)
    },

    /**
     * Opens an http(s) URL in the system browser (validated in the main process).
     */
    openExternal(url: string): Promise<void> {
      return ipcRenderer.invoke('shell:open-external', url)
    }
  },

  file: {
    /**
     * Writes content to the given absolute path (within workspace).
     */
    writeFile(absPath: string, content: string): Promise<void> {
      return ipcRenderer.invoke('file:write', absPath, content)
    },

    /**
     * Reads UTF-8 text from the given absolute path (within workspace).
     * Returns empty string if the file does not exist.
     */
    readFile(absPath: string): Promise<string> {
      return ipcRenderer.invoke('file:read', absPath)
    },

    /**
     * Deletes the file at the given absolute path (within workspace).
     */
    deleteFile(absPath: string): Promise<void> {
      return ipcRenderer.invoke('file:delete', absPath)
    }
  },

  git: {
    /**
     * Stages the given files and creates a commit with the provided message.
     * Returns the git output on success.
     */
    commit(workspacePath: string, files: string[], message: string): Promise<string> {
      return ipcRenderer.invoke('git:commit', workspacePath, files, message)
    }
  },

  workspace: {
    /**
     * Opens the native folder picker dialog.
     * Returns the selected path, or null if cancelled.
     */
    pickFolder(): Promise<string | null> {
      return ipcRenderer.invoke('workspace:pick-folder')
    },

    /**
     * Triggers a full workspace switch to the given path.
     * The Main process tears down the current AppServer and spawns a new one.
     */
    switch(newPath: string): Promise<void> {
      return ipcRenderer.invoke('workspace:switch', newPath)
    },

    /**
     * Returns the list of recently opened workspaces (up to 20).
     */
    getRecent(): Promise<Array<{ path: string; name: string; lastOpenedAt: string }>> {
      return ipcRenderer.invoke('workspace:get-recent')
    },

    /**
     * Opens a new independent application window.
     */
    openNewWindow(): Promise<void> {
      return ipcRenderer.invoke('workspace:open-new-window')
    },

    /**
     * Checks whether the given workspace path is currently locked by another
     * running DotCraft Desktop process.
     * Returns { locked: true, pid } if occupied, or { locked: false } if free.
     */
    checkLock(wsPath: string): Promise<{ locked: boolean; pid?: number }> {
      return ipcRenderer.invoke('workspace:check-lock', wsPath)
    },

    /**
     * Writes a data URL image to `.craft/tmp/images/` and returns the absolute path for `localImage`.
     */
    saveImageToTemp(params: { dataUrl: string; fileName?: string }): Promise<{ path: string }> {
      return ipcRenderer.invoke('workspace:save-image-to-temp', params)
    },

    /**
     * Fuzzy filename search within the workspace for @ file autocomplete.
     */
    searchFiles(params: {
      query: string
      workspacePath: string
      limit?: number
    }): Promise<{ files: Array<{ name: string; relativePath: string; dir: string }> }> {
      return ipcRenderer.invoke('workspace:search-files', params)
    }
  },

  settings: {
    /**
     * Returns the current application settings.
     */
    get(): Promise<{
      appServerBinaryPath?: string
      lastWorkspacePath?: string
      connectionMode?: ConnectionMode
      webSocket?: {
        host?: string
        port?: number
      }
      remote?: {
        url?: string
        token?: string
      }
      theme?: 'dark' | 'light'
      locale?: 'en' | 'zh-Hans'
    }> {
      return ipcRenderer.invoke('settings:get')
    },

    /**
     * Merges and persists partial settings updates.
     */
    set(partial: {
      appServerBinaryPath?: string
      connectionMode?: ConnectionMode
      webSocket?: {
        host?: string
        port?: number
      }
      remote?: {
        url?: string
        token?: string
      }
      theme?: 'dark' | 'light'
      locale?: 'en' | 'zh-Hans'
    }): Promise<void> {
      return ipcRenderer.invoke('settings:set', partial)
    }
  }
}

contextBridge.exposeInMainWorld('api', api)

export type Api = typeof api
