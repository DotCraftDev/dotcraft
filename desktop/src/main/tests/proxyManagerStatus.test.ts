import { describe, expect, it, vi } from 'vitest'
import { ProxyProcessManager } from '../ProxyProcessManager'
import {
  registerGuardedProxyManagerStatusHandlers,
  runIfCurrentProxyManager
} from '../proxyManagerStatus'
import type { ProxyStatusPayload } from '../ipcBridge'

function createManager(workspacePath = '/workspace'): ProxyProcessManager {
  return new ProxyProcessManager({
    workspacePath,
    configPath: '/tmp/proxy-config.json'
  })
}

describe('proxyManagerStatus', () => {
  it('ignores stopped events from a stale manager', () => {
    const staleManager = createManager('/workspace/old')
    const currentManager = createManager('/workspace/new')
    let activeManager: ProxyProcessManager | null = staleManager
    let proxyStatus: ProxyStatusPayload = { status: 'starting', port: 8317 }
    const cleanup = vi.fn()

    registerGuardedProxyManagerStatusHandlers({
      manager: staleManager,
      workspacePath: '/workspace/old',
      port: 8317,
      apiKey: 'sk-old',
      getCurrentManager: () => activeManager,
      setProxyStatus: (status) => {
        proxyStatus = status
      },
      cleanupWorkspaceProxyOverrides: cleanup
    })

    activeManager = currentManager
    staleManager.emit('stopped')

    expect(proxyStatus).toEqual({ status: 'starting', port: 8317 })
    expect(cleanup).not.toHaveBeenCalled()
  })

  it('ignores error and crash events from a stale manager', () => {
    const staleManager = createManager('/workspace/old')
    const currentManager = createManager('/workspace/new')
    let activeManager: ProxyProcessManager | null = staleManager
    let proxyStatus: ProxyStatusPayload = { status: 'running', port: 8317, pid: 42 }
    const cleanup = vi.fn()

    registerGuardedProxyManagerStatusHandlers({
      manager: staleManager,
      workspacePath: '/workspace/old',
      port: 8317,
      apiKey: 'sk-old',
      getCurrentManager: () => activeManager,
      setProxyStatus: (status) => {
        proxyStatus = status
      },
      cleanupWorkspaceProxyOverrides: cleanup
    })

    activeManager = currentManager
    staleManager.emit('error', new Error('old manager failed'))
    staleManager.emit('crash', { code: 1, signal: null })

    expect(proxyStatus).toEqual({ status: 'running', port: 8317, pid: 42 })
    expect(cleanup).not.toHaveBeenCalled()
  })

  it('updates status and cleanup for the current manager', () => {
    const manager = createManager('/workspace/current')
    let activeManager: ProxyProcessManager | null = manager
    let proxyStatus: ProxyStatusPayload = { status: 'starting', port: 8317 }
    const cleanup = vi.fn()

    registerGuardedProxyManagerStatusHandlers({
      manager,
      workspacePath: '/workspace/current',
      port: 8317,
      apiKey: 'sk-current',
      getCurrentManager: () => activeManager,
      setProxyStatus: (status) => {
        proxyStatus = status
      },
      cleanupWorkspaceProxyOverrides: cleanup
    })

    manager.emit('crash', { code: 1, signal: null })
    expect(proxyStatus).toEqual({
      status: 'error',
      errorMessage: 'CLIProxyAPI process crashed unexpectedly',
      port: 8317
    })
    expect(cleanup).toHaveBeenCalledOnce()
    expect(cleanup).toHaveBeenCalledWith('/workspace/current', {
      proxyPort: 8317,
      proxyApiKey: 'sk-current'
    })

    manager.emit('stopped')
    expect(proxyStatus).toEqual({ status: 'stopped', port: 8317 })
  })

  it('only marks running for the current manager', () => {
    const staleManager = createManager('/workspace/old')
    const currentManager = createManager('/workspace/new')
    let activeManager: ProxyProcessManager | null = staleManager
    let proxyStatus: ProxyStatusPayload = { status: 'starting', port: 8317 }

    const staleResult = runIfCurrentProxyManager(staleManager, () => activeManager, () => {
      proxyStatus = {
        status: 'running',
        port: 8317,
        pid: 1001,
        baseUrl: 'http://127.0.0.1:8317/v1',
        managementUrl: 'http://127.0.0.1:8317'
      }
    })
    expect(staleResult).toBe(true)
    expect(proxyStatus.status).toBe('running')

    activeManager = currentManager
    const ignoredResult = runIfCurrentProxyManager(staleManager, () => activeManager, () => {
      proxyStatus = { status: 'error', errorMessage: 'stale overwrite' }
    })
    expect(ignoredResult).toBe(false)
    expect(proxyStatus).toEqual({
      status: 'running',
      port: 8317,
      pid: 1001,
      baseUrl: 'http://127.0.0.1:8317/v1',
      managementUrl: 'http://127.0.0.1:8317'
    })

    const currentResult = runIfCurrentProxyManager(currentManager, () => activeManager, () => {
      proxyStatus = {
        status: 'running',
        port: 8317,
        pid: 2002,
        baseUrl: 'http://127.0.0.1:8317/v1',
        managementUrl: 'http://127.0.0.1:8317'
      }
    })
    expect(currentResult).toBe(true)
    expect(proxyStatus).toEqual({
      status: 'running',
      port: 8317,
      pid: 2002,
      baseUrl: 'http://127.0.0.1:8317/v1',
      managementUrl: 'http://127.0.0.1:8317'
    })
  })
})
