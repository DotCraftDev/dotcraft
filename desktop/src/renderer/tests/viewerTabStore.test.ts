import { beforeEach, describe, expect, it } from 'vitest'
import { useViewerTabStore } from '../stores/viewerTabStore'

const store = () => useViewerTabStore.getState()

const THREAD_A = 'thread-a'
const THREAD_B = 'thread-b'
const WS_PATH = '/home/user/project'

function openFile(
  threadId: string,
  relativePath: string,
  absolutePath = `${WS_PATH}/${relativePath}`
): string {
  return store().openFile({
    threadId,
    absolutePath,
    relativePath,
    contentClass: 'text'
  })
}

beforeEach(() => {
  useViewerTabStore.setState({
    byThread: new Map(),
    currentThreadId: null,
    currentWorkspacePath: null
  })
})

// ---------------------------------------------------------------------------
// openFile — basic behaviour
// ---------------------------------------------------------------------------

describe('openFile', () => {
  it('adds a new tab and sets it as active', () => {
    store().onThreadSwitched(THREAD_A)
    const id = openFile(THREAD_A, 'src/index.ts')

    const state = store().getThreadState(THREAD_A)
    expect(state.tabs).toHaveLength(1)
    expect(state.activeTabId).toBe(id)
    expect(state.tabs[0]!.relativePath).toBe('src/index.ts')
  })

  it('returns existing tab id on duplicate absolutePath (deduplication)', () => {
    store().onThreadSwitched(THREAD_A)
    const id1 = openFile(THREAD_A, 'src/foo.ts')
    const id2 = openFile(THREAD_A, 'src/foo.ts') // same file

    expect(id1).toBe(id2)
    expect(store().getThreadState(THREAD_A).tabs).toHaveLength(1)
  })

  it('creates separate tabs for different files', () => {
    store().onThreadSwitched(THREAD_A)
    openFile(THREAD_A, 'src/a.ts')
    openFile(THREAD_A, 'src/b.ts')

    expect(store().getThreadState(THREAD_A).tabs).toHaveLength(2)
  })
})

describe('openBrowser / updateBrowserTab', () => {
  it('opens a browser tab with default state and makes it active', () => {
    store().onThreadSwitched(THREAD_A)
    const id = store().openBrowser({
      threadId: THREAD_A,
      initialUrl: 'https://example.com'
    })

    const threadState = store().getThreadState(THREAD_A)
    const tab = threadState.tabs.find((item) => item.id === id)
    expect(tab).toBeTruthy()
    expect(tab?.kind).toBe('browser')
    if (tab?.kind === 'browser') {
      expect(tab.currentUrl).toBe('https://example.com')
      expect(tab.label).toBe('example.com')
      expect(tab.canGoBack).toBe(false)
      expect(tab.canGoForward).toBe(false)
    }
    expect(threadState.activeTabId).toBe(id)
  })

  it('updates browser tabs via patch and keeps browser kind stable', () => {
    store().onThreadSwitched(THREAD_A)
    const id = store().openBrowser({ threadId: THREAD_A, initialUrl: 'https://example.com' })

    store().updateBrowserTab(THREAD_A, id, {
      title: 'Example Domain',
      canGoBack: true,
      faviconDataUrl: 'data:image/png;base64,test'
    })

    const tab = store().getThreadState(THREAD_A).tabs.find((item) => item.id === id)
    expect(tab?.kind).toBe('browser')
    if (tab?.kind === 'browser') {
      expect(tab.title).toBe('Example Domain')
      expect(tab.label).toBe('Example Domain')
      expect(tab.canGoBack).toBe(true)
      expect(tab.faviconDataUrl).toContain('data:image/png;base64')
    }
  })
})

// ---------------------------------------------------------------------------
// closeTab — nearest-neighbor fallback
// ---------------------------------------------------------------------------

describe('closeTab', () => {
  it('selects left neighbor when closing the rightmost active tab', () => {
    store().onThreadSwitched(THREAD_A)
    const id1 = openFile(THREAD_A, 'a.ts')
    const id2 = openFile(THREAD_A, 'b.ts') // active

    store().closeTab(THREAD_A, id2)

    expect(store().getThreadState(THREAD_A).activeTabId).toBe(id1)
  })

  it('selects right neighbor when closing the leftmost tab', () => {
    store().onThreadSwitched(THREAD_A)
    const id1 = openFile(THREAD_A, 'a.ts') // active first, then overridden
    const id2 = openFile(THREAD_A, 'b.ts')
    store().setActiveTab(THREAD_A, id1) // make id1 active

    store().closeTab(THREAD_A, id1)

    expect(store().getThreadState(THREAD_A).activeTabId).toBe(id2)
  })

  it('sets activeTabId to null when last tab is closed', () => {
    store().onThreadSwitched(THREAD_A)
    const id = openFile(THREAD_A, 'only.ts')

    store().closeTab(THREAD_A, id)

    expect(store().getThreadState(THREAD_A).activeTabId).toBeNull()
    expect(store().getThreadState(THREAD_A).tabs).toHaveLength(0)
  })

  it('is a no-op when tabId does not exist', () => {
    store().onThreadSwitched(THREAD_A)
    const id = openFile(THREAD_A, 'a.ts')

    store().closeTab(THREAD_A, 'nonexistent')

    expect(store().getThreadState(THREAD_A).tabs).toHaveLength(1)
    expect(store().getThreadState(THREAD_A).activeTabId).toBe(id)
  })
})

// ---------------------------------------------------------------------------
// Label collision deduplication
// ---------------------------------------------------------------------------

