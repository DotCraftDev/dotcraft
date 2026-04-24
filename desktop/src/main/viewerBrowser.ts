import { BrowserWindow, WebContentsView, nativeImage, session, shell } from 'electron'
import { createHash } from 'crypto'
import { fileURLToPath } from 'url'
import type { BrowserEventPayload } from '../shared/viewer/types'
import { installViewerProtocolHandlerForSession, viewerUrlToPath } from './viewerFileProtocol'

const BROWSER_EVENT_CHANNEL = 'viewer:browser:event'
const START_URL = 'about:blank'
const VIEWER_SCHEME = 'dotcraft-viewer:'
const ALLOWED_SCHEMES = new Set(['http:', 'https:', VIEWER_SCHEME])
const EXTERNAL_HANDOFF_SCHEMES = new Set(['mailto:', 'tel:'])
const DEFAULT_START_TITLE = 'DotCraft Browser'

interface BrowserTabRuntime {
  tabId: string
  workspacePath: string
  view: WebContentsView
  desiredVisible: boolean
  visible: boolean
  boundsInitialized: boolean
  currentUrl: string
  title: string
  faviconDataUrl?: string
  allowFileScheme?: boolean
}

interface WindowRuntime {
  tabs: Map<string, BrowserTabRuntime>
  activeTabId: string | null
}

export interface BrowserSnapshot {
  tabId: string
  currentUrl: string
  title: string
  faviconDataUrl?: string
  canGoBack: boolean
  canGoForward: boolean
  loading: boolean
}

function emitBrowserEvent(win: BrowserWindow, payload: BrowserEventPayload): void {
  if (win.isDestroyed() || win.webContents.isDestroyed()) return
  win.webContents.send(BROWSER_EVENT_CHANNEL, payload)
}

function historyOf(webContents: Electron.WebContents): Electron.NavigationHistory {
  return webContents.navigationHistory
}

function ensureDataUrl(html: string): string {
  return `data:text/html;charset=utf-8,${encodeURIComponent(html)}`
}

function buildStartPageHtml(message: string): string {
  return `<!doctype html><html><head><meta charset="utf-8"><title>${DEFAULT_START_TITLE}</title><style>
  body{margin:0;font-family:system-ui,-apple-system,Segoe UI,Roboto,sans-serif;background:#1f1f1f;color:#d8d8d8;display:flex;align-items:center;justify-content:center;height:100vh}
  .wrap{max-width:520px;padding:24px 28px;border:1px solid rgba(255,255,255,0.12);border-radius:10px;background:rgba(255,255,255,0.03)}
  h1{margin:0 0 8px;font-size:18px}p{margin:0;font-size:13px;line-height:1.5;color:#b8b8b8}
  </style></head><body><div class="wrap"><h1>${DEFAULT_START_TITLE}</h1><p>${escapeHtml(message)}</p></div></body></html>`
}

function escapeHtml(input: string): string {
  return input
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;')
}

function extractScheme(raw: string): string | null {
  try {
    return new URL(raw).protocol.toLowerCase()
  } catch {
    return null
  }
}

export type BrowserNavigationDecision = 'allow' | 'external-handoff' | 'blocked'

export function classifyBrowserUrl(url: string): BrowserNavigationDecision {
  const scheme = extractScheme(url)
  if (!scheme) return 'blocked'
  if (ALLOWED_SCHEMES.has(scheme)) return 'allow'
  if (EXTERNAL_HANDOFF_SCHEMES.has(scheme)) return 'external-handoff'
  return 'blocked'
}

export function partitionForWorkspace(workspacePath: string): string {
  const normalized = workspacePath.trim().toLowerCase()
  const hash = createHash('sha1').update(normalized).digest('hex').slice(0, 12)
  return `persist:dotcraft-viewer:${hash}`
}

export function normalizeBrowserUrl(input: string): string | null {
  const trimmed = input.trim()
  if (!trimmed) return null
  if (/[\u0000-\u001f]/.test(trimmed)) return null

  const withScheme = /^[a-zA-Z][a-zA-Z\d+\-.]*:/.test(trimmed)
    ? trimmed
    : `https://${trimmed}`
  try {
    const parsed = new URL(withScheme)
    return parsed.toString()
  } catch {
    return null
  }
}

