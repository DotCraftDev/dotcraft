import { app, Menu, nativeImage, shell, Tray, type MenuItemConstructorOptions } from 'electron'
import { spawn } from 'child_process'
import { basename, join } from 'path'
import { existsSync } from 'fs'
import { HubClient, type HubAppServerResponse, type HubEvent } from './HubClient'
import {
  getRecentWorkspaces,
  loadSettings,
  type AppSettings,
  type RecentWorkspace
} from './settings'
import { tryAcquireTrayLock, type TrayLockHandle } from './trayLock'
import { normalizeLocale, translate, type AppLocale } from '../shared/locales'

interface TrayState {
  status: 'starting' | 'running' | 'offline'
  hubLabel: string
  appServers: HubAppServerResponse[]
  recentWorkspaces: RecentWorkspace[]
  locale: AppLocale
}

const REFRESH_INTERVAL_MS = 5_000

function resolveTrayIconPath(): string | null {
  const packaged = join(process.resourcesPath, 'icon.png')
  const dev = join(__dirname, '../../resources/icon.png')
  const path = app.isPackaged ? packaged : dev
  return existsSync(path) ? path : null
}

function createTrayIcon(): Electron.NativeImage {
  const iconPath = resolveTrayIconPath()
  return iconPath ? nativeImage.createFromPath(iconPath) : nativeImage.createEmpty()
}

function stripArgPair(argv: string[], name: string): string[] {
  const result: string[] = []
  for (let i = 0; i < argv.length; i++) {
    if (argv[i] === name) {
      i++
      continue
    }
    result.push(argv[i])
  }
  return result
}

function baseDesktopArgs(): string[] {
  return stripArgPair(
    stripArgPair(process.argv.slice(1).filter((arg) => arg !== '--tray'), '--workspace'),
    '--remote'
  )
}

export function spawnDesktopWindow(workspacePath?: string): void {
  const args = baseDesktopArgs()
  if (workspacePath) {
    args.push('--workspace', workspacePath)
  }
  const child = spawn(process.execPath, args, {
    detached: true,
    stdio: 'ignore',
    windowsHide: true
  })
  child.unref()
}

function displayWorkspaceName(path: string): string {
  return basename(path) || path
}

function dashboardUrlOf(server: HubAppServerResponse): string | null {
  return server.endpoints.dashboard ?? server.serviceStatus.dashboard?.url ?? null
}

function buildAppServerMenu(
  server: HubAppServerResponse,
  hubClient: HubClient,
  refresh: () => void,
  locale: AppLocale
): MenuItemConstructorOptions {
  const L = (key: string) => translate(locale, key)
  const workspacePath = server.canonicalWorkspacePath || server.workspacePath
  const dashboardUrl = dashboardUrlOf(server)
  const running = server.state === 'running'
  return {
    label: `${displayWorkspaceName(workspacePath)} (${server.state})`,
    submenu: [
      {
        label: L('tray.openDesktop'),
        click: () => spawnDesktopWindow(workspacePath)
      },
      {
        label: L('tray.openDashboard'),
        enabled: Boolean(dashboardUrl),
        click: async () => {
          if (dashboardUrl) await shell.openExternal(dashboardUrl)
        }
      },
      { type: 'separator' },
      {
        label: L('tray.restartAppServer'),
        enabled: Boolean(workspacePath),
        click: async () => {
          await hubClient.restartAppServer(workspacePath)
          refresh()
        }
      },
      {
        label: L('tray.stopAppServer'),
        enabled: running,
        click: async () => {
          await hubClient.stopAppServer(workspacePath)
          refresh()
        }
      }
    ]
  }
}

function buildRecentMenu(recent: RecentWorkspace[], locale: AppLocale): MenuItemConstructorOptions {
  const L = (key: string) => translate(locale, key)
  const items = recent.slice(0, 8).map((workspace) => ({
    label: workspace.name || displayWorkspaceName(workspace.path),
    click: () => spawnDesktopWindow(workspace.path)
  }))
  return {
    label: L('tray.recent'),
    enabled: items.length > 0,
    submenu: items.length > 0 ? items : [{ label: L('tray.noRecentWorkspaces'), enabled: false }]
  }
}

