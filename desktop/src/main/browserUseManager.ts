import { BrowserWindow } from 'electron'
import vm from 'vm'
import { viewerBrowserManager } from './viewerBrowser'
import type { AppSettings } from './settings'
import {
  isBrowserUseUrlAllowed as isBrowserUseUrlAllowedByPolicy,
  normalizeBrowserUseDomainList,
  resolveBrowserUseNavigationDecision
} from './browserUsePolicy'

const BROWSER_USE_OPEN_CHANNEL = 'viewer:browser-use:open'
const BROWSER_USE_APPROVAL_REQUEST_CHANNEL = 'viewer:browser-use:approval-request'
const BROWSER_USE_APPROVAL_TIMEOUT_MS = 120_000

export interface BrowserUseEvaluateParams {
  threadId: string
  code: string
  timeoutMs?: number
  workspacePath?: string
}

export interface BrowserUseImageResult {
  mediaType: string
  dataBase64: string
}

export interface BrowserUseEvaluateResult {
  text?: string
  resultText?: string
  images: BrowserUseImageResult[]
  logs: string[]
  error?: string
}

export interface BrowserUseOpenPayload {
  threadId: string
  tabId: string
  initialUrl: string
  title?: string
  focusMode: 'first-open' | 'none'
}

export type BrowserUseApprovalResponseAction = 'allowOnce' | 'allowDomain' | 'blockDomain' | 'deny'

export interface BrowserUseApprovalRequestPayload {
  requestId: string
  threadId: string
  tabId: string
  url: string
  domain: string
  sessionName?: string
}

export interface BrowserUseApprovalResponsePayload {
  requestId: string
  action: BrowserUseApprovalResponseAction
}

interface BrowserUseViewerHost {
  createAutomationTab(win: BrowserWindow, params: {
    tabId: string
    threadId?: string
    workspacePath: string
    initialUrl?: string
    width?: number
    height?: number
    allowFileScheme?: boolean
  }): unknown
  getTabWebContents(win: BrowserWindow, tabId: string): Electron.WebContents | null
  getAutomationTargetTab?(win: BrowserWindow, threadId: string): {
    tabId: string
    currentUrl: string
    title: string
    loading: boolean
  } | null
  loadAutomationUrl(win: BrowserWindow, params: { tabId: string; url: string }): Promise<void>
  destroyTab(win: BrowserWindow, tabId: string): void
  snapshotState(win: BrowserWindow, tabId: string): {
    tabId: string
    currentUrl: string
    title: string
    loading: boolean
  } | null
  setAutomationState(win: BrowserWindow, params: {
    tabId: string
    active: boolean
    sessionName?: string
    action?: string
  }): void
  moveMouse(win: BrowserWindow, params: { tabId: string; x: number; y: number; waitForArrival?: boolean }): Promise<void>
  clickMouse(win: BrowserWindow, params: { tabId: string; x: number; y: number; button?: 'left' | 'right' | 'middle' }): Promise<void>
  doubleClickMouse(win: BrowserWindow, params: { tabId: string; x: number; y: number; button?: 'left' | 'right' | 'middle' }): Promise<void>
  dragMouse(win: BrowserWindow, params: { tabId: string; path: Array<{ x: number; y: number }> }): Promise<void>
  scrollMouse(win: BrowserWindow, params: { tabId: string; x: number; y: number; scrollX: number; scrollY: number }): Promise<void>
  typeText(win: BrowserWindow, params: { tabId: string; text: string }): Promise<void>
  keypress(win: BrowserWindow, params: { tabId: string; keys: string[] }): void
}

interface BrowserUsePolicyHost {
  getSettings(): AppSettings
  updateSettings(partial: Partial<AppSettings>): Promise<void>
}

interface BrowserUseTabRuntime {
  id: string
  owner: BrowserWindow
  logs: BrowserUseLogEntry[]
  adopted?: boolean
}

interface BrowserUseLogEntry {
  level: string
  message: string
  timestamp: string
  url?: string
}

interface BrowserUseThreadRuntime {
  threadId: string
  workspacePath: string
  sessionName?: string
  context: vm.Context
  tabs: Map<string, BrowserUseTabRuntime>
  selectedTabId: string | null
  logs: string[]
  images: BrowserUseImageResult[]
  hasFocusedFirstTab: boolean
}

type BrowserUseLocatorKind = 'css' | 'text' | 'role' | 'label' | 'placeholder' | 'testId'

interface BrowserUseLocatorDescriptor {
  kind: BrowserUseLocatorKind
  value: string
  exact?: boolean
  name?: string
}

interface BrowserUseElementMatch {
  index: number
  tagName: string
  visibleText: string
  ariaName: string
  boundingBox: {
    x: number
    y: number
    width: number
    height: number
  } | null
}

export function normalizeBrowserUseUrl(input: string): string | null {
  const trimmed = input.trim()
  if (!trimmed || /[\u0000-\u001f]/.test(trimmed)) return null
  if (trimmed === 'about:blank') return trimmed
  const looksLikeLocalHost =
    /^(localhost|127\.0\.0\.1|\[?::1\]?)(:\d+)?(\/|$)/i.test(trimmed)
  const withScheme = looksLikeLocalHost
    ? `http://${trimmed}`
    : /^[a-zA-Z][a-zA-Z\d+\-.]*:/.test(trimmed)
      ? trimmed
      : `https://${trimmed}`
  try {
    return new URL(withScheme).toString()
  } catch {
    return null
  }
}

export function isBrowserUseUrlAllowed(url: string): boolean {
  return isBrowserUseUrlAllowedByPolicy(url)
}

function imageFromDataUrl(dataUrl: string): BrowserUseImageResult | null {
  const match = /^data:([^;,]+);base64,(.*)$/i.exec(dataUrl)
  if (!match) return null
  return { mediaType: match[1], dataBase64: match[2] }
}

function describeResult(value: unknown): string {
  if (value == null) return ''
  if (typeof value === 'string') return value
  try {
    return JSON.stringify(value, null, 2)
  } catch {
    return String(value)
  }
}

function sanitizeThreadId(threadId: string): string {
  return threadId.replace(/[^a-zA-Z0-9_-]/g, '_')
}

