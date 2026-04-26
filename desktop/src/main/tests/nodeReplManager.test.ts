import { afterEach, describe, expect, it, vi } from 'vitest'
import { NodeReplManager } from '../nodeReplManager'

vi.mock('electron', () => ({
  app: { getAppPath: () => process.cwd() },
  BrowserWindow: vi.fn()
}))

function createFakeBrowserManager() {
  const images: Array<{ mediaType: string; dataBase64: string }> = []
  const logs: string[] = []
  const pendingActions: Array<() => void> = []
  return {
    prepareNodeRepl: vi.fn(() => ({
      agent: {
        hang: vi.fn(() => new Promise((resolve) => {
          pendingActions.push(() => resolve('late'))
        })),
        browser: {
          nameSession: vi.fn(async (name: string) => ({ ok: true, name }))
        }
      },
      display: vi.fn(async (imageLike: { mediaType?: string; dataBase64?: string }) => {
        images.push({
          mediaType: imageLike.mediaType ?? 'image/png',
          dataBase64: imageLike.dataBase64 ?? ''
        })
      }),
      collect: () => ({ images: [...images], logs: [...logs] })
    })),
    abortEvaluation: vi.fn(() => {
      logs.push('Browser evaluation aborted.\nRecent browser operations:\ncua.click status=active tab=tab-1 url=http://127.0.0.1:5173/ elapsedMs=1000 timeoutMs=10000')
      return { ok: true }
    }),
    reset: vi.fn(() => ({ ok: true })),
    releasePending: () => {
      while (pendingActions.length) pendingActions.shift()?.()
    }
  }
}

