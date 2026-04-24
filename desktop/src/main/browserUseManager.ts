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
    workspacePath: string
    initialUrl?: string
    width?: number
    height?: number
    allowFileScheme?: boolean
  }): unknown
  getTabWebContents(win: BrowserWindow, tabId: string): Electron.WebContents | null
  loadAutomationUrl(win: BrowserWindow, params: { tabId: string; url: string }): Promise<void>
  destroyTab(win: BrowserWindow, tabId: string): void
  snapshotState(win: BrowserWindow, tabId: string): {
    tabId: string
    currentUrl: string
    title: string
    loading: boolean
  } | null
}

interface BrowserUsePolicyHost {
  getSettings(): AppSettings
  updateSettings(partial: Partial<AppSettings>): Promise<void>
}

interface BrowserUseTabRuntime {
  id: string
  owner: BrowserWindow
  logs: string[]
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
      this.viewerHost.destroyTab(tab.owner, tab.id)
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
    const runtime = {
      threadId,
      workspacePath: resolvedWorkspace,
      context: vm.createContext({}, { codeGeneration: { strings: false, wasm: false } }),
      tabs: new Map<string, BrowserUseTabRuntime>(),
      selectedTabId: null,
      logs: [],
      images: [],
      hasFocusedFirstTab: false
    } satisfies BrowserUseThreadRuntime

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
          return { ok: true, name: runtime.sessionName }
        },
        tabs: {
          list: async () => [...runtime.tabs.values()].map((tab) => this.tabSnapshot(tab)),
          new: async (url?: string) => {
            const tab = await this.createTab(owner, runtime, url)
            runtime.selectedTabId = tab.id
            return this.createTabApi(tab)
          },
          selected: async () => {
            const tab = this.getSelectedTab(runtime)
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
      workspacePath: runtime.workspacePath || owner.getTitle(),
      initialUrl: 'about:blank',
      width: 1280,
      height: 900,
      allowFileScheme: true
    })

    const wc = this.webContentsFor(owner, id)
    const tab: BrowserUseTabRuntime = { id, owner, logs: [] }
    runtime.tabs.set(id, tab)

    wc.on('console-message', (_event, _level, message) => {
      tab.logs.push(message)
    })
    wc.once('destroyed', () => {
      runtime.tabs.delete(id)
      if (runtime.selectedTabId === id) runtime.selectedTabId = null
    })

    const focusMode = runtime.hasFocusedFirstTab ? 'none' : 'first-open'
    runtime.hasFocusedFirstTab = true
    this.emitOpen(owner, {
      threadId: runtime.threadId,
      tabId: id,
      initialUrl: normalizedInitial ?? 'about:blank',
      title: runtime.sessionName?.trim() || 'Browser Use',
      focusMode
    })

    if (normalizedInitial) await this.navigate(tab, normalizedInitial)
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

  private getSelectedTab(runtime: BrowserUseThreadRuntime): BrowserUseTabRuntime {
    if (runtime.selectedTabId) {
      const existing = runtime.tabs.get(runtime.selectedTabId)
      if (existing) return existing
    }
    const first = runtime.tabs.values().next().value as BrowserUseTabRuntime | undefined
    if (first) return first
    throw new Error('No browser-use tab is open. Call agent.browser.tabs.new(url) first.')
  }

  private createTabApi(tab: BrowserUseTabRuntime): Record<string, unknown> {
    return {
      id: tab.id,
      navigate: async (url: string) => this.navigate(tab, url),
      url: async () => this.webContentsFor(tab.owner, tab.id).getURL(),
      title: async () => this.webContentsFor(tab.owner, tab.id).getTitle(),
      screenshot: async () => this.screenshot(tab),
      domSnapshot: async () => this.domSnapshot(tab),
      evaluate: async (expressionOrFunction: string | (() => unknown)) => this.evaluateInPage(tab, expressionOrFunction),
      click: async (selector: string) => this.click(tab, selector),
      type: async (selector: string, text: string) => this.type(tab, selector, text),
      press: async (selector: string, key: string) => this.press(tab, selector, key),
      waitForLoadState: async (_state = 'load', timeoutMs = 30_000) => this.waitForLoad(tab, timeoutMs),
      consoleLogs: async () => [...tab.logs]
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

  private async navigate(tab: BrowserUseTabRuntime, url: string): Promise<Record<string, unknown>> {
    const normalized = normalizeBrowserUseUrl(url)
    if (!normalized) throw new Error(`Invalid browser URL: ${url}`)
    const runtime = this.getRuntimeForTab(tab)
    await this.ensureNavigationAllowed(tab.owner, runtime, tab.id, normalized)
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

  private async screenshot(tab: BrowserUseTabRuntime): Promise<BrowserUseImageResult> {
    const image = await this.webContentsFor(tab.owner, tab.id).capturePage()
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
    const quotedSelector = JSON.stringify(selector)
    await this.webContentsFor(tab.owner, tab.id).executeJavaScript(`
      (() => {
        const selector = ${quotedSelector};
        const el = document.querySelector(selector);
        if (!el) throw new Error('Selector not found: ' + selector);
        el.click();
      })()
    `)
  }

  private async type(tab: BrowserUseTabRuntime, selector: string, text: string): Promise<void> {
    const wc = this.webContentsFor(tab.owner, tab.id)
    const quotedSelector = JSON.stringify(selector)
    await wc.executeJavaScript(`
      (() => {
        const selector = ${quotedSelector};
        const el = document.querySelector(selector);
        if (!el) throw new Error('Selector not found: ' + selector);
        el.focus();
      })()
    `)
    await wc.insertText(text)
  }

  private async press(tab: BrowserUseTabRuntime, selector: string, key: string): Promise<void> {
    const wc = this.webContentsFor(tab.owner, tab.id)
    const quotedSelector = JSON.stringify(selector)
    await wc.executeJavaScript(`
      (() => {
        const selector = ${quotedSelector};
        const el = document.querySelector(selector);
        if (!el) throw new Error('Selector not found: ' + selector);
        el.focus();
      })()
    `)
    wc.sendInputEvent({ type: 'keyDown', keyCode: key })
    wc.sendInputEvent({ type: 'keyUp', keyCode: key })
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
