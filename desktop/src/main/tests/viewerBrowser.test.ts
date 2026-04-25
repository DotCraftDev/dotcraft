import { describe, expect, it, vi } from 'vitest'
import {
  classifyBrowserUrl,
  loadOrReport,
  normalizeBrowserUrl,
  partitionForWorkspace,
  ViewerBrowserManager
} from '../viewerBrowser'

describe('normalizeBrowserUrl', () => {
  it('normalizes absolute http/https urls', () => {
    expect(normalizeBrowserUrl('https://example.com/docs')).toBe('https://example.com/docs')
    expect(normalizeBrowserUrl('http://example.com')).toBe('http://example.com/')
  })

  it('promotes host-like input to https', () => {
    expect(normalizeBrowserUrl('example.com')).toBe('https://example.com/')
    expect(normalizeBrowserUrl('docs.example.com/path')).toBe('https://docs.example.com/path')
  })

  it('returns null for empty or control-character input', () => {
    expect(normalizeBrowserUrl('')).toBeNull()
    expect(normalizeBrowserUrl('   ')).toBeNull()
    expect(normalizeBrowserUrl('\u0000https://example.com')).toBeNull()
  })
})

describe('classifyBrowserUrl', () => {
  it('allows http/https and blocks unsupported schemes', () => {
    expect(classifyBrowserUrl('https://example.com')).toBe('allow')
    expect(classifyBrowserUrl('http://example.com')).toBe('allow')
    expect(classifyBrowserUrl('dotcraft-viewer://workspace/F%3A/workspace/index.html')).toBe('allow')
    expect(classifyBrowserUrl('file:///tmp/a.txt')).toBe('blocked')
    expect(classifyBrowserUrl('chrome://settings')).toBe('blocked')
    expect(classifyBrowserUrl('javascript:alert(1)')).toBe('blocked')
  })

  it('marks mailto/tel as external handoff', () => {
    expect(classifyBrowserUrl('mailto:test@example.com')).toBe('external-handoff')
    expect(classifyBrowserUrl('tel:10086')).toBe('external-handoff')
  })
})

describe('partitionForWorkspace', () => {
  it('creates deterministic partition ids', () => {
    const p1 = partitionForWorkspace('F:/dotcraft')
    const p2 = partitionForWorkspace('F:/dotcraft')
    expect(p1).toBe(p2)
    expect(p1.startsWith('persist:dotcraft-viewer:')).toBe(true)
  })

  it('is path-casing-insensitive on Windows style paths', () => {
    const upper = partitionForWorkspace('F:/DOTCRAFT/Workspace')
    const lower = partitionForWorkspace('f:/dotcraft/workspace')
    expect(upper).toBe(lower)
  })
})

describe('ViewerBrowserManager partition configuration', () => {
  it('installs the viewer protocol handler on browser partition sessions once', () => {
    const handle = vi.fn()
    const fakeSession = {
      protocol: { handle },
      on: vi.fn(),
      setPermissionCheckHandler: vi.fn(),
      setPermissionRequestHandler: vi.fn()
    } as unknown as Electron.Session
    const manager = new ViewerBrowserManager()

    manager.configurePartitionSession('persist:dotcraft-viewer:test', fakeSession)
    manager.configurePartitionSession('persist:dotcraft-viewer:test', fakeSession)

    expect(handle).toHaveBeenCalledTimes(1)
    expect(handle).toHaveBeenCalledWith('dotcraft-viewer', expect.any(Function))
  })
})

describe('loadOrReport', () => {
  it('emits did-fail-load and did-stop-loading when load rejects', async () => {
    const events: Array<{ type: string; message?: string; url?: string }> = []
    await expect(loadOrReport({
      tabId: 'tab-1',
      url: 'https://example.com/',
      load: () => Promise.reject(new Error('load failed')),
      emit: (payload) => {
        events.push({
          type: payload.type,
          message: 'message' in payload ? payload.message : undefined,
          url: 'url' in payload ? payload.url : undefined
        })
      }
    })).resolves.toBeUndefined()

    expect(events).toHaveLength(2)
    expect(events[0]).toEqual({
      type: 'did-fail-load',
      message: 'load failed',
      url: 'https://example.com/'
    })
    expect(events[1]).toEqual({
      type: 'did-stop-loading',
      message: undefined,
      url: 'https://example.com/'
    })
  })
})