export async function loadOrReport(params: {
  tabId: string
  url: string
  load: () => Promise<unknown>
  emit: (payload: BrowserEventPayload) => void
}): Promise<void> {
  try {
    await params.load()
  } catch (error: unknown) {
    const message = error instanceof Error ? error.message : String(error)
    params.emit({
      tabId: params.tabId,
      type: 'did-fail-load',
      url: params.url,
      message
    })
    params.emit({
      tabId: params.tabId,
      type: 'did-stop-loading',
      url: params.url
    })
  }
}

export class ViewerBrowserManager {
  private readonly byWindowId = new Map<number, WindowRuntime>()
  private readonly configuredPartitions = new Set<string>()
  private startPageHint = 'Enter a URL in the address bar to begin browsing.'

  setStartPageHint(hint: string): void {
    this.startPageHint = hint.trim() || this.startPageHint
  }

  createTab(win: BrowserWindow, params: {
    tabId: string
    workspacePath: string
    initialUrl?: string
    allowFileScheme?: boolean
  }): BrowserSnapshot {
    const runtime = this.ensureWindowRuntime(win)
    const existing = runtime.tabs.get(params.tabId)
    if (existing) return this.snapshotFromRuntime(existing)

    const partition = partitionForWorkspace(params.workspacePath)
    const partitionSession = session.fromPartition(partition)
    this.configurePartitionSession(partition, partitionSession)

    const view = new WebContentsView({
      webPreferences: {
        session: partitionSession,
        contextIsolation: true,
        sandbox: true,
        nodeIntegration: false
      }
    })
    const tabRuntime: BrowserTabRuntime = {
      tabId: params.tabId,
      workspacePath: params.workspacePath,
      view,
      desiredVisible: false,
      visible: false,
      boundsInitialized: false,
      currentUrl: START_URL,
      title: DEFAULT_START_TITLE,
      allowFileScheme: params.allowFileScheme === true
    }
    runtime.tabs.set(params.tabId, tabRuntime)
    this.bindWebContentsEvents(win, tabRuntime)

    const desired = normalizeBrowserUrl(params.initialUrl ?? '') ?? START_URL
    if (desired === START_URL) {
      const startPageUrl = ensureDataUrl(buildStartPageHtml(this.startPageHint))
      void loadOrReport({
        tabId: params.tabId,
        url: START_URL,
        load: () => view.webContents.loadURL(startPageUrl),
        emit: (payload) => emitBrowserEvent(win, payload)
      })
    } else {
      void this.navigate(win, { tabId: params.tabId, url: desired })
    }

    emitBrowserEvent(win, {
      tabId: params.tabId,
      type: 'page-title-updated',
      title: DEFAULT_START_TITLE
    })
    return this.snapshotFromRuntime(tabRuntime)
  }

  destroyTab(win: BrowserWindow, tabId: string): void {
    const runtime = this.byWindowId.get(win.id)
    if (!runtime) return
    const tab = runtime.tabs.get(tabId)
    if (!tab) return

    this.detachView(win, tab)
    runtime.tabs.delete(tabId)
    if (!tab.view.webContents.isDestroyed()) {
      tab.view.webContents.close({ waitForBeforeUnload: false })
    }
    if (runtime.activeTabId === tabId) runtime.activeTabId = null
  }

  destroyAllTabs(win: BrowserWindow): void {
    const runtime = this.byWindowId.get(win.id)
    if (!runtime) return
    for (const tabId of [...runtime.tabs.keys()]) {
      this.destroyTab(win, tabId)
    }
    this.byWindowId.delete(win.id)
  }

  createAutomationTab(win: BrowserWindow, params: {
    tabId: string
    workspacePath: string
    initialUrl?: string
    width?: number
    height?: number
    allowFileScheme?: boolean
  }): BrowserSnapshot {
    const snapshot = this.createTab(win, {
      tabId: params.tabId,
      workspacePath: params.workspacePath,
      initialUrl: params.initialUrl,
      allowFileScheme: params.allowFileScheme
    })
    const tab = this.getTab(win, params.tabId)
    if (tab) {
      // Keep automation pages at a useful capture size before the renderer has
      // measured the actual detail-panel slot. This must not count as initialized
      // UI bounds, otherwise addChildView can briefly cover the whole window.
      tab.view.setBounds({
        x: -10000,
        y: -10000,
        width: Math.max(1, Math.round(params.width ?? 1280)),
        height: Math.max(1, Math.round(params.height ?? 900))
      })
    }
    return snapshot
  }

