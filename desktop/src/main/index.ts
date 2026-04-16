import { app, BrowserWindow, session, Menu, ipcMain, shell, nativeImage } from 'electron'
import type { MenuItemConstructorOptions } from 'electron'
import { join, basename } from 'path'
import { existsSync } from 'fs'
import { promises as fs } from 'fs'
import { spawn } from 'child_process'
import { AppServerManager } from './AppServerManager'
import { ProxyProcessManager, type ProxyBinarySource } from './ProxyProcessManager'
import { WireProtocolClient, type InitializeResult } from './WireProtocolClient'
import {
  registerIpcHandlers,
  unregisterIpcHandlers,
  getModuleProcessManager,
  autoStartModuleProcessesByChannelName,
  refreshNodeRuntimeStatus,
  broadcastConnectionStatus,
  broadcastWorkspaceStatus,
  broadcastNotification,
  broadcastServerRequest,
  createServerRequestBridge,
  sanitizeHttpOrHttpsUrl,
  openExternalHttpUrl,
  type ConnectionStatusPayload,
  type ProxyStatusPayload,
  type IpcHandlerCallbacks
} from './ipcBridge'
import {
  loadSettings,
  saveSettings,
  addRecentWorkspace,
  getRecentWorkspaces,
  type AppSettings,
  type BinarySource,
  type ConnectionMode,
  type ProxyOAuthProvider,
  type ProxySettings
} from './settings'
import { acquireWorkspaceLock, releaseWorkspaceLock } from './workspaceLock'
import {
  getWorkspaceStatus,
  runWorkspaceSetup,
  listSetupModels,
  type WorkspaceStatusPayload,
  type WorkspaceSetupRequest,
  type WorkspaceSetupModelListRequest
} from './workspaceSetup'
import {
  TITLE_BAR_OVERLAY_BY_THEME,
  TITLE_BAR_OVERLAY_HEIGHT
} from '../shared/titleBarOverlay'
import { WORKSPACE_LOCKED_IPC_PREFIX } from '../shared/workspaceSwitchErrors'
import {
  normalizeLocale,
  translate,
  type AppLocale,
  type TopLevelMenuId
} from '../shared/locales'
import {
  writeProxyConfig,
  createLocalSecret,
  buildLocalProxyEndpoint,
  buildLocalProxyManagementBaseUrl,
  buildManagementHeaders,
  buildProxyOAuthPath
} from './proxyConfig'

// ─── Single-process state ─────────────────────────────────────────────────────
// Each Electron process owns exactly one window and one AppServer connection.
// "New Window" spawns a separate OS process instead of creating another
// BrowserWindow, avoiding the global-IPC-handler conflict that the previous
// multi-window-in-one-process design had.

let mainWindow: BrowserWindow | null = null
let appServerManager: AppServerManager | null = null
let proxyManager: ProxyProcessManager | null = null
let wireClient: WireProtocolClient | null = null
let currentWorkspacePath = ''
let crashRetries = 0
/** Last DashBoard URL from a successful initialize (for View menu). */
let lastDashboardUrl: string | null = null
let lastConnectionStatus: ConnectionStatusPayload = { status: 'disconnected' }
let lastWorkspaceStatus: WorkspaceStatusPayload = {
  status: 'no-workspace',
  workspacePath: '',
  hasUserConfig: false
}
let crashRetryTimer: ReturnType<typeof setTimeout> | null = null
let isAppQuitting = false
let ipcHandlersRegistered = false
let finalQuitCleanupDone = false
let proxyStatus: ProxyStatusPayload = { status: 'stopped' }

/** PNG shipped via `build.extraResources` (prod) or repo `resources/` (dev). macOS uses bundle icon. */
function resolveWindowIconPath(): string | null {
  if (process.platform === 'darwin') {
    return null
  }
  const packaged = join(process.resourcesPath, 'icon.png')
  const dev = join(__dirname, '../../resources/icon.png')
  const path = app.isPackaged ? packaged : dev
  return existsSync(path) ? path : null
}

// ─── Shared (mutable) settings ────────────────────────────────────────────────

let sharedSettings: AppSettings = {}
const DEFAULT_WS_HOST = '127.0.0.1'
const DEFAULT_WS_PORT = 9100
const DEFAULT_PROXY_HOST = '127.0.0.1'
const DEFAULT_PROXY_PORT = 8317
const WINDOW_SHOW_FALLBACK_MS = 3000

// ─── Workspace resolution ─────────────────────────────────────────────────────

function resolveWorkspacePath(settings: AppSettings): string | null {
  const argIdx = process.argv.indexOf('--workspace')
  if (argIdx !== -1 && process.argv[argIdx + 1]) {
    return process.argv[argIdx + 1]
  }

  if (settings.lastWorkspacePath && existsSync(settings.lastWorkspacePath)) {
    return settings.lastWorkspacePath
  }

  return null
}

function resolveConnectionMode(settings: AppSettings): ConnectionMode {
  const mode = settings.connectionMode
  if (
    mode === 'stdio' ||
    mode === 'websocket' ||
    mode === 'stdioAndWebSocket' ||
    mode === 'remote'
  ) {
    return mode
  }
  return 'stdio'
}

function resolveBinarySource(settings: AppSettings): BinarySource {
  const source = settings.binarySource
  if (source === 'bundled' || source === 'path' || source === 'custom') {
    return source
  }
  return settings.appServerBinaryPath?.trim() ? 'custom' : 'bundled'
}

function resolveWebSocketHostPort(settings: AppSettings): { host: string; port: number } {
  const host = settings.webSocket?.host?.trim() || DEFAULT_WS_HOST
  const candidatePort = settings.webSocket?.port
  const port =
    typeof candidatePort === 'number' && Number.isInteger(candidatePort) && candidatePort > 0 && candidatePort <= 65535
      ? candidatePort
      : DEFAULT_WS_PORT
  return { host, port }
}

