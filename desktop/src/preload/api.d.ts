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

export type ConfigReloadBehavior = 'processRestart' | 'subsystemRestart' | 'hot' | string

export interface WorkspaceConfigSchemaField {
  key: string
  displayName?: string
  type: string
  sensitive: boolean
  options?: string[]
  min?: number
  max?: number
  hint?: string
  defaultValue?: unknown
  reload?: ConfigReloadBehavior
  subsystemKey?: string
}

export interface WorkspaceConfigSchemaSection {
  section: string
  order: number
  path?: string[]
  rootKey?: string
  itemFields?: WorkspaceConfigSchemaField[]
  fields: WorkspaceConfigSchemaField[]
}

export interface WorkspaceConfigSchema {
  sections: WorkspaceConfigSchemaSection[]
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

export interface EditorInfo {
  id: EditorId
  labelKey: string
  iconKey: string
  iconDataUrl?: string
}

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
        requestWorkspaceConfigSchema(): Promise<WorkspaceConfigSchema | null>
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
      workspaceConfig: {
        getCore(): Promise<{ model: string | null; apiKey: string | null; endPoint: string | null }>
      }
      proxy: {
        getStatus(): Promise<ProxyStatusPayload>
        getResolvedBinary(request?: {
          binarySource?: BinarySource
          binaryPath?: string
        }): Promise<ResolvedBinaryPayload>
        pickBinary(): Promise<string | null>
        restartManaged(): Promise<void>
        startOAuth(provider: ProxyOAuthProvider): Promise<{ url: string; state?: string }>
        getAuthStatus(state: string): Promise<{ status: string; error?: string }>
        listAuthFiles(): Promise<ProxyAuthFileSummary[]>
        getUsageSummary(): Promise<{
          totalRequests: number
          successCount: number
          failureCount: number
          totalTokens: number
          failedRequests: number
        }>
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
        listEditors(): Promise<EditorInfo[]>
        launchEditor(id: EditorId, cwd: string): Promise<void>
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
        readImageAsDataUrl(params: { path: string }): Promise<{ dataUrl: string }>
        searchFiles(params: {
          query: string
          workspacePath: string
          limit?: number
        }): Promise<{ files: Array<{ name: string; relativePath: string; dir: string }> }>
      }
      modules: {
        list(): Promise<DiscoveredModule[]>
        userDirectory(): Promise<{ path: string }>
        checkDirectory(path: string): Promise<{ exists: boolean }>
        openFolder(): Promise<{ ok: boolean; error?: string }>
        pickDirectory(): Promise<string | null>
        rescan(): Promise<DiscoveredModule[]>
        setActiveVariant(params: {
          channelName: string
          moduleId: string
        }): Promise<{ ok: boolean; error?: string }>
        readConfig(params: {
          configFileName: string
        }): Promise<{ exists: boolean; config: Record<string, unknown> | null }>
        writeConfig(params: {
          configFileName: string
          config: Record<string, unknown>
        }): Promise<{ ok: boolean }>
        start(params: {
          moduleId: string
        }): Promise<{ ok: boolean; error?: string; missingFields?: string[] }>
        stop(params: { moduleId: string }): Promise<{ ok: boolean; error?: string }>
        running(): Promise<ModuleStatusMap>
        getLogs(moduleId: string): Promise<{ lines: string[] }>
        qrStatus(moduleId: string): Promise<{ active: boolean; qrDataUrl: string | null }>
        onStatusChanged(callback: (statusMap: ModuleStatusMap) => void): UnsubscribeFn
        onQrUpdate(callback: (payload: QrUpdatePayload) => void): UnsubscribeFn
        onRescanSummary(
          callback: (payload: ModulesRescanSummaryPayload) => void
        ): UnsubscribeFn
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
          }
        ): Promise<void>
      }
    }
  }
}

export {}
