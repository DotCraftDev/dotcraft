export type UnsubscribeFn = () => void
export type ConnectionMode = 'stdio' | 'websocket' | 'stdioAndWebSocket' | 'remote'
export type BinarySource = 'bundled' | 'path' | 'custom'

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
        getRecent(): Promise<Array<{ path: string; name: string; lastOpenedAt: string }>>
        openNewWindow(): Promise<void>
        checkLock(wsPath: string): Promise<{ locked: boolean; pid?: number }>
        saveImageToTemp(params: { dataUrl: string; fileName?: string }): Promise<{ path: string }>
        searchFiles(params: {
          query: string
          workspacePath: string
          limit?: number
        }): Promise<{ files: Array<{ name: string; relativePath: string; dir: string }> }>
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