  getTabWebContents(win: BrowserWindow, tabId: string): Electron.WebContents | null {
    const tab = this.getTab(win, tabId)
    if (!tab || tab.view.webContents.isDestroyed()) return null
    return tab.view.webContents
  }

  async loadAutomationUrl(win: BrowserWindow, params: { tabId: string; url: string }): Promise<void> {
    const tab = this.getTab(win, params.tabId)
    if (!tab) return
    tab.currentUrl = params.url
    await loadOrReport({
      tabId: params.tabId,
      url: params.url,
      load: () => tab.view.webContents.loadURL(params.url),
      emit: (payload) => emitBrowserEvent(win, payload)
    })
  }

  async navigate(win: BrowserWindow, params: { tabId: string; url: string }): Promise<void> {
    const tab = this.getTab(win, params.tabId)
    if (!tab) return
    const normalized = normalizeBrowserUrl(params.url)
    if (!normalized) return

    const navigationDecision = this.classifyUrlForTab(tab, normalized)
    if (navigationDecision === 'external-handoff') {
      await shell.openExternal(normalized)
      emitBrowserEvent(win, { tabId: params.tabId, type: 'external-handoff', url: normalized })
      return
    }
    if (navigationDecision !== 'allow') {
      emitBrowserEvent(win, {
        tabId: params.tabId,
        type: 'blocked-navigation',
        message: `Blocked scheme: ${extractScheme(normalized) ?? 'unknown'}`
      })
      return
    }

    tab.currentUrl = normalized
    await loadOrReport({
      tabId: params.tabId,
      url: normalized,
      load: () => tab.view.webContents.loadURL(normalized),
      emit: (payload) => emitBrowserEvent(win, payload)
    })
  }

  goBack(win: BrowserWindow, tabId: string): void {
    const tab = this.getTab(win, tabId)
    if (!tab) return
    const history = historyOf(tab.view.webContents)
    if (history.canGoBack()) history.goBack()
  }

  goForward(win: BrowserWindow, tabId: string): void {
    const tab = this.getTab(win, tabId)
    if (!tab) return
    const history = historyOf(tab.view.webContents)
    if (history.canGoForward()) history.goForward()
  }

  reload(win: BrowserWindow, tabId: string): void {
    const tab = this.getTab(win, tabId)
    if (!tab) return
    tab.view.webContents.reload()
  }

  stop(win: BrowserWindow, tabId: string): void {
    const tab = this.getTab(win, tabId)
    if (!tab) return
    tab.view.webContents.stop()
  }

  setBounds(win: BrowserWindow, params: { tabId: string; x: number; y: number; width: number; height: number }): void {
    const tab = this.getTab(win, params.tabId)
    if (!tab) return
    const width = Math.max(1, Math.round(params.width))
    const height = Math.max(1, Math.round(params.height))
    tab.view.setBounds({
      x: Math.round(params.x),
      y: Math.round(params.y),
      width,
      height
    })
    tab.boundsInitialized = true
    if (tab.desiredVisible && !tab.visible) {
      this.attachView(win, tab)
    }
  }

  setVisible(win: BrowserWindow, params: { tabId: string; visible: boolean }): void {
    const tab = this.getTab(win, params.tabId)
    if (!tab) return
    tab.desiredVisible = params.visible
    if (params.visible) {
      if (tab.boundsInitialized) this.attachView(win, tab)
    } else {
      this.detachView(win, tab)
    }
  }

  setActiveTab(win: BrowserWindow, tabId: string): void {
    const runtime = this.byWindowId.get(win.id)
    if (!runtime) return
    runtime.activeTabId = tabId
    for (const tab of runtime.tabs.values()) {
      const visible = tab.tabId === tabId
      this.setVisible(win, { tabId: tab.tabId, visible })
    }
    const active = runtime.tabs.get(tabId)
    if (!active) return
    this.emitHistoryFlags(win, active)
  }