function buildManagedWsUrl(settings: AppSettings): string {
  const { host, port } = resolveWebSocketHostPort(settings)
  return `ws://${host}:${port}/ws`
}

function buildManagedListenUrl(settings: AppSettings, mode: ConnectionMode): string | undefined {
  const { host, port } = resolveWebSocketHostPort(settings)
  if (mode === 'websocket') return `ws://${host}:${port}`
  if (mode === 'stdioAndWebSocket') return `ws+stdio://${host}:${port}`
  return undefined
}

function appendTokenToWsUrlIfMissing(urlRaw: string, token: string | undefined): string {
  const trimmed = urlRaw.trim()
  if (!trimmed) return trimmed
  let parsed: URL
  try {
    parsed = new URL(trimmed)
  } catch {
    return trimmed
  }
  if ((parsed.protocol !== 'ws:' && parsed.protocol !== 'wss:') || !token?.trim()) return parsed.toString()
  if (!parsed.searchParams.get('token')) {
    parsed.searchParams.set('token', token.trim())
  }
  return parsed.toString()
}

function resolveRemoteWsUrl(settings: AppSettings): string | null {
  const raw = settings.remote?.url?.trim()
  if (!raw) return null
  let parsed: URL
  try {
    parsed = new URL(raw)
  } catch {
    return null
  }
  if (parsed.protocol !== 'ws:' && parsed.protocol !== 'wss:') {
    return null
  }
  return appendTokenToWsUrlIfMissing(parsed.toString(), settings.remote?.token)
}

function resolveProxySettings(settings: AppSettings): Required<Pick<ProxySettings, 'enabled' | 'host' | 'port' | 'binarySource'>> &
  Pick<ProxySettings, 'binaryPath' | 'authDir' | 'apiKey' | 'managementKey'> {
  const raw = settings.proxy ?? {}
  const host = raw.host?.trim() || DEFAULT_PROXY_HOST
  const candidatePort = raw.port
  const port =
    typeof candidatePort === 'number' && Number.isInteger(candidatePort) && candidatePort > 0 && candidatePort <= 65535
      ? candidatePort
      : DEFAULT_PROXY_PORT
  const enabled = raw.enabled === true
  const binarySource: ProxyBinarySource =
    raw.binarySource === 'bundled' || raw.binarySource === 'path' || raw.binarySource === 'custom'
      ? raw.binarySource
      : raw.binaryPath?.trim()
        ? 'custom'
        : 'bundled'
  return {
    enabled,
    host,
    port,
    binarySource,
    binaryPath: raw.binaryPath?.trim() || undefined,
    authDir: raw.authDir?.trim() || undefined,
    apiKey: raw.apiKey?.trim() || undefined,
    managementKey: raw.managementKey?.trim() || undefined
  }
}

function getProxyConfigPath(): string {
  return join(app.getPath('userData'), 'proxy', 'config.yaml')
}

function getDefaultProxyAuthDir(): string {
  return join(app.getPath('userData'), 'proxy', 'auths')
}

function resolveProxyRuntimeSettings(settings: AppSettings): {
  host: string
  port: number
  binarySource: ProxyBinarySource
  binaryPath?: string
  authDir: string
  apiKey: string
  managementKey: string
  configPath: string
} {
  const proxy = resolveProxySettings(settings)
  const apiKey = proxy.apiKey || createLocalSecret('dotcraft_proxy_api')
  const managementKey = proxy.managementKey || createLocalSecret('dotcraft_proxy_mgmt')
  const authDir = proxy.authDir || getDefaultProxyAuthDir()
  if (!settings.proxy) settings.proxy = {}
  settings.proxy.apiKey = apiKey
  settings.proxy.managementKey = managementKey
  settings.proxy.authDir = authDir
  settings.proxy.port = proxy.port
  settings.proxy.host = proxy.host
  settings.proxy.binarySource = proxy.binarySource
  settings.proxy.binaryPath = proxy.binaryPath
  return {
    host: proxy.host,
    port: proxy.port,
    binarySource: proxy.binarySource,
    binaryPath: proxy.binaryPath,
    authDir,
    apiKey,
    managementKey,
    configPath: getProxyConfigPath()
  }
}

async function writeWorkspaceProxyOverrides(workspacePath: string, port: number, apiKey: string): Promise<void> {
  const craftDir = join(workspacePath, '.craft')
  const configPath = join(craftDir, 'config.json')
  let current: Record<string, unknown> = {}
  try {
    const raw = await fs.readFile(configPath, 'utf8')
    const parsed = JSON.parse(raw) as unknown
    if (parsed && typeof parsed === 'object' && !Array.isArray(parsed)) {
      current = parsed as Record<string, unknown>
    }
  } catch (error) {
    const code = (error as NodeJS.ErrnoException | undefined)?.code
    if (code !== 'ENOENT') {
      throw error
    }
  }
  current.EndPoint = buildLocalProxyEndpoint(port)
  current.ApiKey = apiKey
  await fs.mkdir(craftDir, { recursive: true })
  await fs.writeFile(configPath, `${JSON.stringify(current, null, 2)}\n`, 'utf8')
}

async function waitForProxyReady(port: number, apiKey: string, timeoutMs = 15_000): Promise<void> {
  const started = Date.now()
  const modelsUrl = `${buildLocalProxyEndpoint(port)}/models`
  while (Date.now() - started < timeoutMs) {
    try {
      const res = await fetch(modelsUrl, {
        headers: {
          Authorization: `Bearer ${apiKey}`
        }
      })
      if (res.ok) return
    } catch {
      // Keep polling until timeout.
    }
    await new Promise((resolve) => setTimeout(resolve, 300))
  }
  throw new Error(`CLIProxyAPI did not become ready in ${timeoutMs}ms`)
}

