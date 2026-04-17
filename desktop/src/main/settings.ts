import { app } from 'electron'
import { join, basename, normalize } from 'path'
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'fs'
import { normalizeLocale, type AppLocale } from '../shared/locales'

export interface RecentWorkspace {
  path: string
  name: string
  lastOpenedAt: string
}

export type UiTheme = 'dark' | 'light'
export type ConnectionMode = 'stdio' | 'websocket' | 'stdioAndWebSocket' | 'remote'
export type BinarySource = 'bundled' | 'path' | 'custom'
export type LastOpenEditorId =
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

export interface WebSocketConnectionSettings {
  host?: string
  port?: number
}

export interface RemoteConnectionSettings {
  url?: string
  token?: string
}

export type ProxyStatus = 'stopped' | 'starting' | 'running' | 'error'
export type ProxyOAuthProvider = 'codex' | 'claude' | 'gemini' | 'qwen' | 'iflow'

export interface ProxySettings {
  enabled?: boolean
  host?: string
  port?: number
  binarySource?: BinarySource
  binaryPath?: string
  authDir?: string
  apiKey?: string
  managementKey?: string
}

export interface AppSettings {
  lastWorkspacePath?: string
  modulesDirectory?: string
  activeModuleVariants?: Record<string, string>
  binarySource?: BinarySource
  appServerBinaryPath?: string
  connectionMode?: ConnectionMode
  webSocket?: WebSocketConnectionSettings
  remote?: RemoteConnectionSettings
  /** UI theme; omitted or invalid values are treated as dark by the renderer */
  theme?: UiTheme
  /** Display language (BCP 47); omitted or invalid values are treated as English */
  locale?: AppLocale
  proxy?: ProxySettings
  recentWorkspaces?: RecentWorkspace[]
  /**
   * Passed as `crossChannelOrigins` on `thread/list`. If the key is absent, the client
   * seeds it from all `builtin` channels in `channel/list` (see specs/desktop-client.md §9.4.1).
   */
  visibleChannels?: string[]
  lastOpenEditorId?: LastOpenEditorId
}

const MAX_RECENT = 20

function normalizeBinarySource(settings: AppSettings): BinarySource {
  const source = settings.binarySource
  if (source === 'bundled' || source === 'path' || source === 'custom') {
    return source
  }
  return settings.appServerBinaryPath?.trim() ? 'custom' : 'bundled'
}

function normalizeModulesDirectory(settings: AppSettings): string | undefined {
  const raw = settings.modulesDirectory?.trim()
  if (!raw) return undefined
  return normalize(raw)
}

function normalizeLastOpenEditorId(settings: AppSettings): LastOpenEditorId | undefined {
  const value = settings.lastOpenEditorId
  if (
    value === 'explorer' ||
    value === 'vs' ||
    value === 'cursor' ||
    value === 'vscode' ||
    value === 'rider' ||
    value === 'webstorm' ||
    value === 'idea' ||
    value === 'github-desktop' ||
    value === 'git-bash' ||
    value === 'terminal'
  ) {
    return value
  }
  return undefined
}

function normalizeProxySettings(settings: AppSettings): ProxySettings | undefined {
  const raw = settings.proxy
  if (raw == null || typeof raw !== 'object' || Array.isArray(raw)) {
    return undefined
  }
  const normalized: ProxySettings = {}
  if (typeof raw.enabled === 'boolean') {
    normalized.enabled = raw.enabled
  }
  if (typeof raw.host === 'string' && raw.host.trim().length > 0) {
    normalized.host = raw.host.trim()
  }
  if (typeof raw.port === 'number' && Number.isInteger(raw.port) && raw.port > 0 && raw.port <= 65535) {
    normalized.port = raw.port
  }
  if (raw.binarySource === 'bundled' || raw.binarySource === 'path' || raw.binarySource === 'custom') {
    normalized.binarySource = raw.binarySource
  } else if (typeof raw.binaryPath === 'string' && raw.binaryPath.trim().length > 0) {
    normalized.binarySource = 'custom'
  }
  if (typeof raw.binaryPath === 'string' && raw.binaryPath.trim().length > 0) {
    normalized.binaryPath = raw.binaryPath.trim()
  }
  if (typeof raw.authDir === 'string' && raw.authDir.trim().length > 0) {
    normalized.authDir = normalize(raw.authDir.trim())
  }
  if (typeof raw.apiKey === 'string' && raw.apiKey.trim().length > 0) {
    normalized.apiKey = raw.apiKey.trim()
  }
  if (typeof raw.managementKey === 'string' && raw.managementKey.trim().length > 0) {
    normalized.managementKey = raw.managementKey.trim()
  }
  return Object.keys(normalized).length > 0 ? normalized : undefined
}

