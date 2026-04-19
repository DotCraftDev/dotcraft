import { app, ipcMain, BrowserWindow, dialog, Notification, shell } from 'electron'
import { promises as fs } from 'fs'
import { execFile } from 'child_process'
import * as os from 'os'
import * as path from 'path'
import type { WireProtocolClient } from './WireProtocolClient'
import type {
  AppSettings,
  RecentWorkspace,
  BinarySource,
  ProxyStatus,
  ProxyOAuthProvider
} from './settings'
import type { ProxyAuthFileSummary } from './proxyAuthFiles'
import { resolveBinaryLocation } from './AppServerManager'
import { resolveProxyBinaryLocation } from './ProxyProcessManager'
import { checkWorkspaceLock } from './workspaceLock'
import {
  TITLE_BAR_OVERLAY_BY_THEME,
  TITLE_BAR_OVERLAY_HEIGHT
} from '../shared/titleBarOverlay'
import {
  invalidateFileIndex,
  readImageAsDataUrl,
  saveImageDataUrlToTemp,
  searchWorkspaceFiles,
  warmFileSearchIndex
} from './workspaceComposerIpc'
import {
  scanModules,
  groupModulesByChannel,
  type DiscoveredModule
} from './moduleScanner'
import {
  ModuleProcessManager,
  type ModuleStatusMap
} from './moduleProcessManager'
import type { QrUpdatePayload } from './qrWatcher'
import type {
  WorkspaceSetupRequest,
  WorkspaceStatusPayload,
  WorkspaceSetupModelListRequest,
  WorkspaceSetupModelListResult
} from './workspaceSetup'
import { translate, normalizeLocale, DEFAULT_LOCALE, type AppLocale } from '../shared/locales'
import { parseJsonConfig, parseJsonObjectConfig } from '../shared/jsonConfig'
import { detectEditors, launchEditor, type EditorId } from './externalEditors'

export type ConnectionStatus = 'connecting' | 'connected' | 'disconnected' | 'error'

export type ConnectionErrorType = 'binary-not-found' | 'handshake-timeout' | 'crash'

export interface ConnectionStatusPayload {
  status: ConnectionStatus
  serverInfo?: {
    name: string
    version: string
    protocolVersion?: string
  }
  capabilities?: Record<string, unknown>
  /** DashBoard URL when the server hosts it (initialize). */
  dashboardUrl?: string
  errorMessage?: string
  errorType?: ConnectionErrorType
  binarySource?: BinarySource
}

export interface ResolvedBinaryRequest {
  binarySource?: BinarySource
  binaryPath?: string
}

export interface ResolvedBinaryPayload {
  source: BinarySource
  path: string | null
}

export interface ResolvedProxyBinaryPayload {
  source: 'bundled' | 'path' | 'custom'
  path: string | null
}

export interface ProxyStatusPayload {
  status: ProxyStatus
  errorMessage?: string
  port?: number
  baseUrl?: string
  managementUrl?: string
  pid?: number
}

interface ModulesRescanSummaryPayload {
  addedModuleIds: string[]
  removedModuleIds: string[]
  changedModuleIds: string[]
  changedRunningModuleIds: string[]
}

export interface ServerRequestPayload {
  bridgeId: string
  method: string
  params: unknown
}

function assertPathWithinWorkspace(
  absPath: string,
  workspacePath: string,
  locale: AppLocale
): string {
  const resolved = path.resolve(absPath)
  const wsResolved = path.resolve(workspacePath)
  if (!resolved.startsWith(wsResolved + path.sep) && resolved !== wsResolved) {
    throw new Error(
      translate(locale, 'ipc.pathOutsideWorkspace', { path: absPath })
    )
  }
  return resolved
}

/**
 * Returns normalized http(s) URL string, or null if empty / malformed / wrong protocol.
 * Used for DashBoard URLs from initialize and for `shell:open-external` validation.
 */
export function sanitizeHttpOrHttpsUrl(url: string | undefined): string | null {
  if (url === undefined || typeof url !== 'string') return null
  const trimmed = url.trim()
  if (trimmed === '') return null
  let parsed: URL
  try {
    parsed = new URL(trimmed)
  } catch {
    return null
  }
  if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') return null
  return parsed.href
}

/**
 * Opens an http(s) URL in the system browser. Throws the same errors as the legacy
 * `shell:open-external` IPC handler for invalid input.
 */
export async function openExternalHttpUrl(url: string): Promise<void> {
  if (typeof url !== 'string' || url.trim() === '') {
    throw new Error('Invalid URL')
  }
  const safe = sanitizeHttpOrHttpsUrl(url)
  if (safe === null) {
    try {
      new URL(url.trim())
    } catch {
      throw new Error('Invalid URL')
    }
    throw new Error('Only http(s) URLs are allowed')
  }
  await shell.openExternal(safe)
}

function isSafeConfigFileName(configFileName: string): boolean {
  if (configFileName.trim() === '') return false
  if (configFileName.includes('..')) return false
  return !configFileName.includes('/') && !configFileName.includes('\\')
}

function ensureObjectConfig(config: unknown): Record<string, unknown> {
  if (config == null || typeof config !== 'object' || Array.isArray(config)) {
    throw new Error('Config payload must be a JSON object')
  }
  return config as Record<string, unknown>
}

function normalizeOptionalStringValue(value: unknown): string | null {
  if (typeof value !== 'string') return null
  const trimmed = value.trim()
  return trimmed === '' ? null : trimmed
}

interface WorkspaceCoreConfigSnapshot {
  apiKey: string | null
  endPoint: string | null
  welcomeSuggestionsEnabled: boolean | null
}

function getCaseInsensitiveRecordValue(
  record: Record<string, unknown>,
  key: string
): unknown {
  const expected = key.toLowerCase()
  for (const [candidate, value] of Object.entries(record)) {
    if (candidate.toLowerCase() === expected) {
      return value
    }
  }
  return undefined
}

function readNestedBoolean(
  record: Record<string, unknown>,
  sectionKey: string,
  fieldKey: string
): boolean | null {
  const section = getCaseInsensitiveRecordValue(record, sectionKey)
  if (section == null || typeof section !== 'object' || Array.isArray(section)) {
    return null
  }
  const raw = getCaseInsensitiveRecordValue(section as Record<string, unknown>, fieldKey)
  return typeof raw === 'boolean' ? raw : null
}