async function fetchProxyManagementJson<T>(settings: AppSettings, path: string): Promise<T> {
  const runtime = resolveProxyRuntimeSettings(settings)
  const url = `${buildLocalProxyManagementBaseUrl(runtime.port)}${path}`
  const res = await fetch(url, {
    headers: buildManagementHeaders(runtime.managementKey)
  })
  if (!res.ok) {
    const body = await res.text().catch(() => '')
    throw new Error(`CLIProxyAPI management API failed (${res.status}): ${body || res.statusText}`)
  }
  return (await res.json()) as T
}

async function ensureProxyRunningForWorkspace(workspacePath: string): Promise<void> {
  const proxy = resolveProxySettings(sharedSettings)
  if (!proxy.enabled) {
    proxyManager?.shutdown()
    proxyManager = null
    proxyStatus = { status: 'stopped' }
    return
  }

  const runtime = resolveProxyRuntimeSettings(sharedSettings)
  saveSettings(sharedSettings)
  writeProxyConfig(runtime.configPath, {
    host: runtime.host,
    port: runtime.port,
    authDir: runtime.authDir,
    apiKey: runtime.apiKey,
    managementKey: runtime.managementKey
  })
  await writeWorkspaceProxyOverrides(workspacePath, runtime.port, runtime.apiKey)

  if (proxyManager?.isRunning) {
    proxyStatus = {
      status: 'running',
      pid: proxyManager.pid ?? undefined,
      port: runtime.port,
      baseUrl: buildLocalProxyEndpoint(runtime.port),
      managementUrl: buildLocalProxyManagementBaseUrl(runtime.port)
    }
    return
  }

  const manager = new ProxyProcessManager({
    workspacePath,
    configPath: runtime.configPath,
    binarySource: runtime.binarySource,
    binaryPath: runtime.binaryPath
  })
  proxyManager = manager
  proxyStatus = { status: 'starting', port: runtime.port }

  manager.on('error', (err: Error) => {
    proxyStatus = { status: 'error', errorMessage: err.message, port: runtime.port }
  })
  manager.on('crash', () => {
    proxyStatus = {
      status: 'error',
      errorMessage: 'CLIProxyAPI process crashed unexpectedly',
      port: runtime.port
    }
  })
  manager.on('stopped', () => {
    proxyStatus = { status: 'stopped', port: runtime.port }
  })

  manager.spawn()
  await new Promise((resolve) => setTimeout(resolve, 0))
  if (proxyStatus.status === 'error') {
    throw new Error(proxyStatus.errorMessage || 'CLIProxyAPI failed to start')
  }
  await waitForProxyReady(runtime.port, runtime.apiKey)
  proxyStatus = {
    status: 'running',
    pid: manager.pid ?? undefined,
    port: runtime.port,
    baseUrl: buildLocalProxyEndpoint(runtime.port),
    managementUrl: buildLocalProxyManagementBaseUrl(runtime.port)
  }
}

async function waitForReadyz(host: string, port: number, timeoutMs = 15_000): Promise<void> {
  const base = `http://${host}:${port}`
  const started = Date.now()
  while (Date.now() - started < timeoutMs) {
    try {
      const res = await fetch(`${base}/readyz`)
      if (res.ok) return
    } catch {
      // Keep polling until timeout.
    }
    await new Promise((resolve) => setTimeout(resolve, 250))
  }
  throw new Error(`AppServer WebSocket endpoint did not become ready in ${timeoutMs}ms`)
}

function clearCrashRetryTimer(): boolean {
  if (crashRetryTimer) {
    clearTimeout(crashRetryTimer)
    crashRetryTimer = null
    return true
  }
  return false
}

function releaseCurrentWorkspaceLock(): void {
  if (!currentWorkspacePath) return
  releaseWorkspaceLock(currentWorkspacePath)
  currentWorkspacePath = ''
}

function registerDesktopIpcHandlers(
  workspacePath: string,
  getWireClient: () => WireProtocolClient | null
): void {
  if (ipcHandlersRegistered) {
    unregisterIpcHandlers()
    ipcHandlersRegistered = false
  }
  try {
    registerIpcHandlers(null, getWireClient, workspacePath, buildCallbacks())
    ipcHandlersRegistered = true
  } catch (err) {
    ipcHandlersRegistered = false
    console.error('[desktop] failed to register IPC handlers', err)
    throw err
  }
}

function unregisterDesktopIpcHandlers(): boolean {
  if (!ipcHandlersRegistered) {
    return false
  }
  unregisterIpcHandlers()
  ipcHandlersRegistered = false
  return true
}

async function autoStartEnabledModules(): Promise<void> {
  const client = wireClient
  if (!client) return
  try {
    const response = await client.sendRequest<{ channels?: Array<{ name?: string; enabled?: boolean; transport?: string | null }> }>(
      'externalChannel/list',
      {}
    )
    const enabledChannelNames = (response.channels ?? [])
      .filter((channel) => channel.enabled === true && channel.transport === 'websocket')
      .map((channel) => channel.name?.trim() ?? '')
      .filter(Boolean)
    await autoStartModuleProcessesByChannelName(enabledChannelNames)
  } catch (error) {
    console.warn('[desktop] failed to auto-start persisted modules', error)
  }
}

