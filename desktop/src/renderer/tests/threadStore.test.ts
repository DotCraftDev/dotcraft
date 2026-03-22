import { describe, it, expect, beforeEach } from 'vitest'
import { useThreadStore, selectFilteredThreads } from '../stores/threadStore'
import type { ThreadSummary, Thread } from '../types/thread'

function makeThreadSummary(id: string, overrides: Partial<ThreadSummary> = {}): ThreadSummary {
  return {
    id,
    displayName: `Thread ${id}`,
    status: 'active',
    originChannel: 'test',
    createdAt: '2024-01-01T00:00:00Z',
    lastActiveAt: '2024-01-01T12:00:00Z',
    ...overrides
  }
}

function makeThread(id: string, overrides: Partial<Thread> = {}): Thread {
  return {
    ...makeThreadSummary(id),
    workspacePath: '/test/workspace',
    userId: 'local',
    metadata: {},
    turns: [],
    ...overrides
  }
}

// Reset store between tests
beforeEach(() => {
  useThreadStore.getState().reset()
})

describe('threadStore.setThreadList', () => {
  it('sets the thread list', () => {
    const threads = [makeThreadSummary('a'), makeThreadSummary('b')]
    useThreadStore.getState().setThreadList(threads)
    expect(useThreadStore.getState().threadList).toEqual(threads)
  })

  it('replaces existing list', () => {
    useThreadStore.getState().setThreadList([makeThreadSummary('old')])
    useThreadStore.getState().setThreadList([makeThreadSummary('new1'), makeThreadSummary('new2')])
    expect(useThreadStore.getState().threadList.map((t) => t.id)).toEqual(['new1', 'new2'])
  })
})

describe('threadStore.addThread', () => {
  it('prepends thread to the list', () => {
    useThreadStore.getState().setThreadList([makeThreadSummary('existing')])
    useThreadStore.getState().addThread(makeThreadSummary('new'))
    const list = useThreadStore.getState().threadList
    expect(list[0].id).toBe('new')
    expect(list[1].id).toBe('existing')
  })

  it('adds to empty list', () => {
    useThreadStore.getState().addThread(makeThreadSummary('t1'))
    expect(useThreadStore.getState().threadList).toHaveLength(1)
  })

  it('skips duplicate thread id (idempotent)', () => {
    const t = makeThreadSummary('same-id')
    useThreadStore.getState().addThread(t)
    useThreadStore.getState().addThread(t)
    expect(useThreadStore.getState().threadList).toHaveLength(1)
    expect(useThreadStore.getState().threadList[0].id).toBe('same-id')
  })

  it('allows adding threads with different ids', () => {
    useThreadStore.getState().addThread(makeThreadSummary('a'))
    useThreadStore.getState().addThread(makeThreadSummary('b'))
    expect(useThreadStore.getState().threadList.map((x) => x.id)).toEqual(['b', 'a'])
  })

  it('does not add duplicate when list already contains that id', () => {
    useThreadStore.getState().setThreadList([makeThreadSummary('existing')])
    useThreadStore.getState().addThread(makeThreadSummary('existing'))
    expect(useThreadStore.getState().threadList).toHaveLength(1)
    expect(useThreadStore.getState().threadList[0].id).toBe('existing')
  })
})

describe('threadStore.updateThreadStatus', () => {
  it('updates the status of the matching thread', () => {
    useThreadStore.getState().setThreadList([makeThreadSummary('t1'), makeThreadSummary('t2')])
    useThreadStore.getState().updateThreadStatus('t1', 'paused')
    expect(useThreadStore.getState().threadList[0].status).toBe('paused')
    expect(useThreadStore.getState().threadList[1].status).toBe('active')
  })

  it('updates the active thread if it matches', () => {
    const thread = makeThread('t1')
    useThreadStore.getState().setActiveThread(thread)
    useThreadStore.getState().updateThreadStatus('t1', 'archived')
    expect(useThreadStore.getState().activeThread?.status).toBe('archived')
  })

  it('does not change other threads', () => {
    useThreadStore.getState().setThreadList([
      makeThreadSummary('t1'),
      makeThreadSummary('t2'),
      makeThreadSummary('t3')
    ])
    useThreadStore.getState().updateThreadStatus('t2', 'paused')
    expect(useThreadStore.getState().threadList[0].status).toBe('active')
    expect(useThreadStore.getState().threadList[2].status).toBe('active')
  })
})