async function readCoreConfigSnapshot(configPath: string): Promise<WorkspaceCoreConfigSnapshot> {
  try {
    const raw = await fs.readFile(configPath, 'utf8')
    const parsed = parseJsonObjectConfig(raw)
    return {
      apiKey: normalizeOptionalStringValue(parsed.ApiKey ?? parsed.apiKey),
      endPoint: normalizeOptionalStringValue(parsed.EndPoint ?? parsed.endPoint),
      welcomeSuggestionsEnabled: readNestedBoolean(parsed, 'WelcomeSuggestions', 'Enabled')
    }
  } catch (error) {
    const code = (error as NodeJS.ErrnoException | undefined)?.code
    if (code === 'ENOENT') {
      return { apiKey: null, endPoint: null, welcomeSuggestionsEnabled: null }
    }
    throw error
  }
}

function resolveConnectionMode(settings: AppSettings): 'stdio' | 'websocket' | 'stdioAndWebSocket' | 'remote' {
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

function resolveModuleWsConfig(settings: AppSettings): { wsUrl: string; token?: string } {
  const mode = resolveConnectionMode(settings)
  if (mode === 'remote') {
    const raw = settings.remote?.url?.trim()
    if (!raw) {
      throw new Error('Remote WebSocket URL is not configured')
    }
    let parsed: URL
    try {
      parsed = new URL(raw)
    } catch {
      throw new Error('Invalid remote WebSocket URL')
    }
    if (parsed.protocol !== 'ws:' && parsed.protocol !== 'wss:') {
      throw new Error('Remote WebSocket URL must use ws:// or wss://')
    }
    const token = settings.remote?.token?.trim()
    return token ? { wsUrl: parsed.toString(), token } : { wsUrl: parsed.toString() }
  }

  const host = settings.webSocket?.host?.trim() || '127.0.0.1'
  const candidatePort = settings.webSocket?.port
  const port =
    typeof candidatePort === 'number' &&
    Number.isInteger(candidatePort) &&
    candidatePort > 0 &&
    candidatePort <= 65535
      ? candidatePort
      : 9100
  return { wsUrl: `ws://${host}:${port}/ws` }
}

function resolveUserModulesDirectory(settings: AppSettings): string {
  const configured = settings.modulesDirectory?.trim()
  if (configured) {
    return path.normalize(configured)
  }
  return path.join(app.getPath('home'), '.craft', 'modules')
}

function injectModuleDotcraftConfig(
  config: Record<string, unknown>,
  wsConfig: { wsUrl: string; token?: string }
): Record<string, unknown> {
  const next: Record<string, unknown> = { ...config }
  const dotcraftRaw = next.dotcraft
  const dotcraft =
    dotcraftRaw != null && typeof dotcraftRaw === 'object' && !Array.isArray(dotcraftRaw)
      ? { ...(dotcraftRaw as Record<string, unknown>) }
      : {}
  dotcraft.wsUrl = wsConfig.wsUrl
  if (wsConfig.token !== undefined) {
    dotcraft.token = wsConfig.token
  }
  next.dotcraft = dotcraft
  return next
}

// ---------------------------------------------------------------------------
// Pending server-request bridge
//
// When AppServer sends a server-initiated request (e.g. item/approval/request),
// Main forwards it to Renderer and waits for a response. A "bridge ID" links
// the forward to the matching renderer reply.
// ---------------------------------------------------------------------------

let nextBridgeId = 1
const pendingServerRequests = new Map<string, (result: unknown) => void>()

/**
 * Creates a pending entry and returns a Promise that resolves when the Renderer
 * calls `appserver:server-response` with the matching bridgeId.
 */
export function createServerRequestBridge(): { bridgeId: string; promise: Promise<unknown> } {
  const bridgeId = String(nextBridgeId++)
  const promise = new Promise<unknown>((resolve) => {
    pendingServerRequests.set(bridgeId, resolve)
  })
  return { bridgeId, promise }
}

export interface IpcHandlerCallbacks {
  /** Called when the renderer requests a workspace switch. */
  onSwitchWorkspace: (newPath: string) => Promise<void>
  /** Clears the current workspace selection and returns to the welcome screen. */
  onClearWorkspaceSelection: () => Promise<void>
  /** Runs the one-shot `dotcraft setup` workflow for the current workspace. */
  onRunWorkspaceSetup: (request: WorkspaceSetupRequest) => Promise<void>
  /** Lists available models for setup using explicit or inherited key. */
  onListSetupModels: (
    request: WorkspaceSetupModelListRequest
  ) => Promise<WorkspaceSetupModelListResult>
  /** Called when the renderer requests a new window. */
  onOpenNewWindow: () => void
  /** Restarts the Desktop-managed AppServer subprocess for the current workspace. */
  onRestartManagedAppServer: () => Promise<void>
  /** Restarts the Desktop-managed local CLI proxy subprocess. */
  onRestartManagedProxy: () => Promise<void>
  /** Returns local proxy runtime status. */
  getProxyStatus: () => ProxyStatusPayload
  /** Starts OAuth flow and returns state for polling. */
  startProxyOAuth: (provider: ProxyOAuthProvider) => Promise<{ url: string; state?: string }>
  /** Polls OAuth status with previous state token. */
  getProxyOAuthStatus: (state: string) => Promise<{ status: string; error?: string }>
  /** Lists persisted/runtime provider auth entries from CLIProxyAPI. */
  getProxyAuthFiles: () => Promise<ProxyAuthFileSummary[]>
  /** Returns usage summary from management API. */
  getProxyUsageSummary: () => Promise<{
    totalRequests: number
    successCount: number
    failureCount: number
    totalTokens: number
    failedRequests: number
  }>
  /** Returns the current settings object. */
  getSettings: () => AppSettings
  /** Updates and persists partial settings. */
  updateSettings: (partial: Partial<AppSettings>) => void | Promise<void>
  /** Returns the recent workspaces list. */
  getRecentWorkspaces: () => RecentWorkspace[]
  /** Clears and persists the recent workspaces list. */
  clearRecentWorkspaces?: () => void
  /** Returns the latest known connection status snapshot. */
  getConnectionStatus: () => ConnectionStatusPayload
  /** Returns the latest known workspace selection/setup snapshot. */
  getWorkspaceStatus: () => WorkspaceStatusPayload
}

/**
 * Registers all ipcMain handlers that bridge the Renderer and the WireProtocolClient.
 *
 * IPC channels:
 * - `appserver:send-request`      (renderer -> main, invoke) -> forwards to WireProtocolClient
 * - `appserver:server-response`   (renderer -> main, invoke) -> resolves pending server request
 * - `appserver:notification`      (main -> renderer, send)   -> forwarded from WireProtocolClient
 * - `appserver:server-request`    (main -> renderer, send)   -> server-initiated request
 * - `appserver:connection-status` (main -> renderer, send)   -> connection state changes
 * - `appserver:get-connection-status` (renderer -> main, invoke) -> latest status snapshot
 * - `appserver:workspace-config-schema` (renderer -> main, invoke) -> workspace config schema metadata
 * - `appserver:resolved-binary`      (renderer -> main, invoke) -> resolves the selected binary source
 * - `appserver:pick-binary`          (renderer -> main, invoke) -> opens native file picker for dotcraft
 * - `appserver:restart-managed`   (renderer -> main, invoke) -> restarts Desktop-managed AppServer
 * - `window:set-title`            (renderer -> main, invoke) -> sets window title
 * - `window:get-workspace-path`   (renderer -> main, invoke) -> returns workspace path
 * - `workspace:pick-folder`       (renderer -> main, invoke) -> opens native folder picker
 * - `workspace:pick-files`        (renderer -> main, invoke) -> opens native file picker
 * - `workspace:switch`            (renderer -> main, invoke) -> triggers workspace switch
 * - `workspace:clear-selection`   (renderer -> main, invoke) -> returns to the welcome screen
 * - `workspace:get-recent`        (renderer -> main, invoke) -> returns recent workspaces
 * - `workspace:clear-recent`      (renderer -> main, invoke) -> clears recent workspaces
 * - `workspace:get-status`        (renderer -> main, invoke) -> returns current workspace setup state
 * - `workspace:run-setup`         (renderer -> main, invoke) -> runs the one-shot setup command
 * - `workspace:open-new-window`   (renderer -> main, invoke) -> opens a new window
 * - `workspace:check-lock`        (renderer -> main, invoke) -> checks if workspace is locked
 * - `settings:get`                (renderer -> main, invoke) -> returns current settings
 * - `settings:set`                (renderer -> main, invoke) -> merges partial settings
 * - `file:write`                  (renderer -> main, invoke) -> writes file within workspace
 * - `file:read`                   (renderer -> main, invoke) -> reads UTF-8 file within workspace
 * - `file:delete`                 (renderer -> main, invoke) -> deletes file within workspace
 * - `file:exists`                 (renderer -> main, invoke) -> checks whether file exists within workspace
 * - `git:commit`                  (renderer -> main, invoke) -> git add + commit
 * - `shell:open-external`         (renderer -> main, invoke) -> opens http(s) URL in system browser
 * - `editors:list`                (renderer -> main, invoke) -> returns detected editor targets
 * - `editors:launch`              (renderer -> main, invoke) -> opens workspace path with editor target
 */
function mainLocale(callbacks?: IpcHandlerCallbacks): AppLocale {
  return normalizeLocale(callbacks?.getSettings()?.locale ?? DEFAULT_LOCALE)
}

let moduleProcessManager: ModuleProcessManager | null = null
let ensureModulesScanned: (() => Promise<DiscoveredModule[]>) | null = null
let getSettingsSnapshotForModules: (() => AppSettings) | null = null

function normalizeChannelName(channelName: string): string {
  return channelName.trim().toLowerCase()
}

function getNestedValue(config: Record<string, unknown>, dottedKey: string): unknown {
  const parts = dottedKey.split('.').filter(Boolean)
  if (parts.length === 0) return undefined
  let current: unknown = config
  for (const part of parts) {
    if (current == null || typeof current !== 'object' || Array.isArray(current)) return undefined
    current = (current as Record<string, unknown>)[part]
  }
  return current
}

function findMissingRequiredFields(
  config: Record<string, unknown>,
  module: DiscoveredModule
): string[] {
  const missing: string[] = []
  for (const descriptor of module.configDescriptors) {
    if (!descriptor.required) continue
    if (descriptor.key.startsWith('dotcraft.')) continue
    const value = getNestedValue(config, descriptor.key)
    const isMissing =
      value == null ||
      (typeof value === 'string' && value.trim() === '') ||
      (Array.isArray(value) && value.length === 0)
    if (isMissing) {
      missing.push(descriptor.displayLabel || descriptor.key)
    }
  }
  return missing
}

function isRunningProcessState(state: ModuleStatusMap[string]['processState'] | undefined): boolean {
  return state === 'starting' || state === 'running'
}

function areModulesEquivalent(previous: DiscoveredModule, next: DiscoveredModule): boolean {
  return JSON.stringify(previous) === JSON.stringify(next)
}

export function getModuleProcessManager(): ModuleProcessManager | null {
  return moduleProcessManager
}

export async function autoStartModuleProcessesByChannelName(
  enabledChannelNames: string[]
): Promise<void> {
  if (enabledChannelNames.length === 0) return
  const discoveredModules = ensureModulesScanned ? await ensureModulesScanned() : []
  const grouped = groupModulesByChannel(
    discoveredModules,
    getSettingsSnapshotForModules?.().activeModuleVariants
  )
  const enabledNames = new Set(
    enabledChannelNames.map((name) => normalizeChannelName(name)).filter(Boolean)
  )
  const moduleIdsToStart = grouped
    .filter((group) => enabledNames.has(normalizeChannelName(group.channelName)))
    .map((group) => group.activeModuleId)
    .filter(Boolean)
  if (moduleIdsToStart.length === 0) return
  await moduleProcessManager?.autoStartModules(moduleIdsToStart)
}

export function registerIpcHandlers(
  _wireClient: WireProtocolClient | null,
  getWireClient: () => WireProtocolClient | null,
  workspacePath: string,
  callbacks?: IpcHandlerCallbacks
): void {
  invalidateFileIndex()
  const handleSafe = (
    channel: string,
    listener: Parameters<typeof ipcMain.handle>[1]
  ): void => {
    ipcMain.removeHandler(channel)
    ipcMain.handle(channel, listener)
  }
  let cachedModules: DiscoveredModule[] | null = null
  const configWriteQueues = new Map<string, Promise<void>>()
  const scanAndCacheModules = async (
    options?: { emitSummary?: boolean }
  ): Promise<DiscoveredModule[]> => {
    const previousModules = cachedModules ?? []
    const nextModules = await scanModules(callbacks?.getSettings() ?? {}, !app.isPackaged)
    const previousById = new Map(previousModules.map((module) => [module.moduleId, module] as const))
    const nextById = new Map(nextModules.map((module) => [module.moduleId, module] as const))

    const addedModuleIds: string[] = []
    const removedModuleIds: string[] = []
    const changedModuleIds: string[] = []
    for (const module of nextModules) {
      const previous = previousById.get(module.moduleId)
      if (!previous) {
        addedModuleIds.push(module.moduleId)
        continue
      }
      if (!areModulesEquivalent(previous, module)) {
        changedModuleIds.push(module.moduleId)
      }
    }
    for (const previous of previousModules) {
      if (!nextById.has(previous.moduleId)) {
        removedModuleIds.push(previous.moduleId)
      }
    }

    for (const moduleId of removedModuleIds) {
      await moduleProcessManager?.stop(moduleId)
    }

    const statusMap = moduleProcessManager?.getStatusMap() ?? {}
    const changedRunningModuleIds = changedModuleIds.filter((moduleId) =>
      isRunningProcessState(statusMap[moduleId]?.processState)
    )

    cachedModules = nextModules

    if (options?.emitSummary === true) {
      const summary: ModulesRescanSummaryPayload = {
        addedModuleIds,
        removedModuleIds,
        changedModuleIds,
        changedRunningModuleIds
      }
      for (const win of BrowserWindow.getAllWindows()) {
        if (!win.isDestroyed()) {
          win.webContents.send('modules:rescan-summary', summary)
        }
      }
    }

    return nextModules
  }
  ensureModulesScanned = scanAndCacheModules
  getSettingsSnapshotForModules = () => callbacks?.getSettings() ?? {}
  moduleProcessManager = new ModuleProcessManager({
    workspacePath,
    getWireClient,
    getCachedModules: () => cachedModules,
    onStatusChanged: (statusMap) => {
      for (const win of BrowserWindow.getAllWindows()) {
        broadcastModuleStatus(win, statusMap)
      }
    },
    onQrUpdate: (payload) => {
      for (const win of BrowserWindow.getAllWindows()) {
        broadcastModuleQrUpdate(win, payload)
      }
    }
  })

  // Renderer -> Main: send a JSON-RPC request to AppServer
  handleSafe(
    'appserver:send-request',
    async (_event, method: string, params?: unknown, timeoutMs?: number) => {
      const client = getWireClient()
      if (!client) {
        throw new Error(translate(mainLocale(callbacks), 'ipc.appServerNotConnected'))
      }
      return client.sendRequest(method, params, timeoutMs)
    }
  )

  handleSafe('appserver:model-list', async () => {
    const client = getWireClient()
    if (!client) {
      throw new Error(translate(mainLocale(callbacks), 'ipc.appServerNotConnected'))
    }
    return client.sendRequest('model/list', {}, 20_000)
  })

  handleSafe('appserver:workspace-config-schema', async () => {
    const client = getWireClient()
    if (!client) {
      throw new Error(translate(mainLocale(callbacks), 'ipc.appServerNotConnected'))
    }

    try {
      return await client.sendRequest('workspace/config/schema', {}, 20_000)
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error)
      if (message.toLowerCase().includes('method not found')) {
        return null
      }
      throw error
    }
  })

  handleSafe('workspace-config:get-core', async () => {
    const workspacePath = callbacks?.getWorkspaceStatus().workspacePath?.trim()
      if (!workspacePath) {
        return {
          workspace: { apiKey: null, endPoint: null, welcomeSuggestionsEnabled: null },
          userDefaults: await readCoreConfigSnapshot(path.join(os.homedir(), '.craft', 'config.json'))
        }
      }

    return {
      workspace: await readCoreConfigSnapshot(path.join(workspacePath, '.craft', 'config.json')),
      userDefaults: await readCoreConfigSnapshot(path.join(os.homedir(), '.craft', 'config.json'))
    }
  })

  handleSafe('appserver:get-connection-status', () => {
    return callbacks?.getConnectionStatus() ?? { status: 'disconnected' }
  })

  handleSafe('appserver:resolved-binary', (_event, request?: ResolvedBinaryRequest) => {
    const settings = callbacks?.getSettings() ?? {}
    return resolveBinaryLocation({
      binarySource: request?.binarySource ?? settings.binarySource,
      binaryPath: request?.binaryPath ?? settings.appServerBinaryPath
    })
  })

  handleSafe('appserver:pick-binary', async (_event) => {
    const focusedWin = BrowserWindow.getFocusedWindow()
    const options =
      process.platform === 'win32'
        ? {
            title: translate(mainLocale(callbacks), 'settings.pickBinaryTitle'),
            properties: ['openFile'] as const,
            filters: [{ name: 'DotCraft', extensions: ['exe'] }]
          }
        : {
            title: translate(mainLocale(callbacks), 'settings.pickBinaryTitle'),
            properties: ['openFile'] as const
          }
    const result = await dialog.showOpenDialog(
      focusedWin ?? BrowserWindow.getAllWindows()[0],
      options
    )
    if (result.canceled || result.filePaths.length === 0) return null
    return result.filePaths[0]
  })

  handleSafe('appserver:restart-managed', async () => {
    await callbacks?.onRestartManagedAppServer()
  })

  handleSafe('proxy:get-status', () => {
    return callbacks?.getProxyStatus() ?? { status: 'stopped' }
  })

  handleSafe('proxy:resolved-binary', (_event, request?: { binarySource?: 'bundled' | 'path' | 'custom'; binaryPath?: string }) => {
    const settings = callbacks?.getSettings() ?? {}
    return resolveProxyBinaryLocation({
      binarySource: request?.binarySource ?? settings.proxy?.binarySource,
      binaryPath: request?.binaryPath ?? settings.proxy?.binaryPath
    })
  })

  handleSafe('proxy:pick-binary', async (_event) => {
    const focusedWin = BrowserWindow.getFocusedWindow()
    const options =
      process.platform === 'win32'
        ? {
            title: 'Select CLIProxyAPI binary',
            properties: ['openFile'] as const,
            filters: [{ name: 'CLIProxyAPI', extensions: ['exe'] }]
          }
        : {
            title: 'Select CLIProxyAPI binary',
            properties: ['openFile'] as const
          }
    const result = await dialog.showOpenDialog(
      focusedWin ?? BrowserWindow.getAllWindows()[0],
      options
    )
    if (result.canceled || result.filePaths.length === 0) return null
    return result.filePaths[0]
  })

  handleSafe('proxy:restart-managed', async () => {
    await callbacks?.onRestartManagedProxy()
  })

  handleSafe('proxy:start-oauth', async (_event, provider: ProxyOAuthProvider) => {
    return callbacks?.startProxyOAuth(provider)
  })

  handleSafe('proxy:get-auth-status', async (_event, state: string) => {
    return callbacks?.getProxyOAuthStatus(state)
  })

  handleSafe('proxy:list-auth-files', async () => {
    return callbacks?.getProxyAuthFiles() ?? []
  })

  handleSafe('proxy:get-usage-summary', async () => {
    return callbacks?.getProxyUsageSummary()
  })

  // Renderer -> Main: send back the user's decision for a server-initiated request
  handleSafe('appserver:server-response', (_event, bridgeId: string, result: unknown) => {
    const resolve = pendingServerRequests.get(bridgeId)
    if (resolve) {
      pendingServerRequests.delete(bridgeId)
      resolve(result)
    }
  })

  // Renderer -> Main: set window title (targets the sender's own window)
  handleSafe('window:set-title', (event, title: string) => {
    const win = BrowserWindow.fromWebContents(event.sender)
    win?.setTitle(title)
  })

  // Renderer -> Main: sync titleBarOverlay colors with app theme (Windows / Linux only)
  handleSafe('window:set-title-bar-overlay-theme', (event, theme: 'dark' | 'light') => {
    if (process.platform === 'darwin') return
    const win = BrowserWindow.fromWebContents(event.sender)
    if (!win || win.isDestroyed()) return
    const t = theme === 'light' ? 'light' : 'dark'
    const { color, symbolColor } = TITLE_BAR_OVERLAY_BY_THEME[t]
    win.setTitleBarOverlay({
      color,
      symbolColor,
      height: TITLE_BAR_OVERLAY_HEIGHT
    })
  })

  // Renderer -> Main: get workspace path
  handleSafe('window:get-workspace-path', () => workspacePath)

  // Renderer -> Main: open http(s) URL in the system browser (DashBoard, etc.)
  handleSafe('shell:open-external', async (_event, url: string) => {
    await openExternalHttpUrl(url)
  })

  handleSafe('editors:list', async () => {
    return detectEditors()
  })

  handleSafe('editors:launch', async (_event, editorId: EditorId, targetPath: string) => {
    const locale = mainLocale(callbacks)
    const resolved = assertPathWithinWorkspace(targetPath, workspacePath, locale)
    await launchEditor(editorId, resolved)
  })

  // Renderer -> Main: write a file to disk (used for revert/re-apply)
  handleSafe('file:write', async (_event, absPath: string, content: string) => {
    const resolved = assertPathWithinWorkspace(absPath, workspacePath, mainLocale(callbacks))
    await fs.mkdir(path.dirname(resolved), { recursive: true })
    await fs.writeFile(resolved, content, 'utf-8')
  })

  // Renderer -> Main: read a file from disk (used for cumulative diff computation)
  handleSafe('file:read', async (_event, absPath: string): Promise<string> => {
    const resolved = assertPathWithinWorkspace(absPath, workspacePath, mainLocale(callbacks))
    try {
      return await fs.readFile(resolved, 'utf-8')
    } catch (err: unknown) {
      const code = (err as NodeJS.ErrnoException)?.code
      if (code === 'ENOENT') return ''
      throw err
    }
  })

  // Renderer -> Main: delete a file (used for reverting new files)
  handleSafe('file:delete', async (_event, absPath: string) => {
    const resolved = assertPathWithinWorkspace(absPath, workspacePath, mainLocale(callbacks))
    await fs.unlink(resolved)
  })

  handleSafe('file:exists', async (_event, absPath: string): Promise<boolean> => {
    const resolved = assertPathWithinWorkspace(absPath, workspacePath, mainLocale(callbacks))
    try {
      await fs.access(resolved)
      return true
    } catch {
      return false
    }
  })

  // Renderer -> Main: git add + commit
  handleSafe(
    'git:commit',
    (_event, wsPath: string, files: string[], message: string): Promise<string> => {
      return new Promise((resolve, reject) => {
        execFile(
          'git',
          ['add', '--', ...files],
          { cwd: wsPath },
          (addErr, _addStdout, addStderr) => {
            if (addErr) {
              reject(new Error(addStderr || addErr.message))
              return
            }
            execFile(
              'git',
              ['commit', '-m', message],
              { cwd: wsPath },
              (commitErr, commitStdout, commitStderr) => {
                if (commitErr) {
                  reject(new Error(commitStderr || commitErr.message))
                  return
                }
                resolve(commitStdout.trim())
              }
            )
          }
        )
      })
    }
  )

  // ─── Workspace management ──────────────────────────────────────────────────

  // Renderer -> Main: open native folder picker dialog
  handleSafe('workspace:pick-folder', async (_event) => {
    const focusedWin = BrowserWindow.getFocusedWindow()
    const result = await dialog.showOpenDialog(
      focusedWin ?? BrowserWindow.getAllWindows()[0],
      {
        title: 'Select Workspace Folder',
        properties: ['openDirectory', 'createDirectory']
      }
    )
    if (result.canceled || result.filePaths.length === 0) return null
    return result.filePaths[0]
  })

  handleSafe('workspace:pick-files', async () => {
    const focusedWin = BrowserWindow.getFocusedWindow()
    const result = await dialog.showOpenDialog(
      focusedWin ?? BrowserWindow.getAllWindows()[0],
      {
        title: 'Select Files',
        properties: ['openFile', 'multiSelections']
      }
    )
    if (result.canceled || result.filePaths.length === 0) {
      return [] as Array<{ path: string; fileName: string }>
    }
    return result.filePaths.map((filePath) => ({
      path: filePath,
      fileName: path.basename(filePath)
    }))
  })

  // Renderer -> Main: switch to a different workspace
  handleSafe('workspace:switch', async (_event, newPath: string) => {
    if (callbacks?.onSwitchWorkspace) {
      await callbacks.onSwitchWorkspace(newPath)
    }
  })

  handleSafe('workspace:clear-selection', async () => {
    await callbacks?.onClearWorkspaceSelection()
  })

  // Renderer -> Main: get recent workspaces
  handleSafe('workspace:get-recent', () => {
    return callbacks?.getRecentWorkspaces() ?? []
  })

  handleSafe('workspace:clear-recent', () => {
    callbacks?.clearRecentWorkspaces?.()
  })

  handleSafe('workspace:get-status', () => {
    return callbacks?.getWorkspaceStatus() ?? { status: 'no-workspace', workspacePath: '', hasUserConfig: false }
  })

  handleSafe('workspace:run-setup', async (_event, request: WorkspaceSetupRequest) => {
    await callbacks?.onRunWorkspaceSetup(request)
  })

  handleSafe(
    'workspace:list-setup-models',
    async (_event, request: WorkspaceSetupModelListRequest) => {
      if (!callbacks?.onListSetupModels) {
        return { kind: 'error' } satisfies WorkspaceSetupModelListResult
      }
      return callbacks.onListSetupModels(request)
    }
  )

  // Renderer -> Main: open a new independent window
  handleSafe('workspace:open-new-window', () => {
    callbacks?.onOpenNewWindow()
  })

  // Renderer -> Main: check if a workspace is already locked by another process
  handleSafe('workspace:check-lock', (_event, wsPath: string) => {
    return checkWorkspaceLock(wsPath)
  })

  // Renderer -> Main: save clipboard/drag image bytes to .craft/attachments/images for localImage wire part
  handleSafe(
    'workspace:save-image-to-temp',
    async (_event, params: { dataUrl: string; fileName?: string }) => {
      const ws = workspacePath
      if (!ws) {
        throw new Error(translate(mainLocale(callbacks), 'ipc.noWorkspaceOpen'))
      }
      const loc = mainLocale(callbacks)
      const pathAbs = await saveImageDataUrlToTemp(ws, params.dataUrl, params.fileName, loc)
      return { path: pathAbs }
    }
  )

  // Renderer -> Main: read local attachment image and return data URL for rehydration.
  handleSafe(
    'workspace:read-image-as-data-url',
    async (_event, params: { path: string }) => {
      const ws = workspacePath
      if (!ws) {
        throw new Error(translate(mainLocale(callbacks), 'ipc.noWorkspaceOpen'))
      }
      const loc = mainLocale(callbacks)
      const dataUrl = await readImageAsDataUrl(ws, params.path, loc)
      return { dataUrl }
    }
  )

  // Renderer -> Main: fuzzy file name search for @ mentions
  handleSafe(
    'workspace:search-files',
    async (
      _event,
      params: { query: string; workspacePath: string; limit?: number }
    ) => {
      const ws = path.resolve(workspacePath)
      const req = path.resolve(params.workspacePath)
      if (ws !== req) {
        throw new Error(translate(mainLocale(callbacks), 'ipc.workspacePathMismatch'))
      }
      if (!ws) {
        return { files: [] as { name: string; relativePath: string; dir: string }[] }
      }
      const limit = Math.min(20, Math.max(1, params.limit ?? 10))
      const files = await searchWorkspaceFiles(ws, params.query, limit)
      return { files }
    }
  )

  // ─── Settings ──────────────────────────────────────────────────────────────

  // Renderer -> Main: get current settings
  handleSafe('settings:get', () => {
    return callbacks?.getSettings() ?? {}
  })

  // Renderer -> Main: merge + persist partial settings update
  handleSafe(
    'settings:set',
    async (_event, partial: Partial<AppSettings>) => {
      await callbacks?.updateSettings(partial)
    }
  )

  handleSafe('modules:list', async () => {
    if (cachedModules !== null) return cachedModules
    return scanAndCacheModules()
  })

  handleSafe('modules:user-directory', async (): Promise<{ path: string }> => {
    return { path: resolveUserModulesDirectory(callbacks?.getSettings() ?? {}) }
  })

  handleSafe(
    'modules:check-directory',
    async (_event, params: { path: string }): Promise<{ exists: boolean }> => {
      if (!params?.path || typeof params.path !== 'string') {
        return { exists: false }
      }
      try {
        const stat = await fs.stat(path.normalize(params.path))
        return { exists: stat.isDirectory() }
      } catch {
        return { exists: false }
      }
    }
  )

  handleSafe('modules:open-folder', async (): Promise<{ ok: boolean; error?: string }> => {
    const targetPath = resolveUserModulesDirectory(callbacks?.getSettings() ?? {})
    const error = await shell.openPath(targetPath)
    if (error) {
      return { ok: false, error }
    }
    return { ok: true }
  })

  handleSafe('modules:pick-directory', async (): Promise<string | null> => {
    const focusedWin = BrowserWindow.getFocusedWindow()
    const result = await dialog.showOpenDialog(
      focusedWin ?? BrowserWindow.getAllWindows()[0],
      {
        title: 'Select Module Directory',
        properties: ['openDirectory', 'createDirectory']
      }
    )
    if (result.canceled || result.filePaths.length === 0) {
      return null
    }
    return result.filePaths[0]
  })

  handleSafe('modules:rescan', async () => scanAndCacheModules({ emitSummary: true }))

  handleSafe(
    'modules:set-active-variant',
    async (
      _event,
      params: { channelName: string; moduleId: string }
    ): Promise<{ ok: boolean; error?: string }> => {
      if (cachedModules === null) {
        await scanAndCacheModules()
      }
      const channelName = params?.channelName
      const moduleId = params?.moduleId
      if (typeof channelName !== 'string' || typeof moduleId !== 'string') {
        return { ok: false, error: 'Invalid payload' }
      }
      const normalizedChannelName = normalizeChannelName(channelName)
      if (!normalizedChannelName || !moduleId.trim()) {
        return { ok: false, error: 'Invalid payload' }
      }
      const module = cachedModules?.find((item) => item.moduleId === moduleId)
      if (!module) {
        return { ok: false, error: `Module '${moduleId}' not found` }
      }
      if (normalizeChannelName(module.channelName) !== normalizedChannelName) {
        return { ok: false, error: `Module '${moduleId}' does not belong to channel '${channelName}'` }
      }

      const groups = groupModulesByChannel(cachedModules ?? [], callbacks?.getSettings().activeModuleVariants)
      const currentGroup = groups.find(
        (group) => normalizeChannelName(group.channelName) === normalizedChannelName
      )
      if (currentGroup && currentGroup.activeModuleId !== moduleId) {
        await moduleProcessManager?.stop(currentGroup.activeModuleId)
      }

      const currentSettings = callbacks?.getSettings() ?? {}
      await callbacks?.updateSettings({
        activeModuleVariants: {
          ...(currentSettings.activeModuleVariants ?? {}),
          [normalizedChannelName]: moduleId
        }
      })
      return { ok: true }
    }
  )

  handleSafe(
    'modules:read-config',
    async (
      _event,
      params: { configFileName: string }
    ): Promise<{ exists: boolean; config: Record<string, unknown> | null }> => {
      if (!isSafeConfigFileName(params.configFileName)) {
        throw new Error('Invalid config file name')
      }
      const configPath = path.join(workspacePath, '.craft', params.configFileName)
      try {
        const stat = await fs.stat(configPath)
        if (stat.size > 1_000_000) {
          throw new Error(`Config file is too large to load: ${params.configFileName}`)
        }
        const raw = await fs.readFile(configPath, 'utf-8')
        return { exists: true, config: parseJsonObjectConfig(raw) }
      } catch (error) {
        const code = (error as NodeJS.ErrnoException | null)?.code
        if (code === 'ENOENT') {
          return { exists: false, config: null }
        }
        throw error
      }
    }
  )

  handleSafe(
    'modules:write-config',
    async (
      _event,
      params: { configFileName: string; config: Record<string, unknown> }
    ): Promise<{ ok: true }> => {
      if (!isSafeConfigFileName(params.configFileName)) {
        throw new Error('Invalid config file name')
      }
      const configPath = path.join(workspacePath, '.craft', params.configFileName)
      const settings = callbacks?.getSettings() ?? {}
      const wsConfig = resolveModuleWsConfig(settings)
      const mergedConfig = injectModuleDotcraftConfig(ensureObjectConfig(params.config), wsConfig)
      const previous = configWriteQueues.get(configPath) ?? Promise.resolve()
      const writeTask = previous
        .catch(() => {})
        .then(async () => {
          await fs.mkdir(path.dirname(configPath), { recursive: true })
          await fs.writeFile(
            configPath,
            `${JSON.stringify(mergedConfig, null, 2)}\n`,
            'utf-8'
          )
        })
      configWriteQueues.set(configPath, writeTask)
      await writeTask
      if (configWriteQueues.get(configPath) === writeTask) {
        configWriteQueues.delete(configPath)
      }
      return { ok: true }
    }
  )

  handleSafe(
    'modules:start',
    async (
      _event,
      params: { moduleId: string }
    ): Promise<{ ok: boolean; error?: string; missingFields?: string[] }> => {
      if (cachedModules === null) {
        await scanAndCacheModules()
      }
      if (!params?.moduleId || typeof params.moduleId !== 'string') {
        return { ok: false, error: 'Invalid module id' }
      }
      const module = cachedModules?.find((item) => item.moduleId === params.moduleId)
      if (!module) {
        return { ok: false, error: `Module '${params.moduleId}' not found` }
      }
      try {
        const configPath = path.join(workspacePath, '.craft', module.configFileName)
        const raw = await fs.readFile(configPath, 'utf-8')
        const parsed = parseJsonObjectConfig(raw)
        const settings = callbacks?.getSettings() ?? {}
        const wsConfig = resolveModuleWsConfig(settings)
        const merged = injectModuleDotcraftConfig(parsed, wsConfig)
        const missingFields = findMissingRequiredFields(merged, module)
        if (missingFields.length > 0) {
          return {
            ok: false,
            error: `Required fields missing: ${missingFields.join(', ')}`,
            missingFields
          }
        }
        if (JSON.stringify(merged) !== JSON.stringify(parsed)) {
          await fs.writeFile(configPath, `${JSON.stringify(merged, null, 2)}\n`, 'utf-8')
        }
      } catch (error) {
        const code = (error as NodeJS.ErrnoException | null)?.code
        if (code !== 'ENOENT') {
          return { ok: false, error: error instanceof Error ? error.message : String(error) }
        }
      }
      return moduleProcessManager?.start(params.moduleId) ?? { ok: false, error: 'Process manager is not available' }
    }
  )

  handleSafe(
    'modules:stop',
    async (_event, params: { moduleId: string }): Promise<{ ok: boolean; error?: string }> => {
      if (!params?.moduleId || typeof params.moduleId !== 'string') {
        return { ok: false, error: 'Invalid module id' }
      }
      return moduleProcessManager?.stop(params.moduleId) ?? { ok: false, error: 'Process manager is not available' }
    }
  )

  handleSafe('modules:running', async (): Promise<ModuleStatusMap> => {
    return moduleProcessManager?.getStatusMap() ?? {}
  })

  handleSafe(
    'modules:get-logs',
    async (_event, params: { moduleId: string }): Promise<{ lines: string[] }> => {
      if (!params?.moduleId || typeof params.moduleId !== 'string') {
        return { lines: [] }
      }
      return { lines: moduleProcessManager?.getRecentLogs(params.moduleId) ?? [] }
    }
  )

  handleSafe(
    'modules:qr-status',
    async (
      _event,
      params: { moduleId: string }
    ): Promise<{ active: boolean; qrDataUrl: string | null }> => {
      if (!params?.moduleId || typeof params.moduleId !== 'string') {
        return { active: false, qrDataUrl: null }
      }
      return moduleProcessManager?.getQrStatus(params.moduleId) ?? { active: false, qrDataUrl: null }
    }
  )

  if (workspacePath) {
    warmFileSearchIndex(workspacePath)
  }
}