function teardownRuntime(
  reason: string,
  options?: {
    releaseWorkspaceLock?: boolean
    clearMainWindow?: boolean
    cleanupIpcHandlers?: boolean
  }
): void {
  const moduleManager = getModuleProcessManager()
  const clearedCrashRetry = clearCrashRetryTimer()
  const cleanedIpc = options?.cleanupIpcHandlers
    ? unregisterDesktopIpcHandlers()
    : false
  const hadAppServer = appServerManager !== null
  const hadProxy = proxyManager !== null
  const hadWireClient = wireClient !== null
  if (moduleManager) {
    void moduleManager.stopAll({ preserveExternalChannels: true }).catch((error) => {
      console.warn('[desktop] failed to stop channel modules during teardown', error)
    })
  }
  appServerManager?.shutdown()
  proxyManager?.shutdown()
  wireClient?.dispose()
  appServerManager = null
  proxyManager = null
  wireClient = null
  proxyStatus = { status: 'stopped' }
  let releasedWorkspaceLock = false
  if (options?.releaseWorkspaceLock) {
    releasedWorkspaceLock = currentWorkspacePath !== ''
    releaseCurrentWorkspaceLock()
  }
  let clearedMainWindow = false
  if (options?.clearMainWindow) {
    clearedMainWindow = mainWindow !== null
    mainWindow = null
  }
  const changed =
    clearedCrashRetry ||
    cleanedIpc ||
    hadAppServer ||
    hadProxy ||
    hadWireClient ||
    releasedWorkspaceLock ||
    clearedMainWindow
  if (changed) {
    console.info(`[desktop] teardown runtime: ${reason}`)
  }
}

function showWindowSafely(win: BrowserWindow): void {
  if (win.isDestroyed()) return
  if (win.isMinimized()) {
    win.restore()
  }
  if (!win.isVisible()) {
    win.show()
  }
  win.focus()
}

// ─── Window creation ──────────────────────────────────────────────────────────

function createWindow(workspacePath: string | null): BrowserWindow {
  const isMac = process.platform === 'darwin'
  const isDev = import.meta.env.DEV
  const iconPath = resolveWindowIconPath()
  const win = new BrowserWindow({
    width: 1400,
    height: 800,
    minWidth: 900,
    minHeight: 600,
    backgroundColor: '#1a1a1a',
    ...(iconPath
      ? {
          icon: nativeImage.createFromPath(iconPath)
        }
      : {}),
    show: isDev,
    titleBarStyle: isMac ? 'hiddenInset' : 'hidden',
    ...(isMac
      ? {}
      : {
          titleBarOverlay: {
            ...TITLE_BAR_OVERLAY_BY_THEME.dark,
            height: TITLE_BAR_OVERLAY_HEIGHT
          }
        }),
    autoHideMenuBar: !isMac,
    webPreferences: {
      preload: join(__dirname, '../preload/index.js'),
      sandbox: false,
      contextIsolation: true,
      nodeIntegration: false
    }
  })

  const workspaceName = workspacePath ? basename(workspacePath) : 'DotCraft'
  const loc = normalizeLocale(sharedSettings.locale)
  win.setTitle(translate(loc, 'app.titleWithWorkspace', { name: workspaceName }))

  let showFallbackTimer: ReturnType<typeof setTimeout> | null = null
  const clearShowFallbackTimer = (): void => {
    if (showFallbackTimer) {
      clearTimeout(showFallbackTimer)
      showFallbackTimer = null
    }
  }

  if (!isDev) {
    win.once('ready-to-show', () => {
      clearShowFallbackTimer()
      showWindowSafely(win)
    })
    showFallbackTimer = setTimeout(() => {
      console.warn('[desktop] ready-to-show timeout; forcing window show fallback')
      showWindowSafely(win)
    }, WINDOW_SHOW_FALLBACK_MS)
  }

  win.webContents.on(
    'did-fail-load',
    (_event, errorCode, errorDescription, validatedURL, isMainFrame) => {
      if (!isMainFrame || errorCode === -3) {
        return
      }
      const message = `Renderer failed to load (${errorCode}): ${errorDescription} (${validatedURL || 'unknown URL'})`
      console.error('[desktop] did-fail-load', message)
      showWindowSafely(win)
      emitConnectionStatus(win, { status: 'error', errorMessage: message })
    }
  )

  win.webContents.on('render-process-gone', (_event, details) => {
    const message = `Renderer process exited (${details.reason})`
    console.error('[desktop] render-process-gone', details)
    showWindowSafely(win)
    emitConnectionStatus(win, { status: 'error', errorMessage: message })
  })

  win.webContents.on('unresponsive', () => {
    console.warn('[desktop] renderer became unresponsive')
    showWindowSafely(win)
  })

  win.on('close', () => {
    teardownRuntime('window close', { releaseWorkspaceLock: true })
  })

  win.on('closed', () => {
    clearShowFallbackTimer()
    mainWindow = null
  })

  return win
}

// ─── Spawn a new process for "New Window" ─────────────────────────────────────
// Always spawns without a --workspace argument so the new process shows the
// welcome screen. This prevents two processes from accidentally opening the
// same workspace simultaneously.

function openNewProcess(): void {
  const filteredArgs = stripWorkspaceArgs(process.argv.slice(1))
  const child = spawn(process.execPath, filteredArgs, {
    detached: true,
    stdio: 'ignore'
  })
  child.unref()
}

/** Remove any existing --workspace <path> pair from argv so the new process can set its own. */
function stripWorkspaceArgs(argv: string[]): string[] {
  const result: string[] = []
  for (let i = 0; i < argv.length; i++) {
    if (argv[i] === '--workspace') {
      i++ // skip the value too
    } else {
      result.push(argv[i])
    }
  }
  return result
}

// ─── WebSocket remote connection ─────────────────────────────────────────────