export class BrowserUseManager {
  private readonly runtimes = new Map<string, BrowserUseThreadRuntime>()
  private readonly pendingApprovals = new Map<string, {
    resolve: (action: BrowserUseApprovalResponseAction) => void
    timer: ReturnType<typeof setTimeout>
    onClosed: () => void
    owner: BrowserWindow
  }>()
  private nextTabId = 1
  private nextApprovalId = 1
  private policyHost: BrowserUsePolicyHost | null = null

  constructor(private readonly viewerHost: BrowserUseViewerHost = viewerBrowserManager) {}

  setPolicyHost(host: BrowserUsePolicyHost): void {
    this.policyHost = host
  }

  handleApprovalResponse(payload: BrowserUseApprovalResponsePayload): boolean {
    const pending = this.pendingApprovals.get(payload.requestId)
    if (!pending) return false
    this.pendingApprovals.delete(payload.requestId)
    clearTimeout(pending.timer)
    pending.owner.off('closed', pending.onClosed)
    pending.resolve(payload.action)
    return true
  }

  async evaluate(owner: BrowserWindow, params: BrowserUseEvaluateParams): Promise<BrowserUseEvaluateResult> {
    const runtime = this.getOrCreateRuntime(owner, params.threadId, params.workspacePath)
    runtime.logs = []
    runtime.images = []

    const timeoutMs = Math.max(1_000, Math.min(params.timeoutMs ?? 30_000, 120_000))
    try {
      const script = new vm.Script(`(async () => {\n${params.code}\n})()`)
      const run = script.runInContext(runtime.context, { timeout: timeoutMs }) as Promise<unknown>
      const value = await this.withTimeout(run, timeoutMs)
      return {
        resultText: describeResult(value),
        images: [...runtime.images],
        logs: [...runtime.logs]
      }
    } catch (error: unknown) {
      return {
        error: error instanceof Error ? error.message : String(error),
        images: [...runtime.images],
        logs: [...runtime.logs]
      }
    }
  }

  reset(threadId: string): { ok: boolean } {
    const runtime = this.runtimes.get(threadId)
    if (!runtime) return { ok: false }
    for (const tab of [...runtime.tabs.values()]) {
      if (tab.adopted) {
        this.setAutomationState(runtime, tab, false)
      } else {
        this.viewerHost.destroyTab(tab.owner, tab.id)
      }
    }
    this.runtimes.delete(threadId)
    return { ok: true }
  }

  private getOrCreateRuntime(
    owner: BrowserWindow,
    threadId: string,
    workspacePath?: string
  ): BrowserUseThreadRuntime {
    const existing = this.runtimes.get(threadId)
    if (existing) return existing

    const resolvedWorkspace = workspacePath || ''
    const runtime: BrowserUseThreadRuntime = {
      threadId,
      workspacePath: resolvedWorkspace,
      context: vm.createContext({}, { codeGeneration: { strings: false, wasm: false } }),
      tabs: new Map<string, BrowserUseTabRuntime>(),
      selectedTabId: null,
      logs: [],
      images: [],
      hasFocusedFirstTab: false
    }

    const display = async (imageLike: unknown): Promise<void> => {
      if (typeof imageLike === 'string') {
        const image = imageFromDataUrl(imageLike)
        if (image) runtime.images.push(image)
        return
      }
      if (imageLike && typeof imageLike === 'object') {
        const obj = imageLike as Partial<BrowserUseImageResult> & { mimeType?: string }
        const dataBase64 = typeof obj.dataBase64 === 'string' ? obj.dataBase64 : ''
        if (dataBase64) {
          runtime.images.push({
            mediaType: obj.mediaType ?? obj.mimeType ?? 'image/png',
            dataBase64
          })
        }
      }
    }

    const agent = {
      browser: {
        nameSession: async (name: string) => {
          runtime.sessionName = String(name ?? '').trim()
          for (const tab of runtime.tabs.values()) {
            this.setAutomationState(runtime, tab, true, 'session')
          }
          return { ok: true, name: runtime.sessionName }
        },
        goto: async (url: string) => {
          const tab = await this.getOrAdoptSelectedTab(owner, runtime)
          await this.navigate(tab, url)
          return this.createTabApi(tab)
        },
        tabs: {
          list: async () => [...runtime.tabs.values()].map((tab) => this.tabSnapshot(tab)),
          new: async (url?: string) => {
            const tab = await this.createTab(owner, runtime, url)
            runtime.selectedTabId = tab.id
            return this.createTabApi(tab)
          },
          selected: async () => {
            const tab = await this.getOrAdoptSelectedTab(owner, runtime)
            return this.createTabApi(tab)
          },
          get: async (id: string) => {
            const tab = runtime.tabs.get(id)
            if (!tab) throw new Error(`Browser tab not found: ${id}`)
            return this.createTabApi(tab)
          }
        }
      }
    }

    const consoleApi = {
      log: (...args: unknown[]) => runtime.logs.push(args.map(describeResult).join(' ')),
      warn: (...args: unknown[]) => runtime.logs.push(args.map(describeResult).join(' ')),
      error: (...args: unknown[]) => runtime.logs.push(args.map(describeResult).join(' '))
    }

    Object.assign(runtime.context, {
      agent,
      display,
      console: consoleApi,
      setTimeout,
      clearTimeout,
      Promise,
      URL
    })

    this.runtimes.set(threadId, runtime)
    return runtime
  }

  private async createTab(
    owner: BrowserWindow,
    runtime: BrowserUseThreadRuntime,
    initialUrl?: string
  ): Promise<BrowserUseTabRuntime> {
    const normalizedInitial = initialUrl ? normalizeBrowserUseUrl(initialUrl) : null
    if (initialUrl && !normalizedInitial) throw new Error(`Invalid browser URL: ${initialUrl}`)
    const id = `browser-use-${sanitizeThreadId(runtime.threadId)}-${this.nextTabId++}`
    if (normalizedInitial) {
      await this.ensureNavigationAllowed(owner, runtime, id, normalizedInitial)
    }

    this.viewerHost.createAutomationTab(owner, {
      tabId: id,
      threadId: runtime.threadId,
      workspacePath: runtime.workspacePath || owner.getTitle(),
      initialUrl: 'about:blank',
      width: 1280,
      height: 900,
      allowFileScheme: true
    })

    const tab = this.registerTab(owner, runtime, id, false)

    const focusMode = runtime.hasFocusedFirstTab ? 'none' : 'first-open'
    runtime.hasFocusedFirstTab = true
    this.emitOpen(owner, {
      threadId: runtime.threadId,
      tabId: id,
      initialUrl: normalizedInitial ?? 'about:blank',
      title: runtime.sessionName?.trim() || 'Browser Use',
      focusMode
    })

    if (normalizedInitial) await this.navigate(tab, normalizedInitial, { skipPolicyCheck: true })
    return tab
  }

