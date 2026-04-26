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
import { resolveBrowserUseNavigationDecision } from '../browserUsePolicy'

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
    getAutomationTargetTab: vi.fn((): { tabId: string; currentUrl: string; title: string; loading: boolean } | null => null),
    loadAutomationUrl: vi.fn(async (_win: Electron.BrowserWindow, params: { tabId: string; url: string }) => {
      webContents.setUrl(params.url)
    }),
    destroyTab: vi.fn(),
    snapshotState: vi.fn((_win: Electron.BrowserWindow, tabId: string) => ({
      tabId,
      currentUrl: webContents.getURL(),
      title: webContents.getTitle(),
      loading: webContents.isLoading()
    })),
    setAutomationState: vi.fn(),
    moveMouse: vi.fn(),
    clickMouse: vi.fn(),
    doubleClickMouse: vi.fn(),
    dragMouse: vi.fn(),
    scrollMouse: vi.fn(),
    typeText: vi.fn(),
    keypress: vi.fn()
  }
}

function createFakeOwner() {
  const emitter = new EventEmitter()
  return {
    on: emitter.on.bind(emitter),
    once: emitter.once.bind(emitter),
    off: emitter.off.bind(emitter),
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

describe('browser-use navigation policy', () => {
  it('allows configured external domains and their subdomains', () => {
    expect(resolveBrowserUseNavigationDecision('https://example.com/', {
      approvalMode: 'alwaysAsk',
      allowedDomains: ['example.com']
    })).toEqual({ kind: 'allow', local: false, domain: 'example.com' })
    expect(resolveBrowserUseNavigationDecision('https://docs.example.com/', {
      approvalMode: 'alwaysAsk',
      allowedDomains: ['example.com']
    })).toEqual({ kind: 'allow', local: false, domain: 'docs.example.com' })
  })

  it('lets blocked domains override allowed domains', () => {
    expect(resolveBrowserUseNavigationDecision('https://docs.example.com/', {
      approvalMode: 'neverAsk',
      allowedDomains: ['example.com'],
      blockedDomains: ['docs.example.com']
    })).toMatchObject({ kind: 'block', domain: 'docs.example.com' })
  })

  it('requires approval for unknown external domains by default', () => {
    expect(resolveBrowserUseNavigationDecision('https://example.com/')).toEqual({
      kind: 'needs-approval',
      domain: 'example.com'
    })
  })

  it('allows unknown external domains when approval is disabled', () => {
    expect(resolveBrowserUseNavigationDecision('https://example.com/', {
      approvalMode: 'neverAsk'
    })).toEqual({ kind: 'allow', local: false, domain: 'example.com' })
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

  it('opens 127.0.0.1 dev server URLs through the viewer host', async () => {
    const host = createFakeHost()
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()

    const result = await manager.evaluate(owner, {
      threadId: 'thread-1',
      workspacePath: 'F:/workspace',
      code: `
        const tab = await agent.browser.tabs.new("127.0.0.1:5173");
        return await tab.url();
      `
    })

    expect(result.error).toBeUndefined()
    expect(result.resultText).toBe('http://127.0.0.1:5173/')
    expect(host.loadAutomationUrl).toHaveBeenCalledWith(owner, {
      tabId: expect.stringMatching(/^browser-use-thread-1-/),
      url: 'http://127.0.0.1:5173/'
    })
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

  it('adopts the current thread browser tab for default BrowserJs navigation', async () => {
    const host = createFakeHost()
    host.getAutomationTargetTab.mockReturnValue({
      tabId: 'user-browser-tab',
      currentUrl: 'about:blank',
      title: 'User tab',
      loading: false
    })
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()

    const result = await manager.evaluate(owner, {
      threadId: 'thread-1',
      workspacePath: 'F:/workspace',
      code: 'const tab = await agent.browser.goto("localhost:5173"); return await tab.url();'
    })

    expect(result.error).toBeUndefined()
    expect(host.createAutomationTab).not.toHaveBeenCalled()
    expect(host.loadAutomationUrl).toHaveBeenCalledWith(owner, {
      tabId: 'user-browser-tab',
      url: 'http://localhost:5173/'
    })
    expect(host.setAutomationState).toHaveBeenCalledWith(owner, expect.objectContaining({
      tabId: 'user-browser-tab',
      active: true,
      action: 'navigate'
    }))
  })

  it('reuses an adopted selected tab across BrowserJs calls', async () => {
    const host = createFakeHost()
    host.getAutomationTargetTab.mockReturnValue({
      tabId: 'user-browser-tab',
      currentUrl: 'about:blank',
      title: 'User tab',
      loading: false
    })
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()

    await manager.evaluate(owner, {
      threadId: 'thread-1',
      workspacePath: 'F:/workspace',
      code: 'await agent.browser.tabs.selected();'
    })
    await manager.evaluate(owner, {
      threadId: 'thread-1',
      workspacePath: 'F:/workspace',
      code: 'const tab = await agent.browser.tabs.selected(); await tab.goto("localhost:5174");'
    })

    expect(host.createAutomationTab).not.toHaveBeenCalled()
    expect(host.loadAutomationUrl).toHaveBeenCalledWith(owner, {
      tabId: 'user-browser-tab',
      url: 'http://localhost:5174/'
    })
  })

  it('keeps an existing selected runtime tab over a later automation target', async () => {
    const host = createFakeHost()
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()

    await manager.evaluate(owner, {
      threadId: 'thread-1',
      workspacePath: 'F:/workspace',
      code: 'await agent.browser.tabs.new("localhost:3000");'
    })
    host.loadAutomationUrl.mockClear()
    host.getAutomationTargetTab.mockReturnValue({
      tabId: 'user-browser-tab',
      currentUrl: 'about:blank',
      title: 'User tab',
      loading: false
    })

    const result = await manager.evaluate(owner, {
      threadId: 'thread-1',
      workspacePath: 'F:/workspace',
      code: 'const tab = await agent.browser.goto("localhost:5174"); return tab.id;'
    })

    expect(result.error).toBeUndefined()
    expect(result.resultText).toMatch(/^browser-use-thread-1-/)
    expect(host.getAutomationTargetTab).not.toHaveBeenCalled()
    expect(host.loadAutomationUrl).toHaveBeenCalledWith(owner, {
      tabId: expect.stringMatching(/^browser-use-thread-1-/),
      url: 'http://localhost:5174/'
    })
    expect(host.loadAutomationUrl).not.toHaveBeenCalledWith(owner, {
      tabId: 'user-browser-tab',
      url: 'http://localhost:5174/'
    })
  })

  it('reset leaves adopted user browser tabs open but clears automation state', async () => {
    const host = createFakeHost()
    host.getAutomationTargetTab.mockReturnValue({
      tabId: 'user-browser-tab',
      currentUrl: 'about:blank',
      title: 'User tab',
      loading: false
    })
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()

    await manager.evaluate(owner, {
      threadId: 'thread-1',
      workspacePath: 'F:/workspace',
      code: 'await agent.browser.goto("localhost:5173");'
    })
    expect(manager.reset('thread-1')).toEqual({ ok: true })

    expect(host.destroyTab).not.toHaveBeenCalled()
    expect(host.setAutomationState).toHaveBeenCalledWith(owner, expect.objectContaining({
      tabId: 'user-browser-tab',
      active: false
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

  it('opens external URLs when approval is disabled', async () => {
    const host = createFakeHost()
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()
    manager.setPolicyHost({
      getSettings: () => ({ browserUse: { approvalMode: 'neverAsk' } }),
      updateSettings: vi.fn()
    })

    const result = await manager.evaluate(owner, {
      threadId: 'thread-1',
      workspacePath: 'F:/workspace',
      code: 'const tab = await agent.browser.tabs.new("https://example.com"); return await tab.url();'
    })

    expect(result.error).toBeUndefined()
    expect(result.resultText).toBe('https://example.com/')
    expect(host.loadAutomationUrl).toHaveBeenCalledWith(owner, expect.objectContaining({
      url: 'https://example.com/'
    }))
  })

  it('blocks configured external domains before loading', async () => {
    const host = createFakeHost()
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()
    manager.setPolicyHost({
      getSettings: () => ({ browserUse: { blockedDomains: ['example.com'] } }),
      updateSettings: vi.fn()
    })

    const result = await manager.evaluate(owner, {
      threadId: 'thread-1',
      workspacePath: 'F:/workspace',
      code: 'await agent.browser.tabs.new("https://example.com");'
    })

    expect(result.error).toContain('Blocked browser-use domain: example.com')
    expect(host.loadAutomationUrl).not.toHaveBeenCalled()
  })

  it('persists allow-domain approval and continues navigation', async () => {
    const host = createFakeHost()
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()
    const settings = { browserUse: { approvalMode: 'alwaysAsk' as const, allowedDomains: [] as string[] } }
    manager.setPolicyHost({
      getSettings: () => settings,
      updateSettings: vi.fn(async (partial) => {
        Object.assign(settings, partial)
      })
    })

    const pending = manager.evaluate(owner, {
      threadId: 'thread-1',
      workspacePath: 'F:/workspace',
      code: 'const tab = await agent.browser.tabs.new("https://example.com"); return await tab.url();'
    })

    await vi.waitFor(() => {
      expect(owner.webContents.send).toHaveBeenCalledWith('viewer:browser-use:approval-request', expect.objectContaining({
        domain: 'example.com'
      }))
    })
    const payload = (owner.webContents.send as ReturnType<typeof vi.fn>).mock.calls[0][1] as { requestId: string }
    expect(manager.handleApprovalResponse({ requestId: payload.requestId, action: 'allowDomain' })).toBe(true)

    const result = await pending
    expect(result.error).toBeUndefined()
    expect(settings.browserUse.allowedDomains).toEqual(['example.com'])
    expect(host.loadAutomationUrl).toHaveBeenCalledWith(owner, expect.objectContaining({
      url: 'https://example.com/'
    }))
  })

  it('uses allow-once approval for initial URL without prompting twice', async () => {
    const host = createFakeHost()
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()
    const updateSettings = vi.fn()
    manager.setPolicyHost({
      getSettings: () => ({ browserUse: { approvalMode: 'alwaysAsk', allowedDomains: [] } }),
      updateSettings
    })

    const pending = manager.evaluate(owner, {
      threadId: 'thread-1',
      workspacePath: 'F:/workspace',
      code: 'const tab = await agent.browser.tabs.new("https://example.com"); return await tab.url();'
    })

    await vi.waitFor(() => {
      expect(owner.webContents.send).toHaveBeenCalledWith('viewer:browser-use:approval-request', expect.objectContaining({
        domain: 'example.com'
      }))
    })
    const payload = (owner.webContents.send as ReturnType<typeof vi.fn>).mock.calls[0][1] as { requestId: string }
    expect(manager.handleApprovalResponse({ requestId: payload.requestId, action: 'allowOnce' })).toBe(true)

    const result = await pending
    expect(result.error).toBeUndefined()
    expect(result.resultText).toBe('https://example.com/')
    expect(updateSettings).not.toHaveBeenCalled()
    const approvalRequests = (owner.webContents.send as ReturnType<typeof vi.fn>).mock.calls.filter(
      ([channel]) => channel === 'viewer:browser-use:approval-request'
    )
    expect(approvalRequests).toHaveLength(1)
    expect(host.loadAutomationUrl).toHaveBeenCalledWith(owner, expect.objectContaining({
      url: 'https://example.com/'
    }))
  })

  it('still requires approval for explicit navigation after initial allow-once', async () => {
    const host = createFakeHost()
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()
    manager.setPolicyHost({
      getSettings: () => ({ browserUse: { approvalMode: 'alwaysAsk', allowedDomains: [] } }),
      updateSettings: vi.fn()
    })

    const pending = manager.evaluate(owner, {
      threadId: 'thread-1',
      workspacePath: 'F:/workspace',
      code: `
        const tab = await agent.browser.tabs.new("https://example.com");
        await tab.navigate("https://another.example");
        return await tab.url();
      `
    })

    await vi.waitFor(() => {
      expect(owner.webContents.send).toHaveBeenCalledWith('viewer:browser-use:approval-request', expect.objectContaining({
        domain: 'example.com'
      }))
    })
    const firstPayload = (owner.webContents.send as ReturnType<typeof vi.fn>).mock.calls[0][1] as { requestId: string }
    expect(manager.handleApprovalResponse({ requestId: firstPayload.requestId, action: 'allowOnce' })).toBe(true)

    await vi.waitFor(() => {
      expect((owner.webContents.send as ReturnType<typeof vi.fn>).mock.calls.filter(
        ([channel]) => channel === 'viewer:browser-use:approval-request'
      )).toHaveLength(2)
    })
    const secondPayload = (owner.webContents.send as ReturnType<typeof vi.fn>).mock.calls.find(
      ([channel, payload]) => channel === 'viewer:browser-use:approval-request' && payload.domain === 'another.example'
    )?.[1] as { requestId: string } | undefined
    expect(secondPayload).toBeDefined()
    expect(manager.handleApprovalResponse({ requestId: secondPayload!.requestId, action: 'allowOnce' })).toBe(true)

    const result = await pending
    expect(result.error).toBeUndefined()
    expect(result.resultText).toBe('https://another.example/')
  })

  it('routes CUA click through the viewer host input layer', async () => {
    const host = createFakeHost()
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()

    const result = await manager.evaluate(owner, {
      threadId: 'thread-1',
      code: `
        const tab = await agent.browser.tabs.new("localhost:3000");
        await tab.cua.click({ x: 40, y: 50 });
      `
    })

    expect(result.error).toBeUndefined()
    expect(host.clickMouse).toHaveBeenCalledWith(owner, expect.objectContaining({
      x: 40,
      y: 50
    }))
    expect(host.setAutomationState).toHaveBeenCalledWith(owner, expect.objectContaining({
      active: true,
      action: 'click'
    }))
  })

  it('resolves locator clicks strictly and sends coordinate input', async () => {
    const wc = createFakeWebContents()
    ;(wc.executeJavaScript as ReturnType<typeof vi.fn>).mockImplementation(async (script: string) => {
      if (script.includes('querySelectorAll')) {
        return [{
          index: 0,
          tagName: 'button',
          visibleText: 'Save',
          ariaName: 'Save',
          boundingBox: { x: 10, y: 20, width: 100, height: 40 }
        }]
      }
      return 'ok'
    })
    const host = createFakeHost(wc)
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()

    const result = await manager.evaluate(owner, {
      threadId: 'thread-1',
      code: `
        const tab = await agent.browser.tabs.new("localhost:3000");
        await tab.playwright.getByRole("button", { name: "Save" }).click();
      `
    })

    expect(result.error).toBeUndefined()
    expect(host.clickMouse).toHaveBeenCalledWith(owner, expect.objectContaining({
      x: 60,
      y: 40
    }))
  })

  it('reports strict locator violations instead of guessing', async () => {
    const wc = createFakeWebContents()
    ;(wc.executeJavaScript as ReturnType<typeof vi.fn>).mockImplementation(async (script: string) => {
      if (script.includes('querySelectorAll')) {
        return [
          { index: 0, tagName: 'button', visibleText: 'Save', ariaName: 'Save', boundingBox: { x: 0, y: 0, width: 10, height: 10 } },
          { index: 1, tagName: 'button', visibleText: 'Save', ariaName: 'Save', boundingBox: { x: 20, y: 0, width: 10, height: 10 } }
        ]
      }
      return 'ok'
    })
    const host = createFakeHost(wc)
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()

    const result = await manager.evaluate(owner, {
      threadId: 'thread-1',
      code: `
        const tab = await agent.browser.tabs.new("localhost:3000");
        await tab.playwright.getByText("Save").click();
      `
    })

    expect(result.error).toContain('Strict mode violation')
    expect(host.clickMouse).not.toHaveBeenCalled()
  })
})