async function connectViaWebSocket(
  workspacePath: string,
  wsUrl: string
): Promise<void> {
  if (isAppQuitting || !mainWindow || mainWindow.isDestroyed()) {
    return
  }
  const win = mainWindow!
  emitConnectionStatus(win, { status: 'connecting' })
  reregisterIpcForWorkspace(workspacePath)

  const client = WireProtocolClient.fromWebSocket(wsUrl)
  wireClient = client

  client.onNotification((method, params) => {
    if (mainWindow && !mainWindow.isDestroyed()) {
      broadcastNotification(mainWindow, method, params)
    }
  })

  client.onServerRequest(async (method, params) => {
    if (!mainWindow || mainWindow.isDestroyed()) {
      return Promise.reject(
        new Error('Window is not available to handle server request')
      )
    }
    const { bridgeId, promise } = createServerRequestBridge()
    broadcastServerRequest(mainWindow, { bridgeId, method, params })
    return promise
  })
  const emitConnected = (result: InitializeResult): void => {
    if (mainWindow && !mainWindow.isDestroyed()) {
      emitConnectionStatus(mainWindow, {
        status: 'connected',
        serverInfo: result.serverInfo,
        capabilities: result.capabilities as Record<string, unknown>,
        dashboardUrl: result.dashboardUrl
      })
    }
    void autoStartEnabledModules()
  }
  client.on('ready', (result: InitializeResult) => emitConnected(result))
  client.on('reconnected', (result: InitializeResult) => emitConnected(result))
  client.on('close', () => {
    if (mainWindow && !mainWindow.isDestroyed()) {
      const loc = normalizeLocale(sharedSettings.locale)
      emitConnectionStatus(mainWindow, {
        status: 'disconnected',
        errorMessage: translate(loc, 'main.status.reconnecting')
      })
    }
  })
  client.on('reconnect-error', (err) => {
    const message = err instanceof Error ? err.message : String(err)
    if (mainWindow && !mainWindow.isDestroyed()) {
      emitConnectionStatus(mainWindow, { status: 'error', errorMessage: message })
    }
  })
}

// ─── AppServer connection ─────────────────────────────────────────────────────

function buildCallbacks(): IpcHandlerCallbacks {
  return {
    onSwitchWorkspace: async (newPath: string) => {
      addRecentWorkspace(sharedSettings, newPath)
      saveSettings(sharedSettings)
      const workspaceStatus = getWorkspaceStatus(newPath)
      if (workspaceStatus.status === 'needs-setup') {
        await openWorkspaceWithoutConnection(newPath)
      } else {
        await connectToAppServer(newPath)
      }
      if (mainWindow && !mainWindow.isDestroyed()) {
        const loc = normalizeLocale(sharedSettings.locale)
        mainWindow.setTitle(
          translate(loc, 'app.titleWithWorkspace', { name: basename(newPath) })
        )
      }
    },
    onClearWorkspaceSelection: async () => {
      await clearWorkspaceSelection()
    },
    onRunWorkspaceSetup: async (request: WorkspaceSetupRequest) => {
      if (!currentWorkspacePath) {
        throw new Error('Open a workspace before running setup.')
      }
      await runWorkspaceSetup(currentWorkspacePath, request, sharedSettings)
      if (mainWindow && !mainWindow.isDestroyed()) {
        emitWorkspaceStatus(mainWindow, getWorkspaceStatus(currentWorkspacePath))
      }
      await connectToAppServer(currentWorkspacePath)
    },
    onListSetupModels: async (request: WorkspaceSetupModelListRequest) => {
      return listSetupModels(request)
    },
    onOpenNewWindow: () => {
      openNewProcess()
    },
    onRestartManagedAppServer: async () => {
      if (!currentWorkspacePath) {
        throw new Error('Open a workspace before restarting AppServer.')
      }
      if (process.argv.includes('--remote')) {
        throw new Error('Cannot restart AppServer while using a remote WebSocket connection.')
      }
      if (resolveConnectionMode(sharedSettings) === 'remote') {
        throw new Error('Restart is only available for Desktop-managed AppServer subprocesses.')
      }
      await connectToAppServer(currentWorkspacePath)
    },
    onRestartManagedProxy: async () => {
      if (!currentWorkspacePath) {
        throw new Error('Open a workspace before restarting proxy.')
      }
      const proxy = resolveProxySettings(sharedSettings)
      if (!proxy.enabled) {
        throw new Error('Local proxy is disabled in Settings.')
      }
      proxyManager?.shutdown()
      proxyManager = null
      proxyStatus = { status: 'stopped' }
      await ensureProxyRunningForWorkspace(currentWorkspacePath)
    },
    getSettings: () => sharedSettings,
    updateSettings: (partial) => {
      const prevLocale = normalizeLocale(sharedSettings.locale)
      const next: Partial<typeof sharedSettings> = { ...partial }
      if (partial.locale !== undefined) {
        next.locale = normalizeLocale(partial.locale)
      }
      Object.assign(sharedSettings, next)
      saveSettings(sharedSettings)
      if (resolveProxySettings(sharedSettings).enabled !== true) {
        proxyManager?.shutdown()
        proxyManager = null
        proxyStatus = { status: 'stopped' }
      }
      if (partial.locale !== undefined && normalizeLocale(sharedSettings.locale) !== prevLocale) {
        refreshAppMenu()
      }
    },
    getRecentWorkspaces: () => getRecentWorkspaces(sharedSettings),
    getConnectionStatus: () => lastConnectionStatus,
    getWorkspaceStatus: () => getWorkspaceStatus(currentWorkspacePath),
    getProxyStatus: () => proxyStatus,
    startProxyOAuth: async (provider: ProxyOAuthProvider) => {
      const response = await fetchProxyManagementJson<{ url?: string; state?: string; status?: string; error?: string }>(
        sharedSettings,
        buildProxyOAuthPath(provider)
      )
      if (!response.url) {
        throw new Error(response.error || 'OAuth URL was not returned by CLIProxyAPI')
      }
      await openExternalHttpUrl(response.url)
      return { url: response.url, state: response.state }
    },
    getProxyOAuthStatus: async (state: string) => {
      if (!state.trim()) {
        throw new Error('Missing OAuth state')
      }
      return fetchProxyManagementJson<{ status: string; error?: string }>(
        sharedSettings,
        `/get-auth-status?state=${encodeURIComponent(state)}`
      )
    },
    getProxyUsageSummary: async () => {
      const usage = await fetchProxyManagementJson<{
        usage?: {
          total_requests?: number
          success_count?: number
          failure_count?: number
          total_tokens?: number
        }
        failed_requests?: number
      }>(sharedSettings, '/usage')
      return {
        totalRequests: usage.usage?.total_requests ?? 0,
        successCount: usage.usage?.success_count ?? 0,
        failureCount: usage.usage?.failure_count ?? 0,
        totalTokens: usage.usage?.total_tokens ?? 0,
        failedRequests: usage.failed_requests ?? usage.usage?.failure_count ?? 0
      }
    }
  }
}