  private emitOpen(owner: BrowserWindow, payload: BrowserUseOpenPayload): void {
    if (owner.isDestroyed() || owner.webContents.isDestroyed()) return
    owner.webContents.send(BROWSER_USE_OPEN_CHANNEL, payload)
  }

  private webContentsFor(owner: BrowserWindow, tabId: string): Electron.WebContents {
    const wc = this.viewerHost.getTabWebContents(owner, tabId)
    if (!wc || wc.isDestroyed()) throw new Error(`Browser tab is no longer available: ${tabId}`)
    return wc
  }

  private getSelectedTab(runtime: BrowserUseThreadRuntime): BrowserUseTabRuntime | null {
    if (runtime.selectedTabId) {
      const existing = runtime.tabs.get(runtime.selectedTabId)
      if (existing) return existing
    }
    const first = runtime.tabs.values().next().value as BrowserUseTabRuntime | undefined
    return first ?? null
  }

  private async getOrAdoptSelectedTab(
    owner: BrowserWindow,
    runtime: BrowserUseThreadRuntime
  ): Promise<BrowserUseTabRuntime> {
    if (runtime.selectedTabId) {
      const existing = runtime.tabs.get(runtime.selectedTabId)
      if (existing) return existing
    }

    const candidate = this.viewerHost.getAutomationTargetTab?.(owner, runtime.threadId)
    if (candidate) {
      const adopted = this.registerTab(owner, runtime, candidate.tabId, true)
      runtime.selectedTabId = adopted.id
      return adopted
    }

    const selected = this.getSelectedTab(runtime)
    if (selected) return selected

    const created = await this.createTab(owner, runtime)
    runtime.selectedTabId = created.id
    return created
  }

  private registerTab(
    owner: BrowserWindow,
    runtime: BrowserUseThreadRuntime,
    id: string,
    adopted: boolean
  ): BrowserUseTabRuntime {
    const existing = runtime.tabs.get(id)
    if (existing) return existing
    const wc = this.webContentsFor(owner, id)
    const tab: BrowserUseTabRuntime = { id, owner, logs: [], adopted }
    runtime.tabs.set(id, tab)

    wc.on('console-message', (_event, level, message) => {
      const levelNames = ['verbose', 'info', 'warning', 'error'] as const
      tab.logs.push({
        level: levelNames[level as number] ?? String(level ?? 'log'),
        message,
        timestamp: new Date().toISOString(),
        url: wc.getURL()
      })
    })
    wc.once('destroyed', () => {
      runtime.tabs.delete(id)
      if (runtime.selectedTabId === id) runtime.selectedTabId = null
    })
    return tab
  }

  private createTabApi(tab: BrowserUseTabRuntime): Record<string, unknown> {
    return {
      id: tab.id,
      navigate: async (url: string) => this.navigate(tab, url),
      goto: async (url: string) => this.navigate(tab, url),
      back: async () => this.goBack(tab),
      forward: async () => this.goForward(tab),
      reload: async () => this.reload(tab),
      close: async () => this.closeTab(tab),
      url: async () => this.webContentsFor(tab.owner, tab.id).getURL(),
      title: async () => this.webContentsFor(tab.owner, tab.id).getTitle(),
      screenshot: async (options?: { fullPage?: boolean; clip?: Electron.Rectangle }) => this.screenshot(tab, options),
      domSnapshot: async () => this.domSnapshot(tab),
      evaluate: async (expressionOrFunction: string | (() => unknown)) => this.evaluateInPage(tab, expressionOrFunction),
      click: async (selector: string) => this.click(tab, selector),
      type: async (selector: string, text: string) => this.type(tab, selector, text),
      press: async (selector: string, key: string) => this.press(tab, selector, key),
      waitForLoadState: async (_state = 'load', timeoutMs = 30_000) => this.waitForLoad(tab, timeoutMs),
      consoleLogs: async () => tab.logs.map((entry) => entry.message),
      playwright: this.createPlaywrightApi(tab),
      cua: this.createCuaApi(tab),
      dev: {
        logs: async (options?: { filter?: string; levels?: string[]; limit?: number }) => this.devLogs(tab, options)
      },
      clipboard: {
        readText: async () => this.webContentsFor(tab.owner, tab.id).executeJavaScript('navigator.clipboard.readText()'),
        writeText: async (text: string) => this.webContentsFor(tab.owner, tab.id).executeJavaScript(
          `navigator.clipboard.writeText(${JSON.stringify(String(text ?? ''))})`
        )
      }
    }
  }

  private createCuaApi(tab: BrowserUseTabRuntime): Record<string, unknown> {
    return {
      move: async (options: { x: number; y: number; waitForArrival?: boolean }) => this.cuaMove(tab, options),
      click: async (options: { x: number; y: number; button?: number | string }) => this.cuaClick(tab, options),
      double_click: async (options: { x: number; y: number; button?: number | string }) => this.cuaDoubleClick(tab, options),
      drag: async (options: { path: Array<{ x: number; y: number }> }) => this.cuaDrag(tab, options),
      scroll: async (options: { x: number; y: number; scrollX: number; scrollY: number }) => this.cuaScroll(tab, options),
      type: async (options: { text: string }) => this.cuaType(tab, options),
      keypress: async (options: { keys: string[] }) => this.cuaKeypress(tab, options),
      get_visible_screenshot: async () => this.screenshot(tab)
    }
  }

