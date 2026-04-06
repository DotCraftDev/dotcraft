import { app } from 'electron'
import { join, basename } from 'path'
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'fs'
import { normalizeLocale, type AppLocale } from '../shared/locales'

export interface RecentWorkspace {
  path: string
  name: string
  lastOpenedAt: string
}

export type UiTheme = 'dark' | 'light'
export type ConnectionMode = 'stdio' | 'websocket' | 'stdioAndWebSocket' | 'remote'

export interface WebSocketConnectionSettings {
  host?: string
  port?: number
}

export interface RemoteConnectionSettings {
  url?: string
  token?: string
}

export interface AppSettings {
  lastWorkspacePath?: string
  appServerBinaryPath?: string
  connectionMode?: ConnectionMode
  webSocket?: WebSocketConnectionSettings
  remote?: RemoteConnectionSettings
  /** UI theme; omitted or invalid values are treated as dark by the renderer */
  theme?: UiTheme
  /** Display language (BCP 47); omitted or invalid values are treated as English */
  locale?: AppLocale
  recentWorkspaces?: RecentWorkspace[]
  /**
   * Passed as `crossChannelOrigins` on `thread/list`. If the key is absent, the client
   * seeds it from all `builtin` channels in `channel/list` (see specs/desktop-client.md §9.4.1).
   */
  visibleChannels?: string[]
}

const MAX_RECENT = 20

function getSettingsPath(): string {
  return join(app.getPath('userData'), 'settings.json')
}

export function loadSettings(): AppSettings {
  const filePath = getSettingsPath()
  try {
    if (existsSync(filePath)) {
      const raw = JSON.parse(readFileSync(filePath, 'utf8')) as AppSettings
      if (raw.locale !== undefined) {
        raw.locale = normalizeLocale(raw.locale)
      }
      return raw
    }
  } catch {
    // Ignore corrupt settings
  }
  return {}
}

export function saveSettings(settings: AppSettings): void {
  const filePath = getSettingsPath()
  try {
    const dir = join(filePath, '..')
    if (!existsSync(dir)) mkdirSync(dir, { recursive: true })
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
