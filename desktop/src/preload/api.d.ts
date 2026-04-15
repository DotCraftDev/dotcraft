export type UnsubscribeFn = () => void
export type ConnectionMode = 'stdio' | 'websocket' | 'stdioAndWebSocket' | 'remote'
export type BinarySource = 'bundled' | 'path' | 'custom'
export type WorkspaceSetupState = 'no-workspace' | 'needs-setup' | 'ready'
export type WorkspaceBootstrapProfile = 'default' | 'developer' | 'personal-assistant'
export type WorkspaceLanguage = 'Chinese' | 'English'

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
  required: boolean
  dataKind: string
  masked: boolean
  interactiveSetupOnly: boolean
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
}

export type ModuleStatusMap = Record<string, ModuleStatusEntry>

declare global {
  interface Window {
    api: {
      platform: 'darwin' | 'win32' | 'linux'
      titleBarOverlayHeight: number
      titleBarOverlayRightReserve: number
      menu: {
        popupTopLevel(
          menuId: 'file' | 'edit' | 'view' | 'window' | 'help',
          x: number,
          y: number
        ): Promise<void>
      }
      appServer: {
        sendRequest(method: string, params?: unknown, timeoutMs?: number): Promise<unknown>
        listModels(): Promise<unknown>
        getConnectionStatus(): Promise<ConnectionStatusPayload>
        getResolvedBinary(request?: {
          binarySource?: BinarySource
          binaryPath?: string
        }): Promise<ResolvedBinaryPayload>
        pickBinary(): Promise<string | null>
        restartManaged(): Promise<void>
        onNotification(callback: (payload: NotificationPayload) => void): UnsubscribeFn
        onConnectionStatus(
          callback: (status: ConnectionStatusPayload) => void
        ): UnsubscribeFn
        onServerRequest(callback: (payload: ServerRequestPayload) => void): UnsubscribeFn
        sendServerResponse(bridgeId: string, result: unknown): void
      }
      window: {
        setTitle(title: string): void
        setTitleBarOverlayTheme(theme: 'dark' | 'light'): Promise<void>
        getWorkspacePath(): Promise<string>
      }
      shell: {
        openPath(path: string): Promise<string>
        /** Opens http(s) URLs in the system browser (validated in the main process). */
        openExternal(url: string): Promise<void>
      }
      file: {
        writeFile(absPath: string, content: string): Promise<void>
        readFile(absPath: string): Promise<string>
        deleteFile(absPath: string): Promise<void>
        exists(absPath: string): Promise<boolean>
      }
      git: {
        commit(workspacePath: string, files: string[], message: string): Promise<string>
      }
      workspace: {
        pickFolder(): Promise<string | null>
        switch(newPath: string): Promise<void>
        clearSelection(): Promise<void>
        getRecent(): Promise<Array<{ path: string; name: string; lastOpenedAt: string }>>
        getStatus(): Promise<WorkspaceStatusPayload>
        onStatusChange(
          callback: (status: WorkspaceStatusPayload) => void
        ): UnsubscribeFn
        listSetupModels(
          request: WorkspaceSetupModelListRequest
        ): Promise<WorkspaceSetupModelListResult>
        runSetup(request: WorkspaceSetupRequest): Promise<void>
        openNewWindow(): Promise<void>
        checkLock(wsPath: string): Promise<{ locked: boolean; pid?: number }>
        saveImageToTemp(params: { dataUrl: string; fileName?: string }): Promise<{ path: string }>
        searchFiles(params: {
          query: string
          workspacePath: string
          limit?: number
        }): Promise<{ files: Array<{ name: string; relativePath: string; dir: string }> }>
      }
      modules: {
        list(): Promise<DiscoveredModule[]>
        rescan(): Promise<DiscoveredModule[]>
        readConfig(params: {
          configFileName: string
        }): Promise<{ exists: boolean; config: Record<string, unknown> | null }>
        writeConfig(params: {
          configFileName: string
          config: Record<string, unknown>
        }): Promise<{ ok: boolean }>
        start(params: { moduleId: string }): Promise<{ ok: boolean; error?: string }>
        stop(params: { moduleId: string }): Promise<{ ok: boolean; error?: string }>
        running(): Promise<ModuleStatusMap>
        onStatusChanged(callback: (statusMap: ModuleStatusMap) => void): UnsubscribeFn
      }
      settings: {
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
          modulesDirectory?: string
          theme?: 'dark' | 'light'
          locale?: 'en' | 'zh-Hans'
          visibleChannels?: string[]
        }>
        set(
          partial: {
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
            modulesDirectory?: string
            theme?: 'dark' | 'light'
            locale?: 'en' | 'zh-Hans'
            visibleChannels?: string[]
          }
        ): Promise<void>
      }
    }
  }
}

export {}
