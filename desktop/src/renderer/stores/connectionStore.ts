import { create } from 'zustand'
import type { ConnectionStatusPayload } from '../../preload/api.d'

export type ConnectionStatus = 'connecting' | 'connected' | 'disconnected' | 'error'
export type ConnectionErrorType = 'binary-not-found' | 'handshake-timeout' | 'crash'

export interface ServerInfo {
  name: string
  version: string
  protocolVersion?: string
}

export interface ServerCapabilities {
  threadManagement?: boolean
  threadSubscriptions?: boolean
  approvalFlow?: boolean
  modeSwitch?: boolean
  configOverride?: boolean
  cronManagement?: boolean
  heartbeatManagement?: boolean
  [key: string]: unknown
}

export interface ConnectionState {
  status: ConnectionStatus
  serverInfo: ServerInfo | null
  capabilities: ServerCapabilities | null
  /** DashBoard URL when AppServer reports it at initialize; null if unavailable. */
  dashboardUrl: string | null
  errorMessage: string | null
  errorType: ConnectionErrorType | null
}

interface ConnectionStore extends ConnectionState {
  setStatus(payload: ConnectionStatusPayload): void
  reset(): void
}

const initialState: ConnectionState = {
  status: 'connecting',
  serverInfo: null,
  capabilities: null,
  dashboardUrl: null,
  errorMessage: null,
  errorType: null
}

export const useConnectionStore = create<ConnectionStore>((set) => ({
  ...initialState,

  setStatus(payload: ConnectionStatusPayload) {
    const connected = payload.status === 'connected'
    set({
      status: payload.status,
      serverInfo: payload.serverInfo ?? null,
      capabilities: (payload.capabilities as ServerCapabilities) ?? null,
      dashboardUrl: connected ? (payload.dashboardUrl ?? null) : null,
      errorMessage: payload.errorMessage ?? null,
      errorType: (payload.errorType as ConnectionErrorType) ?? null
    })
  },

  reset() {
    set(initialState)
  }
}))

/**
 * Subscribe to connection status updates from Main Process.
 * Call this once at app initialization.
 * Returns an unsubscribe function.
 */
export function initConnectionStore(): () => void {
  return window.api.appServer.onConnectionStatus((payload) => {
    useConnectionStore.getState().setStatus(payload)
  })
}
