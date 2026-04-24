import { BrowserWindow, session } from 'electron'
import vm from 'vm'
import { installViewerProtocolHandlerForSession } from './viewerFileProtocol'
import { partitionForWorkspace } from './viewerBrowser'

const LOCAL_HOSTS = new Set(['localhost', '127.0.0.1', '::1', '[::1]'])
const VIEWER_SCHEME = 'dotcraft-viewer:'

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

interface BrowserUseTabRuntime {
  id: string
  window: BrowserWindow
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
  if (url === 'about:blank') return true
  try {
    const parsed = new URL(url)
    if (parsed.protocol === 'file:' || parsed.protocol === VIEWER_SCHEME) return true
    if (parsed.protocol === 'http:' || parsed.protocol === 'https:') {
      return LOCAL_HOSTS.has(parsed.hostname.toLowerCase())
    }
    return false
  } catch {
    return false
  }
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

export class BrowserUseManager {
  private readonly runtimes = new Map<string, BrowserUseThreadRuntime>()
  private readonly configuredPartitions = new Set<string>()
  private nextTabId = 1

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
    for (const tab of runtime.tabs.values()) {
      if (!tab.window.isDestroyed()) tab.window.close()
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
      images: []
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
    const id = `browser-use-tab-${this.nextTabId++}`
    const partitionName = partitionForWorkspace(runtime.workspacePath || owner.getTitle())
    const partitionSession = session.fromPartition(partitionName)
    this.configurePartition(partitionName, partitionSession)

    const win = new BrowserWindow({
      show: false,
      width: 1280,
      height: 900,
      webPreferences: {
        session: partitionSession,
        contextIsolation: true,
        sandbox: true,
        nodeIntegration: false
      }
    })
    const tab: BrowserUseTabRuntime = { id, window: win, logs: [] }
    runtime.tabs.set(id, tab)

    win.webContents.on('console-message', (_event, _level, message) => {
      tab.logs.push(message)
    })
    win.on('closed', () => {
      runtime.tabs.delete(id)
      if (runtime.selectedTabId === id) runtime.selectedTabId = null
    })

    if (initialUrl) await this.navigate(tab, initialUrl)
    return tab
  }

  private configurePartition(partitionName: string, partitionSession: Electron.Session): void {
    if (this.configuredPartitions.has(partitionName)) return
    installViewerProtocolHandlerForSession(partitionSession)
    partitionSession.on('will-download', (event, item) => {
      event.preventDefault()
      item.cancel()
    })
    partitionSession.setPermissionCheckHandler((_wc, permission) => permission === 'clipboard-sanitized-write')
    partitionSession.setPermissionRequestHandler((_wc, permission, callback) => {
      callback(permission === 'clipboard-sanitized-write')
    })
    this.configuredPartitions.add(partitionName)
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
      url: async () => tab.window.webContents.getURL(),
      title: async () => tab.window.webContents.getTitle(),
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
    return {
      id: tab.id,
      url: tab.window.webContents.getURL(),
      title: tab.window.webContents.getTitle(),
      loading: tab.window.webContents.isLoading()
    }
  }

  private async navigate(tab: BrowserUseTabRuntime, url: string): Promise<Record<string, unknown>> {
    const normalized = normalizeBrowserUseUrl(url)
    if (!normalized) throw new Error(`Invalid browser URL: ${url}`)
    if (!isBrowserUseUrlAllowed(normalized)) {
      throw new Error(`Blocked browser-use navigation to non-local URL: ${normalized}`)
    }
    await tab.window.webContents.loadURL(normalized)
    return this.tabSnapshot(tab)
  }

  private async screenshot(tab: BrowserUseTabRuntime): Promise<BrowserUseImageResult> {
    const image = await tab.window.webContents.capturePage()
    return {
      mediaType: 'image/png',
      dataBase64: image.toPNG().toString('base64')
    }
  }

  private async domSnapshot(tab: BrowserUseTabRuntime): Promise<string> {
    return String(await tab.window.webContents.executeJavaScript(`
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
    return tab.window.webContents.executeJavaScript(source)
  }

  private async click(tab: BrowserUseTabRuntime, selector: string): Promise<void> {
    const quotedSelector = JSON.stringify(selector)
    await tab.window.webContents.executeJavaScript(`
      (() => {
        const selector = ${quotedSelector};
        const el = document.querySelector(selector);
        if (!el) throw new Error('Selector not found: ' + selector);
        el.click();
      })()
    `)
  }

  private async type(tab: BrowserUseTabRuntime, selector: string, text: string): Promise<void> {
    const quotedSelector = JSON.stringify(selector)
    await tab.window.webContents.executeJavaScript(`
      (() => {
        const selector = ${quotedSelector};
        const el = document.querySelector(selector);
        if (!el) throw new Error('Selector not found: ' + selector);
        el.focus();
      })()
    `)
    tab.window.webContents.insertText(text)
  }

  private async press(tab: BrowserUseTabRuntime, selector: string, key: string): Promise<void> {
    const quotedSelector = JSON.stringify(selector)
    await tab.window.webContents.executeJavaScript(`
      (() => {
        const selector = ${quotedSelector};
        const el = document.querySelector(selector);
        if (!el) throw new Error('Selector not found: ' + selector);
        el.focus();
      })()
    `)
    tab.window.webContents.sendInputEvent({ type: 'keyDown', keyCode: key })
    tab.window.webContents.sendInputEvent({ type: 'keyUp', keyCode: key })
  }

  private waitForLoad(tab: BrowserUseTabRuntime, timeoutMs: number): Promise<void> {
    if (!tab.window.webContents.isLoading()) return Promise.resolve()
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
        tab.window.webContents.off('did-finish-load', done)
        tab.window.webContents.off('did-stop-loading', done)
      }
      tab.window.webContents.once('did-finish-load', done)
      tab.window.webContents.once('did-stop-loading', done)
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