  async openInOsBrowser(win: BrowserWindow, tabId: string): Promise<void> {
    const tab = this.getTab(win, tabId)
    if (!tab) return
    const current = tab.currentUrl || tab.view.webContents.getURL()
    const scheme = extractScheme(current)
    if (!scheme || this.classifyUrlForTab(tab, current) === 'blocked') return
    if (scheme === VIEWER_SCHEME) {
      await shell.openPath(viewerUrlToPath(current))
      return
    }
    if (scheme === 'file:') {
      await shell.openPath(fileURLToPath(current))
      return
    }
    await shell.openExternal(current)
  }

  snapshotState(win: BrowserWindow, tabId: string): BrowserSnapshot | null {
    const tab = this.getTab(win, tabId)
    if (!tab) return null
    return this.snapshotFromRuntime(tab)
  }

  private snapshotFromRuntime(tab: BrowserTabRuntime): BrowserSnapshot {
    const history = historyOf(tab.view.webContents)
    return {
      tabId: tab.tabId,
      currentUrl: tab.currentUrl,
      title: tab.title,
      faviconDataUrl: tab.faviconDataUrl,
      canGoBack: history.canGoBack(),
      canGoForward: history.canGoForward(),
      loading: tab.view.webContents.isLoading()
    }
  }

  private ensureWindowRuntime(win: BrowserWindow): WindowRuntime {
    const existing = this.byWindowId.get(win.id)
    if (existing) return existing
    const created: WindowRuntime = {
      tabs: new Map(),
      activeTabId: null
    }
    this.byWindowId.set(win.id, created)
    return created
  }

  private getTab(win: BrowserWindow, tabId: string): BrowserTabRuntime | null {
    return this.byWindowId.get(win.id)?.tabs.get(tabId) ?? null
  }

  private emitHistoryFlags(win: BrowserWindow, tab: BrowserTabRuntime): void {
    const history = historyOf(tab.view.webContents)
    emitBrowserEvent(win, {
      tabId: tab.tabId,
      type: 'update-history-flags',
      canGoBack: history.canGoBack(),
      canGoForward: history.canGoForward()
    })
  }

  private bindWebContentsEvents(win: BrowserWindow, tab: BrowserTabRuntime): void {
    const wc = tab.view.webContents

    wc.on('did-start-loading', () => {
      emitBrowserEvent(win, { tabId: tab.tabId, type: 'did-start-loading' })
      this.emitHistoryFlags(win, tab)
    })
    wc.on('did-stop-loading', () => {
      tab.currentUrl = wc.getURL() || tab.currentUrl
      emitBrowserEvent(win, { tabId: tab.tabId, type: 'did-stop-loading', url: tab.currentUrl })
      this.emitHistoryFlags(win, tab)
    })
    wc.on('did-navigate', (_event, url) => {
      tab.currentUrl = url
      emitBrowserEvent(win, { tabId: tab.tabId, type: 'did-navigate', url })
      this.emitHistoryFlags(win, tab)
    })
    wc.on('did-fail-load', (_event, errorCode, errorDescription, validatedURL, isMainFrame) => {
      if (!isMainFrame || errorCode === -3) return
      emitBrowserEvent(win, {
        tabId: tab.tabId,
        type: 'did-fail-load',
        url: validatedURL,
        message: errorDescription
      })
    })
    wc.on('page-title-updated', (event, title) => {
      event.preventDefault()
      tab.title = title || DEFAULT_START_TITLE
      emitBrowserEvent(win, { tabId: tab.tabId, type: 'page-title-updated', title: tab.title })
    })
    wc.on('page-favicon-updated', async (_event, favicons) => {
      const first = favicons[0]
      if (!first) return
      try {
        const res = await fetch(first)
        if (!res.ok) return
        const data = Buffer.from(await res.arrayBuffer())
        const image = nativeImage.createFromBuffer(data)
        if (image.isEmpty()) return
        tab.faviconDataUrl = image.toDataURL()
        emitBrowserEvent(win, {
          tabId: tab.tabId,
          type: 'page-favicon-updated',
          faviconDataUrl: tab.faviconDataUrl
        })
      } catch {
        // Best-effort favicon loading.
      }
    })
    wc.on('render-process-gone', () => {
      emitBrowserEvent(win, { tabId: tab.tabId, type: 'crashed' })
    })

    wc.on('will-navigate', (event, url) => {
      if (this.handleSchemeBoundary(win, tab, url)) {
        event.preventDefault()
      }
    })
    wc.on('will-redirect', (event, url) => {
      if (this.handleSchemeBoundary(win, tab, url)) {
        event.preventDefault()
      }
    })

    wc.setWindowOpenHandler((details) => {
      const normalized = normalizeBrowserUrl(details.url)
      if (!normalized) return { action: 'deny' }
      const navigationDecision = this.classifyUrlForTab(tab, normalized)
      if (navigationDecision === 'allow') {
        emitBrowserEvent(win, {
          tabId: tab.tabId,
          type: 'request-new-tab',
          url: normalized
        })
        return { action: 'deny' }
      }
      if (navigationDecision === 'external-handoff') {
        void shell.openExternal(normalized)
      } else {
        emitBrowserEvent(win, {
          tabId: tab.tabId,
          type: 'blocked-navigation',
          message: `Blocked scheme: ${extractScheme(normalized) ?? 'unknown'}`
        })
      }
      return { action: 'deny' }
    })
  }

