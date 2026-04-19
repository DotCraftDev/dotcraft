import { describe, it, expect, vi, beforeEach } from 'vitest'
import { ipcMain, shell } from 'electron'
import { promises as fs } from 'fs'

const { scanModulesMock, moduleProcessManagerStartMock, detectEditorsMock, launchEditorMock } = vi.hoisted(() => ({
  scanModulesMock: vi.fn(),
  moduleProcessManagerStartMock: vi.fn(),
  detectEditorsMock: vi.fn(),
  launchEditorMock: vi.fn()
}))

vi.mock('fs', () => ({
  promises: {
    readFile: vi.fn(),
    writeFile: vi.fn(),
    stat: vi.fn(),
    mkdir: vi.fn(),
    rm: vi.fn(),
    rename: vi.fn()
  }
}))

vi.mock('electron', () => ({
  app: {
    isPackaged: true,
    getPath: vi.fn(() => 'C:\\Users\\tester')
  },
  ipcMain: {
    handle: vi.fn(),
    removeHandler: vi.fn()
  },
  BrowserWindow: {
    getAllWindows: vi.fn(() => []),
    getFocusedWindow: vi.fn(() => null)
  },
  dialog: {
    showOpenDialog: vi.fn()
  },
  Notification: {
    isSupported: vi.fn(() => false)
  },
  shell: {
    openExternal: vi.fn().mockResolvedValue(undefined),
    openPath: vi.fn().mockResolvedValue('')
  }
}))

vi.mock('../moduleScanner', async () => {
  const actual = await vi.importActual('../moduleScanner')
  return {
    ...actual,
    scanModules: scanModulesMock
  }
})

vi.mock('../moduleProcessManager', async () => {
  const actual = await vi.importActual('../moduleProcessManager')
  class MockModuleProcessManager {
    start = moduleProcessManagerStartMock
    stop = vi.fn()
    stopAll = vi.fn().mockResolvedValue(undefined)
    getStatusMap = vi.fn(() => ({}))
    autoStartModules = vi.fn().mockResolvedValue(undefined)
    getRecentLogs = vi.fn(() => [])
    getQrStatus = vi.fn(() => ({ active: false, qrDataUrl: null }))
  }
  return {
    ...actual,
    ModuleProcessManager: MockModuleProcessManager
  }
})

vi.mock('../externalEditors', () => ({
  detectEditors: detectEditorsMock,
  launchEditor: launchEditorMock
}))

import {
  createServerRequestBridge,
  registerIpcHandlers,
  sanitizeHttpOrHttpsUrl,
  openExternalHttpUrl
} from '../ipcBridge'

// ---------------------------------------------------------------------------
// ipcBridge — server-request bridge tests
//
// The bridge creates a pending Promise per request (identified by bridgeId),
// which resolves when the Renderer sends back a response via
// appserver:server-response. These tests verify the pending-map logic directly
// (without standing up a real Electron IPC environment).
// ---------------------------------------------------------------------------

describe('createServerRequestBridge', () => {
  it('returns a unique bridgeId for each call', () => {
    const a = createServerRequestBridge()
    const b = createServerRequestBridge()
    expect(a.bridgeId).not.toBe(b.bridgeId)
  })

  it('returns a promise that is pending until resolved externally', async () => {
    const { promise } = createServerRequestBridge()
    let settled = false
    void promise.then(() => { settled = true })
    await new Promise((r) => setTimeout(r, 10))
    expect(settled).toBe(false)
  })

  it('bridge IDs are numeric strings in ascending order', () => {
    const ids = [
      createServerRequestBridge().bridgeId,
      createServerRequestBridge().bridgeId,
      createServerRequestBridge().bridgeId
    ]
    const nums = ids.map(Number)
    expect(nums[0]).toBeLessThan(nums[1])
    expect(nums[1]).toBeLessThan(nums[2])
  })
})