function buildTrayMenu(
  state: TrayState,
  hubClient: HubClient,
  refresh: () => void,
  exitAll: () => Promise<void>
): Menu {
  const L = (key: string) => translate(state.locale, key)
  const appServerItems = state.appServers.length > 0
    ? state.appServers.map((server) => buildAppServerMenu(server, hubClient, refresh, state.locale))
    : [{ label: L('tray.noManagedAppServers'), enabled: false } satisfies MenuItemConstructorOptions]

  const template: MenuItemConstructorOptions[] = [
    { label: `${L('tray.hub')}: ${state.hubLabel}`, enabled: false },
    { type: 'separator' },
    {
      label: L('tray.openDotCraft'),
      click: () => spawnDesktopWindow()
    },
    buildRecentMenu(state.recentWorkspaces, state.locale),
    { type: 'separator' },
    {
      label: L('tray.appServers'),
      submenu: appServerItems
    },
    { type: 'separator' },
    {
      label: L('tray.refresh'),
      click: refresh
    },
    {
      label: L('tray.exit'),
      click: () => {
        void exitAll()
      }
    }
  ]

  return Menu.buildFromTemplate(template)
}

export async function runTrayProcess(): Promise<void> {
  const lock = tryAcquireTrayLock()
  if (!lock) {
    app.quit()
    return
  }

  let tray: Tray | null = new Tray(createTrayIcon())
  let settings: AppSettings = loadSettings()
  const hubClient = new HubClient({
    binarySource: settings.binarySource,
    binaryPath: settings.appServerBinaryPath
  })
  let eventAbortController: AbortController | null = null
  let refreshTimer: ReturnType<typeof setInterval> | null = null
  let disposed = false

  const setMenu = (state: TrayState): void => {
    if (!tray) return
    tray.setToolTip(`DotCraft Hub: ${state.hubLabel}`)
    tray.setContextMenu(buildTrayMenu(state, hubClient, () => {
      void refresh()
    }, exitAll))
  }

  const refresh = async (): Promise<void> => {
    if (disposed) return
    settings = loadSettings()
    try {
      const [status, appServers] = await Promise.all([
        hubClient.getStatus(),
        hubClient.listAppServers()
      ])
      setMenu({
        status: 'running',
        hubLabel: `${translate(normalizeLocale(settings.locale), 'tray.running')} (pid ${status.pid})`,
        appServers,
        recentWorkspaces: getRecentWorkspaces(settings),
        locale: normalizeLocale(settings.locale)
      })
      subscribeEvents()
    } catch {
      setMenu({
        status: 'offline',
        hubLabel: translate(normalizeLocale(settings.locale), 'tray.offline'),
        appServers: [],
        recentWorkspaces: getRecentWorkspaces(settings),
        locale: normalizeLocale(settings.locale)
      })
    }
  }

  const subscribeEvents = (): void => {
    if (eventAbortController) return
    const controller = new AbortController()
    eventAbortController = controller
    void hubClient.subscribeEvents((_event: HubEvent) => {
      void refresh()
    }, controller.signal).catch(() => {
      eventAbortController = null
    })
  }

  async function exitAll(): Promise<void> {
    disposed = true
    if (refreshTimer) {
      clearInterval(refreshTimer)
      refreshTimer = null
    }
    eventAbortController?.abort()
    eventAbortController = null
    try {
      await hubClient.shutdownHub()
    } catch {
      // Hub may already be stopped.
    }
    app.quit()
  }

  const cleanup = (lockHandle: TrayLockHandle): void => {
    disposed = true
    if (refreshTimer) {
      clearInterval(refreshTimer)
      refreshTimer = null
    }
    eventAbortController?.abort()
    eventAbortController = null
    tray?.destroy()
    tray = null
    lockHandle.release()
  }

  app.on('before-quit', () => cleanup(lock))

  setMenu({
    status: 'starting',
    hubLabel: translate(normalizeLocale(settings.locale), 'tray.starting'),
    appServers: [],
    recentWorkspaces: getRecentWorkspaces(settings),
    locale: normalizeLocale(settings.locale)
  })
  await refresh()
  refreshTimer = setInterval(() => {
    void refresh()
  }, REFRESH_INTERVAL_MS)
}

export function ensureTrayProcess(): void {
  if (process.argv.includes('--tray')) return

  const args = baseDesktopArgs()
  args.push('--tray')
  const child = spawn(process.execPath, args, {
    detached: true,
    stdio: 'ignore',
    windowsHide: true
  })
  child.unref()
}
