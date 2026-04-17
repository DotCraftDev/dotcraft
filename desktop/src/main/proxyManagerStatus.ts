import type { ProxyStatusPayload } from './ipcBridge'
import type { ProxyProcessManager } from './ProxyProcessManager'

type CleanupWorkspaceProxyOverrides = (
  workspacePath: string,
  options: {
    proxyPort: number
    proxyApiKey: string
  }
) => Promise<void> | void

interface RegisterGuardedProxyManagerStatusHandlersOptions {
  manager: ProxyProcessManager
  workspacePath: string
  port: number
  apiKey: string
  getCurrentManager: () => ProxyProcessManager | null
  setProxyStatus: (status: ProxyStatusPayload) => void
  cleanupWorkspaceProxyOverrides: CleanupWorkspaceProxyOverrides
}

export function isCurrentProxyManager(
  manager: ProxyProcessManager,
  getCurrentManager: () => ProxyProcessManager | null
): boolean {
  return getCurrentManager() === manager
}

export function runIfCurrentProxyManager(
  manager: ProxyProcessManager,
  getCurrentManager: () => ProxyProcessManager | null,
  fn: () => void
): boolean {
  if (!isCurrentProxyManager(manager, getCurrentManager)) {
    return false
  }
  fn()
  return true
}

export function registerGuardedProxyManagerStatusHandlers(
  options: RegisterGuardedProxyManagerStatusHandlersOptions
): void {
  const {
    manager,
    workspacePath,
    port,
    apiKey,
    getCurrentManager,
    setProxyStatus,
    cleanupWorkspaceProxyOverrides
  } = options

  manager.on('error', (err: Error) => {
    runIfCurrentProxyManager(manager, getCurrentManager, () => {
      setProxyStatus({ status: 'error', errorMessage: err.message, port })
    })
  })

  manager.on('crash', () => {
    runIfCurrentProxyManager(manager, getCurrentManager, () => {
      setProxyStatus({
        status: 'error',
        errorMessage: 'CLIProxyAPI process crashed unexpectedly',
        port
      })
      void cleanupWorkspaceProxyOverrides(workspacePath, {
        proxyPort: port,
        proxyApiKey: apiKey
      })
    })
  })

  manager.on('stopped', () => {
    runIfCurrentProxyManager(manager, getCurrentManager, () => {
      setProxyStatus({ status: 'stopped', port })
    })
  })
}
