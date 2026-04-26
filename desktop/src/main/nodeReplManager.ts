import { BrowserWindow, app } from 'electron'
import { existsSync } from 'fs'
import { PassThrough } from 'stream'
import { join } from 'path'
import { pathToFileURL } from 'url'
import repl from 'repl'
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
  replServer: repl.REPLServer
  logs: string[]
  activeEvaluationId?: string
  activeAbortController?: AbortController
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
  const input = new PassThrough()
  const output = new PassThrough()
  const replServer = repl.start({
    prompt: '',
    input,
    output,
    terminal: false,
    useGlobal: false,
    ignoreUndefined: true
  })
  return { replServer, logs: [] }
}

function newEvaluationId(): string {
  return `node-repl-${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`
}

export class NodeReplManager {
  private readonly runtimes = new Map<string, NodeReplThreadRuntime>()

  constructor(private readonly browserManager: BrowserUseManager = browserUseManager) {}

  async evaluate(owner: BrowserWindow, params: NodeReplEvaluateParams): Promise<NodeReplEvaluateResult> {
    if (!params.threadId || typeof params.code !== 'string') {
      return { error: 'Invalid Node REPL evaluate request.', images: [], logs: [] }
    }

    const runtime = this.getOrCreateRuntime(params.threadId)
    const evaluationId = params.evaluationId?.trim() || newEvaluationId()
    const abortController = new AbortController()
    runtime.activeEvaluationId = evaluationId
    runtime.activeAbortController = abortController
    runtime.logs = []
    const browserRuntime = this.browserManager.prepareNodeRepl(owner, {
      threadId: params.threadId,
      workspacePath: params.workspacePath,
      evaluationId,
      signal: abortController.signal
    })
    this.refreshContext(runtime, browserRuntime)

    const timeoutMs = Math.max(1_000, Math.min(params.timeoutMs ?? 30_000, 120_000))
    try {
      const value = await this.withTimeout(
        new Promise<unknown>((resolve, reject) => {
          runtime.replServer.eval(params.code, runtime.replServer.context, 'NodeReplJs', (error, result) => {
            if (error) reject(error)
            else resolve(result)
          })
        }),
        timeoutMs,
        abortController.signal)
      const collected = browserRuntime.collect()
      return {
        resultText: describeResult(value),
        images: collected.images,
        logs: [...runtime.logs, ...collected.logs]
      }
    } catch (error: unknown) {
      abortController.abort()
      this.browserManager.abortEvaluation(params.threadId, evaluationId)
      this.disposeReplRuntime(params.threadId, runtime)
      const collected = browserRuntime.collect()
      return {
        error: error instanceof Error ? error.message : String(error),
        images: collected.images,
        logs: [...runtime.logs, ...collected.logs]
      }
    } finally {
      if (runtime.activeEvaluationId === evaluationId) {
        runtime.activeEvaluationId = undefined
        runtime.activeAbortController = undefined
      }
    }
  }

  cancel(threadId: string, evaluationId: string): { ok: boolean } {
    const runtime = this.runtimes.get(threadId)
    if (!runtime || runtime.activeEvaluationId !== evaluationId) return { ok: false }
    runtime.activeAbortController?.abort(new Error('NodeReplJs cancelled.'))
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

  private getOrCreateRuntime(threadId: string): NodeReplThreadRuntime {
    const existing = this.runtimes.get(threadId)
    if (existing) return existing

    const runtime = createReplRuntime()
    this.runtimes.set(threadId, runtime)
    return runtime
  }

  private refreshContext(
    runtime: NodeReplThreadRuntime,
    browserRuntime: {
      agent: Record<string, unknown>
      display: (imageLike: unknown) => Promise<void>
    }
  ): void {
    const context = runtime.replServer.context as Record<string, unknown>
    const consoleApi = {
      log: (...args: unknown[]) => runtime.logs.push(args.map(describeResult).join(' ')),
      warn: (...args: unknown[]) => runtime.logs.push(args.map(describeResult).join(' ')),
      error: (...args: unknown[]) => runtime.logs.push(args.map(describeResult).join(' '))
    }
    context.agent = browserRuntime.agent
    context.display = browserRuntime.display
    context.console = consoleApi
    context.dotcraft = { browserUseClientPath: resolveBrowserClientPath() }
    context.__dotcraftSetupAtlasRuntime = async (
      options?: { globals?: Record<string, unknown>; backend?: string }
    ) => {
      const globals = options?.globals ?? context
      globals.agent = browserRuntime.agent
      globals.display = browserRuntime.display
      globals.dotcraft = context.dotcraft
      return { backend: options?.backend ?? 'iab' }
    }
  }

  private disposeReplRuntime(threadId: string, runtime: NodeReplThreadRuntime): void {
    if (this.runtimes.get(threadId) !== runtime) return
    runtime.activeEvaluationId = undefined
    runtime.activeAbortController = undefined
    try {
      runtime.replServer.close()
    } catch {
      // Best effort cleanup. A fresh REPL will be created for the next call.
    }
    this.runtimes.delete(threadId)
  }

  private withTimeout<T>(promise: Promise<T>, timeoutMs: number, signal: AbortSignal): Promise<T> {
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
        finish(() => reject(reason instanceof Error ? reason : new Error('NodeReplJs cancelled.')))
      }
      const timeout = setTimeout(
        () => finish(() => reject(new Error(`NodeReplJs timed out after ${timeoutMs}ms.`))),
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