  private classifyUrlForTab(tab: BrowserTabRuntime, url: string): BrowserNavigationDecision {
    const scheme = extractScheme(url)
    if (scheme === 'file:' && tab.allowFileScheme === true) return 'allow'
    return classifyBrowserUrl(url)
  }

  private handleSchemeBoundary(win: BrowserWindow, tab: BrowserTabRuntime, url: string): boolean {
    const scheme = extractScheme(url)
    const navigationDecision = this.classifyUrlForTab(tab, url)
    if (navigationDecision === 'allow') return false
    if (navigationDecision === 'external-handoff') {
      void shell.openExternal(url)
      emitBrowserEvent(win, { tabId: tab.tabId, type: 'external-handoff', url })
      return true
    }
    if (navigationDecision === 'blocked') {
      emitBrowserEvent(win, {
        tabId: tab.tabId,
        type: 'blocked-navigation',
        message: `Blocked scheme: ${scheme ?? 'unknown'}`
      })
      return true
    }
    return true
  }

  configurePartitionSession(partitionName: string, partitionSession: Electron.Session): void {
    if (this.configuredPartitions.has(partitionName)) return
    installViewerProtocolHandlerForSession(partitionSession)
    this.configuredPartitions.add(partitionName)

    partitionSession.on('will-download', (event, item, webContents) => {
      event.preventDefault()
      item.cancel()
      const win = BrowserWindow.fromWebContents(webContents)
      if (!win) return
      const runtime = this.byWindowId.get(win.id)
      if (!runtime) return
      for (const tab of runtime.tabs.values()) {
        if (tab.view.webContents.id === webContents.id) {
          emitBrowserEvent(win, {
            tabId: tab.tabId,
            type: 'download-blocked',
            message: 'Downloads are disabled in embedded browser tabs.'
          })
          return
        }
      }
    })

    partitionSession.setPermissionCheckHandler((_wc, permission) => {
      return permission === 'clipboard-sanitized-write'
    })
    partitionSession.setPermissionRequestHandler((_wc, permission, callback) => {
      callback(permission === 'clipboard-sanitized-write')
    })
  }

  private detachView(win: BrowserWindow, tab: BrowserTabRuntime): void {
    if (!tab.visible) return
    if (tab.view.webContents.isDestroyed()) {
      tab.visible = false
      return
    }
    try {
      win.contentView.removeChildView(tab.view)
    } catch {
      // Ignore removal races when window is tearing down.
    }
    tab.visible = false
  }

  private attachView(win: BrowserWindow, tab: BrowserTabRuntime): void {
    if (tab.visible) return
    if (tab.view.webContents.isDestroyed()) return
    win.contentView.addChildView(tab.view)
    tab.visible = true
  }
}

export const viewerBrowserManager = new ViewerBrowserManager()
export { BROWSER_EVENT_CHANNEL, START_URL }