  private createPlaywrightApi(tab: BrowserUseTabRuntime): Record<string, unknown> {
    return {
      domSnapshot: async () => this.domSnapshot(tab),
      screenshot: async (options?: { fullPage?: boolean; clip?: Electron.Rectangle }) => this.screenshot(tab, options),
      waitForLoadState: async (options?: { state?: string; timeoutMs?: number }) => this.waitForLoad(tab, options?.timeoutMs ?? 30_000),
      waitForTimeout: async (timeoutMs: number) => new Promise((resolve) => {
        setTimeout(resolve, Math.max(0, Math.min(timeoutMs, 120_000)))
      }),
      waitForURL: async (url: string, options?: { timeoutMs?: number }) => this.waitForUrl(tab, url, options?.timeoutMs ?? 30_000),
      expectNavigation: async <T>(action: () => Promise<T>, options?: { timeoutMs?: number; url?: string }) => {
        const result = await action()
        if (options?.url) {
          await this.waitForUrl(tab, options.url, options.timeoutMs ?? 30_000)
        } else {
          await this.waitForLoad(tab, options?.timeoutMs ?? 30_000)
        }
        return result
      },
      locator: (selector: string) => this.createLocatorApi(tab, { kind: 'css', value: String(selector) }),
      getByTestId: (testId: string) => this.createLocatorApi(tab, { kind: 'testId', value: String(testId) }),
      getByText: (text: string, options?: { exact?: boolean }) => this.createLocatorApi(tab, {
        kind: 'text',
        value: String(text),
        exact: options?.exact === true
      }),
      getByLabel: (text: string, options?: { exact?: boolean }) => this.createLocatorApi(tab, {
        kind: 'label',
        value: String(text),
        exact: options?.exact === true
      }),
      getByPlaceholder: (text: string, options?: { exact?: boolean }) => this.createLocatorApi(tab, {
        kind: 'placeholder',
        value: String(text),
        exact: options?.exact === true
      }),
      getByRole: (role: string, options?: { exact?: boolean; name?: string }) => this.createLocatorApi(tab, {
        kind: 'role',
        value: String(role),
        exact: options?.exact === true,
        name: options?.name == null ? undefined : String(options.name)
      }),
      frameLocator: () => {
        throw new Error('Browser Use frameLocator is not supported in this Desktop runtime yet.')
      }
    }
  }

  private createLocatorApi(tab: BrowserUseTabRuntime, descriptor: BrowserUseLocatorDescriptor): Record<string, unknown> {
    return {
      count: async () => (await this.resolveLocator(tab, descriptor)).length,
      click: async () => this.locatorClick(tab, descriptor),
      dblclick: async () => this.locatorDoubleClick(tab, descriptor),
      fill: async (value: string) => this.locatorFill(tab, descriptor, value),
      type: async (value: string) => this.locatorType(tab, descriptor, value),
      press: async (value: string) => this.locatorPress(tab, descriptor, value),
      innerText: async () => (await this.strictLocator(tab, descriptor)).visibleText,
      textContent: async () => this.locatorEvaluate(tab, descriptor, 'textContent'),
      getAttribute: async (name: string) => this.locatorEvaluate(tab, descriptor, 'getAttribute', name),
      isVisible: async () => (await this.resolveLocator(tab, descriptor)).length > 0,
      isEnabled: async () => this.locatorEvaluate(tab, descriptor, 'isEnabled'),
      waitFor: async (options?: { state?: string; timeoutMs?: number }) => this.locatorWaitFor(tab, descriptor, options),
      getByText: (text: string, options?: { exact?: boolean }) => this.createLocatorApi(tab, {
        kind: 'text',
        value: String(text),
        exact: options?.exact === true
      }),
      getByRole: (role: string, options?: { exact?: boolean; name?: string }) => this.createLocatorApi(tab, {
        kind: 'role',
        value: String(role),
        exact: options?.exact === true,
        name: options?.name == null ? undefined : String(options.name)
      }),
      getByTestId: (testId: string) => this.createLocatorApi(tab, { kind: 'testId', value: String(testId) }),
      locator: (selector: string) => this.createLocatorApi(tab, { kind: 'css', value: String(selector) }),
      first: () => this.createLocatorApi(tab, descriptor),
      last: () => this.createLocatorApi(tab, descriptor),
      nth: () => this.createLocatorApi(tab, descriptor)
    }
  }

  private tabSnapshot(tab: BrowserUseTabRuntime): Record<string, unknown> {
    const snapshot = this.viewerHost.snapshotState(tab.owner, tab.id)
    if (snapshot) {
      return {
        id: tab.id,
        url: snapshot.currentUrl,
        title: snapshot.title,
        loading: snapshot.loading
      }
    }
    const wc = this.webContentsFor(tab.owner, tab.id)
    return {
      id: tab.id,
      url: wc.getURL(),
      title: wc.getTitle(),
      loading: wc.isLoading()
    }
  }

  private setAutomationState(
    runtime: BrowserUseThreadRuntime,
    tab: BrowserUseTabRuntime,
    active: boolean,
    action?: string
  ): void {
    this.viewerHost.setAutomationState(tab.owner, {
      tabId: tab.id,
      active,
      sessionName: runtime.sessionName,
      action
    })
  }

  private markAutomation(tab: BrowserUseTabRuntime, action: string): void {
    const runtime = this.getRuntimeForTab(tab)
    this.setAutomationState(runtime, tab, true, action)
  }

  private async goBack(tab: BrowserUseTabRuntime): Promise<Record<string, unknown>> {
    this.markAutomation(tab, 'back')
    const wc = this.webContentsFor(tab.owner, tab.id)
    if (wc.navigationHistory.canGoBack()) wc.navigationHistory.goBack()
    await this.waitForLoad(tab, 30_000).catch(() => {})
    return this.tabSnapshot(tab)
  }

  private async goForward(tab: BrowserUseTabRuntime): Promise<Record<string, unknown>> {
    this.markAutomation(tab, 'forward')
    const wc = this.webContentsFor(tab.owner, tab.id)
    if (wc.navigationHistory.canGoForward()) wc.navigationHistory.goForward()
    await this.waitForLoad(tab, 30_000).catch(() => {})
    return this.tabSnapshot(tab)
  }

  private async reload(tab: BrowserUseTabRuntime): Promise<Record<string, unknown>> {
    this.markAutomation(tab, 'reload')
    this.webContentsFor(tab.owner, tab.id).reload()
    await this.waitForLoad(tab, 30_000).catch(() => {})
    return this.tabSnapshot(tab)
  }

  private closeTab(tab: BrowserUseTabRuntime): void {
    this.markAutomation(tab, 'close')
    this.viewerHost.destroyTab(tab.owner, tab.id)
  }

  private async navigate(
    tab: BrowserUseTabRuntime,
    url: string,
    options: { skipPolicyCheck?: boolean } = {}
  ): Promise<Record<string, unknown>> {
    const normalized = normalizeBrowserUseUrl(url)
    if (!normalized) throw new Error(`Invalid browser URL: ${url}`)
    this.markAutomation(tab, 'navigate')
    if (options.skipPolicyCheck !== true) {
      const runtime = this.getRuntimeForTab(tab)
      await this.ensureNavigationAllowed(tab.owner, runtime, tab.id, normalized)
    }
    await this.viewerHost.loadAutomationUrl(tab.owner, { tabId: tab.id, url: normalized })
    return this.tabSnapshot(tab)
  }

