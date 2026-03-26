export type UnsubscribeFn = () => void

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
  errorMessage?: string
  errorType?: 'binary-not-found' | 'handshake-timeout' | 'crash'
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
      }
      file: {
        writeFile(absPath: string, content: string): Promise<void>
        readFile(absPath: string): Promise<string>
        deleteFile(absPath: string): Promise<void>
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
          appServerBinaryPath?: string
          lastWorkspacePath?: string
          theme?: 'dark' | 'light'
          locale?: 'en' | 'zh-Hans'
          visibleChannels?: string[]
        }>
        set(
          partial: {
            appServerBinaryPath?: string
            theme?: 'dark' | 'light'
            locale?: 'en' | 'zh-Hans'
            visibleChannels?: string[]
            clearVisibleChannels?: boolean
          }
        ): Promise<void>
      }
    }
  }
}

export {}