function normalizeActiveModuleVariants(settings: AppSettings): Record<string, string> | undefined {
  const raw = settings.activeModuleVariants
  if (raw == null || typeof raw !== 'object' || Array.isArray(raw)) {
    return undefined
  }
  const normalized: Record<string, string> = {}
  for (const [key, value] of Object.entries(raw)) {
    if (typeof value !== 'string') continue
    const trimmedKey = key.trim().toLowerCase()
    const trimmedValue = value.trim()
    if (!trimmedKey || !trimmedValue) continue
    normalized[trimmedKey] = trimmedValue
  }
  return Object.keys(normalized).length > 0 ? normalized : undefined
}

function getSettingsPath(): string {
  return join(app.getPath('userData'), 'settings.json')
}

export function loadSettings(): AppSettings {
  const filePath = getSettingsPath()
  const systemLocale = normalizeLocale(app.getLocale())
  try {
    if (existsSync(filePath)) {
      const raw = JSON.parse(readFileSync(filePath, 'utf8')) as AppSettings
      raw.binarySource = normalizeBinarySource(raw)
      raw.modulesDirectory = normalizeModulesDirectory(raw)
      raw.lastOpenEditorId = normalizeLastOpenEditorId(raw)
      raw.proxy = normalizeProxySettings(raw)
      raw.activeModuleVariants = normalizeActiveModuleVariants(raw)
      if (raw.locale !== undefined) {
        raw.locale = normalizeLocale(raw.locale)
      } else {
        raw.locale = systemLocale
      }
      return raw
    }
  } catch {
    // Ignore corrupt settings
  }
  return { locale: systemLocale }
}

export function saveSettings(settings: AppSettings): void {
  const filePath = getSettingsPath()
  try {
    const dir = join(filePath, '..')
    if (!existsSync(dir)) mkdirSync(dir, { recursive: true })
    settings.binarySource = normalizeBinarySource(settings)
    settings.modulesDirectory = normalizeModulesDirectory(settings)
    settings.lastOpenEditorId = normalizeLastOpenEditorId(settings)
    settings.proxy = normalizeProxySettings(settings)
    settings.activeModuleVariants = normalizeActiveModuleVariants(settings)
    writeFileSync(filePath, JSON.stringify(settings, null, 2), 'utf8')
  } catch {
    // Non-fatal
  }
}

/**
 * Adds (or moves) a workspace to the top of the recent list with LRU eviction.
 * Mutates and returns the settings object.
 */
export function addRecentWorkspace(settings: AppSettings, workspacePath: string): AppSettings {
  const name = basename(workspacePath)
  const entry: RecentWorkspace = {
    path: workspacePath,
    name,
    lastOpenedAt: new Date().toISOString()
  }
  const existing = settings.recentWorkspaces ?? []
  // Remove duplicate if present, then prepend
  const filtered = existing.filter((r) => r.path !== workspacePath)
  settings.recentWorkspaces = [entry, ...filtered].slice(0, MAX_RECENT)
  settings.lastWorkspacePath = workspacePath
  return settings
}

export function getRecentWorkspaces(settings: AppSettings): RecentWorkspace[] {
  return settings.recentWorkspaces ?? []
}