describe('sanitizeHttpOrHttpsUrl', () => {
  it('accepts http and https URLs and returns normalized href', () => {
    expect(sanitizeHttpOrHttpsUrl('http://127.0.0.1:8080/dashboard')).toBe(
      'http://127.0.0.1:8080/dashboard'
    )
    expect(sanitizeHttpOrHttpsUrl('https://example.com/path')).toBe('https://example.com/path')
  })

  it('returns null for empty, whitespace-only, or undefined', () => {
    expect(sanitizeHttpOrHttpsUrl(undefined)).toBeNull()
    expect(sanitizeHttpOrHttpsUrl('')).toBeNull()
    expect(sanitizeHttpOrHttpsUrl('   ')).toBeNull()
  })

  it('returns null for non-http(s) protocols', () => {
    expect(sanitizeHttpOrHttpsUrl('file:///etc/passwd')).toBeNull()
    expect(sanitizeHttpOrHttpsUrl('ms-msdt:foo')).toBeNull()
    expect(sanitizeHttpOrHttpsUrl('custom:host')).toBeNull()
  })

  it('returns null for malformed strings', () => {
    expect(sanitizeHttpOrHttpsUrl('not a url')).toBeNull()
  })
})

describe('openExternalHttpUrl', () => {
  it('throws Invalid URL for empty input', async () => {
    await expect(openExternalHttpUrl('')).rejects.toThrow('Invalid URL')
  })

  it('throws Only http(s) URLs are allowed for disallowed protocols', async () => {
    await expect(openExternalHttpUrl('file:///tmp/x')).rejects.toThrow('Only http(s) URLs are allowed')
  })

  it('calls shell.openExternal with sanitized href for https URL', async () => {
    vi.mocked(shell.openExternal).mockClear()
    await openExternalHttpUrl('https://example.com')
    expect(shell.openExternal).toHaveBeenCalledTimes(1)
    expect(shell.openExternal).toHaveBeenCalledWith('https://example.com/')
  })
})