  private getRuntimeForTab(tab: BrowserUseTabRuntime): BrowserUseThreadRuntime {
    for (const runtime of this.runtimes.values()) {
      if (runtime.tabs.get(tab.id) === tab) return runtime
    }
    throw new Error(`Browser tab is no longer attached to a runtime: ${tab.id}`)
  }

  private async ensureNavigationAllowed(
    owner: BrowserWindow,
    runtime: BrowserUseThreadRuntime,
    tabId: string,
    url: string
  ): Promise<void> {
    const settings = this.policyHost?.getSettings().browserUse
    const decision = resolveBrowserUseNavigationDecision(url, settings)
    if (decision.kind === 'allow') return
    if (decision.kind === 'block') throw new Error(decision.reason)

    const action = await this.requestApproval(owner, {
      requestId: `browser-use-approval-${this.nextApprovalId++}`,
      threadId: runtime.threadId,
      tabId,
      url,
      domain: decision.domain,
      sessionName: runtime.sessionName
    })

    if (action === 'allowOnce') return
    if (action === 'allowDomain') {
      await this.addDomainToBrowserUseSettings(decision.domain, 'allowedDomains')
      return
    }
    if (action === 'blockDomain') {
      await this.addDomainToBrowserUseSettings(decision.domain, 'blockedDomains')
      throw new Error(`Blocked browser-use domain: ${decision.domain}`)
    }
    throw new Error(`Browser-use navigation denied for domain: ${decision.domain}`)
  }

  private requestApproval(
    owner: BrowserWindow,
    payload: BrowserUseApprovalRequestPayload
  ): Promise<BrowserUseApprovalResponseAction> {
    if (owner.isDestroyed() || owner.webContents.isDestroyed()) {
      return Promise.resolve('deny')
    }
    return new Promise((resolve) => {
      const onClosed = () => {
        this.pendingApprovals.delete(payload.requestId)
        clearTimeout(timer)
        resolve('deny')
      }
      const timer = setTimeout(() => {
        owner.off('closed', onClosed)
        this.pendingApprovals.delete(payload.requestId)
        resolve('deny')
      }, BROWSER_USE_APPROVAL_TIMEOUT_MS)
      this.pendingApprovals.set(payload.requestId, { resolve, timer, onClosed, owner })
      owner.once('closed', onClosed)
      owner.webContents.send(BROWSER_USE_APPROVAL_REQUEST_CHANNEL, payload)
    })
  }

  private async addDomainToBrowserUseSettings(
    domain: string,
    listName: 'allowedDomains' | 'blockedDomains'
  ): Promise<void> {
    if (!this.policyHost) return
    const current = this.policyHost.getSettings().browserUse ?? {}
    const allowedDomains = normalizeBrowserUseDomainList(current.allowedDomains)
    const blockedDomains = normalizeBrowserUseDomainList(current.blockedDomains)
    if (listName === 'allowedDomains') {
      await this.policyHost.updateSettings({
        browserUse: {
          ...current,
          allowedDomains: Array.from(new Set([...allowedDomains, domain])),
          blockedDomains: blockedDomains.filter((item) => item !== domain)
        }
      })
      return
    }
    await this.policyHost.updateSettings({
      browserUse: {
        ...current,
        blockedDomains: Array.from(new Set([...blockedDomains, domain])),
        allowedDomains: allowedDomains.filter((item) => item !== domain)
      }
    })
  }

  private async screenshot(
    tab: BrowserUseTabRuntime,
    options?: { fullPage?: boolean; clip?: Electron.Rectangle }
  ): Promise<BrowserUseImageResult> {
    this.markAutomation(tab, 'screenshot')
    const image = await this.webContentsFor(tab.owner, tab.id).capturePage(options?.clip)
    return {
      mediaType: 'image/png',
      dataBase64: image.toPNG().toString('base64')
    }
  }

  private async domSnapshot(tab: BrowserUseTabRuntime): Promise<string> {
    return String(await this.webContentsFor(tab.owner, tab.id).executeJavaScript(`
      (() => {
        const interesting = ['a','button','input','textarea','select','summary','[role="button"]','[role="link"]'];
        const labels = Array.from(document.querySelectorAll(interesting.join(','))).slice(0, 200).map((el) => {
          const tag = el.tagName.toLowerCase();
          const text = (el.innerText || el.value || el.getAttribute('aria-label') || el.getAttribute('title') || '').trim().replace(/\\s+/g, ' ');
          const id = el.id ? '#' + el.id : '';
          const name = el.getAttribute('name') ? '[name="' + el.getAttribute('name') + '"]' : '';
          return tag + id + name + (text ? ' "' + text.slice(0, 120) + '"' : '');
        });
        const bodyText = (document.body?.innerText || '').trim().replace(/\\s+/g, ' ').slice(0, 4000);
        return JSON.stringify({ title: document.title, url: location.href, bodyText, elements: labels }, null, 2);
      })()
    `))
  }

  private async evaluateInPage(tab: BrowserUseTabRuntime, expressionOrFunction: string | (() => unknown)): Promise<unknown> {
    const source = typeof expressionOrFunction === 'function'
      ? `(${expressionOrFunction.toString()})()`
      : String(expressionOrFunction)
    return this.webContentsFor(tab.owner, tab.id).executeJavaScript(source)
  }

  private async click(tab: BrowserUseTabRuntime, selector: string): Promise<void> {
    await this.locatorClick(tab, { kind: 'css', value: selector })
  }

  private async type(tab: BrowserUseTabRuntime, selector: string, text: string): Promise<void> {
    await this.locatorType(tab, { kind: 'css', value: selector }, text)
  }

  private async press(tab: BrowserUseTabRuntime, selector: string, key: string): Promise<void> {
    await this.locatorPress(tab, { kind: 'css', value: selector }, key)
  }

  private async cuaMove(tab: BrowserUseTabRuntime, options: { x: number; y: number; waitForArrival?: boolean }): Promise<void> {
    this.markAutomation(tab, 'move')
    await this.viewerHost.moveMouse(tab.owner, {
      tabId: tab.id,
      x: Number(options.x),
      y: Number(options.y),
      waitForArrival: options.waitForArrival
    })
  }