describe('ViewerBrowserManager automation input', () => {
  function createAutomationHarness() {
    const events: unknown[] = []
    const webContents = {
      isDestroyed: vi.fn(() => false),
      sendInputEvent: vi.fn((event: unknown) => events.push(event)),
      insertText: vi.fn(),
      executeJavaScript: vi.fn(async () => undefined)
    }
    const win = {
      id: 1,
      isDestroyed: () => false,
      webContents: {
        isDestroyed: () => false,
        send: vi.fn()
      },
      contentView: {
        addChildView: vi.fn(),
        removeChildView: vi.fn()
      }
    } as unknown as Electron.BrowserWindow & { webContents: { send: ReturnType<typeof vi.fn> } }
    const manager = new ViewerBrowserManager()
    ;(manager as unknown as {
      byWindowId: Map<number, {
        tabs: Map<string, unknown>
        activeTabId: string | null
      }>
    }).byWindowId.set(1, {
      activeTabId: null,
      tabs: new Map([['tab-1', {
        tabId: 'tab-1',
        workspacePath: 'F:/workspace',
        view: { webContents },
        desiredVisible: true,
        visible: true,
        boundsInitialized: true,
        currentUrl: 'http://localhost:3000/',
        title: 'Test',
        automationEnabled: true
      }]])
    })
    return { manager, win, webContents, events }
  }

  it('clickMouse sends move, down, and up input events', async () => {
    const { manager, win, webContents, events } = createAutomationHarness()

    await manager.clickMouse(win, { tabId: 'tab-1', x: 10, y: 20 })

    expect(webContents.executeJavaScript).toHaveBeenCalled()
    expect(events).toMatchObject([
      { type: 'mouseMove', x: 10, y: 20 },
      { type: 'mouseDown', x: 10, y: 20, button: 'left' },
      { type: 'mouseUp', x: 10, y: 20, button: 'left' }
    ])
  })

  it('scrollMouse sends wheel input through the tab webContents', async () => {
    const { manager, win, events } = createAutomationHarness()

    await manager.scrollMouse(win, { tabId: 'tab-1', x: 5, y: 6, scrollX: 0, scrollY: 120 })

    expect(events.at(-1)).toMatchObject({
      type: 'mouseWheel',
      x: 5,
      y: 6,
      deltaY: 120
    })
  })

  it('keypress sends keyDown and keyUp with modifiers', () => {
    const { manager, win, events } = createAutomationHarness()

    manager.keypress(win, { tabId: 'tab-1', keys: ['Control', 'A'] })

    expect(events).toMatchObject([
      { type: 'keyDown', keyCode: 'A', modifiers: ['control'] },
      { type: 'keyUp', keyCode: 'A', modifiers: ['control'] }
    ])
  })

  it('returns the active browser tab for the requested thread as automation target', () => {
    const { manager, win } = createAutomationHarness()
    const webContents = {
      isDestroyed: vi.fn(() => false),
      getURL: vi.fn(() => 'http://localhost:5173/'),
      getTitle: vi.fn(() => 'Local app'),
      isLoading: vi.fn(() => false),
      navigationHistory: {
        canGoBack: vi.fn(() => false),
        canGoForward: vi.fn(() => false)
      }
    }
    ;(manager as unknown as {
      byWindowId: Map<number, {
        tabs: Map<string, unknown>
        activeTabId: string | null
      }>
    }).byWindowId.set(1, {
      activeTabId: 'tab-current',
      tabs: new Map([
        ['tab-other', {
          tabId: 'tab-other',
          threadId: 'thread-other',
          workspacePath: 'F:/workspace',
          view: { webContents },
          desiredVisible: true,
          visible: true,
          boundsInitialized: true,
          currentUrl: 'http://localhost:4000/',
          title: 'Other'
        }],
        ['tab-current', {
          tabId: 'tab-current',
          threadId: 'thread-a',
          workspacePath: 'F:/workspace',
          view: { webContents },
          desiredVisible: true,
          visible: true,
          boundsInitialized: true,
          currentUrl: 'http://localhost:5173/',
          title: 'Local app'
        }]
      ])
    })

    expect(manager.getAutomationTargetTab(win, 'thread-a')).toMatchObject({
      tabId: 'tab-current',
      threadId: 'thread-a',
      currentUrl: 'http://localhost:5173/',
      title: 'Local app'
    })
    expect(manager.getAutomationTargetTab(win, 'thread-missing')).toBeNull()
  })
})
