import { describe, expect, it, vi } from 'vitest'
import { EventEmitter } from 'events'

vi.mock('electron', () => ({
  BrowserWindow: vi.fn(),
  WebContentsView: vi.fn(),
  nativeImage: { createFromBuffer: vi.fn(() => ({ isEmpty: () => true })) },
  session: { fromPartition: vi.fn() },
  shell: { openExternal: vi.fn(), openPath: vi.fn() }
}))

import { BrowserWindow } from 'electron'
import {
  BrowserUseManager,
  isBrowserUseUrlAllowed,
  normalizeBrowserUseUrl
} from '../browserUseManager'

function createFakeWebContents() {
  const emitter = new EventEmitter()
  let url = 'about:blank'
  return {
    ...emitter,
    on: emitter.on.bind(emitter),
    once: emitter.once.bind(emitter),
    off: emitter.off.bind(emitter),
    isDestroyed: vi.fn(() => false),
    getURL: vi.fn(() => url),
    getTitle: vi.fn(() => 'Test Page'),
    isLoading: vi.fn(() => false),
    loadURL: vi.fn(async (nextUrl: string) => {
      url = nextUrl
    }),
    executeJavaScript: vi.fn(async () => 'ok'),
    capturePage: vi.fn(async () => ({ toPNG: () => Buffer.from([1, 2, 3]) })),
    insertText: vi.fn(),
    sendInputEvent: vi.fn(),
    setUrl(nextUrl: string) {
      url = nextUrl
    }
  } as unknown as Electron.WebContents & { setUrl(nextUrl: string): void }
}

function createFakeHost(webContents = createFakeWebContents()) {
  return {
    createAutomationTab: vi.fn(),
    getTabWebContents: vi.fn(() => webContents),
    loadAutomationUrl: vi.fn(async (_win: Electron.BrowserWindow, params: { tabId: string; url: string }) => {
      webContents.setUrl(params.url)
    }),
    destroyTab: vi.fn(),
    snapshotState: vi.fn((_win: Electron.BrowserWindow, tabId: string) => ({
      tabId,
      currentUrl: webContents.getURL(),
      title: webContents.getTitle(),
      loading: webContents.isLoading()
    }))
  }
}

function createFakeOwner() {
  return {
    getTitle: () => 'test-window',
    isDestroyed: () => false,
    webContents: {
      isDestroyed: () => false,
      send: vi.fn()
    }
  } as unknown as Electron.BrowserWindow & { webContents: { send: ReturnType<typeof vi.fn> } }
}

describe('normalizeBrowserUseUrl', () => {
  it('defaults local host-like URLs to http', () => {
    expect(normalizeBrowserUseUrl('localhost:3000')).toBe('http://localhost:3000/')
    expect(normalizeBrowserUseUrl('127.0.0.1:5173/app')).toBe('http://127.0.0.1:5173/app')
  })

  it('normalizes absolute URLs and rejects invalid input', () => {
    expect(normalizeBrowserUseUrl('http://localhost:3000')).toBe('http://localhost:3000/')
    expect(normalizeBrowserUseUrl('\u0000http://localhost')).toBeNull()
  })
})

describe('isBrowserUseUrlAllowed', () => {
  it('allows local, file, and dotcraft-viewer URLs', () => {
    expect(isBrowserUseUrlAllowed('http://localhost:3000/')).toBe(true)
    expect(isBrowserUseUrlAllowed('https://127.0.0.1:8443/')).toBe(true)
    expect(isBrowserUseUrlAllowed('file:///tmp/index.html')).toBe(true)
    expect(isBrowserUseUrlAllowed('dotcraft-viewer://workspace/E%3A/index.html')).toBe(true)
  })

  it('blocks remote and unsupported URLs', () => {
    expect(isBrowserUseUrlAllowed('https://example.com/')).toBe(false)
    expect(isBrowserUseUrlAllowed('javascript:alert(1)')).toBe(false)
  })
})

describe('BrowserUseManager JavaScript runtime', () => {
  it('does not expose external Node globals', async () => {
    const manager = new BrowserUseManager()
    const owner = createFakeOwner()
    const result = await manager.evaluate(owner, {
      threadId: 'thread-1',
      code: 'return `${typeof process}:${typeof require}`;'
    })

    expect(result.error).toBeUndefined()
    expect(result.resultText).toBe('undefined:undefined')
  })

  it('opens BrowserJs tabs through viewer browser and emits a first-open event', async () => {
    const host = createFakeHost()
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()

    const result = await manager.evaluate(owner, {
      threadId: 'thread-1',
      workspacePath: 'F:/workspace',
      code: `
        await agent.browser.nameSession("mario-test");
        const tab = await agent.browser.tabs.new("localhost:3000");
        return await tab.url();
      `
    })

    expect(result.error).toBeUndefined()
    expect(result.resultText).toBe('http://localhost:3000/')
    expect(host.createAutomationTab).toHaveBeenCalledWith(owner, expect.objectContaining({
      tabId: expect.stringMatching(/^browser-use-thread-1-/),
      workspacePath: 'F:/workspace',
      allowFileScheme: true
    }))
    expect(owner.webContents.send).toHaveBeenCalledWith('viewer:browser-use:open', expect.objectContaining({
      threadId: 'thread-1',
      initialUrl: 'http://localhost:3000/',
      title: 'mario-test',
      focusMode: 'first-open'
    }))
    expect(BrowserWindow).not.toHaveBeenCalled()
  })

  it('does not force focus for additional tabs in the same thread', async () => {
    const host = createFakeHost()
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()

    await manager.evaluate(owner, {
      threadId: 'thread-1',
      code: 'await agent.browser.tabs.new("localhost:3000");'
    })
    await manager.evaluate(owner, {
      threadId: 'thread-1',
      code: 'await agent.browser.tabs.new("localhost:3001");'
    })

    expect(owner.webContents.send).toHaveBeenNthCalledWith(1, 'viewer:browser-use:open', expect.objectContaining({
      focusMode: 'first-open'
    }))
    expect(owner.webContents.send).toHaveBeenNthCalledWith(2, 'viewer:browser-use:open', expect.objectContaining({
      focusMode: 'none'
    }))
  })

  it('reset destroys viewer browser tabs for the thread', async () => {
    const host = createFakeHost()
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()

    await manager.evaluate(owner, {
      threadId: 'thread-1',
      code: 'await agent.browser.tabs.new("localhost:3000");'
    })

    expect(manager.reset('thread-1')).toEqual({ ok: true })
    expect(host.destroyTab).toHaveBeenCalledWith(owner, expect.stringMatching(/^browser-use-thread-1-/))
  })
})