/** Re-register IPC handlers with the current workspace path (used on workspace switch). */
function reregisterIpcForWorkspace(workspacePath: string): void {
  registerDesktopIpcHandlers(workspacePath, () => wireClient)
}

async function openWorkspaceWithoutConnection(workspacePath: string): Promise<void> {
  if (isAppQuitting) {
    return
  }

  const lockResult = acquireWorkspaceLock(workspacePath)
  if (!lockResult.ok) {
    const loc = normalizeLocale(sharedSettings.locale)
    throw new Error(
      WORKSPACE_LOCKED_IPC_PREFIX +
        translate(loc, 'main.error.workspaceLocked', { pid: lockResult.pid ?? 0 })
    )
  }

  if (currentWorkspacePath && currentWorkspacePath !== workspacePath) {
    releaseWorkspaceLock(currentWorkspacePath)
  }

  teardownRuntime('switch to setup-required workspace')
  currentWorkspacePath = workspacePath
  reregisterIpcForWorkspace(workspacePath)

  const win = mainWindow
  if (!win || win.isDestroyed()) {
    return
  }

  emitWorkspaceStatus(win, getWorkspaceStatus(workspacePath))
  emitConnectionStatus(win, { status: 'disconnected' })
}

async function clearWorkspaceSelection(): Promise<void> {
  if (currentWorkspacePath) {
    teardownRuntime('clear workspace selection', { releaseWorkspaceLock: true })
  }

  currentWorkspacePath = ''
  delete sharedSettings.lastWorkspacePath
  saveSettings(sharedSettings)

  const win = mainWindow
  if (!win || win.isDestroyed()) {
    return
  }

  reregisterIpcForWorkspace('')
  const loc = normalizeLocale(sharedSettings.locale)
  win.setTitle(translate(loc, 'app.brandSubtitle'))
  emitWorkspaceStatus(win, getWorkspaceStatus(''))
  emitConnectionStatus(win, { status: 'disconnected' })
}

