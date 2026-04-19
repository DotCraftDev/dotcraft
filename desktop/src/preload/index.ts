import { contextBridge, ipcRenderer, shell } from 'electron'
import {
  TITLE_BAR_OVERLAY_HEIGHT,
  TITLE_BAR_OVERLAY_RIGHT_RESERVE
} from '../shared/titleBarOverlay'
import type { TopLevelMenuId } from '../shared/locales/types'

export type UnsubscribeFn = () => void
export type ConnectionMode = 'stdio' | 'websocket' | 'stdioAndWebSocket' | 'remote'
export type BinarySource = 'bundled' | 'path' | 'custom'
export type ProxyOAuthProvider = 'codex' | 'claude' | 'gemini' | 'qwen' | 'iflow'
export type WorkspaceSetupState = 'no-workspace' | 'needs-setup' | 'ready'
export type WorkspaceBootstrapProfile = 'default' | 'developer' | 'personal-assistant'
export type WorkspaceLanguage = 'Chinese' | 'English'
export type EditorId =
  | 'explorer'
  | 'vs'
  | 'cursor'
  | 'vscode'
  | 'rider'
  | 'webstorm'
  | 'idea'
  | 'github-desktop'
  | 'git-bash'
  | 'terminal'

export interface EditorInfo {
  id: EditorId
  labelKey: string
  iconKey: string
  iconDataUrl?: string
}

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
  binarySource?: BinarySource
}

export interface ResolvedBinaryPayload {
  source: BinarySource
  path: string | null
}

export interface ProxyStatusPayload {
  status: 'stopped' | 'starting' | 'running' | 'error'
  errorMessage?: string
  port?: number
  baseUrl?: string
  managementUrl?: string
  pid?: number
}

export interface ProxyAuthFileSummary {
  provider: ProxyOAuthProvider
  status: string
  statusMessage: string
  disabled: boolean
  unavailable: boolean
  runtimeOnly: boolean
  modtime?: string
  email?: string
  name: string
}

export interface ServerRequestPayload {
  bridgeId: string
  method: string
  params: unknown
}

export interface WorkspaceStatusPayload {
  status: WorkspaceSetupState
  workspacePath: string
  hasUserConfig: boolean
  userConfigDefaults?: {
    language?: WorkspaceLanguage
    endpoint?: string
    model?: string
    apiKeyPresent: boolean
  }
}

export interface WorkspaceSetupRequest {
  language: WorkspaceLanguage
  model: string
  endpoint: string
  apiKey: string
  profile: WorkspaceBootstrapProfile
  saveToUserConfig: boolean
  preferExistingUserConfig: boolean
}

export interface WorkspaceSetupModelListRequest {
  endpoint: string
  apiKey: string
  preferExistingUserConfig: boolean
}

export type WorkspaceSetupModelListResult =
  | { kind: 'success'; models: string[] }
  | { kind: 'unsupported' }
  | { kind: 'missing-key' }
  | { kind: 'error' }

export interface ConfigDescriptorWire {
  key: string
  displayLabel: string
  description: string
  localizedDisplayLabel?: Partial<Record<'en' | 'zh-Hans', string>>
  localizedDescription?: Partial<Record<'en' | 'zh-Hans', string>>
  required: boolean
  dataKind: string
  masked: boolean
  interactiveSetupOnly: boolean
  advanced?: boolean
  defaultValue?: unknown
  enumValues?: string[]
}

export interface DiscoveredModule {
  moduleId: string
  channelName: string
  displayName: string
  packageName: string
  configFileName: string
  supportedTransports: string[]
  requiresInteractiveSetup: boolean
  variant: string
  source: 'bundled' | 'user'
  absolutePath: string
  configDescriptors: ConfigDescriptorWire[]
}

export interface ModuleStatusEntry {
  processState: 'starting' | 'running' | 'stopping' | 'stopped' | 'crashed'
  connected: boolean
  restartCount: number
  lastExitCode: number | null
  lastStderrExcerpt?: string[]
  crashHint?: string
}

export type ModuleStatusMap = Record<string, ModuleStatusEntry>

export interface QrUpdatePayload {
  moduleId: string
  qrDataUrl: string | null
  timestamp: number
}