describe('threadStore.removeThread', () => {
  it('removes the thread from the list', () => {
    useThreadStore.getState().setThreadList([makeThreadSummary('t1'), makeThreadSummary('t2')])
    useThreadStore.getState().removeThread('t1')
    expect(useThreadStore.getState().threadList.map((t) => t.id)).toEqual(['t2'])
  })

  it('clears activeThreadId when the active thread is removed', () => {
    useThreadStore.getState().setThreadList([makeThreadSummary('t1')])
    useThreadStore.getState().setActiveThreadId('t1')
    useThreadStore.getState().removeThread('t1')
    expect(useThreadStore.getState().activeThreadId).toBeNull()
  })

  it('clears activeThread when the active thread is removed', () => {
    const thread = makeThread('t1')
    useThreadStore.getState().setActiveThread(thread)
    useThreadStore.getState().removeThread('t1')
    expect(useThreadStore.getState().activeThread).toBeNull()
  })

  it('does not clear activeThreadId when a different thread is removed', () => {
    useThreadStore.getState().setThreadList([makeThreadSummary('t1'), makeThreadSummary('t2')])
    useThreadStore.getState().setActiveThreadId('t1')
    useThreadStore.getState().removeThread('t2')
    expect(useThreadStore.getState().activeThreadId).toBe('t1')
  })
})

describe('threadStore.renameThread', () => {
  it('updates the displayName of the matching thread', () => {
    useThreadStore.getState().setThreadList([makeThreadSummary('t1')])
    useThreadStore.getState().renameThread('t1', 'My renamed thread')
    expect(useThreadStore.getState().threadList[0].displayName).toBe('My renamed thread')
  })

  it('also updates the active thread displayName', () => {
    const thread = makeThread('t1', { displayName: 'Old name' })
    useThreadStore.getState().setActiveThread(thread)
    useThreadStore.getState().renameThread('t1', 'New name')
    expect(useThreadStore.getState().activeThread?.displayName).toBe('New name')
  })
})

describe('threadStore.setActiveThread', () => {
  it('does not change activeThreadId when loading a different thread id (stale read guard)', () => {
    useThreadStore.getState().setActiveThreadId('A')
    const threadB = makeThread('B', { displayName: 'Loaded B' })
    useThreadStore.getState().setActiveThread(threadB)
    expect(useThreadStore.getState().activeThreadId).toBe('A')
    expect(useThreadStore.getState().activeThread?.id).toBe('B')
  })
})

describe('selectFilteredThreads', () => {
  const threads = [
    makeThreadSummary('t1', { displayName: 'Hello World' }),
    makeThreadSummary('t2', { displayName: 'Goodbye Planet' }),
    makeThreadSummary('t3', { displayName: null })
  ]

  beforeEach(() => {
    useThreadStore.getState().setThreadList(threads)
  })

  function getFiltered(): ThreadSummary[] {
    return selectFilteredThreads(useThreadStore.getState())
  }

  it('returns all threads when searchQuery is empty', () => {
    useThreadStore.getState().setSearchQuery('')
    expect(getFiltered()).toHaveLength(3)
  })

  it('filters case-insensitively by displayName', () => {
    useThreadStore.getState().setSearchQuery('hello')
    expect(getFiltered().map((t) => t.id)).toEqual(['t1'])
  })

  it('returns empty array when no match', () => {
    useThreadStore.getState().setSearchQuery('zzz-no-match')
    expect(getFiltered()).toHaveLength(0)
  })

  it('handles null displayName gracefully (treats as empty string)', () => {
    useThreadStore.getState().setSearchQuery('null')
    // Thread t3 has null displayName; empty string does not include 'null'
    expect(getFiltered()).toHaveLength(0)
  })

  it('matches whitespace-only query as empty (no trim issue)', () => {
    useThreadStore.getState().setSearchQuery('   ')
    // '   '.trim() is '' so all threads returned
    expect(getFiltered()).toHaveLength(3)
  })
})

describe('threadStore full CRUD lifecycle', () => {
  // Helper to always get latest state snapshot
  const s = () => useThreadStore.getState()

  it('simulates create → select → rename → archive → delete flow', () => {
    // Create
    const t = makeThreadSummary('lifecycle-1')
    s().addThread(t)
    expect(s().threadList).toHaveLength(1)

    // Select
    s().setActiveThreadId('lifecycle-1')
    expect(s().activeThreadId).toBe('lifecycle-1')

    // Load full thread
    const full = makeThread('lifecycle-1')
    s().setActiveThread(full)
    expect(s().activeThread?.id).toBe('lifecycle-1')

    // Rename (client-side only in M2)
    s().renameThread('lifecycle-1', 'Renamed Thread')
    expect(s().activeThread?.displayName).toBe('Renamed Thread')
    expect(s().threadList[0].displayName).toBe('Renamed Thread')

    // Archive
    s().updateThreadStatus('lifecycle-1', 'archived')
    expect(s().activeThread?.status).toBe('archived')

    // Delete
    s().removeThread('lifecycle-1')
    expect(s().threadList).toHaveLength(0)
    expect(s().activeThreadId).toBeNull()
    expect(s().activeThread).toBeNull()
  })
})