async function connectToAppServer(workspacePath: string): Promise<void> {
  if (isAppQuitting) {
    return
  }
  // Acquire the lock BEFORE tearing anything down so a failure leaves the
  // current connection intact and propagates as an exception to the caller
  // (e.g. the renderer's workspace:switch IPC).
  const lockResult = acquireWorkspaceLock(workspacePath)
  if (!lockResult.ok) {
    const loc = normalizeLocale(sharedSettings.locale)
    throw new Error(
      WORKSPACE_LOCKED_IPC_PREFIX +
        translate(loc, 'main.error.workspaceLocked', { pid: lockResult.pid ?? 0 })
    )
  }

  // Release lock on previous workspace after the new lock is secured
  if (currentWorkspacePath && currentWorkspacePath !== workspacePath) {
    releaseWorkspaceLock(currentWorkspacePath)
  }

  // Tear down previous connection
  teardownRuntime('switch/reconnect before new connect')

  currentWorkspacePath = workspacePath
  if (mainWindow && !mainWindow.isDestroyed()) {
    emitWorkspaceStatus(mainWindow, getWorkspaceStatus(workspacePath))
  }

  // --remote ws://host:port/ws?token=xxx  → skip AppServerManager, connect via WebSocket
  const remoteIdx = process.argv.indexOf('--remote')
  if (remoteIdx !== -1 && process.argv[remoteIdx + 1]) {
    await connectViaWebSocket(workspacePath, process.argv[remoteIdx + 1])
    return
  }

  const connectionMode = resolveConnectionMode(sharedSettings)
  if (connectionMode === 'remote') {
    const remoteWsUrl = resolveRemoteWsUrl(sharedSettings)
    if (!remoteWsUrl) {
      const win = mainWindow!
      emitConnectionStatus(win, {
        status: 'error',
        errorMessage: 'Invalid remote WebSocket URL in Settings.'
      })
      return
    }
    await connectViaWebSocket(workspacePath, remoteWsUrl)
    return
  }

  const win = mainWindow!
  try {
    await ensureProxyRunningForWorkspace(workspacePath)
  } catch (err) {
    const message = err instanceof Error ? err.message : String(err)
    console.warn('[proxy] Failed to start API proxy, continuing without it:', message)
    proxyStatus = { status: 'error', errorMessage: message }
  }

  emitConnectionStatus(win, { status: 'connecting' })

  const manager = new AppServerManager({
    workspacePath,
    binarySource: resolveBinarySource(sharedSettings),
    binaryPath: sharedSettings.appServerBinaryPath,
    listenUrl: buildManagedListenUrl(sharedSettings, connectionMode)
  })
  appServerManager = manager

  reregisterIpcForWorkspace(workspacePath)

  manager.on('error', (err: Error) => {
    const isBinaryError =
      err.message.includes('not found') || err.message.includes('ENOENT')
    const payload: ConnectionStatusPayload = {
      status: 'error',
      errorMessage: err.message,
      ...(isBinaryError ? { binarySource: resolveBinarySource(sharedSettings) } : {}),
      ...(isBinaryError ? { errorType: 'binary-not-found' } : {})
    }
    if (mainWindow && !mainWindow.isDestroyed()) {
      emitConnectionStatus(mainWindow, payload as ConnectionStatusPayload)
    }
  })

  manager.on('crash', () => {
    console.error('[desktop] appserver crashed')
    wireClient?.dispose()
    wireClient = null
    if (mainWindow && !mainWindow.isDestroyed()) {
      const loc = normalizeLocale(sharedSettings.locale)
      emitConnectionStatus(mainWindow, {
        status: 'disconnected',
        errorMessage: translate(loc, 'main.status.reconnecting')
      })
    }

    if (crashRetries < 3) {
      crashRetries++
      clearCrashRetryTimer()
      crashRetryTimer = setTimeout(() => {
        if (isAppQuitting) {
          return
        }
        if (mainWindow && !mainWindow.isDestroyed()) {
          void connectToAppServer(currentWorkspacePath)
        }
      }, 2000)
    }
  })

  manager.on('started', async () => {
    crashRetries = 0
    clearCrashRetryTimer()

    if (connectionMode === 'websocket') {
      try {
        const { host, port } = resolveWebSocketHostPort(sharedSettings)
        await waitForReadyz(host, port)
        await connectViaWebSocket(workspacePath, buildManagedWsUrl(sharedSettings))
      } catch (err) {
        const message = err instanceof Error ? err.message : String(err)
        if (mainWindow && !mainWindow.isDestroyed()) {
          emitConnectionStatus(mainWindow, { status: 'error', errorMessage: message })
        }
      }
      return
    }

    const { stdin, stdout } = manager
    if (!stdin || !stdout) {
      if (mainWindow && !mainWindow.isDestroyed()) {
        const loc = normalizeLocale(sharedSettings.locale)
        emitConnectionStatus(mainWindow, {
          status: 'error',
          errorMessage: translate(loc, 'main.error.streamsUnavailable')
        })
      }
      return
    }

    const client = new WireProtocolClient(stdout, stdin)
    wireClient = client

    client.onNotification((method, params) => {
      if (mainWindow && !mainWindow.isDestroyed()) {
        broadcastNotification(mainWindow, method, params)
      }
    })

    client.onServerRequest(async (method, params) => {
      if (!mainWindow || mainWindow.isDestroyed()) {
        return Promise.reject(
          new Error('Window is not available to handle server request')
        )
      }
      const { bridgeId, promise } = createServerRequestBridge()
      broadcastServerRequest(mainWindow, { bridgeId, method, params })
      return promise
    })

    try {
      const result = await client.initialize()
      if (mainWindow && !mainWindow.isDestroyed()) {
        emitConnectionStatus(mainWindow, {
          status: 'connected',
          serverInfo: result.serverInfo,
          capabilities: result.capabilities as Record<string, unknown>,
          dashboardUrl: result.dashboardUrl
        })
      }
      await autoStartEnabledModules()
    } catch (err) {
      console.error('[desktop] appserver initialize failed', err)
      const message = err instanceof Error ? err.message : String(err)
      const isTimeout = message.includes('timed out')
      if (mainWindow && !mainWindow.isDestroyed()) {
        const loc = normalizeLocale(sharedSettings.locale)
        emitConnectionStatus(mainWindow, {
          status: 'error',
          errorMessage: isTimeout
            ? translate(loc, 'main.error.handshakeTimeout')
            : message,
          ...(isTimeout ? { errorType: 'handshake-timeout' } : {})
        } as ConnectionStatusPayload)
      }
    }
  })

  manager.spawn()
}

// ─── App menu ─────────────────────────────────────────────────────────────────

function buildAppMenu(locale: AppLocale): Menu {
  const isMac = process.platform === 'darwin'
  const L = (key: string) => translate(locale, key)
  const template: MenuItemConstructorOptions[] = [
    ...(isMac ? ([{ role: 'appMenu' }] as MenuItemConstructorOptions[]) : []),
    {
      id: 'file',
      label: L('menu.file'),
      submenu: [
        {
          label: L('menu.newWindow'),
          accelerator: 'CmdOrCtrl+Shift+N',
          click: () => {
            openNewProcess()
          }
        },
        { type: 'separator' },
        isMac ? { role: 'close' } : { role: 'quit' }
      ]
    },
    {
      id: 'edit',
      label: L('menu.edit'),
      submenu: [
        { role: 'undo' },
        { role: 'redo' },
        { type: 'separator' },
        { role: 'cut' },
        { role: 'copy' },
        { role: 'paste' },
        { role: 'selectAll' }
      ]
    },
    {
      id: 'view',
      label: L('menu.view'),
      submenu: [
        { role: 'reload' },
        { role: 'forceReload' },
        { role: 'toggleDevTools' },
        { type: 'separator' },
        { role: 'resetZoom' },
        { role: 'zoomIn' },
        { role: 'zoomOut' },
        { type: 'separator' },
        {
          label: L('menu.openDashboard'),
          accelerator: 'CmdOrCtrl+Shift+D',
          enabled: Boolean(lastDashboardUrl),
          click: async () => {
            if (lastDashboardUrl) await openExternalHttpUrl(lastDashboardUrl)
          }
        },
        { type: 'separator' },
        { role: 'togglefullscreen' }
      ]
    },
    {
      id: 'window',
      label: L('menu.window'),
      submenu: [
        { role: 'minimize' },
        { role: 'zoom' },
        ...(isMac
          ? ([{ type: 'separator' }, { role: 'front' }] as MenuItemConstructorOptions[])
          : ([{ role: 'close' }] as MenuItemConstructorOptions[]))
      ]
    },
    {
      id: 'help',
      label: L('menu.help'),
      submenu: [
        {
          label: L('menu.documentation'),
          click: async () => {
            await shell.openExternal('https://github.com/DotHarness/dotcraft')
          }
        }
      ]
    }
  ]
  return Menu.buildFromTemplate(template)
}