/**
 * Broadcasts a connection status change to all renderer windows.
 */
export function broadcastConnectionStatus(
  win: BrowserWindow,
  payload: ConnectionStatusPayload
): void {
  if (!win.isDestroyed()) {
    win.webContents.send('appserver:connection-status', payload)
  }
}

export function broadcastWorkspaceStatus(
  win: BrowserWindow,
  payload: WorkspaceStatusPayload
): void {
  if (!win.isDestroyed()) {
    win.webContents.send('workspace:status-changed', payload)
  }
}

export function broadcastModuleStatus(
  win: BrowserWindow,
  payload: ModuleStatusMap
): void {
  if (!win.isDestroyed()) {
    win.webContents.send('modules:status-changed', payload)
  }
}

export function broadcastModuleQrUpdate(
  win: BrowserWindow,
  payload: QrUpdatePayload
): void {
  if (!win.isDestroyed()) {
    win.webContents.send('modules:qr-update', payload)
  }
}

/** Strip common Markdown for OS notification body (plain text). */
function stripMarkdownForNotify(text: string): string {
  return text
    .replace(/\r?\n/g, ' ')
    .replace(/\*\*(.+?)\*\*/g, '$1')
    .replace(/`([^`]+)`/g, '$1')
    .replace(/\s+/g, ' ')
    .trim()
}

/**
 * Forwards a Wire Protocol notification to the renderer.
 * When the window is not focused, shows a native notification for job results (spec §18.6).
 */
export function broadcastNotification(
  win: BrowserWindow,
  method: string,
  params: unknown
): void {
  if (
    method === 'system/jobResult' &&
    !win.isDestroyed() &&
    !win.isFocused()
  ) {
    const p = (params ?? {}) as Record<string, unknown>
    const jobName = String((p.jobName as string) ?? (p.name as string) ?? 'Job')
    const err = (p.error as string) ?? ''
    const result = (p.result as string) ?? (p.text as string) ?? ''
    const bodyRaw = err || result || 'Job completed'
    const body = stripMarkdownForNotify(bodyRaw).slice(0, 240)
    try {
      if (Notification.isSupported()) {
        new Notification({ title: jobName, body }).show()
      }
    } catch {
      /* ignore — notification optional */
    }
  }
  if (!win.isDestroyed()) {
    win.webContents.send('appserver:notification', { method, params })
  }
}

/**
 * Forwards a server-initiated request to the renderer.
 * The renderer must call sendServerResponse(bridgeId, result) to respond.
 */
export function broadcastServerRequest(
  win: BrowserWindow,
  payload: ServerRequestPayload
): void {
  if (!win.isDestroyed()) {
    win.webContents.send('appserver:server-request', payload)
  }
}

/**
 * Removes all registered ipcMain handlers (call before re-registering on workspace switch).
 */
export function unregisterIpcHandlers(): void {
  ipcMain.removeHandler('appserver:send-request')
  ipcMain.removeHandler('appserver:model-list')
  ipcMain.removeHandler('appserver:workspace-config-schema')
  ipcMain.removeHandler('workspace-config:get-core')
  ipcMain.removeHandler('appserver:get-connection-status')
  ipcMain.removeHandler('appserver:resolved-binary')
  ipcMain.removeHandler('appserver:pick-binary')
  ipcMain.removeHandler('appserver:restart-managed')
  ipcMain.removeHandler('proxy:get-status')
  ipcMain.removeHandler('proxy:resolved-binary')
  ipcMain.removeHandler('proxy:pick-binary')
  ipcMain.removeHandler('proxy:restart-managed')
  ipcMain.removeHandler('proxy:start-oauth')
  ipcMain.removeHandler('proxy:get-auth-status')
  ipcMain.removeHandler('proxy:list-auth-files')
  ipcMain.removeHandler('proxy:get-usage-summary')
  ipcMain.removeHandler('appserver:server-response')
  ipcMain.removeHandler('window:set-title')
  ipcMain.removeHandler('window:set-title-bar-overlay-theme')
  ipcMain.removeHandler('window:get-workspace-path')
  ipcMain.removeHandler('shell:open-external')
  ipcMain.removeHandler('editors:list')
  ipcMain.removeHandler('editors:launch')
  ipcMain.removeHandler('file:write')
  ipcMain.removeHandler('file:read')
  ipcMain.removeHandler('file:delete')
  ipcMain.removeHandler('file:exists')
  ipcMain.removeHandler('git:commit')
  ipcMain.removeHandler('workspace:pick-folder')
  ipcMain.removeHandler('workspace:pick-files')
  ipcMain.removeHandler('workspace:switch')
  ipcMain.removeHandler('workspace:clear-selection')
  ipcMain.removeHandler('workspace:get-recent')
  ipcMain.removeHandler('workspace:clear-recent')
  ipcMain.removeHandler('workspace:get-status')
  ipcMain.removeHandler('workspace:run-setup')
  ipcMain.removeHandler('workspace:list-setup-models')
  ipcMain.removeHandler('workspace:open-new-window')
  ipcMain.removeHandler('workspace:check-lock')
  ipcMain.removeHandler('workspace:save-image-to-temp')
  ipcMain.removeHandler('workspace:read-image-as-data-url')
  ipcMain.removeHandler('workspace:search-files')
  ipcMain.removeHandler('settings:get')
  ipcMain.removeHandler('settings:set')
  ipcMain.removeHandler('modules:list')
  ipcMain.removeHandler('modules:user-directory')
  ipcMain.removeHandler('modules:check-directory')
  ipcMain.removeHandler('modules:open-folder')
  ipcMain.removeHandler('modules:pick-directory')
  ipcMain.removeHandler('modules:rescan')
  ipcMain.removeHandler('modules:set-active-variant')
  ipcMain.removeHandler('modules:read-config')
  ipcMain.removeHandler('modules:write-config')
  ipcMain.removeHandler('modules:start')
  ipcMain.removeHandler('modules:stop')
  ipcMain.removeHandler('modules:running')
  ipcMain.removeHandler('modules:get-logs')
  ipcMain.removeHandler('modules:qr-status')
  if (moduleProcessManager) {
    void moduleProcessManager.stopAll({ preserveExternalChannels: true }).catch((err) => {
      console.warn('[ipcBridge] failed to stop module processes during unregister', err)
    })
  }
  moduleProcessManager = null
  ensureModulesScanned = null
  getSettingsSnapshotForModules = null
  invalidateFileIndex()
}