  private async cuaClick(tab: BrowserUseTabRuntime, options: { x: number; y: number; button?: number | string }): Promise<void> {
    this.markAutomation(tab, 'click')
    await this.viewerHost.clickMouse(tab.owner, {
      tabId: tab.id,
      x: Number(options.x),
      y: Number(options.y),
      button: this.normalizeMouseButton(options.button)
    })
  }

  private async cuaDoubleClick(tab: BrowserUseTabRuntime, options: { x: number; y: number; button?: number | string }): Promise<void> {
    this.markAutomation(tab, 'double click')
    await this.viewerHost.doubleClickMouse(tab.owner, {
      tabId: tab.id,
      x: Number(options.x),
      y: Number(options.y),
      button: this.normalizeMouseButton(options.button)
    })
  }

  private async cuaDrag(tab: BrowserUseTabRuntime, options: { path: Array<{ x: number; y: number }> }): Promise<void> {
    this.markAutomation(tab, 'drag')
    await this.viewerHost.dragMouse(tab.owner, {
      tabId: tab.id,
      path: Array.isArray(options.path) ? options.path : []
    })
  }

  private async cuaScroll(tab: BrowserUseTabRuntime, options: { x: number; y: number; scrollX: number; scrollY: number }): Promise<void> {
    this.markAutomation(tab, 'scroll')
    await this.viewerHost.scrollMouse(tab.owner, {
      tabId: tab.id,
      x: Number(options.x),
      y: Number(options.y),
      scrollX: Number(options.scrollX ?? 0),
      scrollY: Number(options.scrollY ?? 0)
    })
  }

  private async cuaType(tab: BrowserUseTabRuntime, options: { text: string }): Promise<void> {
    this.markAutomation(tab, 'type')
    await this.viewerHost.typeText(tab.owner, { tabId: tab.id, text: String(options.text ?? '') })
  }

  private async cuaKeypress(tab: BrowserUseTabRuntime, options: { keys: string[] }): Promise<void> {
    this.markAutomation(tab, 'keypress')
    this.viewerHost.keypress(tab.owner, { tabId: tab.id, keys: Array.isArray(options.keys) ? options.keys.map(String) : [] })
  }

  private async locatorClick(tab: BrowserUseTabRuntime, descriptor: BrowserUseLocatorDescriptor): Promise<void> {
    const target = await this.strictLocator(tab, descriptor)
    const point = this.actionPoint(target)
    await this.cuaClick(tab, { ...point })
  }

  private async locatorDoubleClick(tab: BrowserUseTabRuntime, descriptor: BrowserUseLocatorDescriptor): Promise<void> {
    const target = await this.strictLocator(tab, descriptor)
    const point = this.actionPoint(target)
    await this.cuaDoubleClick(tab, { ...point })
  }

  private async locatorType(tab: BrowserUseTabRuntime, descriptor: BrowserUseLocatorDescriptor, value: string): Promise<void> {
    const target = await this.strictLocator(tab, descriptor)
    const point = this.actionPoint(target)
    await this.cuaClick(tab, { ...point })
    await this.cuaType(tab, { text: String(value ?? '') })
  }

  private async locatorFill(tab: BrowserUseTabRuntime, descriptor: BrowserUseLocatorDescriptor, value: string): Promise<void> {
    const target = await this.strictLocator(tab, descriptor)
    const point = this.actionPoint(target)
    await this.cuaClick(tab, { ...point })
    await this.mutateStrictLocator(tab, descriptor, String(value ?? ''))
  }

  private async locatorPress(tab: BrowserUseTabRuntime, descriptor: BrowserUseLocatorDescriptor, value: string): Promise<void> {
    const target = await this.strictLocator(tab, descriptor)
    const point = this.actionPoint(target)
    await this.cuaClick(tab, { ...point })
    await this.cuaKeypress(tab, { keys: [String(value)] })
  }

  private async strictLocator(tab: BrowserUseTabRuntime, descriptor: BrowserUseLocatorDescriptor): Promise<BrowserUseElementMatch> {
    const matches = await this.resolveLocator(tab, descriptor)
    if (matches.length === 0) {
      throw new Error(`No element found for locator: ${this.describeLocator(descriptor)}`)
    }
    if (matches.length > 1) {
      throw new Error(`Strict mode violation for locator ${this.describeLocator(descriptor)}: ${matches.length} elements matched.`)
    }
    return matches[0]!
  }

  private actionPoint(match: BrowserUseElementMatch): { x: number; y: number } {
    const box = match.boundingBox
    if (!box || box.width <= 0 || box.height <= 0) {
      throw new Error('Element does not have a clickable bounding box.')
    }
    return {
      x: Math.max(0, Math.round(box.x + box.width / 2)),
      y: Math.max(0, Math.round(box.y + box.height / 2))
    }
  }