describe('label collision', () => {
  it('deduplicates tabs with the same basename by prepending parent dir', () => {
    store().onThreadSwitched(THREAD_A)
    openFile(THREAD_A, 'src/components/Button.tsx', `${WS_PATH}/src/components/Button.tsx`)
    openFile(THREAD_A, 'src/elements/Button.tsx', `${WS_PATH}/src/elements/Button.tsx`)

    const tabs = store().getThreadState(THREAD_A).tabs
    const labels = tabs.map((t) => t.label)

    // Labels must be unique
    expect(new Set(labels).size).toBe(2)
    // Each label should contain extra path info, not just 'Button.tsx'
    for (const l of labels) {
      expect(l).not.toBe('Button.tsx')
    }
  })

  it('uses just the basename when there is no collision', () => {
    store().onThreadSwitched(THREAD_A)
    openFile(THREAD_A, 'src/index.ts', `${WS_PATH}/src/index.ts`)
    openFile(THREAD_A, 'lib/utils.ts', `${WS_PATH}/lib/utils.ts`)

    const tabs = store().getThreadState(THREAD_A).tabs
    expect(tabs[0]!.label).toBe('index.ts')
    expect(tabs[1]!.label).toBe('utils.ts')
  })
})

// ---------------------------------------------------------------------------
// Thread isolation
// ---------------------------------------------------------------------------

describe('thread isolation', () => {
  it('tabs in different threads do not interfere', () => {
    openFile(THREAD_A, 'src/a.ts')
    openFile(THREAD_B, 'src/b.ts')

    expect(store().getThreadState(THREAD_A).tabs).toHaveLength(1)
    expect(store().getThreadState(THREAD_B).tabs).toHaveLength(1)
    expect(store().getThreadState(THREAD_A).tabs[0]!.relativePath).toBe('src/a.ts')
    expect(store().getThreadState(THREAD_B).tabs[0]!.relativePath).toBe('src/b.ts')
  })
})

// ---------------------------------------------------------------------------
// onThreadSwitched
// ---------------------------------------------------------------------------

describe('onThreadSwitched', () => {
  it('updates currentThreadId without clearing tab state', () => {
    openFile(THREAD_A, 'a.ts')
    store().onThreadSwitched(THREAD_B)

    expect(store().currentThreadId).toBe(THREAD_B)
    // Thread A state preserved
    expect(store().getThreadState(THREAD_A).tabs).toHaveLength(1)
  })
})

// ---------------------------------------------------------------------------
// onThreadDeleted
// ---------------------------------------------------------------------------

describe('onThreadDeleted', () => {
  it('removes all tabs for the deleted thread', () => {
    openFile(THREAD_A, 'a.ts')
    openFile(THREAD_B, 'b.ts')

    store().onThreadDeleted(THREAD_A)

    expect(store().getThreadState(THREAD_A).tabs).toHaveLength(0)
    expect(store().getThreadState(THREAD_B).tabs).toHaveLength(1)
  })

  it('enumerates browser tabs for cleanup callback', () => {
    openFile(THREAD_A, 'a.ts')
    store().openBrowser({ threadId: THREAD_A, initialUrl: 'https://example.com' })
    store().openBrowser({ threadId: THREAD_A, initialUrl: 'https://dotcraft.ai' })
    const removed: string[] = []

    store().onThreadDeleted(THREAD_A, {
      onBrowserTabRemoved: (tab) => {
        removed.push(tab.id)
      }
    })

    expect(removed).toHaveLength(2)
  })
})

// ---------------------------------------------------------------------------
// onWorkspaceSwitched
// ---------------------------------------------------------------------------

describe('onWorkspaceSwitched', () => {
  it('clears all tabs across all threads', () => {
    openFile(THREAD_A, 'a.ts')
    openFile(THREAD_B, 'b.ts')

    store().onWorkspaceSwitched('/new/workspace')

    expect(store().getThreadState(THREAD_A).tabs).toHaveLength(0)
    expect(store().getThreadState(THREAD_B).tabs).toHaveLength(0)
    expect(store().currentWorkspacePath).toBe('/new/workspace')
  })
})

// ---------------------------------------------------------------------------
// getCurrentTabs / getCurrentActiveTabId
// ---------------------------------------------------------------------------

describe('getCurrentTabs / getCurrentActiveTabId', () => {
  it('returns empty array when no thread is active', () => {
    expect(store().getCurrentTabs()).toHaveLength(0)
    expect(store().getCurrentActiveTabId()).toBeNull()
  })

  it('returns stable references for missing thread and no active thread', () => {
    const threadStateA = store().getThreadState('unknown-thread')
    const threadStateB = store().getThreadState('unknown-thread')
    expect(threadStateA).toBe(threadStateB)
    expect(threadStateA.tabs).toBe(threadStateB.tabs)

    const currentTabsA = store().getCurrentTabs()
    const currentTabsB = store().getCurrentTabs()
    expect(currentTabsA).toBe(currentTabsB)
  })

  it('returns the current thread tabs', () => {
    store().onThreadSwitched(THREAD_A)
    openFile(THREAD_A, 'src/a.ts')
    openFile(THREAD_A, 'src/b.ts')

    expect(store().getCurrentTabs()).toHaveLength(2)
  })

  it('returns the active tab id for the current thread', () => {
    store().onThreadSwitched(THREAD_A)
    const id = openFile(THREAD_A, 'src/a.ts')

    expect(store().getCurrentActiveTabId()).toBe(id)
  })
})
