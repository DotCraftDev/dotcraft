import { create } from 'zustand'
import type { ThreadSummary, Thread, ThreadStatus } from '../types/thread'

interface ThreadStoreState {
  threadList: ThreadSummary[]
  activeThreadId: string | null
  activeThread: Thread | null
  searchQuery: string
  loading: boolean
  /** Set of threadIds that currently have a running turn (for activity indicator). */
  runningTurnThreadIds: Set<string>
}

interface ThreadStoreActions {
  setThreadList(threads: ThreadSummary[]): void
  /** Prepend a new thread to the list (newest first). No-op if the same id already exists. */
  addThread(thread: ThreadSummary): void
  updateThreadStatus(threadId: string, newStatus: ThreadStatus): void
  removeThread(threadId: string): void
  renameThread(threadId: string, displayName: string): void
  setActiveThreadId(id: string | null): void
  setActiveThread(thread: Thread | null): void
  setSearchQuery(query: string): void
  setLoading(loading: boolean): void
  markTurnStarted(threadId: string): void
  markTurnEnded(threadId: string): void
  reset(): void
}

export interface ThreadStore extends ThreadStoreState, ThreadStoreActions {}

const initialState: ThreadStoreState = {
  threadList: [],
  activeThreadId: null,
  activeThread: null,
  searchQuery: '',
  loading: false,
  runningTurnThreadIds: new Set<string>()
}

export const useThreadStore = create<ThreadStore>((set, _get) => ({
  ...initialState,

  setThreadList(threads) {
    set({ threadList: threads })
  },

  addThread(thread) {
    set((state) => {
      if (state.threadList.some((t) => t.id === thread.id)) return state
      return { threadList: [thread, ...state.threadList] }
    })
  },

  updateThreadStatus(threadId, newStatus) {
    set((state) => ({
      threadList: state.threadList.map((t) =>
        t.id === threadId ? { ...t, status: newStatus } : t
      ),
      // If the active thread's status changed, update it too
      activeThread:
        state.activeThread?.id === threadId
          ? { ...state.activeThread, status: newStatus }
          : state.activeThread
    }))
  },

  removeThread(threadId) {
    set((state) => ({
      threadList: state.threadList.filter((t) => t.id !== threadId),
      activeThreadId:
        state.activeThreadId === threadId ? null : state.activeThreadId,
      activeThread:
        state.activeThread?.id === threadId ? null : state.activeThread
    }))
  },

  renameThread(threadId, displayName) {
    set((state) => ({
      threadList: state.threadList.map((t) =>
        t.id === threadId ? { ...t, displayName } : t
      ),
      activeThread:
        state.activeThread?.id === threadId
          ? { ...state.activeThread, displayName }
          : state.activeThread
    }))
  },

  setActiveThreadId(id) {
    set({ activeThreadId: id })
  },

  setActiveThread(thread) {
    // Do not sync activeThreadId here — selection is user-driven; stale thread/read
    // responses must not redirect which thread is selected.
    set({ activeThread: thread })
  },

  setSearchQuery(query) {
    set({ searchQuery: query })
  },

  setLoading(loading) {
    set({ loading })
  },

  markTurnStarted(threadId) {
    set((state) => {
      const next = new Set(state.runningTurnThreadIds)
      next.add(threadId)
      return { runningTurnThreadIds: next }
    })
  },

  markTurnEnded(threadId) {
    set((state) => {
      const next = new Set(state.runningTurnThreadIds)
      next.delete(threadId)
      return { runningTurnThreadIds: next }
    })
  },

  reset() {
    set({ ...initialState, runningTurnThreadIds: new Set<string>() })
  }
}))

// Expose store to E2E / debug tooling via a window global (browser only)
if (typeof window !== 'undefined') {
  ;(window as unknown as Record<string, unknown>).__THREAD_STORE_STATE = () =>
    useThreadStore.getState()
}

// ---------------------------------------------------------------------------
// Selectors
// ---------------------------------------------------------------------------

/**
 * Derived selector: non-archived threads filtered by searchQuery.
 * Archived threads are always hidden from the main sidebar list — they disappear
 * immediately on archive action and also when a thread/statusChanged notification
 * arrives with newStatus: 'archived'.
 * Usage: const filtered = useThreadStore(selectFilteredThreads)
 */
export function selectFilteredThreads(state: ThreadStore): ThreadSummary[] {
  const visible = state.threadList.filter((t) => t.status !== 'archived')
  if (!state.searchQuery.trim()) return visible
  const q = state.searchQuery.toLowerCase()
  return visible.filter((t) => (t.displayName ?? '').toLowerCase().includes(q))
}
