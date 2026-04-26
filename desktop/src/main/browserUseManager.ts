import { createRequire } from 'node:module'
import { dirname, join } from 'node:path'
import { BrowserWindow } from 'electron'
import { viewerBrowserManager } from './viewerBrowser'
import type { AppSettings } from './settings'
import {
  isBrowserUseUrlAllowed as isBrowserUseUrlAllowedByPolicy,
  normalizeBrowserUseDomainList,
  resolveBrowserUseNavigationDecision
} from './browserUsePolicy'

const require = createRequire(import.meta.url)
const playwrightCoreRoot = dirname(require.resolve('playwright-core/package.json'))
const { source: playwrightInjectedScriptSource } = require(join(playwrightCoreRoot, 'lib/generated/injectedScriptSource.js')) as { source: string }
const { parseSelector: parsePlaywrightSelector } = require(join(playwrightCoreRoot, 'lib/utils/isomorphic/selectorParser.js')) as {
  parseSelector: (selector: string) => unknown
}
const {
  getByLabelSelector,
  getByPlaceholderSelector,
  getByRoleSelector,
  getByTestIdSelector,
  getByTextSelector
} = require(join(playwrightCoreRoot, 'lib/utils/isomorphic/locatorUtils.js')) as {
  getByLabelSelector: (text: string, options?: { exact?: boolean }) => string
  getByPlaceholderSelector: (text: string, options?: { exact?: boolean }) => string
  getByRoleSelector: (role: string, options?: { exact?: boolean; name?: string }) => string
  getByTestIdSelector: (testIdAttributeName: string, testId: string) => string
  getByTextSelector: (text: string, options?: { exact?: boolean }) => string
}

const BROWSER_USE_OPEN_CHANNEL = 'viewer:browser-use:open'
const BROWSER_USE_APPROVAL_REQUEST_CHANNEL = 'viewer:browser-use:approval-request'
const BROWSER_USE_APPROVAL_TIMEOUT_MS = 120_000
const BROWSER_USE_OPERATION_TIMEOUT_MS = 10_000
const BROWSER_USE_NAVIGATION_TIMEOUT_MS = 30_000
const BROWSER_USE_BLANK_TAB_READY_TIMEOUT_MS = 5_000
const BROWSER_USE_NETWORK_IDLE_QUIET_MS = 500

type BrowserUseLoadState = 'commit' | 'domcontentloaded' | 'load' | 'networkidle'

export interface BrowserUseImageResult {
  mediaType: string
  dataBase64: string
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
  cdpAttached?: boolean
  snapshotRefs: Map<string, BrowserUseElementMatch>
  snapshotGeneration: number
}

interface BrowserUseOperationTrace {
  operation: string
  tabId: string
  startedAt: number
  elapsedMs?: number
  timeoutMs: number
  url: string
  status: 'active' | 'completed' | 'failed' | 'timeout' | 'cancelled' | 'stale'
  error?: string
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
  agent?: Record<string, unknown>
  display?: (imageLike: unknown) => Promise<void>
  tabs: Map<string, BrowserUseTabRuntime>
  selectedTabId: string | null
  logs: string[]
  images: BrowserUseImageResult[]
  hasFocusedFirstTab: boolean
  activeEvaluationId?: string
  activeAbortSignal?: AbortSignal
  activeOperation?: BrowserUseOperationTrace
  operationHistory: BrowserUseOperationTrace[]
}

interface BrowserUseOperationTimeouts {
  operationMs?: number
  navigationMs?: number
  blankTabReadyMs?: number
}

type BrowserUseLocatorKind = 'css' | 'text' | 'role' | 'label' | 'placeholder' | 'testId' | 'ref'

interface BrowserUseLocatorDescriptor {
  kind: BrowserUseLocatorKind
  value: string
  exact?: boolean
  name?: string
}

interface BrowserUseElementMatch {
  ref?: string
  index: number
  tagName: string
  tag?: string
  role: string
  name: string
  text: string
  href?: string
  testId?: string
  selector: string
  visible: boolean
  enabled: boolean
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

  constructor(
    private readonly viewerHost: BrowserUseViewerHost = viewerBrowserManager,
    private readonly timeouts: BrowserUseOperationTimeouts = {}
  ) {}

  private operationTimeoutMs(): number {
    return this.timeouts.operationMs ?? BROWSER_USE_OPERATION_TIMEOUT_MS
  }

  private navigationTimeoutMs(): number {
    return this.timeouts.navigationMs ?? BROWSER_USE_NAVIGATION_TIMEOUT_MS
  }

  private blankTabReadyTimeoutMs(): number {
    return this.timeouts.blankTabReadyMs ?? BROWSER_USE_BLANK_TAB_READY_TIMEOUT_MS
  }

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

  prepareNodeRepl(owner: BrowserWindow, params: {
    threadId: string
    workspacePath?: string
    evaluationId?: string
    signal?: AbortSignal
  }): {
    agent: Record<string, unknown>
    display: (imageLike: unknown) => Promise<void>
    collect: () => { images: BrowserUseImageResult[]; logs: string[] }
  } {
    const runtime = this.getOrCreateRuntime(owner, params.threadId, params.workspacePath)
    runtime.logs = []
    runtime.images = []
    runtime.operationHistory = []
    runtime.activeOperation = undefined
    runtime.activeEvaluationId = params.evaluationId
    runtime.activeAbortSignal = params.signal
    return {
      agent: runtime.agent!,
      display: runtime.display!,
      collect: () => ({
        images: [...runtime.images],
        logs: [...runtime.logs]
      })
    }
  }

  abortEvaluation(threadId: string, evaluationId?: string): { ok: boolean } {
    const runtime = this.runtimes.get(threadId)
    if (!runtime) return { ok: false }
    if (evaluationId && runtime.activeEvaluationId && runtime.activeEvaluationId !== evaluationId) {
      return { ok: false }
    }
    runtime.activeEvaluationId = undefined
    runtime.activeAbortSignal = undefined
    this.recordActiveOperation(runtime, 'cancelled')
    runtime.activeOperation = undefined
    this.appendOperationDiagnostics(runtime, 'Browser evaluation aborted.')
    for (const tab of runtime.tabs.values()) {
      try {
        this.webContentsFor(tab.owner, tab.id).stop()
      } catch {
        // Best effort: stopping a destroyed or unavailable tab should not block cancellation.
      }
      this.setAutomationState(runtime, tab, false)
    }
    return { ok: true }
  }