  private async resolveLocator(
    tab: BrowserUseTabRuntime,
    descriptor: BrowserUseLocatorDescriptor
  ): Promise<BrowserUseElementMatch[]> {
    const script = `
      ((descriptor) => {
        const normalize = (value) => String(value ?? '').replace(/\\s+/g, ' ').trim();
        const matchesText = (actual, expected, exact) => {
          const a = normalize(actual);
          const e = normalize(expected);
          return exact ? a === e : a.toLowerCase().includes(e.toLowerCase());
        };
        const visible = (el) => {
          const style = window.getComputedStyle(el);
          const rect = el.getBoundingClientRect();
          return style.visibility !== 'hidden' && style.display !== 'none' && rect.width > 0 && rect.height > 0;
        };
        const roleOf = (el) => {
          const explicit = el.getAttribute('role');
          if (explicit) return explicit;
          const tag = el.tagName.toLowerCase();
          if (tag === 'a' && el.hasAttribute('href')) return 'link';
          if (tag === 'button') return 'button';
          if (tag === 'input') {
            const type = (el.getAttribute('type') || 'text').toLowerCase();
            if (type === 'button' || type === 'submit' || type === 'reset') return 'button';
            if (type === 'checkbox') return 'checkbox';
            if (type === 'radio') return 'radio';
            return 'textbox';
          }
          if (tag === 'textarea') return 'textbox';
          if (tag === 'select') return 'combobox';
          if (tag === 'summary') return 'button';
          return '';
        };
        const nameOf = (el) => normalize(
          el.getAttribute('aria-label') ||
          el.getAttribute('title') ||
          el.getAttribute('alt') ||
          el.innerText ||
          el.textContent ||
          el.getAttribute('value') ||
          ''
        );
        const textOf = (el) => normalize(
          el.innerText ||
          el.textContent ||
          el.getAttribute('aria-label') ||
          el.getAttribute('placeholder') ||
          el.getAttribute('value') ||
          ''
        );
        let candidates = [];
        if (descriptor.kind === 'css') {
          candidates = Array.from(document.querySelectorAll(descriptor.value));
        } else if (descriptor.kind === 'testId') {
          candidates = Array.from(document.querySelectorAll('[data-testid="' + CSS.escape(descriptor.value) + '"]'));
        } else if (descriptor.kind === 'placeholder') {
          candidates = Array.from(document.querySelectorAll('input, textarea')).filter((el) => matchesText(el.getAttribute('placeholder'), descriptor.value, descriptor.exact));
        } else if (descriptor.kind === 'label') {
          const fromLabels = Array.from(document.querySelectorAll('label')).filter((el) => matchesText(textOf(el), descriptor.value, descriptor.exact)).map((label) => label.control).filter(Boolean);
          const aria = Array.from(document.querySelectorAll('input, textarea, select, button')).filter((el) => matchesText(el.getAttribute('aria-label'), descriptor.value, descriptor.exact));
          candidates = [...fromLabels, ...aria];
        } else if (descriptor.kind === 'role') {
          candidates = Array.from(document.querySelectorAll('body *')).filter((el) => {
            if (roleOf(el) !== descriptor.value) return false;
            return descriptor.name == null || matchesText(nameOf(el), descriptor.name, descriptor.exact);
          });
        } else if (descriptor.kind === 'text') {
          candidates = Array.from(document.querySelectorAll('body *')).filter((el) => matchesText(textOf(el), descriptor.value, descriptor.exact));
        }
        const seen = new Set();
        return candidates.filter((el) => {
          if (!el || seen.has(el) || !visible(el)) return false;
          seen.add(el);
          return true;
        }).slice(0, 100).map((el, index) => {
          const rect = el.getBoundingClientRect();
          return {
            index,
            tagName: el.tagName.toLowerCase(),
            visibleText: textOf(el),
            ariaName: nameOf(el),
            boundingBox: rect ? { x: rect.left, y: rect.top, width: rect.width, height: rect.height } : null
          };
        });
      })(${JSON.stringify(descriptor)})
    `
    return await this.webContentsFor(tab.owner, tab.id).executeJavaScript(script, true) as BrowserUseElementMatch[]
  }

  private async locatorEvaluate(
    tab: BrowserUseTabRuntime,
    descriptor: BrowserUseLocatorDescriptor,
    operation: 'textContent' | 'getAttribute' | 'isEnabled',
    arg?: string
  ): Promise<unknown> {
    await this.strictLocator(tab, descriptor)
    const script = `
      ((descriptor, operation, arg) => {
        const normalize = (value) => String(value ?? '').replace(/\\s+/g, ' ').trim();
        const matchesText = (actual, expected, exact) => {
          const a = normalize(actual);
          const e = normalize(expected);
          return exact ? a === e : a.toLowerCase().includes(e.toLowerCase());
        };
        const roleOf = (el) => {
          const explicit = el.getAttribute('role');
          if (explicit) return explicit;
          const tag = el.tagName.toLowerCase();
          if (tag === 'a' && el.hasAttribute('href')) return 'link';
          if (tag === 'button') return 'button';
          if (tag === 'input') return (el.getAttribute('type') || 'text').toLowerCase() === 'checkbox' ? 'checkbox' : 'textbox';
          if (tag === 'textarea') return 'textbox';
          if (tag === 'select') return 'combobox';
          return '';
        };
        const textOf = (el) => normalize(el.innerText || el.textContent || el.getAttribute('aria-label') || el.getAttribute('placeholder') || el.getAttribute('value') || '');
        const nameOf = (el) => normalize(el.getAttribute('aria-label') || el.getAttribute('title') || el.innerText || el.textContent || '');
        let candidates = [];
        if (descriptor.kind === 'css') candidates = Array.from(document.querySelectorAll(descriptor.value));
        else if (descriptor.kind === 'testId') candidates = Array.from(document.querySelectorAll('[data-testid="' + CSS.escape(descriptor.value) + '"]'));
        else if (descriptor.kind === 'placeholder') candidates = Array.from(document.querySelectorAll('input, textarea')).filter((el) => matchesText(el.getAttribute('placeholder'), descriptor.value, descriptor.exact));
        else if (descriptor.kind === 'label') candidates = Array.from(document.querySelectorAll('label')).filter((el) => matchesText(textOf(el), descriptor.value, descriptor.exact)).map((label) => label.control).filter(Boolean);
        else if (descriptor.kind === 'role') candidates = Array.from(document.querySelectorAll('body *')).filter((el) => roleOf(el) === descriptor.value && (descriptor.name == null || matchesText(nameOf(el), descriptor.name, descriptor.exact)));
        else if (descriptor.kind === 'text') candidates = Array.from(document.querySelectorAll('body *')).filter((el) => matchesText(textOf(el), descriptor.value, descriptor.exact));
        const el = candidates[0];
        if (!el) return null;
        if (operation === 'textContent') return el.textContent;
        if (operation === 'getAttribute') return el.getAttribute(arg);
        if (operation === 'isEnabled') return !el.disabled && el.getAttribute('aria-disabled') !== 'true';
        return null;
      })(${JSON.stringify(descriptor)}, ${JSON.stringify(operation)}, ${JSON.stringify(arg ?? '')})
    `
    return await this.webContentsFor(tab.owner, tab.id).executeJavaScript(script, true)
  }

