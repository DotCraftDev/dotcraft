import { BrowserWindow, app } from 'electron'
import { existsSync } from 'fs'
import { join } from 'path'
import { pathToFileURL } from 'url'
import { createContext, Script, type Context } from 'vm'
import { browserUseManager, type BrowserUseImageResult, type BrowserUseManager } from './browserUseManager'

export interface NodeReplEvaluateParams {
  threadId: string
  evaluationId?: string
  code: string
  timeoutMs?: number
  workspacePath?: string
}

export interface NodeReplEvaluateResult {
  text?: string
  resultText?: string
  images: BrowserUseImageResult[]
  logs: string[]
  error?: string
}

interface NodeReplThreadRuntime {
  context: Context
  globals: Record<string, unknown>
  logs: string[]
  activeEvaluationId?: string
  activeAbortController?: AbortController
  phase?: string
}

interface BrowserRuntimeBindings {
  agent: Record<string, unknown>
  display: (imageLike: unknown) => Promise<void>
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

function formatError(error: unknown, phase: string | undefined): string {
  const prefix = `phase=${phase ?? 'js-runtime'}`
  if (error instanceof Error) return `${prefix} ${error.name}: ${error.message}`
  return `${prefix} ${String(error)}`
}

function resolveBrowserClientPath(): string {
  const resourcesPath = process.resourcesPath
  if (resourcesPath) {
    const packaged = join(resourcesPath, 'browser-use', 'browser-client.mjs')
    if (existsSync(packaged)) return pathToFileURL(packaged).href
  }

  const dev = join(app.getAppPath(), 'resources', 'browser-use', 'browser-client.mjs')
  return pathToFileURL(dev).href
}

function createReplRuntime(): NodeReplThreadRuntime {
  const globals: Record<string, unknown> = {}
  return {
    globals,
    context: createContext(globals),
    logs: [],
    phase: 'idle'
  }
}

function newEvaluationId(): string {
  return `node-repl-${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`
}

function normalizeCellCode(code: string): string {
  return String(code ?? '').replace(/\bimport\s*\(/g, '__dotcraftDynamicImport(')
}

function compileCell(code: string): { script: Script; kind: 'expression' | 'statement' } {
  const normalized = normalizeCellCode(code)
  const trimmed = normalized.trim()
  if (!trimmed) {
    return {
      script: new Script('(async () => {})()', { filename: 'NodeReplJs' }),
      kind: 'statement'
    }
  }

  const expressionSource = `(async () => { return (${trimmed}\n); })()`
  try {
    return {
      script: new Script(expressionSource, { filename: 'NodeReplJs' }),
      kind: 'expression'
    }
  } catch {
    const statementSource = `(async () => {\n${normalized}\n})()`
    return {
      script: new Script(statementSource, { filename: 'NodeReplJs' }),
      kind: 'statement'
    }
  }
}

export class NodeReplManager {
  private readonly runtimes = new Map<string, NodeReplThreadRuntime>()

  constructor(private readonly browserManager: BrowserUseManager = browserUseManager) {}

  async evaluate(owner: BrowserWindow, params: NodeReplEvaluateParams): Promise<NodeReplEvaluateResult> {
    if (!params.threadId || typeof params.code !== 'string') {
      return { error: 'Invalid Node REPL evaluate request.', images: [], logs: [] }
    }

    const runtime = this.getOrCreateRuntime(params.threadId)
    if (runtime.activeEvaluationId) {
      return { error: `NodeReplJs is already running for this thread: ${runtime.activeEvaluationId}`, images: [], logs: [] }
    }

    const evaluationId = params.evaluationId?.trim() || newEvaluationId()
    const abortController = new AbortController()
    runtime.activeEvaluationId = evaluationId
    runtime.activeAbortController = abortController
    runtime.logs = []
    runtime.phase = 'prepare'
    const browserRuntime = this.browserManager.prepareNodeRepl(owner, {
      threadId: params.threadId,
      workspacePath: params.workspacePath,
      evaluationId,
      signal: abortController.signal
    })
    this.refreshContext(runtime, browserRuntime, evaluationId, abortController.signal)

    const timeoutMs = Math.max(1_000, Math.min(params.timeoutMs ?? 30_000, 120_000))
    try {
      runtime.phase = 'js-compile'
      const cell = compileCell(params.code)
      runtime.phase = `js-runtime:${cell.kind}`
      const value = await this.withTimeout(
        Promise.resolve(cell.script.runInContext(runtime.context, {
          displayErrors: true,
          timeout: 1_000
        })),
        timeoutMs,
        abortController.signal,
        () => runtime.phase)
      const collected = browserRuntime.collect()
      return {
        resultText: describeResult(value),
        images: collected.images,
        logs: [...runtime.logs, ...collected.logs]
      }
    } catch (error: unknown) {
      const isTimeoutOrCancel = error instanceof Error &&
        (error.message.includes('timed out') || error.message.includes('cancelled'))
      if (isTimeoutOrCancel) {
        abortController.abort()
        this.browserManager.abortEvaluation(params.threadId, evaluationId)
        this.disposeReplRuntime(params.threadId, runtime)
      }
      const collected = browserRuntime.collect()
      return {
        error: error instanceof Error && isTimeoutOrCancel
          ? error.message
          : formatError(error, runtime.phase),
        images: collected.images,
        logs: [...runtime.logs, ...collected.logs]
      }
    } finally {
      if (runtime.activeEvaluationId === evaluationId) {
        runtime.activeEvaluationId = undefined
        runtime.activeAbortController = undefined
        runtime.phase = 'idle'
      }
    }
  }

  cancel(threadId: string, evaluationId: string): { ok: boolean } {
    const runtime = this.runtimes.get(threadId)
    if (!runtime || runtime.activeEvaluationId !== evaluationId) return { ok: false }
    runtime.activeAbortController?.abort(new Error(`NodeReplJs cancelled (phase=${runtime.phase ?? 'unknown'}).`))
    this.browserManager.abortEvaluation(threadId, evaluationId)
    this.disposeReplRuntime(threadId, runtime)
    return { ok: true }
  }

  reset(threadId: string): { ok: boolean } {
    const runtime = this.runtimes.get(threadId)
    if (runtime) this.disposeReplRuntime(threadId, runtime)
    const browserReset = this.browserManager.reset(threadId)
    return { ok: Boolean(runtime) || browserReset.ok }
  }

  async disposeAllForTests(): Promise<void> {
    for (const [threadId, runtime] of [...this.runtimes]) {
      this.disposeReplRuntime(threadId, runtime)
    }
  }

  private getOrCreateRuntime(threadId: string): NodeReplThreadRuntime {
    const existing = this.runtimes.get(threadId)
    if (existing) return existing

    const runtime = createReplRuntime()
    this.runtimes.set(threadId, runtime)
    return runtime
  }

  private refreshContext(
    runtime: NodeReplThreadRuntime,
    browserRuntime: BrowserRuntimeBindings,
    evaluationId: string,
    signal: AbortSignal
  ): void {
    const globals = runtime.globals
    const ensureActive = () => {
      if (signal.aborted || runtime.activeEvaluationId !== evaluationId) {
        throw new Error(`NodeReplJs evaluation is no longer active (phase=${runtime.phase ?? 'unknown'}).`)
      }
    }
    const consoleApi = {
      log: (...args: unknown[]) => {
        if (runtime.activeEvaluationId === evaluationId) runtime.logs.push(args.map(describeResult).join(' '))
      },
      warn: (...args: unknown[]) => {
        if (runtime.activeEvaluationId === evaluationId) runtime.logs.push(args.map(describeResult).join(' '))
      },
      error: (...args: unknown[]) => {
        if (runtime.activeEvaluationId === evaluationId) runtime.logs.push(args.map(describeResult).join(' '))
      }
    }
    const display = async (imageLike: unknown) => {
      ensureActive()
      await browserRuntime.display(imageLike)
    }
    globals.agent = browserRuntime.agent
    globals.display = display
    globals.console = consoleApi
    globals.setTimeout = setTimeout
    globals.clearTimeout = clearTimeout
    globals.setInterval = setInterval
    globals.clearInterval = clearInterval
    globals.dotcraft = { browserUseClientPath: resolveBrowserClientPath() }
    globals.__dotcraftSetupAtlasRuntime = async (
      options?: { globals?: Record<string, unknown>; backend?: string }
    ) => {
      ensureActive()
      const targetGlobals = options?.globals ?? globals
      targetGlobals.agent = browserRuntime.agent
      targetGlobals.display = display
      targetGlobals.dotcraft = globals.dotcraft
      return { backend: options?.backend ?? 'iab' }
    }
  }

  private disposeReplRuntime(threadId: string, runtime: NodeReplThreadRuntime): void {
    if (this.runtimes.get(threadId) !== runtime) return
    runtime.activeEvaluationId = undefined
    runtime.activeAbortController = undefined
    this.runtimes.delete(threadId)
  }

  private withTimeout<T>(
    promise: Promise<T>,
    timeoutMs: number,
    signal: AbortSignal,
    phase: () => string | undefined
  ): Promise<T> {
    return new Promise((resolve, reject) => {
      let settled = false
      const cleanup = () => {
        clearTimeout(timeout)
        signal.removeEventListener('abort', onAbort)
      }
      const finish = (callback: () => void) => {
        if (settled) return
        settled = true
        cleanup()
        callback()
      }
      const onAbort = () => {
        const reason = signal.reason
        finish(() => reject(reason instanceof Error
          ? reason
          : new Error(`NodeReplJs cancelled (phase=${phase() ?? 'unknown'}).`)))
      }
      const timeout = setTimeout(
        () => finish(() => reject(new Error(
          `NodeReplJs timed out after ${timeoutMs}ms (phase=${phase() ?? 'unknown'}).`))),
        timeoutMs)
      if (signal.aborted) {
        onAbort()
        return
      }
      signal.addEventListener('abort', onAbort, { once: true })
      promise.then(
        (value) => {
          finish(() => resolve(value))
        },
        (error) => {
          finish(() => reject(error))
        }
      )
    })
  }
}

export const nodeReplManager = new NodeReplManager()