  reset(threadId: string): { ok: boolean } {
    const runtime = this.runtimes.get(threadId)
    if (!runtime) return { ok: false }
    for (const tab of [...runtime.tabs.values()]) {
      this.detachDebugger(tab)
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
      tabs: new Map<string, BrowserUseTabRuntime>(),
      selectedTabId: null,
      logs: [],
      images: [],
      hasFocusedFirstTab: false,
      operationHistory: []
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

    runtime.agent = agent
    runtime.display = display

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

    if (normalizedInitial) {
      await this.navigate(tab, normalizedInitial, { skipPolicyCheck: true })
    } else {
      await this.loadAutomationUrl(
        tab,
        'about:blank',
        this.blankTabReadyTimeoutMs(),
        'initial blank page')
      await this.waitForScriptReady(tab, this.blankTabReadyTimeoutMs())
    }
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

  private async ensureDebuggerAttached(tab: BrowserUseTabRuntime): Promise<void> {
    const wc = this.webContentsFor(tab.owner, tab.id)
    const debuggerApi = wc.debugger
    if (!debuggerApi) {
      throw new Error(`Browser tab ${tab.id} does not expose Electron debugger/CDP.`)
    }
    if (!tab.cdpAttached || !debuggerApi.isAttached()) {
      debuggerApi.attach('1.3')
      tab.cdpAttached = true
    }
  }

  private detachDebugger(tab: BrowserUseTabRuntime): void {
    try {
      const debuggerApi = this.webContentsFor(tab.owner, tab.id).debugger
      if (debuggerApi?.isAttached()) debuggerApi.detach()
    } catch {
      // Best effort only. Browser tab teardown should not be blocked by debugger cleanup.
    } finally {
      tab.cdpAttached = false
    }
  }

  private async cdpCommand<T = unknown>(
    tab: BrowserUseTabRuntime,
    method: string,
    params?: Record<string, unknown>
  ): Promise<T> {
    await this.ensureDebuggerAttached(tab)
    return await this.webContentsFor(tab.owner, tab.id).debugger.sendCommand(method, params) as T
  }

  private operationUrl(tab: BrowserUseTabRuntime): string {
    try {
      return this.webContentsFor(tab.owner, tab.id).getURL() || 'about:blank'
    } catch {
      return 'unknown'
    }
  }

  private beginOperation(
    runtime: BrowserUseThreadRuntime,
    tab: BrowserUseTabRuntime,
    operation: string,
    timeoutMs: number
  ): BrowserUseOperationTrace {
    const trace: BrowserUseOperationTrace = {
      operation,
      tabId: tab.id,
      startedAt: Date.now(),
      timeoutMs,
      url: this.operationUrl(tab),
      status: 'active'
    }
    runtime.activeOperation = trace
    return trace
  }

  private finishOperation(
    runtime: BrowserUseThreadRuntime,
    trace: BrowserUseOperationTrace,
    status: BrowserUseOperationTrace['status'],
    error?: string
  ): void {
    if (runtime.activeOperation === trace) {
      runtime.activeOperation = undefined
    }
    trace.elapsedMs = Math.max(0, Date.now() - trace.startedAt)
    trace.status = status
    trace.error = error
    runtime.operationHistory.push({ ...trace })
    runtime.operationHistory = runtime.operationHistory.slice(-8)
  }

  private recordActiveOperation(
    runtime: BrowserUseThreadRuntime,
    status: BrowserUseOperationTrace['status'],
    error?: string
  ): void {
    if (!runtime.activeOperation) return
    this.finishOperation(runtime, runtime.activeOperation, status, error)
  }

  private appendOperationDiagnostics(runtime: BrowserUseThreadRuntime, prefix: string): void {
    const traces = [...runtime.operationHistory]
    if (runtime.activeOperation) traces.push({
      ...runtime.activeOperation,
      elapsedMs: Math.max(0, Date.now() - runtime.activeOperation.startedAt)
    })
    if (traces.length === 0) return
    const tail = traces.slice(-5).map((trace) => {
      const elapsed = trace.elapsedMs ?? Math.max(0, Date.now() - trace.startedAt)
      const error = trace.error ? ` error=${trace.error}` : ''
      return `${trace.operation} status=${trace.status} tab=${trace.tabId} url=${trace.url} elapsedMs=${elapsed} timeoutMs=${trace.timeoutMs}${error}`
    })
    runtime.logs.push(`${prefix}\nRecent browser operations:\n${tail.join('\n')}`)
  }

  private async withBrowserOperation<T>(
    tab: BrowserUseTabRuntime,
    operation: string,
    run: () => Promise<T> | T,
    timeoutMs?: number
  ): Promise<T> {
    const runtime = this.getRuntimeForTab(tab)
    const signal = runtime.activeAbortSignal
    const evaluationId = runtime.activeEvaluationId
    if (signal?.aborted) {
      throw new Error(`Browser operation '${operation}' was cancelled for tab ${tab.id}.`)
    }
    const effectiveTimeoutMs = Math.max(1, Math.min(timeoutMs ?? this.operationTimeoutMs(), 120_000))
    const trace = this.beginOperation(runtime, tab, operation, effectiveTimeoutMs)

    let operationPromise: Promise<T>
    try {
      operationPromise = Promise.resolve(run())
    } catch (error) {
      this.finishOperation(runtime, trace, 'failed', error instanceof Error ? error.message : String(error))
      throw error
    }
    operationPromise.catch(() => {})

    return new Promise<T>((resolve, reject) => {
      let settled = false
      const cleanup = () => {
        clearTimeout(timeout)
        signal?.removeEventListener('abort', onAbort)
      }
      const finish = (callback: () => void) => {
        if (settled) return
        settled = true
        cleanup()
        callback()
      }
      const ensureStillActive = () => {
        if (signal?.aborted) {
          this.finishOperation(runtime, trace, 'cancelled')
          return new Error(`Browser operation '${operation}' was cancelled for tab ${tab.id} at ${currentUrl()}.`)
        }
        if (evaluationId && runtime.activeEvaluationId !== evaluationId) {
          this.finishOperation(runtime, trace, 'stale')
          return new Error(`Browser operation '${operation}' result arrived after evaluation ${evaluationId} was no longer active for tab ${tab.id} at ${currentUrl()}.`)
        }
        return null
      }
      const currentUrl = () => {
        try {
          return this.webContentsFor(tab.owner, tab.id).getURL() || 'about:blank'
        } catch {
          return 'unknown'
        }
      }
      const onAbort = () => {
        finish(() => {
          this.finishOperation(runtime, trace, 'cancelled')
          reject(new Error(`Browser operation '${operation}' was cancelled for tab ${tab.id} at ${currentUrl()}.`))
        })
      }
      const timeout = setTimeout(() => {
        finish(() => {
          const message = `Browser operation '${operation}' timed out after ${effectiveTimeoutMs}ms for tab ${tab.id} at ${currentUrl()}.`
          this.finishOperation(runtime, trace, 'timeout', message)
          this.appendOperationDiagnostics(runtime, message)
          reject(new Error(message))
        })
      }, effectiveTimeoutMs)

      signal?.addEventListener('abort', onAbort, { once: true })
      operationPromise.then(
        (value) => finish(() => {
          const stale = ensureStillActive()
          if (stale) reject(stale)
          else {
            this.finishOperation(runtime, trace, 'completed')
            resolve(value)
          }
        }),
        (error) => finish(() => {
          this.finishOperation(runtime, trace, 'failed', error instanceof Error ? error.message : String(error))
          reject(error)
        })
      )
    })
  }

  private async loadAutomationUrl(
    tab: BrowserUseTabRuntime,
    url: string,
    timeoutMs = this.navigationTimeoutMs(),
    operation = 'navigate'
  ): Promise<void> {
    await this.withBrowserOperation(
      tab,
      operation,
      () => this.viewerHost.loadAutomationUrl(tab.owner, { tabId: tab.id, url }),
      timeoutMs)
  }

  private async waitForScriptReady(
    tab: BrowserUseTabRuntime,
    timeoutMs = this.blankTabReadyTimeoutMs()
  ): Promise<void> {
    const wc = this.webContentsFor(tab.owner, tab.id)
    if (!wc.isLoading() && wc.getURL()) return
    await this.withBrowserOperation(tab, 'wait for script-ready document', () => new Promise<void>((resolve) => {
      const done = () => {
        cleanup()
        resolve()
      }
      const cleanup = () => {
        wc.off('dom-ready', done)
        wc.off('did-finish-load', done)
        wc.off('did-stop-loading', done)
      }
      wc.once('dom-ready', done)
      wc.once('did-finish-load', done)
      wc.once('did-stop-loading', done)
    }), timeoutMs)
  }

  private executeJavaScript<T = unknown>(
    tab: BrowserUseTabRuntime,
    source: string,
    operation: string,
    userGesture = true
  ): Promise<T> {
    return this.withBrowserOperation(
      tab,
      operation,
      async () => {
        const result = await this.cdpCommand<{
          result?: { value?: T; unserializableValue?: string }
          exceptionDetails?: {
            text?: string
            exception?: { description?: string; value?: unknown }
          }
        }>(tab, 'Runtime.evaluate', {
          expression: source,
          awaitPromise: true,
          returnByValue: true,
          userGesture
        })
        if (result.exceptionDetails) {
          const details = result.exceptionDetails
          const message = details.exception?.description ||
            (details.exception?.value == null ? undefined : String(details.exception.value)) ||
            details.text ||
            `JavaScript evaluation failed during ${operation}`
          throw new Error(message)
        }
        if (result.result?.unserializableValue != null) {
          return result.result.unserializableValue as T
        }
        return result.result?.value as T
      })
  }

  private async waitForPageReady(
    tab: BrowserUseTabRuntime,
    options: { operation: string; requireContent: boolean; timeoutMs: number }
  ): Promise<void> {
    await this.waitForScriptReady(tab, Math.min(options.timeoutMs, this.blankTabReadyTimeoutMs()))
    const deadline = Date.now() + Math.max(1, Math.min(options.timeoutMs, 120_000))
    for (;;) {
      const signal = this.getRuntimeForTab(tab).activeAbortSignal
      if (signal?.aborted) throw new Error(`Browser operation '${options.operation}' was cancelled for tab ${tab.id}.`)
      const rawState = await this.executeJavaScript<unknown>(tab, `
        new Promise((resolve) => {
          const sample = () => {
            const bodyText = (document.body?.innerText || '').trim();
            const interactive = document.querySelectorAll('a,button,input,textarea,select,summary,[role="button"],[role="link"]').length;
            const appRoot = document.querySelector('#app, #root, [data-v-app], main, nav, header');
            resolve({
              url: location.href,
              title: document.title,
              readyState: document.readyState,
              bodyTextLength: bodyText.length,
              interactiveCount: interactive,
              appRootTextLength: (appRoot?.textContent || '').trim().length
            });
          };
          requestAnimationFrame(() => requestAnimationFrame(sample));
        })
      `, options.operation)
      const state = this.normalizeReadinessState(rawState)
      if (!state) {
        if (Date.now() >= deadline) {
          throw new Error(`Browser operation '${options.operation}' timed out after ${Math.max(1, Math.min(options.timeoutMs, 120_000))}ms for tab ${tab.id} at ${this.webContentsFor(tab.owner, tab.id).getURL() || 'about:blank'}.`)
        }
        await this.delay(tab, 100, options.operation)
        continue
      }
      const documentReady = state.readyState === 'interactive' || state.readyState === 'complete'
      const blank = state.url === 'about:blank'
      const hasUsefulContent =
        state.bodyTextLength > 0 ||
        state.interactiveCount > 0 ||
        state.appRootTextLength > 0 ||
        state.title.trim().length > 0
      if (documentReady && (blank || !options.requireContent || hasUsefulContent)) return
      if (Date.now() >= deadline) {
        throw new Error(
          `Browser operation '${options.operation}' timed out after ${Math.max(1, Math.min(options.timeoutMs, 120_000))}ms for tab ${tab.id} at ${state.url || this.webContentsFor(tab.owner, tab.id).getURL() || 'about:blank'}.`)
      }
      await this.delay(tab, 100, options.operation)
    }
  }

  private normalizeReadinessState(rawState: unknown): {
        url: string
        title: string
        readyState: string
        bodyTextLength: number
        interactiveCount: number
        appRootTextLength: number
      } | null {
    let parsed = rawState
    if (typeof parsed === 'string') {
      try {
        parsed = JSON.parse(parsed)
      } catch {
        return null
      }
    }
    if (!parsed || typeof parsed !== 'object') return null
    const state = parsed as Record<string, unknown>
    return {
      url: typeof state.url === 'string' ? state.url : '',
      title: typeof state.title === 'string' ? state.title : '',
      readyState: typeof state.readyState === 'string' ? state.readyState : '',
      bodyTextLength: typeof state.bodyTextLength === 'number' ? state.bodyTextLength : 0,
      interactiveCount: typeof state.interactiveCount === 'number' ? state.interactiveCount : 0,
      appRootTextLength: typeof state.appRootTextLength === 'number' ? state.appRootTextLength : 0
    }
  }

  private delay(tab: BrowserUseTabRuntime, timeoutMs: number, operation: string): Promise<void> {
    const signal = this.getRuntimeForTab(tab).activeAbortSignal
    if (signal?.aborted) return Promise.reject(new Error(`Browser operation '${operation}' was cancelled for tab ${tab.id}.`))
    return new Promise((resolve, reject) => {
      const timeout = setTimeout(() => {
        signal?.removeEventListener('abort', onAbort)
        resolve()
      }, timeoutMs)
      const onAbort = () => {
        clearTimeout(timeout)
        reject(new Error(`Browser operation '${operation}' was cancelled for tab ${tab.id}.`))
      }
      signal?.addEventListener('abort', onAbort, { once: true })
    })
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
    const tab: BrowserUseTabRuntime = {
      id,
      owner,
      logs: [],
      adopted,
      snapshotRefs: new Map(),
      snapshotGeneration: 0
    }
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
      this.detachDebugger(tab)
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
      clickRef: async (ref: string) => this.locatorClick(tab, { kind: 'ref', value: String(ref) }),
      fillRef: async (ref: string, value: string) => this.locatorFill(tab, { kind: 'ref', value: String(ref) }, value),
      pressRef: async (ref: string, key: string) => this.locatorPress(tab, { kind: 'ref', value: String(ref) }, key),
      type: async (selector: string, text: string) => this.type(tab, selector, text),
      press: async (selector: string, key: string) => this.press(tab, selector, key),
      waitForLoadState: async (state = 'load', timeoutMs = 30_000) => this.waitForLoad(tab, state, timeoutMs),
      consoleLogs: async () => tab.logs.map((entry) => entry.message),
      playwright: this.createPlaywrightApi(tab),
      cua: this.createCuaApi(tab),
      dev: {
        logs: async (options?: { filter?: string; levels?: string[]; limit?: number }) => this.devLogs(tab, options)
      },
      clipboard: {
        readText: async () => this.executeJavaScript(tab, 'navigator.clipboard.readText()', 'clipboard.readText'),
        writeText: async (text: string) => this.executeJavaScript(
          tab,
          `navigator.clipboard.writeText(${JSON.stringify(String(text ?? ''))})`,
          'clipboard.writeText')
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
      waitForLoadState: async (stateOrOptions?: string | { state?: string; timeoutMs?: number }, timeoutMs?: number) => {
        const state = typeof stateOrOptions === 'string' ? stateOrOptions : stateOrOptions?.state
        const timeout = typeof stateOrOptions === 'object' ? stateOrOptions.timeoutMs : timeoutMs
        return this.waitForLoad(tab, state ?? 'load', timeout ?? 30_000)
      },
      waitForTimeout: async (timeoutMs: number) => new Promise((resolve) => {
        setTimeout(resolve, Math.max(0, Math.min(timeoutMs, 120_000)))
      }),
      waitForURL: async (url: unknown, options?: { timeoutMs?: number; timeout?: number }) => {
        const timeout = options?.timeoutMs ?? options?.timeout ?? 30_000
        return this.waitForUrl(tab, url, timeout)
      },
      expectNavigation: async <T>(action: () => Promise<T>, options?: { timeoutMs?: number; url?: string }) => {
        const result = await action()
        if (options?.url) {
          await this.waitForUrl(tab, options.url, options.timeoutMs ?? 30_000)
        } else {
          await this.waitForLoad(tab, 'load', options?.timeoutMs ?? 30_000)
        }
        return result
      },
      clickRef: async (ref: string) => this.locatorClick(tab, { kind: 'ref', value: String(ref) }),
      fillRef: async (ref: string, value: string) => this.locatorFill(tab, { kind: 'ref', value: String(ref) }, value),
      pressRef: async (ref: string, key: string) => this.locatorPress(tab, { kind: 'ref', value: String(ref) }, key),
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
      isVisible: async () => (await this.resolveLocator(tab, descriptor)).some((match) => match.visible),
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
    this.invalidateSnapshotRefs(tab)
    const wc = this.webContentsFor(tab.owner, tab.id)
    if (wc.navigationHistory.canGoBack()) wc.navigationHistory.goBack()
    await this.waitForLoad(tab, 'load', 30_000).catch(() => {})
    return this.tabSnapshot(tab)
  }

  private async goForward(tab: BrowserUseTabRuntime): Promise<Record<string, unknown>> {
    this.markAutomation(tab, 'forward')
    this.invalidateSnapshotRefs(tab)
    const wc = this.webContentsFor(tab.owner, tab.id)
    if (wc.navigationHistory.canGoForward()) wc.navigationHistory.goForward()
    await this.waitForLoad(tab, 'load', 30_000).catch(() => {})
    return this.tabSnapshot(tab)
  }

  private async reload(tab: BrowserUseTabRuntime): Promise<Record<string, unknown>> {
    this.markAutomation(tab, 'reload')
    this.invalidateSnapshotRefs(tab)
    this.webContentsFor(tab.owner, tab.id).reload()
    await this.waitForLoad(tab, 'load', 30_000).catch(() => {})
    return this.tabSnapshot(tab)
  }

  private closeTab(tab: BrowserUseTabRuntime): void {
    this.markAutomation(tab, 'close')
    this.detachDebugger(tab)
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
    this.invalidateSnapshotRefs(tab)
    if (options.skipPolicyCheck !== true) {
      const runtime = this.getRuntimeForTab(tab)
      await this.ensureNavigationAllowed(tab.owner, runtime, tab.id, normalized)
    }
    await this.loadAutomationUrl(tab, normalized)
    return this.tabSnapshot(tab)
  }

  private invalidateSnapshotRefs(tab: BrowserUseTabRuntime): void {
    tab.snapshotRefs.clear()
    tab.snapshotGeneration += 1
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
    await this.waitForPageReady(tab, {
      operation: 'screenshot.ready',
      requireContent: false,
      timeoutMs: this.operationTimeoutMs()
    })
    const image = await this.withBrowserOperation(
      tab,
      'screenshot',
      () => this.webContentsFor(tab.owner, tab.id).capturePage(options?.clip))
    return {
      mediaType: 'image/png',
      dataBase64: image.toPNG().toString('base64')
    }
  }

  private async domSnapshot(tab: BrowserUseTabRuntime): Promise<string> {
    await this.waitForPageReady(tab, {
      operation: 'domSnapshot.ready',
      requireContent: true,
      timeoutMs: this.operationTimeoutMs()
    })
    await this.ensurePlaywrightInjected(tab)
    const rawSnapshot = await this.executeJavaScript<unknown>(
      tab,
      'window.__dotcraftBrowserUseSnapshot()',
      'domSnapshot')
    const snapshot = this.normalizeSnapshotPayload(rawSnapshot)
    const elements = this.assignSnapshotRefs(tab, snapshot.elements)
    const accessibilitySnapshot = this.formatAccessibilitySnapshot(elements)
    return JSON.stringify({
      ...snapshot,
      accessibilitySnapshot,
      elements
    }, null, 2)
  }

  private normalizeSnapshotPayload(rawSnapshot: unknown): {
    title: string
    url: string
    bodyText: string
    elements: BrowserUseElementMatch[]
  } {
    const parsed = typeof rawSnapshot === 'string'
      ? this.tryParseJson(rawSnapshot) ?? {}
      : rawSnapshot
    const obj = parsed && typeof parsed === 'object' ? parsed as Record<string, unknown> : {}
    const elements = Array.isArray(obj.elements)
      ? obj.elements.map((item, index) => this.normalizeElementMatch(item, index))
      : []
    return {
      title: typeof obj.title === 'string' ? obj.title : '',
      url: typeof obj.url === 'string' ? obj.url : '',
      bodyText: typeof obj.bodyText === 'string' ? obj.bodyText : '',
      elements
    }
  }

  private tryParseJson(value: string): unknown | null {
    try {
      return JSON.parse(value)
    } catch {
      return null
    }
  }

  private normalizeElementMatch(value: unknown, index: number): BrowserUseElementMatch {
    if (!value || typeof value !== 'object') {
      const text = String(value ?? '')
      return {
        index,
        tagName: '',
        tag: '',
        role: '',
        name: text,
        text,
        selector: '',
        visible: true,
        enabled: true,
        visibleText: text,
        ariaName: text,
        boundingBox: null
      }
    }
    const obj = value as Record<string, unknown>
    const boundingBox = obj.boundingBox && typeof obj.boundingBox === 'object'
      ? obj.boundingBox as BrowserUseElementMatch['boundingBox']
      : null
    const tagName = this.stringValue(obj.tagName ?? obj.tag)
    const text = this.stringValue(obj.text ?? obj.visibleText)
    const name = this.stringValue(obj.name ?? obj.ariaName)
    return {
      ref: typeof obj.ref === 'string' ? obj.ref : undefined,
      index: typeof obj.index === 'number' ? obj.index : index,
      tagName,
      tag: this.stringValue(obj.tag ?? tagName),
      role: this.stringValue(obj.role),
      name,
      text,
      href: typeof obj.href === 'string' ? obj.href : undefined,
      testId: typeof obj.testId === 'string' ? obj.testId : undefined,
      selector: this.stringValue(obj.selector),
      visible: obj.visible !== false,
      enabled: obj.enabled !== false,
      visibleText: this.stringValue(obj.visibleText ?? text),
      ariaName: this.stringValue(obj.ariaName ?? name),
      boundingBox
    }
  }

  private stringValue(value: unknown): string {
    return typeof value === 'string' ? value : value == null ? '' : String(value)
  }

  private assignSnapshotRefs(
    tab: BrowserUseTabRuntime,
    elements: BrowserUseElementMatch[]
  ): BrowserUseElementMatch[] {
    tab.snapshotGeneration += 1
    tab.snapshotRefs.clear()
    return elements.map((element, index) => {
      const ref = `e${index + 1}`
      const withRef = {
        ...element,
        ref,
        index
      }
      tab.snapshotRefs.set(ref, withRef)
      return withRef
    })
  }

  private formatAccessibilitySnapshot(elements: BrowserUseElementMatch[]): string {
    return elements.map((element) => {
      const role = element.role || element.tagName || 'element'
      const label = this.escapeSnapshotText(element.name || element.text || element.visibleText || element.selector)
      const details = [
        `[ref=${element.ref ?? ''}]`,
        element.href ? `[href=${this.escapeSnapshotText(element.href)}]` : '',
        element.testId ? `[testId=${this.escapeSnapshotText(element.testId)}]` : '',
        element.selector ? `[selector=${this.escapeSnapshotText(element.selector)}]` : '',
        element.enabled ? '' : '[disabled]'
      ].filter(Boolean).join(' ')
      return `- ${role} "${label}" ${details}`.trim()
    }).join('\n')
  }

  private escapeSnapshotText(value: string): string {
    return value.replace(/\\/g, '\\\\').replace(/"/g, '\\"').slice(0, 160)
  }

  private async ensurePlaywrightInjected(tab: BrowserUseTabRuntime): Promise<void> {
    const installed = await this.executeJavaScript<boolean>(
      tab,
      'Boolean(window.__dotcraftPlaywrightInjected && window.__dotcraftBrowserUseSnapshot && window.__dotcraftBrowserUseResolveSelector)',
      'playwright.inject.check').catch(() => false)
    if (installed === true) return

    await this.executeJavaScript(tab, `
      (() => {
        const module = { exports: {} };
        ${playwrightInjectedScriptSource}
        const injected = new (module.exports.InjectedScript())(globalThis, {
          isUnderTest: false,
          sdkLanguage: "javascript",
          testIdAttributeName: "data-testid",
          stableRafCount: 2,
          browserName: "chromium",
          isUtilityWorld: false,
          customEngines: []
        });
        const normalize = (value) => String(value ?? '').replace(/\\s+/g, ' ').trim();
        const cssEscape = (value) => window.CSS?.escape
          ? CSS.escape(String(value))
          : String(value).replace(/[^a-zA-Z0-9_-]/g, (ch) => '\\\\' + ch);
        const attrValue = (value) => String(value ?? '').replace(/\\\\/g, '\\\\\\\\').replace(/"/g, '\\\\"');
        const visible = (el) => {
          const style = window.getComputedStyle(el);
          const rect = el.getBoundingClientRect();
          return style.visibility !== 'hidden' && style.display !== 'none' && rect.width > 0 && rect.height > 0;
        };
        const enabled = (el) => !el.disabled && el.getAttribute('aria-disabled') !== 'true' && !el.closest('[aria-disabled="true"]');
        const roleOf = (el) => {
          const explicit = el.getAttribute('role');
          if (explicit) return normalize(explicit).split(' ')[0];
          const tag = el.tagName.toLowerCase();
          if (tag === 'a' && el.hasAttribute('href')) return 'link';
          if (tag === 'button') return 'button';
          if (tag === 'input') {
            const type = (el.getAttribute('type') || 'text').toLowerCase();
            if (type === 'button' || type === 'submit' || type === 'reset') return 'button';
            if (type === 'checkbox') return 'checkbox';
            if (type === 'radio') return 'radio';
            if (type === 'search') return 'searchbox';
            return 'textbox';
          }
          if (tag === 'textarea') return 'textbox';
          if (tag === 'select') return 'combobox';
          if (tag === 'summary') return 'button';
          return '';
        };
        const textOf = (el) => normalize(
          el.innerText ||
          el.textContent ||
          el.getAttribute('aria-label') ||
          el.getAttribute('placeholder') ||
          el.getAttribute('value') ||
          ''
        );
        const nameOf = (el) => {
          return normalize(
            el.getAttribute('aria-label') ||
            el.getAttribute('aria-labelledby')?.split(/\\s+/).map((id) => document.getElementById(id)?.textContent || '').join(' ') ||
            el.getAttribute('title') ||
            el.getAttribute('alt') ||
            el.innerText ||
            el.textContent ||
            el.getAttribute('placeholder') ||
            el.getAttribute('value') ||
            ''
          );
        };
        const fallbackSelectorOf = (el) => {
          const tag = el.tagName.toLowerCase();
          if (el.id) return tag + '#' + cssEscape(el.id);
          const testId = el.getAttribute('data-testid');
          if (testId) return tag + '[data-testid="' + attrValue(testId) + '"]';
          const href = el.getAttribute('href');
          if (tag === 'a' && href) return 'a[href="' + attrValue(href) + '"]';
          const name = el.getAttribute('name');
          if (name) return tag + '[name="' + attrValue(name) + '"]';
          const aria = el.getAttribute('aria-label');
          if (aria) return tag + '[aria-label="' + attrValue(aria) + '"]';
          return tag;
        };
        const selectorOf = (el) => {
          try {
            return injected.generateSelectorSimple(el) || fallbackSelectorOf(el);
          } catch {
            return fallbackSelectorOf(el);
          }
        };
        const elementInfo = (el, index) => {
          const tagName = el.tagName.toLowerCase();
          const rect = el.getBoundingClientRect();
          const text = textOf(el);
          const name = nameOf(el);
          return {
            index,
            tagName,
            tag: tagName,
            role: roleOf(el),
            name,
            text,
            href: el.getAttribute('href') || undefined,
            testId: el.getAttribute('data-testid') || undefined,
            selector: selectorOf(el),
            visible: visible(el),
            enabled: enabled(el),
            visibleText: text,
            ariaName: name,
            boundingBox: rect ? { x: rect.left, y: rect.top, width: rect.width, height: rect.height } : null
          };
        };
        window.__dotcraftPlaywrightInjected = injected;
        window.__dotcraftBrowserUseElementInfo = elementInfo;
        window.__dotcraftBrowserUseResolveSelector = (parsed) => {
          const elements = injected.querySelectorAll(parsed, document);
          injected.checkDeprecatedSelectorUsage(parsed, elements);
          return elements.slice(0, 100).map(elementInfo);
        };
        window.__dotcraftBrowserUseSnapshot = () => {
          const interesting = [
            'a',
            'button',
            'input',
            'textarea',
            'select',
            'summary',
            '[role="button"]',
            '[role="link"]',
            '[role="menuitem"]',
            '[role="tab"]',
            '[contenteditable="true"]'
          ];
          const seen = new Set();
          const elements = Array.from(document.querySelectorAll(interesting.join(',')))
            .filter((el) => {
              if (!el || seen.has(el) || !visible(el)) return false;
              seen.add(el);
              return true;
            })
            .slice(0, 200)
            .map(elementInfo);
          const bodyText = (document.body?.innerText || '').trim().replace(/\\s+/g, ' ').slice(0, 4000);
          return { title: document.title, url: location.href, bodyText, elements };
        };
        return true;
      })()
    `, 'playwright.inject')
  }

  private async evaluateInPage(tab: BrowserUseTabRuntime, expressionOrFunction: string | (() => unknown)): Promise<unknown> {
    const source = typeof expressionOrFunction === 'function'
      ? `(${expressionOrFunction.toString()})()`
      : String(expressionOrFunction)
    await this.waitForPageReady(tab, {
      operation: 'evaluate.ready',
      requireContent: false,
      timeoutMs: this.operationTimeoutMs()
    })
    return this.executeJavaScript(tab, source, 'evaluate')
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
    await this.withBrowserOperation(tab, 'cua.move', () => this.viewerHost.moveMouse(tab.owner, {
      tabId: tab.id,
      x: Number(options.x),
      y: Number(options.y),
      waitForArrival: options.waitForArrival
    }))
  }

  private async cuaClick(tab: BrowserUseTabRuntime, options: { x: number; y: number; button?: number | string; preserveRefs?: boolean }): Promise<void> {
    this.markAutomation(tab, 'click')
    await this.withBrowserOperation(tab, 'cua.click', () => this.viewerHost.clickMouse(tab.owner, {
      tabId: tab.id,
      x: Number(options.x),
      y: Number(options.y),
      button: this.normalizeMouseButton(options.button)
    }))
    if (options.preserveRefs !== true) this.invalidateSnapshotRefs(tab)
  }

  private async cuaDoubleClick(tab: BrowserUseTabRuntime, options: { x: number; y: number; button?: number | string }): Promise<void> {
    this.markAutomation(tab, 'double click')
    await this.withBrowserOperation(tab, 'cua.double_click', () => this.viewerHost.doubleClickMouse(tab.owner, {
      tabId: tab.id,
      x: Number(options.x),
      y: Number(options.y),
      button: this.normalizeMouseButton(options.button)
    }))
    this.invalidateSnapshotRefs(tab)
  }

  private async cuaDrag(tab: BrowserUseTabRuntime, options: { path: Array<{ x: number; y: number }> }): Promise<void> {
    this.markAutomation(tab, 'drag')
    await this.withBrowserOperation(tab, 'cua.drag', () => this.viewerHost.dragMouse(tab.owner, {
      tabId: tab.id,
      path: Array.isArray(options.path) ? options.path : []
    }))
    this.invalidateSnapshotRefs(tab)
  }

  private async cuaScroll(tab: BrowserUseTabRuntime, options: { x: number; y: number; scrollX: number; scrollY: number }): Promise<void> {
    this.markAutomation(tab, 'scroll')
    await this.withBrowserOperation(tab, 'cua.scroll', () => this.viewerHost.scrollMouse(tab.owner, {
      tabId: tab.id,
      x: Number(options.x),
      y: Number(options.y),
      scrollX: Number(options.scrollX ?? 0),
      scrollY: Number(options.scrollY ?? 0)
    }))
  }

  private async cuaType(tab: BrowserUseTabRuntime, options: { text: string }): Promise<void> {
    this.markAutomation(tab, 'type')
    await this.withBrowserOperation(
      tab,
      'cua.type',
      () => this.viewerHost.typeText(tab.owner, { tabId: tab.id, text: String(options.text ?? '') }))
    this.invalidateSnapshotRefs(tab)
  }

  private async cuaKeypress(tab: BrowserUseTabRuntime, options: { keys: string[] }): Promise<void> {
    this.markAutomation(tab, 'keypress')
    await this.withBrowserOperation(tab, 'cua.keypress', async () => {
      this.viewerHost.keypress(tab.owner, { tabId: tab.id, keys: Array.isArray(options.keys) ? options.keys.map(String) : [] })
    })
    this.invalidateSnapshotRefs(tab)
  }

  private async locatorClick(tab: BrowserUseTabRuntime, descriptor: BrowserUseLocatorDescriptor): Promise<void> {
    const target = await this.waitForActionableLocator(tab, descriptor)
    const point = this.actionPoint(target)
    await this.cuaClick(tab, { ...point, preserveRefs: true })
    this.invalidateSnapshotRefs(tab)
  }

  private async locatorDoubleClick(tab: BrowserUseTabRuntime, descriptor: BrowserUseLocatorDescriptor): Promise<void> {
    const target = await this.waitForActionableLocator(tab, descriptor)
    const point = this.actionPoint(target)
    await this.cuaDoubleClick(tab, { ...point })
  }

  private async locatorType(tab: BrowserUseTabRuntime, descriptor: BrowserUseLocatorDescriptor, value: string): Promise<void> {
    const target = await this.waitForActionableLocator(tab, descriptor)
    const point = this.actionPoint(target)
    await this.cuaClick(tab, { ...point, preserveRefs: true })
    await this.cuaType(tab, { text: String(value ?? '') })
  }

  private async locatorFill(tab: BrowserUseTabRuntime, descriptor: BrowserUseLocatorDescriptor, value: string): Promise<void> {
    const target = await this.waitForActionableLocator(tab, descriptor)
    const point = this.actionPoint(target)
    await this.cuaClick(tab, { ...point, preserveRefs: true })
    await this.mutateStrictLocator(tab, descriptor, String(value ?? ''))
    this.invalidateSnapshotRefs(tab)
  }

  private async locatorPress(tab: BrowserUseTabRuntime, descriptor: BrowserUseLocatorDescriptor, value: string): Promise<void> {
    const target = await this.waitForActionableLocator(tab, descriptor)
    const point = this.actionPoint(target)
    await this.cuaClick(tab, { ...point, preserveRefs: true })
    await this.cuaKeypress(tab, { keys: [String(value)] })
  }

  private async waitForActionableLocator(
    tab: BrowserUseTabRuntime,
    descriptor: BrowserUseLocatorDescriptor
  ): Promise<BrowserUseElementMatch> {
    const deadline = Date.now() + this.operationTimeoutMs()
    let lastError: Error | null = null
    for (;;) {
      try {
        const target = await this.strictLocator(tab, descriptor)
        this.assertActionable(target, descriptor)
        return target
      } catch (error) {
        const current = error instanceof Error ? error : new Error(String(error))
        if (
          current.message.startsWith('Strict mode violation') ||
          current.message.startsWith('Unknown browser snapshot ref')
        ) {
          throw current
        }
        lastError = current
      }
      if (Date.now() >= deadline) {
        throw new Error(`Timed out waiting for locator ${this.describeLocator(descriptor)} to become visible and enabled. Last error: ${lastError?.message ?? 'unknown'}`)
      }
      await new Promise((resolve) => setTimeout(resolve, 100))
    }
  }

  private assertActionable(match: BrowserUseElementMatch, descriptor: BrowserUseLocatorDescriptor): void {
    if (!match.visible) {
      throw new Error(`Locator ${this.describeLocator(descriptor)} resolved to a hidden element: ${this.describeElementMatch(match)}`)
    }
    if (!match.enabled) {
      throw new Error(`Locator ${this.describeLocator(descriptor)} resolved to a disabled element: ${this.describeElementMatch(match)}`)
    }
  }

  private async strictLocator(tab: BrowserUseTabRuntime, descriptor: BrowserUseLocatorDescriptor): Promise<BrowserUseElementMatch> {
    const matches = await this.resolveLocator(tab, descriptor)
    if (matches.length === 0) {
      throw new Error(`No element found for locator: ${this.describeLocator(descriptor)}`)
    }
    if (matches.length > 1) {
      const examples = matches.slice(0, 5).map((match) => this.describeElementMatch(match)).join('; ')
      throw new Error(`Strict mode violation for locator ${this.describeLocator(descriptor)}: ${matches.length} elements matched. Matches: ${examples}`)
    }
    return matches[0]!
  }

  private describeElementMatch(match: BrowserUseElementMatch): string {
    const box = match.boundingBox
      ? `@${Math.round(match.boundingBox.x)},${Math.round(match.boundingBox.y)} ${Math.round(match.boundingBox.width)}x${Math.round(match.boundingBox.height)}`
      : '@no-box'
    const label = match.name || match.text || match.visibleText || match.ariaName || ''
    const href = match.href ? ` href=${match.href}` : ''
    const ref = match.ref ? ` ref=${match.ref}` : ''
    const state = `${match.visible ? 'visible' : 'hidden'}/${match.enabled ? 'enabled' : 'disabled'}`
    return `${match.tagName || match.tag || 'element'}[${match.role || 'generic'}] "${label}" ${match.selector}${href}${ref} ${state} ${box}`
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
    const snapshotRef = descriptor.kind === 'ref'
      ? this.snapshotRef(tab, descriptor.value)
      : null
    const selector = snapshotRef?.selector || (descriptor.kind !== 'ref' ? this.playwrightSelectorFor(descriptor) : '')
    if (!selector && snapshotRef) return [snapshotRef]
    await this.ensurePlaywrightInjected(tab)
    const parsed = parsePlaywrightSelector(selector)
    const matches = await this.executeJavaScript<BrowserUseElementMatch[]>(
      tab,
      `window.__dotcraftBrowserUseResolveSelector(${JSON.stringify(parsed)})`,
      'locator.resolve')
    const normalized = Array.isArray(matches)
      ? matches.map((match, index) => this.normalizeElementMatch(match, index))
      : []
    if (!snapshotRef) return normalized
    return normalized
      .filter((match) => this.matchesSnapshotRef(match, snapshotRef))
      .map((match) => ({ ...match, ref: snapshotRef.ref }))
  }

  private snapshotRef(tab: BrowserUseTabRuntime, ref: string): BrowserUseElementMatch {
    const snapshotRef = tab.snapshotRefs.get(ref)
    if (!snapshotRef) {
      throw new Error(`Unknown browser snapshot ref '${ref}' for tab ${tab.id}. Take a fresh domSnapshot() and use a current ref.`)
    }
    return snapshotRef
  }

  private matchesSnapshotRef(current: BrowserUseElementMatch, snapshotRef: BrowserUseElementMatch): boolean {
    if (snapshotRef.href && current.href !== snapshotRef.href) return false
    if (snapshotRef.testId && current.testId !== snapshotRef.testId) return false
    if (snapshotRef.role && current.role !== snapshotRef.role) return false
    const expectedName = snapshotRef.name || snapshotRef.text || snapshotRef.visibleText
    if (expectedName) {
      const actualName = current.name || current.text || current.visibleText
      if (actualName !== expectedName) return false
    }
    return true
  }

  private playwrightSelectorFor(descriptor: BrowserUseLocatorDescriptor): string {
    if (descriptor.kind === 'css') return descriptor.value
    if (descriptor.kind === 'text') return getByTextSelector(descriptor.value, { exact: descriptor.exact === true })
    if (descriptor.kind === 'label') return getByLabelSelector(descriptor.value, { exact: descriptor.exact === true })
    if (descriptor.kind === 'placeholder') return getByPlaceholderSelector(descriptor.value, { exact: descriptor.exact === true })
    if (descriptor.kind === 'testId') return getByTestIdSelector('data-testid', descriptor.value)
    if (descriptor.kind === 'role') {
      return getByRoleSelector(descriptor.value, {
        name: descriptor.name,
        exact: descriptor.exact === true
      })
    }
    throw new Error(`Unsupported browser locator: ${this.describeLocator(descriptor)}`)
  }

  private async locatorEvaluate(
    tab: BrowserUseTabRuntime,
    descriptor: BrowserUseLocatorDescriptor,
    operation: 'textContent' | 'getAttribute' | 'isEnabled',
    arg?: string
  ): Promise<unknown> {
    const target = await this.strictLocator(tab, descriptor)
    const selector = target.selector || this.playwrightSelectorFor(descriptor)
    const parsed = parsePlaywrightSelector(selector)
    const script = `
      ((parsed, operation, arg) => {
        const injected = window.__dotcraftPlaywrightInjected;
        const el = injected.querySelector(parsed, document, true);
        if (!el) return null;
        if (operation === 'textContent') return el.textContent;
        if (operation === 'getAttribute') return el.getAttribute(arg);
        if (operation === 'isEnabled') return !el.disabled && el.getAttribute('aria-disabled') !== 'true' && !el.closest('[aria-disabled="true"]');
        return null;
      })(${JSON.stringify(parsed)}, ${JSON.stringify(operation)}, ${JSON.stringify(arg ?? '')})
    `
    return await this.executeJavaScript(tab, script, `locator.${operation}`)
  }

  private async mutateStrictLocator(
    tab: BrowserUseTabRuntime,
    descriptor: BrowserUseLocatorDescriptor,
    value: string
  ): Promise<void> {
    const target = await this.strictLocator(tab, descriptor)
    const selector = target.selector || this.playwrightSelectorFor(descriptor)
    const parsed = parsePlaywrightSelector(selector)
    const script = `
      ((parsed, value) => {
        const injected = window.__dotcraftPlaywrightInjected;
        const el = injected.querySelector(parsed, document, true);
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
      })(${JSON.stringify(parsed)}, ${JSON.stringify(value)})
    `
    await this.executeJavaScript(tab, script, 'locator.fill')
  }

  private async locatorWaitFor(
    tab: BrowserUseTabRuntime,
    descriptor: BrowserUseLocatorDescriptor,
    options?: { state?: string; timeoutMs?: number }
  ): Promise<void> {
    const expected = options?.state ?? 'visible'
    const deadline = Date.now() + Math.max(1_000, Math.min(options?.timeoutMs ?? 30_000, 120_000))
    for (;;) {
      const signal = this.getRuntimeForTab(tab).activeAbortSignal
      if (signal?.aborted) throw new Error(`Browser operation 'locator.waitFor' was cancelled for tab ${tab.id}.`)
      const matches = await this.resolveLocator(tab, descriptor)
      const visibleCount = matches.filter((m) => m.visible).length
      if ((expected === 'hidden') ? visibleCount === 0 : (expected === 'detached') ? matches.length === 0 : visibleCount > 0) return
      if (Date.now() > deadline) throw new Error(`Timed out waiting for locator ${this.describeLocator(descriptor)} to be ${expected}.`)
      await new Promise((resolve) => setTimeout(resolve, 100))
    }
  }

  private waitForUrl(tab: BrowserUseTabRuntime, expectedUrl: unknown, timeoutMs: number): Promise<void> {
    return this.withBrowserOperation(
      tab,
      'waitForURL',
      () => this.waitForUrlInner(tab, expectedUrl, timeoutMs),
      timeoutMs)
  }

  private waitForUrlInner(tab: BrowserUseTabRuntime, expectedUrl: unknown, timeoutMs: number): Promise<void> {
    const wc = this.webContentsFor(tab.owner, tab.id)
    const expectedDescription = this.describeExpectedUrl(expectedUrl)
    const matches = (url: string) => this.urlMatches(url, expectedUrl)
    if (matches(wc.getURL())) return Promise.resolve()
    return new Promise((resolve, reject) => {
      const signal = this.getRuntimeForTab(tab).activeAbortSignal
      if (signal?.aborted) {
        reject(new Error(`Browser operation 'waitForURL' was cancelled for tab ${tab.id}.`))
        return
      }
      const effectiveTimeoutMs = Math.max(1_000, Math.min(timeoutMs, 120_000))
      const timeout = setTimeout(() => {
        cleanup()
        reject(new Error(`Browser operation 'waitForURL' timed out after ${effectiveTimeoutMs}ms for tab ${tab.id}: ${expectedDescription}; current=${wc.getURL() || 'about:blank'}`))
      }, effectiveTimeoutMs)
      const done = () => {
        if (!matches(wc.getURL())) return
        cleanup()
        resolve()
      }
      const poll = setInterval(done, 100)
      const onAbort = () => {
        cleanup()
        reject(new Error(`Browser operation 'waitForURL' was cancelled for tab ${tab.id}.`))
      }
      const cleanup = () => {
        clearTimeout(timeout)
        clearInterval(poll)
        wc.off('did-navigate', done)
        wc.off('did-navigate-in-page', done)
        wc.off('did-stop-loading', done)
        signal?.removeEventListener('abort', onAbort)
      }
      signal?.addEventListener('abort', onAbort, { once: true })
      wc.on('did-navigate', done)
      wc.on('did-navigate-in-page', done)
      wc.on('did-stop-loading', done)
      done()
    })
  }

  private describeExpectedUrl(expectedUrl: unknown): string {
    if (expectedUrl instanceof RegExp) return expectedUrl.toString()
    if (typeof expectedUrl === 'string') return expectedUrl
    return String(expectedUrl)
  }

  private urlMatches(actualUrl: string, expectedUrl: unknown): boolean {
    if (expectedUrl instanceof RegExp) {
      expectedUrl.lastIndex = 0
      return expectedUrl.test(actualUrl)
    }
    if (typeof expectedUrl === 'string') return actualUrl === expectedUrl
    return actualUrl === String(expectedUrl)
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

  private normalizeLoadState(state: string): BrowserUseLoadState {
    const normalized = String(state ?? 'load').toLowerCase()
    if (normalized === 'commit' || normalized === 'domcontentloaded' || normalized === 'load' || normalized === 'networkidle') {
      return normalized
    }
    throw new Error(`Unsupported browser load state: ${state}`)
  }

  private async waitForLoad(
    tab: BrowserUseTabRuntime,
    state: string = 'load',
    timeoutMs: number = 30_000
  ): Promise<void> {
    const loadState = this.normalizeLoadState(state)
    const effectiveTimeoutMs = Math.max(1, Math.min(timeoutMs, 120_000))
    if (loadState === 'commit') {
      await this.waitForCommit(tab, effectiveTimeoutMs)
      return
    }
    if (loadState === 'domcontentloaded') {
      await this.waitForPageReady(tab, {
        operation: 'waitForLoadState.domcontentloaded',
        requireContent: false,
        timeoutMs: effectiveTimeoutMs
      })
      return
    }
    await this.waitForLoadEvent(tab, effectiveTimeoutMs)
    await this.waitForPageReady(tab, {
      operation: `waitForLoadState.${loadState}`,
      requireContent: loadState === 'networkidle',
      timeoutMs: effectiveTimeoutMs
    })
    if (loadState === 'networkidle') {
      await this.waitForNetworkIdle(tab, effectiveTimeoutMs)
    }
  }

  private waitForCommit(tab: BrowserUseTabRuntime, timeoutMs: number): Promise<void> {
    const wc = this.webContentsFor(tab.owner, tab.id)
    if (wc.getURL()) return Promise.resolve()
    return new Promise((resolve, reject) => {
      const signal = this.getRuntimeForTab(tab).activeAbortSignal
      if (signal?.aborted) {
        reject(new Error(`Browser operation 'waitForLoadState.commit' was cancelled for tab ${tab.id}.`))
        return
      }
      const timeout = setTimeout(() => {
        cleanup()
        reject(new Error(`Browser operation 'waitForLoadState.commit' timed out after ${timeoutMs}ms for tab ${tab.id}.`))
      }, timeoutMs)
      const done = () => {
        cleanup()
        resolve()
      }
      const onAbort = () => {
        cleanup()
        reject(new Error(`Browser operation 'waitForLoadState.commit' was cancelled for tab ${tab.id}.`))
      }
      const cleanup = () => {
        clearTimeout(timeout)
        wc.off('did-start-loading', done)
        wc.off('did-navigate', done)
        signal?.removeEventListener('abort', onAbort)
      }
      signal?.addEventListener('abort', onAbort, { once: true })
      wc.once('did-start-loading', done)
      wc.once('did-navigate', done)
    })
  }

  private waitForLoadEvent(tab: BrowserUseTabRuntime, timeoutMs: number): Promise<void> {
    const wc = this.webContentsFor(tab.owner, tab.id)
    if (!wc.isLoading()) return Promise.resolve()
    return new Promise((resolve, reject) => {
      const signal = this.getRuntimeForTab(tab).activeAbortSignal
      if (signal?.aborted) {
        reject(new Error(`Browser operation 'waitForLoadState' was cancelled for tab ${tab.id}.`))
        return
      }
      const timeout = setTimeout(() => {
        cleanup()
        reject(new Error(`Browser operation 'waitForLoadState' timed out after ${Math.max(1_000, Math.min(timeoutMs, 120_000))}ms for tab ${tab.id}.`))
      }, Math.max(1_000, Math.min(timeoutMs, 120_000)))
      const done = () => {
        cleanup()
        resolve()
      }
      const onAbort = () => {
        cleanup()
        reject(new Error(`Browser operation 'waitForLoadState' was cancelled for tab ${tab.id}.`))
      }
      const cleanup = () => {
        clearTimeout(timeout)
        wc.off('did-finish-load', done)
        wc.off('did-stop-loading', done)
        signal?.removeEventListener('abort', onAbort)
      }
      signal?.addEventListener('abort', onAbort, { once: true })
      wc.once('did-finish-load', done)
      wc.once('did-stop-loading', done)
    })
  }

  private async waitForNetworkIdle(tab: BrowserUseTabRuntime, timeoutMs: number): Promise<void> {
    const wc = this.webContentsFor(tab.owner, tab.id)
    const deadline = Date.now() + timeoutMs
    for (;;) {
      if (Date.now() >= deadline) {
        throw new Error(`Browser operation 'waitForLoadState.networkidle' timed out after ${timeoutMs}ms for tab ${tab.id} at ${wc.getURL() || 'about:blank'}.`)
      }
      await this.waitForLoadEvent(tab, Math.max(1, deadline - Date.now()))
      await new Promise<void>((resolve, reject) => {
        const signal = this.getRuntimeForTab(tab).activeAbortSignal
        if (signal?.aborted) {
          reject(new Error(`Browser operation 'waitForLoadState.networkidle' was cancelled for tab ${tab.id}.`))
          return
        }
        let quietTimer: ReturnType<typeof setTimeout>
        const hardTimer = setTimeout(() => {
          cleanup()
          reject(new Error(`Browser operation 'waitForLoadState.networkidle' timed out after ${timeoutMs}ms for tab ${tab.id} at ${wc.getURL() || 'about:blank'}.`))
        }, Math.max(1, deadline - Date.now()))
        const finish = () => {
          cleanup()
          resolve()
        }
        const restart = () => {
          clearTimeout(quietTimer)
          quietTimer = setTimeout(finish, BROWSER_USE_NETWORK_IDLE_QUIET_MS)
        }
        const onAbort = () => {
          cleanup()
          reject(new Error(`Browser operation 'waitForLoadState.networkidle' was cancelled for tab ${tab.id}.`))
        }
        const cleanup = () => {
          clearTimeout(quietTimer)
          clearTimeout(hardTimer)
          wc.off('did-start-loading', restart)
          wc.off('did-stop-loading', restart)
          signal?.removeEventListener('abort', onAbort)
        }
        signal?.addEventListener('abort', onAbort, { once: true })
        wc.on('did-start-loading', restart)
        wc.on('did-stop-loading', restart)
        restart()
      })
      if (!wc.isLoading()) return
    }
  }

}

export const browserUseManager = new BrowserUseManager()
export { BROWSER_USE_OPEN_CHANNEL }