describe('registerIpcHandlers', () => {
  beforeEach(async () => {
    vi.clearAllMocks()
    scanModulesMock.mockResolvedValue([])
    moduleProcessManagerStartMock.mockResolvedValue({ ok: true })
    detectEditorsMock.mockResolvedValue([
      { id: 'cursor', labelKey: 'editors.cursor', iconKey: 'editor-generic' },
      { id: 'explorer', labelKey: 'editors.explorer', iconKey: 'explorer' }
    ])
    launchEditorMock.mockResolvedValue(undefined)
  })

  it('registers editors:list and returns detected editor entries', async () => {
    const handlers = new Map<string, (...args: unknown[]) => unknown>()
    vi.mocked(ipcMain.handle).mockImplementation((channel, handler) => {
      handlers.set(channel, handler as (...args: unknown[]) => unknown)
    })

    registerIpcHandlers(null, () => null, '/workspace', {
      onSwitchWorkspace: vi.fn().mockResolvedValue(undefined),
      onClearWorkspaceSelection: vi.fn().mockResolvedValue(undefined),
      onRunWorkspaceSetup: vi.fn().mockResolvedValue(undefined),
      onListSetupModels: vi.fn().mockResolvedValue({ kind: 'unsupported' }),
      onOpenNewWindow: vi.fn(),
      onRestartManagedAppServer: vi.fn().mockResolvedValue(undefined),
      onRestartManagedProxy: vi.fn().mockResolvedValue(undefined),
      getProxyStatus: vi.fn(() => ({ status: 'stopped' })),
      startProxyOAuth: vi.fn().mockResolvedValue({ url: 'http://127.0.0.1/oauth', state: 's1' }),
      getProxyOAuthStatus: vi.fn().mockResolvedValue({ status: 'wait' }),
      getProxyAuthFiles: vi.fn().mockResolvedValue([]),
      getProxyUsageSummary: vi.fn().mockResolvedValue({
        totalRequests: 0,
        successCount: 0,
        failureCount: 0,
        totalTokens: 0,
        failedRequests: 0
      }),
      getSettings: vi.fn(() => ({})),
      updateSettings: vi.fn(),
      getRecentWorkspaces: vi.fn(() => []),
      getConnectionStatus: vi.fn(() => ({ status: 'disconnected' })),
      getWorkspaceStatus: vi.fn(() => ({ status: 'no-workspace', workspacePath: '', hasUserConfig: false }))
    })

    const result = await handlers.get('editors:list')?.({})
    expect(detectEditorsMock).toHaveBeenCalledOnce()
    expect(result).toEqual([
      { id: 'cursor', labelKey: 'editors.cursor', iconKey: 'editor-generic' },
      { id: 'explorer', labelKey: 'editors.explorer', iconKey: 'explorer' }
    ])
  })

  it('registers editors:launch and validates workspace path before launch', async () => {
    const handlers = new Map<string, (...args: unknown[]) => unknown>()
    vi.mocked(ipcMain.handle).mockImplementation((channel, handler) => {
      handlers.set(channel, handler as (...args: unknown[]) => unknown)
    })

    registerIpcHandlers(null, () => null, '/workspace', {
      onSwitchWorkspace: vi.fn().mockResolvedValue(undefined),
      onClearWorkspaceSelection: vi.fn().mockResolvedValue(undefined),
      onRunWorkspaceSetup: vi.fn().mockResolvedValue(undefined),
      onListSetupModels: vi.fn().mockResolvedValue({ kind: 'unsupported' }),
      onOpenNewWindow: vi.fn(),
      onRestartManagedAppServer: vi.fn().mockResolvedValue(undefined),
      onRestartManagedProxy: vi.fn().mockResolvedValue(undefined),
      getProxyStatus: vi.fn(() => ({ status: 'stopped' })),
      startProxyOAuth: vi.fn().mockResolvedValue({ url: 'http://127.0.0.1/oauth', state: 's1' }),
      getProxyOAuthStatus: vi.fn().mockResolvedValue({ status: 'wait' }),
      getProxyAuthFiles: vi.fn().mockResolvedValue([]),
      getProxyUsageSummary: vi.fn().mockResolvedValue({
        totalRequests: 0,
        successCount: 0,
        failureCount: 0,
        totalTokens: 0,
        failedRequests: 0
      }),
      getSettings: vi.fn(() => ({ locale: 'en' })),
      updateSettings: vi.fn(),
      getRecentWorkspaces: vi.fn(() => []),
      getConnectionStatus: vi.fn(() => ({ status: 'disconnected' })),
      getWorkspaceStatus: vi.fn(() => ({ status: 'no-workspace', workspacePath: '', hasUserConfig: false }))
    })

    await handlers.get('editors:launch')?.({}, 'cursor', '/workspace')
    expect(launchEditorMock).toHaveBeenCalledWith('cursor', 'F:\\workspace')

    await expect(
      handlers.get('editors:launch')?.({}, 'cursor', '/outside')
    ).rejects.toThrow()
  })

  it('registers appserver:restart-managed and forwards to callback', async () => {
    const handlers = new Map<string, (...args: unknown[]) => unknown>()
    vi.mocked(ipcMain.handle).mockImplementation((channel, handler) => {
      handlers.set(channel, handler as (...args: unknown[]) => unknown)
    })

    const onRestartManagedAppServer = vi.fn().mockResolvedValue(undefined)
    const onListSetupModels = vi.fn().mockResolvedValue({ kind: 'unsupported' })

    registerIpcHandlers(null, () => null, '/workspace', {
      onSwitchWorkspace: vi.fn().mockResolvedValue(undefined),
      onClearWorkspaceSelection: vi.fn().mockResolvedValue(undefined),
      onRunWorkspaceSetup: vi.fn().mockResolvedValue(undefined),
      onListSetupModels,
      onOpenNewWindow: vi.fn(),
      onRestartManagedAppServer,
      onRestartManagedProxy: vi.fn().mockResolvedValue(undefined),
      getProxyStatus: vi.fn(() => ({ status: 'stopped' })),
      startProxyOAuth: vi.fn().mockResolvedValue({ url: 'http://127.0.0.1/oauth', state: 's1' }),
      getProxyOAuthStatus: vi.fn().mockResolvedValue({ status: 'wait' }),
      getProxyAuthFiles: vi.fn().mockResolvedValue([]),
      getProxyUsageSummary: vi.fn().mockResolvedValue({
        totalRequests: 0,
        successCount: 0,
        failureCount: 0,
        totalTokens: 0,
        failedRequests: 0
      }),
      getSettings: vi.fn(() => ({})),
      updateSettings: vi.fn(),
      getRecentWorkspaces: vi.fn(() => []),
      getConnectionStatus: vi.fn(() => ({ status: 'disconnected' })),
      getWorkspaceStatus: vi.fn(() => ({ status: 'no-workspace', workspacePath: '', hasUserConfig: false }))
    })

    expect(handlers.has('appserver:restart-managed')).toBe(true)
    await handlers.get('appserver:restart-managed')?.({})
    expect(onRestartManagedAppServer).toHaveBeenCalledOnce()
  })

  it('registers workspace:list-setup-models and forwards to callback', async () => {
    const handlers = new Map<string, (...args: unknown[]) => unknown>()
    vi.mocked(ipcMain.handle).mockImplementation((channel, handler) => {
      handlers.set(channel, handler as (...args: unknown[]) => unknown)
    })

    const onListSetupModels = vi.fn().mockResolvedValue({ kind: 'success', models: ['gpt-4.1'] })

    registerIpcHandlers(null, () => null, '/workspace', {
      onSwitchWorkspace: vi.fn().mockResolvedValue(undefined),
      onClearWorkspaceSelection: vi.fn().mockResolvedValue(undefined),
      onRunWorkspaceSetup: vi.fn().mockResolvedValue(undefined),
      onListSetupModels,
      onOpenNewWindow: vi.fn(),
      onRestartManagedAppServer: vi.fn().mockResolvedValue(undefined),
      onRestartManagedProxy: vi.fn().mockResolvedValue(undefined),
      getProxyStatus: vi.fn(() => ({ status: 'stopped' })),
      startProxyOAuth: vi.fn().mockResolvedValue({ url: 'http://127.0.0.1/oauth', state: 's1' }),
      getProxyOAuthStatus: vi.fn().mockResolvedValue({ status: 'wait' }),
      getProxyAuthFiles: vi.fn().mockResolvedValue([]),
      getProxyUsageSummary: vi.fn().mockResolvedValue({
        totalRequests: 0,
        successCount: 0,
        failureCount: 0,
        totalTokens: 0,
        failedRequests: 0
      }),
      getSettings: vi.fn(() => ({})),
      updateSettings: vi.fn(),
      getRecentWorkspaces: vi.fn(() => []),
      getConnectionStatus: vi.fn(() => ({ status: 'disconnected' })),
      getWorkspaceStatus: vi.fn(() => ({ status: 'no-workspace', workspacePath: '', hasUserConfig: false }))
    })

    expect(handlers.has('workspace:list-setup-models')).toBe(true)
    const result = await handlers.get('workspace:list-setup-models')?.({}, {
      endpoint: 'https://example.com/v1',
      apiKey: '',
      preferExistingUserConfig: true
    })
    expect(onListSetupModels).toHaveBeenCalledOnce()
    expect(result).toEqual({ kind: 'success', models: ['gpt-4.1'] })
  })

  it('registers proxy:list-auth-files and forwards to callback', async () => {
    const handlers = new Map<string, (...args: unknown[]) => unknown>()
    vi.mocked(ipcMain.handle).mockImplementation((channel, handler) => {
      handlers.set(channel, handler as (...args: unknown[]) => unknown)
    })

    const getProxyAuthFiles = vi.fn().mockResolvedValue([
      {
        provider: 'codex',
        status: 'ready',
        statusMessage: 'ok',
        disabled: false,
        unavailable: false,
        runtimeOnly: false,
        name: 'codex-user.json'
      }
    ])

    registerIpcHandlers(null, () => null, '/workspace', {
      onSwitchWorkspace: vi.fn().mockResolvedValue(undefined),
      onClearWorkspaceSelection: vi.fn().mockResolvedValue(undefined),
      onRunWorkspaceSetup: vi.fn().mockResolvedValue(undefined),
      onListSetupModels: vi.fn().mockResolvedValue({ kind: 'unsupported' }),
      onOpenNewWindow: vi.fn(),
      onRestartManagedAppServer: vi.fn().mockResolvedValue(undefined),
      onRestartManagedProxy: vi.fn().mockResolvedValue(undefined),
      getProxyStatus: vi.fn(() => ({ status: 'stopped' })),
      startProxyOAuth: vi.fn().mockResolvedValue({ url: 'http://127.0.0.1/oauth', state: 's1' }),
      getProxyOAuthStatus: vi.fn().mockResolvedValue({ status: 'wait' }),
      getProxyAuthFiles,
      getProxyUsageSummary: vi.fn().mockResolvedValue({
        totalRequests: 0,
        successCount: 0,
        failureCount: 0,
        totalTokens: 0,
        failedRequests: 0
      }),
      getSettings: vi.fn(() => ({})),
      updateSettings: vi.fn(),
      getRecentWorkspaces: vi.fn(() => []),
      getConnectionStatus: vi.fn(() => ({ status: 'disconnected' })),
      getWorkspaceStatus: vi.fn(() => ({ status: 'no-workspace', workspacePath: '', hasUserConfig: false }))
    })

    expect(handlers.has('proxy:list-auth-files')).toBe(true)
    const result = await handlers.get('proxy:list-auth-files')?.({})
    expect(getProxyAuthFiles).toHaveBeenCalledOnce()
    expect(result).toEqual([
      {
        provider: 'codex',
        status: 'ready',
        statusMessage: 'ok',
        disabled: false,
        unavailable: false,
        runtimeOnly: false,
        name: 'codex-user.json'
      }
    ])
  })

  it('rethrows invalid JSON from modules:read-config instead of returning an empty object', async () => {
    const handlers = new Map<string, (...args: unknown[]) => unknown>()
    vi.mocked(ipcMain.handle).mockImplementation((channel, handler) => {
      handlers.set(channel, handler as (...args: unknown[]) => unknown)
    })
    vi.mocked(fs.stat).mockResolvedValue({ size: 32 } as Awaited<ReturnType<typeof fs.stat>>)
    vi.mocked(fs.readFile).mockResolvedValue('{invalid-json' as Awaited<ReturnType<typeof fs.readFile>>)

    registerIpcHandlers(null, () => null, '/workspace', {
      onSwitchWorkspace: vi.fn().mockResolvedValue(undefined),
      onClearWorkspaceSelection: vi.fn().mockResolvedValue(undefined),
      onRunWorkspaceSetup: vi.fn().mockResolvedValue(undefined),
      onListSetupModels: vi.fn().mockResolvedValue({ kind: 'unsupported' }),
      onOpenNewWindow: vi.fn(),
      onRestartManagedAppServer: vi.fn().mockResolvedValue(undefined),
      onRestartManagedProxy: vi.fn().mockResolvedValue(undefined),
      getProxyStatus: vi.fn(() => ({ status: 'stopped' })),
      startProxyOAuth: vi.fn().mockResolvedValue({ url: 'http://127.0.0.1/oauth', state: 's1' }),
      getProxyOAuthStatus: vi.fn().mockResolvedValue({ status: 'wait' }),
      getProxyAuthFiles: vi.fn().mockResolvedValue([]),
      getProxyUsageSummary: vi.fn().mockResolvedValue({
        totalRequests: 0,
        successCount: 0,
        failureCount: 0,
        totalTokens: 0,
        failedRequests: 0
      }),
      getSettings: vi.fn(() => ({})),
      updateSettings: vi.fn(),
      getRecentWorkspaces: vi.fn(() => []),
      getConnectionStatus: vi.fn(() => ({ status: 'disconnected' })),
      getWorkspaceStatus: vi.fn(() => ({ status: 'no-workspace', workspacePath: '', hasUserConfig: false }))
    })

    await expect(
      handlers.get('modules:read-config')?.({}, { configFileName: 'module.json' })
    ).rejects.toThrow()
  })

  it('reads BOM-prefixed JSON in modules:read-config', async () => {
    const handlers = new Map<string, (...args: unknown[]) => unknown>()
    vi.mocked(ipcMain.handle).mockImplementation((channel, handler) => {
      handlers.set(channel, handler as (...args: unknown[]) => unknown)
    })
    vi.mocked(fs.stat).mockResolvedValue({ size: 32 } as Awaited<ReturnType<typeof fs.stat>>)
    vi.mocked(fs.readFile).mockResolvedValue('\uFEFF{"Enabled":true}' as Awaited<ReturnType<typeof fs.readFile>>)

    registerIpcHandlers(null, () => null, '/workspace', {
      onSwitchWorkspace: vi.fn().mockResolvedValue(undefined),
      onClearWorkspaceSelection: vi.fn().mockResolvedValue(undefined),
      onRunWorkspaceSetup: vi.fn().mockResolvedValue(undefined),
      onListSetupModels: vi.fn().mockResolvedValue({ kind: 'unsupported' }),
      onOpenNewWindow: vi.fn(),
      onRestartManagedAppServer: vi.fn().mockResolvedValue(undefined),
      onRestartManagedProxy: vi.fn().mockResolvedValue(undefined),
      getProxyStatus: vi.fn(() => ({ status: 'stopped' })),
      startProxyOAuth: vi.fn().mockResolvedValue({ url: 'http://127.0.0.1/oauth', state: 's1' }),
      getProxyOAuthStatus: vi.fn().mockResolvedValue({ status: 'wait' }),
      getProxyAuthFiles: vi.fn().mockResolvedValue([]),
      getProxyUsageSummary: vi.fn().mockResolvedValue({
        totalRequests: 0,
        successCount: 0,
        failureCount: 0,
        totalTokens: 0,
        failedRequests: 0
      }),
      getSettings: vi.fn(() => ({})),
      updateSettings: vi.fn(),
      getRecentWorkspaces: vi.fn(() => []),
      getConnectionStatus: vi.fn(() => ({ status: 'disconnected' })),
      getWorkspaceStatus: vi.fn(() => ({ status: 'no-workspace', workspacePath: '', hasUserConfig: false }))
    })

    await expect(
      handlers.get('modules:read-config')?.({}, { configFileName: 'module.json' })
    ).resolves.toEqual({
      exists: true,
      config: { Enabled: true }
    })
  })

  it('returns an error for invalid JSON in modules:start and does not overwrite the config file', async () => {
    const handlers = new Map<string, (...args: unknown[]) => unknown>()
    vi.mocked(ipcMain.handle).mockImplementation((channel, handler) => {
      handlers.set(channel, handler as (...args: unknown[]) => unknown)
    })
    scanModulesMock.mockResolvedValue([
      {
        moduleId: 'demo-module',
        channelName: 'demo',
        displayName: 'Demo',
        packageName: 'demo-module',
        configFileName: 'module.json',
        supportedTransports: ['stdio'],
        requiresInteractiveSetup: false,
        variant: 'default',
        source: 'user',
        absolutePath: '/workspace/modules/demo',
        configDescriptors: []
      }
    ])
    vi.mocked(fs.readFile).mockResolvedValue('{invalid-json' as Awaited<ReturnType<typeof fs.readFile>>)

    registerIpcHandlers(null, () => null, '/workspace', {
      onSwitchWorkspace: vi.fn().mockResolvedValue(undefined),
      onClearWorkspaceSelection: vi.fn().mockResolvedValue(undefined),
      onRunWorkspaceSetup: vi.fn().mockResolvedValue(undefined),
      onListSetupModels: vi.fn().mockResolvedValue({ kind: 'unsupported' }),
      onOpenNewWindow: vi.fn(),
      onRestartManagedAppServer: vi.fn().mockResolvedValue(undefined),
      onRestartManagedProxy: vi.fn().mockResolvedValue(undefined),
      getProxyStatus: vi.fn(() => ({ status: 'stopped' })),
      startProxyOAuth: vi.fn().mockResolvedValue({ url: 'http://127.0.0.1/oauth', state: 's1' }),
      getProxyOAuthStatus: vi.fn().mockResolvedValue({ status: 'wait' }),
      getProxyAuthFiles: vi.fn().mockResolvedValue([]),
      getProxyUsageSummary: vi.fn().mockResolvedValue({
        totalRequests: 0,
        successCount: 0,
        failureCount: 0,
        totalTokens: 0,
        failedRequests: 0
      }),
      getSettings: vi.fn(() => ({})),
      updateSettings: vi.fn(),
      getRecentWorkspaces: vi.fn(() => []),
      getConnectionStatus: vi.fn(() => ({ status: 'disconnected' })),
      getWorkspaceStatus: vi.fn(() => ({ status: 'no-workspace', workspacePath: '', hasUserConfig: false }))
    })

    await expect(
      handlers.get('modules:start')?.({}, { moduleId: 'demo-module' })
    ).resolves.toMatchObject({ ok: false })
    expect(vi.mocked(fs.writeFile)).not.toHaveBeenCalled()
    expect(moduleProcessManagerStartMock).not.toHaveBeenCalled()
  })

  it('returns an object-type error for non-object JSON in modules:start and does not overwrite the config file', async () => {
    const handlers = new Map<string, (...args: unknown[]) => unknown>()
    vi.mocked(ipcMain.handle).mockImplementation((channel, handler) => {
      handlers.set(channel, handler as (...args: unknown[]) => unknown)
    })
    scanModulesMock.mockResolvedValue([
      {
        moduleId: 'demo-module',
        channelName: 'demo',
        displayName: 'Demo',
        packageName: 'demo-module',
        configFileName: 'module.json',
        supportedTransports: ['stdio'],
        requiresInteractiveSetup: false,
        variant: 'default',
        source: 'user',
        absolutePath: '/workspace/modules/demo',
        configDescriptors: []
      }
    ])
    vi.mocked(fs.readFile).mockResolvedValue('["not-an-object"]' as Awaited<ReturnType<typeof fs.readFile>>)

    registerIpcHandlers(null, () => null, '/workspace', {
      onSwitchWorkspace: vi.fn().mockResolvedValue(undefined),
      onClearWorkspaceSelection: vi.fn().mockResolvedValue(undefined),
      onRunWorkspaceSetup: vi.fn().mockResolvedValue(undefined),
      onListSetupModels: vi.fn().mockResolvedValue({ kind: 'unsupported' }),
      onOpenNewWindow: vi.fn(),
      onRestartManagedAppServer: vi.fn().mockResolvedValue(undefined),
      onRestartManagedProxy: vi.fn().mockResolvedValue(undefined),
      getProxyStatus: vi.fn(() => ({ status: 'stopped' })),
      startProxyOAuth: vi.fn().mockResolvedValue({ url: 'http://127.0.0.1/oauth', state: 's1' }),
      getProxyOAuthStatus: vi.fn().mockResolvedValue({ status: 'wait' }),
      getProxyAuthFiles: vi.fn().mockResolvedValue([]),
      getProxyUsageSummary: vi.fn().mockResolvedValue({
        totalRequests: 0,
        successCount: 0,
        failureCount: 0,
        totalTokens: 0,
        failedRequests: 0
      }),
      getSettings: vi.fn(() => ({})),
      updateSettings: vi.fn(),
      getRecentWorkspaces: vi.fn(() => []),
      getConnectionStatus: vi.fn(() => ({ status: 'disconnected' })),
      getWorkspaceStatus: vi.fn(() => ({ status: 'no-workspace', workspacePath: '', hasUserConfig: false }))
    })

    await expect(
      handlers.get('modules:start')?.({}, { moduleId: 'demo-module' })
    ).resolves.toEqual({ ok: false, error: 'Config payload must be a JSON object' })
    expect(vi.mocked(fs.writeFile)).not.toHaveBeenCalled()
    expect(moduleProcessManagerStartMock).not.toHaveBeenCalled()
  })

  it('awaits async updateSettings in settings:set handler', async () => {
    const handlers = new Map<string, (...args: unknown[]) => unknown>()
    vi.mocked(ipcMain.handle).mockImplementation((channel, handler) => {
      handlers.set(channel, handler as (...args: unknown[]) => unknown)
    })

    let resolveUpdate: (() => void) | null = null
    const updateSettings = vi.fn(
      () =>
        new Promise<void>((resolve) => {
          resolveUpdate = resolve
        })
    )

    registerIpcHandlers(null, () => null, '/workspace', {
      onSwitchWorkspace: vi.fn().mockResolvedValue(undefined),
      onClearWorkspaceSelection: vi.fn().mockResolvedValue(undefined),
      onRunWorkspaceSetup: vi.fn().mockResolvedValue(undefined),
      onListSetupModels: vi.fn().mockResolvedValue({ kind: 'unsupported' }),
      onOpenNewWindow: vi.fn(),
      onRestartManagedAppServer: vi.fn().mockResolvedValue(undefined),
      onRestartManagedProxy: vi.fn().mockResolvedValue(undefined),
      getProxyStatus: vi.fn(() => ({ status: 'stopped' })),
      startProxyOAuth: vi.fn().mockResolvedValue({ url: 'http://127.0.0.1/oauth', state: 's1' }),
      getProxyOAuthStatus: vi.fn().mockResolvedValue({ status: 'wait' }),
      getProxyAuthFiles: vi.fn().mockResolvedValue([]),
      getProxyUsageSummary: vi.fn().mockResolvedValue({
        totalRequests: 0,
        successCount: 0,
        failureCount: 0,
        totalTokens: 0,
        failedRequests: 0
      }),
      getSettings: vi.fn(() => ({})),
      updateSettings,
      getRecentWorkspaces: vi.fn(() => []),
      getConnectionStatus: vi.fn(() => ({ status: 'disconnected' })),
      getWorkspaceStatus: vi.fn(() => ({ status: 'no-workspace', workspacePath: '', hasUserConfig: false }))
    })

    const settingsSet = handlers.get('settings:set')
    expect(settingsSet).toBeDefined()

    let settled = false
    const pending = Promise.resolve(settingsSet?.({}, { proxy: { enabled: false } })).then(() => {
      settled = true
    })

    await Promise.resolve()
    expect(updateSettings).toHaveBeenCalledOnce()
    expect(settled).toBe(false)

    resolveUpdate?.()
    await pending
    expect(settled).toBe(true)
  })
})