describe('NodeReplManager', () => {
  const managers: NodeReplManager[] = []
  const createManager = (browserManager: ReturnType<typeof createFakeBrowserManager>) => {
    const manager = new NodeReplManager(browserManager as never)
    managers.push(manager)
    return manager
  }

  afterEach(async () => {
    await Promise.all(managers.map((manager) => manager.disposeAllForTests()))
    managers.length = 0
  })

  it('persists explicit globalThis variables across evaluations', async () => {
    const browserManager = createFakeBrowserManager()
    const manager = createManager(browserManager)
    const owner = {} as Electron.BrowserWindow

    await manager.evaluate(owner, { threadId: 'thread-1', code: 'globalThis.count = 1' })
    const result = await manager.evaluate(owner, { threadId: 'thread-1', code: 'globalThis.count += 1' })

    expect(result.error).toBeUndefined()
    expect(result.resultText).toBe('2')
    manager.reset('thread-1')
  })

  it('keeps cell-local const declarations from poisoning later evaluations', async () => {
    const browserManager = createFakeBrowserManager()
    const manager = createManager(browserManager)
    const owner = {} as Electron.BrowserWindow

    const first = await manager.evaluate(owner, { threadId: 'thread-1', code: 'const snapshot = 1; return snapshot' })
    const second = await manager.evaluate(owner, { threadId: 'thread-1', code: 'const snapshot = 2; return snapshot' })

    expect(first.error).toBeUndefined()
    expect(first.resultText).toBe('1')
    expect(second.error).toBeUndefined()
    expect(second.resultText).toBe('2')
    manager.reset('thread-1')
  })

  it('returns console logs and displayed images', async () => {
    const browserManager = createFakeBrowserManager()
    const manager = createManager(browserManager)
    const owner = {} as Electron.BrowserWindow

    const result = await manager.evaluate(owner, {
      threadId: 'thread-1',
      code: `
        console.log("hello", 42)
        await display({ mediaType: "image/png", dataBase64: "AQID" })
        return "done"
      `
    })

    expect(result.resultText).toBe('done')
    expect(result.logs).toEqual(['hello 42'])
    expect(result.images).toEqual([{ mediaType: 'image/png', dataBase64: 'AQID' }])
    manager.reset('thread-1')
  })

  it('loads browser-client.mjs and initializes IAB globals', async () => {
    const browserManager = createFakeBrowserManager()
    const manager = createManager(browserManager)
    const owner = {} as Electron.BrowserWindow

    const result = await manager.evaluate(owner, {
      threadId: 'thread-1',
      code: `
        const { setupAtlasRuntime } = await import(dotcraft.browserUseClientPath)
        const initialized = await setupAtlasRuntime({ globals: globalThis, backend: "iab" })
        await agent.browser.nameSession("docs")
        return initialized
      `
    })

    expect(result.error).toBeUndefined()
    expect(result.resultText).toContain('"backend": "iab"')
    manager.reset('thread-1')
  })

  it('resets the REPL and browser runtime for a thread', async () => {
    const browserManager = createFakeBrowserManager()
    const manager = createManager(browserManager)
    const owner = {} as Electron.BrowserWindow

    await manager.evaluate(owner, { threadId: 'thread-1', code: 'globalThis.count = 1' })
    const reset = manager.reset('thread-1')
    const result = await manager.evaluate(owner, { threadId: 'thread-1', code: 'typeof globalThis.count' })

    expect(reset.ok).toBe(true)
    expect(browserManager.reset).toHaveBeenCalledWith('thread-1')
    expect(result.resultText).toBe('undefined')
    manager.reset('thread-1')
  })

  it('returns JavaScript runtime errors instead of waiting for tool timeout', async () => {
    const browserManager = createFakeBrowserManager()
    const manager = createManager(browserManager)
    const owner = {} as Electron.BrowserWindow

    const thrown = await manager.evaluate(owner, {
      threadId: 'thread-1',
      code: 'throw new Error("boom")',
      timeoutMs: 5_000
    })
    const rejected = await manager.evaluate(owner, {
      threadId: 'thread-1',
      code: 'await Promise.reject(new Error("nope"))',
      timeoutMs: 5_000
    })
    const typeError = await manager.evaluate(owner, {
      threadId: 'thread-1',
      code: 'await globalThis.missing.url()',
      timeoutMs: 5_000
    })

    expect(thrown.error).toContain('Error: boom')
    expect(rejected.error).toContain('Error: nope')
    expect(typeError.error).toContain('TypeError')
    manager.reset('thread-1')
  })

  it('passes evaluation id and abort signal into the browser runtime', async () => {
    const browserManager = createFakeBrowserManager()
    const manager = createManager(browserManager)
    const owner = {} as Electron.BrowserWindow

    const result = await manager.evaluate(owner, {
      threadId: 'thread-1',
      evaluationId: 'eval-1',
      code: '1 + 1'
    })

    expect(result.error).toBeUndefined()
    expect(browserManager.prepareNodeRepl).toHaveBeenCalledWith(owner, expect.objectContaining({
      threadId: 'thread-1',
      evaluationId: 'eval-1',
      signal: expect.any(AbortSignal)
    }))
    manager.reset('thread-1')
  })

  it('resets the REPL runtime after timeout so the next evaluation is fresh', async () => {
    const browserManager = createFakeBrowserManager()
    const manager = createManager(browserManager)
    const owner = {} as Electron.BrowserWindow

    const pending = manager.evaluate(owner, {
      threadId: 'thread-1',
      code: 'await agent.hang()',
      timeoutMs: 1
    })
    const timedOut = await pending
    browserManager.releasePending()

    expect(timedOut.error).toContain('timed out')
    expect(timedOut.logs.join('\n')).toContain('Recent browser operations')
    expect(browserManager.abortEvaluation).toHaveBeenCalledWith('thread-1', expect.stringMatching(/^node-repl-/))
    const result = await manager.evaluate(owner, { threadId: 'thread-1', code: 'typeof globalThis.count' })
    expect(result.error).toBeUndefined()
    expect(result.resultText).toBe('undefined')
    manager.reset('thread-1')
  })

  it('cancels an active evaluation and allows a later evaluation to run', async () => {
    const browserManager = createFakeBrowserManager()
    const manager = createManager(browserManager)
    const owner = {} as Electron.BrowserWindow

    const pending = manager.evaluate(owner, {
      threadId: 'thread-1',
      evaluationId: 'eval-1',
      code: 'await agent.hang()',
      timeoutMs: 120_000
    })
    const cancel = manager.cancel('thread-1', 'eval-1')
    const cancelled = await pending
    browserManager.releasePending()

    expect(cancel).toEqual({ ok: true })
    expect(cancelled.error).toContain('cancelled')
    expect(browserManager.abortEvaluation).toHaveBeenCalledWith('thread-1', 'eval-1')
    const result = await manager.evaluate(owner, { threadId: 'thread-1', code: '1 + 1' })
    expect(result.error).toBeUndefined()
    expect(result.resultText).toBe('2')
    manager.reset('thread-1')
  })

})