  private async mutateStrictLocator(
    tab: BrowserUseTabRuntime,
    descriptor: BrowserUseLocatorDescriptor,
    value: string
  ): Promise<void> {
    await this.strictLocator(tab, descriptor)
    const script = `
      ((descriptor, value) => {
        const normalize = (item) => String(item ?? '').replace(/\\s+/g, ' ').trim();
        const matchesText = (actual, expected, exact) => {
          const a = normalize(actual);
          const e = normalize(expected);
          return exact ? a === e : a.toLowerCase().includes(e.toLowerCase());
        };
        const roleOf = (node) => {
          const explicit = node.getAttribute('role');
          if (explicit) return explicit;
          const tag = node.tagName.toLowerCase();
          if (tag === 'a' && node.hasAttribute('href')) return 'link';
          if (tag === 'button') return 'button';
          if (tag === 'input') return (node.getAttribute('type') || 'text').toLowerCase() === 'checkbox' ? 'checkbox' : 'textbox';
          if (tag === 'textarea') return 'textbox';
          if (tag === 'select') return 'combobox';
          return '';
        };
        const textOf = (node) => normalize(node.innerText || node.textContent || node.getAttribute('aria-label') || node.getAttribute('placeholder') || node.getAttribute('value') || '');
        const nameOf = (node) => normalize(node.getAttribute('aria-label') || node.getAttribute('title') || node.innerText || node.textContent || '');
        let candidates = [];
        if (descriptor.kind === 'css') candidates = Array.from(document.querySelectorAll(descriptor.value));
        else if (descriptor.kind === 'testId') candidates = Array.from(document.querySelectorAll('[data-testid="' + CSS.escape(descriptor.value) + '"]'));
        else if (descriptor.kind === 'placeholder') candidates = Array.from(document.querySelectorAll('input, textarea')).filter((node) => matchesText(node.getAttribute('placeholder'), descriptor.value, descriptor.exact));
        else if (descriptor.kind === 'label') candidates = Array.from(document.querySelectorAll('label')).filter((node) => matchesText(textOf(node), descriptor.value, descriptor.exact)).map((label) => label.control).filter(Boolean);
        else if (descriptor.kind === 'role') candidates = Array.from(document.querySelectorAll('body *')).filter((node) => roleOf(node) === descriptor.value && (descriptor.name == null || matchesText(nameOf(node), descriptor.name, descriptor.exact)));
        else if (descriptor.kind === 'text') candidates = Array.from(document.querySelectorAll('body *')).filter((node) => matchesText(textOf(node), descriptor.value, descriptor.exact));
        const el = candidates[0];
        if (!el) throw new Error('Element is no longer available.');
        el.focus();
        if ('value' in el) {
          el.value = value;
          el.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: 'insertText', data: value }));
          el.dispatchEvent(new Event('change', { bubbles: true }));
          return true;
        }
        el.textContent = value;
        el.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: 'insertText', data: value }));
        return true;
      })(${JSON.stringify(descriptor)}, ${JSON.stringify(value)})
    `
    await this.webContentsFor(tab.owner, tab.id).executeJavaScript(script, true)
  }

  private async locatorWaitFor(
    tab: BrowserUseTabRuntime,
    descriptor: BrowserUseLocatorDescriptor,
    options?: { state?: string; timeoutMs?: number }
  ): Promise<void> {
    const expected = options?.state ?? 'visible'
    const deadline = Date.now() + Math.max(1_000, Math.min(options?.timeoutMs ?? 30_000, 120_000))
    for (;;) {
      const count = (await this.resolveLocator(tab, descriptor)).length
      if ((expected === 'hidden' || expected === 'detached') ? count === 0 : count > 0) return
      if (Date.now() > deadline) throw new Error(`Timed out waiting for locator ${this.describeLocator(descriptor)} to be ${expected}.`)
      await new Promise((resolve) => setTimeout(resolve, 100))
    }
  }

  private waitForUrl(tab: BrowserUseTabRuntime, expectedUrl: string, timeoutMs: number): Promise<void> {
    const wc = this.webContentsFor(tab.owner, tab.id)
    if (wc.getURL() === expectedUrl) return Promise.resolve()
    return new Promise((resolve, reject) => {
      const timeout = setTimeout(() => {
        cleanup()
        reject(new Error(`Timed out waiting for browser URL: ${expectedUrl}`))
      }, Math.max(1_000, Math.min(timeoutMs, 120_000)))
      const done = () => {
        if (wc.getURL() !== expectedUrl) return
        cleanup()
        resolve()
      }
      const cleanup = () => {
        clearTimeout(timeout)
        wc.off('did-navigate', done)
        wc.off('did-stop-loading', done)
      }
      wc.on('did-navigate', done)
      wc.on('did-stop-loading', done)
    })
  }

  private devLogs(tab: BrowserUseTabRuntime, options?: { filter?: string; levels?: string[]; limit?: number }): BrowserUseLogEntry[] {
    let entries = [...tab.logs]
    if (options?.filter) entries = entries.filter((entry) => entry.message.includes(options.filter!))
    if (options?.levels?.length) {
      const levels = new Set(options.levels.map((level) => level.toLowerCase()))
      entries = entries.filter((entry) => levels.has(entry.level.toLowerCase()))
    }
    const limit = Math.max(1, Math.min(options?.limit ?? entries.length, 500))
    return entries.slice(-limit)
  }

  private normalizeMouseButton(value: number | string | undefined): 'left' | 'right' | 'middle' {
    if (value === 2 || value === 'right') return 'right'
    if (value === 1 || value === 'middle') return 'middle'
    return 'left'
  }

  private describeLocator(descriptor: BrowserUseLocatorDescriptor): string {
    return `${descriptor.kind}=${descriptor.name ?? descriptor.value}`
  }

  private waitForLoad(tab: BrowserUseTabRuntime, timeoutMs: number): Promise<void> {
    const wc = this.webContentsFor(tab.owner, tab.id)
    if (!wc.isLoading()) return Promise.resolve()
    return new Promise((resolve, reject) => {
      const timeout = setTimeout(() => {
        cleanup()
        reject(new Error('Timed out waiting for browser load state.'))
      }, Math.max(1_000, Math.min(timeoutMs, 120_000)))
      const done = () => {
        cleanup()
        resolve()
      }
      const cleanup = () => {
        clearTimeout(timeout)
        wc.off('did-finish-load', done)
        wc.off('did-stop-loading', done)
      }
      wc.once('did-finish-load', done)
      wc.once('did-stop-loading', done)
    })
  }

  private withTimeout<T>(promise: Promise<T>, timeoutMs: number): Promise<T> {
    return new Promise((resolve, reject) => {
      const timeout = setTimeout(() => reject(new Error('BrowserJs timed out.')), timeoutMs)
      promise.then(
        (value) => {
          clearTimeout(timeout)
          resolve(value)
        },
        (error) => {
          clearTimeout(timeout)
          reject(error)
        }
      )
    })
  }
}

export const browserUseManager = new BrowserUseManager()
export { BROWSER_USE_OPEN_CHANNEL }