// ---------------------------------------------------------------------------
// WireProtocolClient — bidirectional request routing
// (covered in WireProtocolClient.test.ts, but verified here as integration)
// ---------------------------------------------------------------------------

import { Readable, Writable, PassThrough } from 'stream'
import { WireProtocolClient } from '../WireProtocolClient'

describe('WireProtocolClient bidirectional routing', () => {
  it('server request handler result is sent back as JSON-RPC response with original id', async () => {
    const toServer = new PassThrough()
    const fromServer = new PassThrough()
    const client = new WireProtocolClient(
      fromServer as unknown as Readable,
      toServer as unknown as Writable
    )

    const responseLines: string[] = []
    toServer.on('data', (chunk: Buffer) => {
      chunk.toString('utf8').split('\n').filter(Boolean).forEach((l) => responseLines.push(l))
    })

    // Register a handler that simulates the approval bridge: returns the decision
    client.onServerRequest(async (_method, params) => {
      const p = params as Record<string, unknown>
      return { decision: p.defaultDecision ?? 'accept' }
    })

    // AppServer sends a server-initiated request
    fromServer.push(
      JSON.stringify({
        jsonrpc: '2.0',
        id: 42,
        method: 'item/approval/request',
        params: { approvalType: 'shell', operation: 'rm -rf /tmp', defaultDecision: 'decline' }
      }) + '\n'
    )

    await new Promise((r) => setTimeout(r, 20))

    // Filter out any initialize or other requests from the response lines
    const approvalResponse = responseLines
      .map((l) => JSON.parse(l))
      .find((m) => m.id === 42 && 'result' in m)

    expect(approvalResponse).toBeDefined()
    expect(approvalResponse).toMatchObject({
      jsonrpc: '2.0',
      id: 42,
      result: { decision: 'decline' }
    })

    client.dispose()
    toServer.destroy()
    fromServer.destroy()
  })

  it('sends an error response when handler throws', async () => {
    const toServer = new PassThrough()
    const fromServer = new PassThrough()
    const client = new WireProtocolClient(
      fromServer as unknown as Readable,
      toServer as unknown as Writable
    )

    const responseLines: string[] = []
    toServer.on('data', (chunk: Buffer) => {
      chunk.toString('utf8').split('\n').filter(Boolean).forEach((l) => responseLines.push(l))
    })

    client.onServerRequest(async () => {
      throw new Error('Bridge unavailable')
    })

    fromServer.push(
      JSON.stringify({ jsonrpc: '2.0', id: 77, method: 'item/approval/request', params: {} }) + '\n'
    )

    await new Promise((r) => setTimeout(r, 20))

    const errorResponse = responseLines
      .map((l) => JSON.parse(l))
      .find((m) => m.id === 77 && 'error' in m)

    expect(errorResponse).toBeDefined()
    expect(errorResponse.error.code).toBe(-32603)

    client.dispose()
    toServer.destroy()
    fromServer.destroy()
  })
})