function refreshAppMenu(): void {
  Menu.setApplicationMenu(buildAppMenu(normalizeLocale(sharedSettings.locale)))
}

function emitConnectionStatus(win: BrowserWindow, payload: ConnectionStatusPayload): void {
  if (payload.status === 'connected') {
    const sanitized = sanitizeHttpOrHttpsUrl(payload.dashboardUrl)
    lastConnectionStatus = {
      ...payload,
      dashboardUrl: sanitized ?? undefined
    }
    lastDashboardUrl = sanitized
    broadcastConnectionStatus(win, {
      ...payload,
      dashboardUrl: sanitized ?? undefined
    })
  } else {
    lastConnectionStatus = { ...payload, dashboardUrl: undefined }
    lastDashboardUrl = null
    broadcastConnectionStatus(win, payload)
  }
  refreshAppMenu()
}

function emitWorkspaceStatus(win: BrowserWindow, payload: WorkspaceStatusPayload): void {
  lastWorkspaceStatus = payload
  broadcastWorkspaceStatus(win, payload)
}

function registerMenuPopupIpc(): void {
  ipcMain.removeHandler('menu:popup-top-level')
  ipcMain.handle(
    'menu:popup-top-level',
    (event, payload: { menuId: TopLevelMenuId; x: number; y: number }) => {
      const win = BrowserWindow.fromWebContents(event.sender)
      if (!win || win.isDestroyed()) return
      const appMenu = Menu.getApplicationMenu()
      if (!appMenu) return
      const item = appMenu.items.find((i) => i.id === payload.menuId)
      if (!item?.submenu) return
      item.submenu.popup({
        window: win,
        x: Math.round(payload.x),
        y: Math.round(payload.y)
      })
    }
  )
}

// ─── App lifecycle ────────────────────────────────────────────────────────────

app.whenReady().then(() => {
  isAppQuitting = false
  registerMenuPopupIpc()
  void refreshNodeRuntimeStatus()
  sharedSettings = loadSettings()
  refreshAppMenu()

  if (!import.meta.env.DEV) {
    session.defaultSession.webRequest.onHeadersReceived((details, callback) => {
      callback({
        responseHeaders: {
          ...details.responseHeaders,
          'Content-Security-Policy': [
            "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self' data:; connect-src 'self'"
          ]
        }
      })
    })
  }

  let workspacePath = resolveWorkspacePath(sharedSettings)

  // If another process is already using this workspace, start without one
  // so the user sees the welcome screen and can pick a different workspace.
  if (workspacePath) {
    const lockCheck = acquireWorkspaceLock(workspacePath)
    if (!lockCheck.ok) {
      workspacePath = null
    } else {
      addRecentWorkspace(sharedSettings, workspacePath)
      saveSettings(sharedSettings)
    }
  }

  const initialWorkspaceStatus = getWorkspaceStatus(workspacePath)
  lastWorkspaceStatus = initialWorkspaceStatus
  const win = createWindow(workspacePath)
  mainWindow = win
  currentWorkspacePath = workspacePath ?? ''

  registerDesktopIpcHandlers(workspacePath ?? '', () => wireClient)

  if (import.meta.env.DEV) {
    win.loadURL('http://localhost:5173')
    win.webContents.once('did-finish-load', () => {
      win.webContents.openDevTools()
    })
  } else {
    win.loadFile(join(__dirname, '../renderer/index.html'))
  }

  win.webContents.once('did-finish-load', () => {
    emitWorkspaceStatus(win, initialWorkspaceStatus)
    if (workspacePath && initialWorkspaceStatus.status === 'ready') {
      void connectToAppServer(workspacePath)
    } else {
      emitConnectionStatus(win, { status: 'disconnected' })
    }
  })

  app.on('activate', () => {
    const windows = BrowserWindow.getAllWindows()
    if (windows.length === 0) {
      sharedSettings = loadSettings()
      let wsPath = resolveWorkspacePath(sharedSettings)
      if (wsPath) {
        const lockCheck = acquireWorkspaceLock(wsPath)
        if (!lockCheck.ok) {
          wsPath = null
        } else {
          addRecentWorkspace(sharedSettings, wsPath)
          saveSettings(sharedSettings)
        }
      }
      const workspaceStatus = getWorkspaceStatus(wsPath)
      lastWorkspaceStatus = workspaceStatus
      const newWin = createWindow(wsPath)
      mainWindow = newWin
      currentWorkspacePath = wsPath ?? ''

      if (wsPath) {
        reregisterIpcForWorkspace(wsPath)
      } else {
        registerDesktopIpcHandlers('', () => null)
      }

      if (import.meta.env.DEV) {
        newWin.loadURL('http://localhost:5173')
      } else {
        newWin.loadFile(join(__dirname, '../renderer/index.html'))
      }

      newWin.webContents.once('did-finish-load', () => {
        emitWorkspaceStatus(newWin, workspaceStatus)
        if (wsPath && workspaceStatus.status === 'ready') {
          void connectToAppServer(wsPath)
        } else {
          emitConnectionStatus(newWin, { status: 'disconnected' })
        }
      })
    } else {
      showWindowSafely(windows[0]!)
    }
  })
})

app.on('window-all-closed', () => {
  if (process.platform === 'darwin') {
    teardownRuntime('window-all-closed', {
      releaseWorkspaceLock: true,
      clearMainWindow: true,
      cleanupIpcHandlers: true
    })
    return
  }
  // Non-macOS exits via app.quit() -> before-quit for final cleanup.
  if (!isAppQuitting) {
    app.quit()
  }
})

app.on('before-quit', () => {
  isAppQuitting = true
  if (finalQuitCleanupDone) {
    return
  }
  finalQuitCleanupDone = true
  teardownRuntime('before-quit', {
    releaseWorkspaceLock: true,
    clearMainWindow: true,
    cleanupIpcHandlers: true
  })
})