export interface ModulesRescanSummaryPayload {
  addedModuleIds: string[]
  removedModuleIds: string[]
  changedModuleIds: string[]
  changedRunningModuleIds: string[]
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

let workspaceStatusToken = 0
let activeWorkspaceStatusCallback: ((status: WorkspaceStatusPayload) => void) | null = null
ipcRenderer.on(
  'workspace:status-changed',
  (_event: Electron.IpcRendererEvent, status: WorkspaceStatusPayload) => {
    activeWorkspaceStatusCallback?.(status)
  }
)

let moduleStatusToken = 0
let activeModuleStatusCallback: ((statusMap: ModuleStatusMap) => void) | null = null
ipcRenderer.on(
  'modules:status-changed',
  (_event: Electron.IpcRendererEvent, statusMap: ModuleStatusMap) => {
    activeModuleStatusCallback?.(statusMap)
  }
)

let moduleQrUpdateToken = 0
let activeModuleQrUpdateCallback: ((payload: QrUpdatePayload) => void) | null = null
ipcRenderer.on(
  'modules:qr-update',
  (_event: Electron.IpcRendererEvent, payload: QrUpdatePayload) => {
    activeModuleQrUpdateCallback?.(payload)
  }
)

let moduleRescanSummaryToken = 0
let activeModuleRescanSummaryCallback: ((payload: ModulesRescanSummaryPayload) => void) | null = null
ipcRenderer.on(
  'modules:rescan-summary',
  (_event: Electron.IpcRendererEvent, payload: ModulesRescanSummaryPayload) => {
    activeModuleRescanSummaryCallback?.(payload)
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

    listModels(): Promise<unknown> {
      return ipcRenderer.invoke('appserver:model-list')
    },

    /**
     * Returns the latest connection status snapshot from Main Process.
     * This avoids missing early status events during renderer bootstrap.
     */
    getConnectionStatus(): Promise<ConnectionStatusPayload> {
      return ipcRenderer.invoke('appserver:get-connection-status')
    },

    getResolvedBinary(request?: {
      binarySource?: BinarySource
      binaryPath?: string
    }): Promise<ResolvedBinaryPayload> {
      return ipcRenderer.invoke('appserver:resolved-binary', request)
    },

    pickBinary(): Promise<string | null> {
      return ipcRenderer.invoke('appserver:pick-binary')
    },

    restartManaged(): Promise<void> {
      return ipcRenderer.invoke('appserver:restart-managed')
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

  proxy: {
    getStatus(): Promise<ProxyStatusPayload> {
      return ipcRenderer.invoke('proxy:get-status')
    },
    getResolvedBinary(request?: {
      binarySource?: BinarySource
      binaryPath?: string
    }): Promise<ResolvedBinaryPayload> {
      return ipcRenderer.invoke('proxy:resolved-binary', request)
    },
    pickBinary(): Promise<string | null> {
      return ipcRenderer.invoke('proxy:pick-binary')
    },
    restartManaged(): Promise<void> {
      return ipcRenderer.invoke('proxy:restart-managed')
    },
    startOAuth(provider: ProxyOAuthProvider): Promise<{ url: string; state?: string }> {
      return ipcRenderer.invoke('proxy:start-oauth', provider)
    },
    getAuthStatus(state: string): Promise<{ status: string; error?: string }> {
      return ipcRenderer.invoke('proxy:get-auth-status', state)
    },
    listAuthFiles(): Promise<ProxyAuthFileSummary[]> {
      return ipcRenderer.invoke('proxy:list-auth-files')
    },
    getUsageSummary(): Promise<{
      totalRequests: number
      successCount: number
      failureCount: number
      totalTokens: number
      failedRequests: number
    }> {
      return ipcRenderer.invoke('proxy:get-usage-summary')
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
    },

    listEditors(): Promise<EditorInfo[]> {
      return ipcRenderer.invoke('editors:list')
    },

    launchEditor(id: EditorId, cwd: string): Promise<void> {
      return ipcRenderer.invoke('editors:launch', id, cwd)
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
    },

    /**
     * Returns true when the given absolute path exists within the workspace.
     */
    exists(absPath: string): Promise<boolean> {
      return ipcRenderer.invoke('file:exists', absPath)
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

    clearSelection(): Promise<void> {
      return ipcRenderer.invoke('workspace:clear-selection')
    },

    /**
     * Returns the list of recently opened workspaces (up to 20).
     */
    getRecent(): Promise<Array<{ path: string; name: string; lastOpenedAt: string }>> {
      return ipcRenderer.invoke('workspace:get-recent')
    },

    getStatus(): Promise<WorkspaceStatusPayload> {
      return ipcRenderer.invoke('workspace:get-status')
    },

    onStatusChange(callback: (status: WorkspaceStatusPayload) => void): UnsubscribeFn {
      const token = ++workspaceStatusToken
      activeWorkspaceStatusCallback = callback
      return () => {
        if (workspaceStatusToken === token) activeWorkspaceStatusCallback = null
      }
    },

    listSetupModels(request: WorkspaceSetupModelListRequest): Promise<WorkspaceSetupModelListResult> {
      return ipcRenderer.invoke('workspace:list-setup-models', request)
    },

    runSetup(request: WorkspaceSetupRequest): Promise<void> {
      return ipcRenderer.invoke('workspace:run-setup', request)
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
     * Writes a data URL image to `.craft/attachments/images/` and returns the absolute path for `localImage`.
     */
    saveImageToTemp(params: { dataUrl: string; fileName?: string }): Promise<{ path: string }> {
      return ipcRenderer.invoke('workspace:save-image-to-temp', params)
    },

    /**
     * Reads an attached image from disk and returns a data URL for UI rehydration.
     */
    readImageAsDataUrl(params: { path: string }): Promise<{ dataUrl: string }> {
      return ipcRenderer.invoke('workspace:read-image-as-data-url', params)
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

  modules: {
    list(): Promise<DiscoveredModule[]> {
      return ipcRenderer.invoke('modules:list')
    },
    userDirectory(): Promise<{ path: string }> {
      return ipcRenderer.invoke('modules:user-directory')
    },
    checkDirectory(path: string): Promise<{ exists: boolean }> {
      return ipcRenderer.invoke('modules:check-directory', { path })
    },
    openFolder(): Promise<{ ok: boolean; error?: string }> {
      return ipcRenderer.invoke('modules:open-folder')
    },
    pickDirectory(): Promise<string | null> {
      return ipcRenderer.invoke('modules:pick-directory')
    },
    rescan(): Promise<DiscoveredModule[]> {
      return ipcRenderer.invoke('modules:rescan')
    },
    setActiveVariant(params: {
      channelName: string
      moduleId: string
    }): Promise<{ ok: boolean; error?: string }> {
      return ipcRenderer.invoke('modules:set-active-variant', params)
    },
    readConfig(params: {
      configFileName: string
    }): Promise<{ exists: boolean; config: Record<string, unknown> | null }> {
      return ipcRenderer.invoke('modules:read-config', params)
    },
    writeConfig(params: {
      configFileName: string
      config: Record<string, unknown>
    }): Promise<{ ok: boolean }> {
      return ipcRenderer.invoke('modules:write-config', params)
    },
    start(params: {
      moduleId: string
    }): Promise<{ ok: boolean; error?: string; missingFields?: string[] }> {
      return ipcRenderer.invoke('modules:start', params)
    },
    stop(params: { moduleId: string }): Promise<{ ok: boolean; error?: string }> {
      return ipcRenderer.invoke('modules:stop', params)
    },
    running(): Promise<ModuleStatusMap> {
      return ipcRenderer.invoke('modules:running')
    },
    getLogs(moduleId: string): Promise<{ lines: string[] }> {
      return ipcRenderer.invoke('modules:get-logs', { moduleId })
    },
    qrStatus(moduleId: string): Promise<{ active: boolean; qrDataUrl: string | null }> {
      return ipcRenderer.invoke('modules:qr-status', { moduleId })
    },
    onStatusChanged(callback: (statusMap: ModuleStatusMap) => void): UnsubscribeFn {
      const token = ++moduleStatusToken
      activeModuleStatusCallback = callback
      return () => {
        if (moduleStatusToken === token) activeModuleStatusCallback = null
      }
    },
    onQrUpdate(callback: (payload: QrUpdatePayload) => void): UnsubscribeFn {
      const token = ++moduleQrUpdateToken
      activeModuleQrUpdateCallback = callback
      return () => {
        if (moduleQrUpdateToken === token) activeModuleQrUpdateCallback = null
      }
    },
    onRescanSummary(callback: (payload: ModulesRescanSummaryPayload) => void): UnsubscribeFn {
      const token = ++moduleRescanSummaryToken
      activeModuleRescanSummaryCallback = callback
      return () => {
        if (moduleRescanSummaryToken === token) activeModuleRescanSummaryCallback = null
      }
    }
  },

  settings: {
    /**
     * Returns the current application settings.
     */
    get(): Promise<{
      binarySource?: BinarySource
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
      proxy?: {
        enabled?: boolean
        host?: string
        port?: number
        binarySource?: BinarySource
        binaryPath?: string
        authDir?: string
      }
      modulesDirectory?: string
      activeModuleVariants?: Record<string, string>
      theme?: 'dark' | 'light'
      locale?: 'en' | 'zh-Hans'
      visibleChannels?: string[]
      lastOpenEditorId?: EditorId
    }> {
      return ipcRenderer.invoke('settings:get')
    },

    /**
     * Merges and persists partial settings updates.
     */
    set(partial: {
      binarySource?: BinarySource
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
      proxy?: {
        enabled?: boolean
        host?: string
        port?: number
        binarySource?: BinarySource
        binaryPath?: string
        authDir?: string
      }
      modulesDirectory?: string
      activeModuleVariants?: Record<string, string>
      theme?: 'dark' | 'light'
      locale?: 'en' | 'zh-Hans'
      visibleChannels?: string[]
      lastOpenEditorId?: EditorId
    }): Promise<void> {
      return ipcRenderer.invoke('settings:set', partial)
    }
  }
}

contextBridge.exposeInMainWorld('api', api)

export type Api = typeof api
