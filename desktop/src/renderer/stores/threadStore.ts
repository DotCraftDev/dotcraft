import { create } from 'zustand'
import type { ThreadSummary, Thread, ThreadStatus, ThreadRuntimeSnapshot } from '../types/thread'
import { useViewerTabStore } from './viewerTabStore'
export type { ThreadRuntimeSnapshot } from '../types/thread'

export interface ParkedApproval {
  bridgeId: string
  turnId: string | null
  rawParams: Record<string, unknown>
}

interface ApplyRuntimeSnapshotOptions {
  isActive: boolean
  isDesktopOrigin: boolean
}

interface ThreadStoreState {
  threadList: ThreadSummary[]
  activeThreadId: string | null
  activeThread: Thread | null
  searchQuery: string
  loading: boolean
  /** Set of threadIds that currently have a running turn (for activity indicator). */
  runningTurnThreadIds: Set<string>
  /** Background-thread approvals waiting for user to return to that thread. */
  parkedApprovals: Map<string, ParkedApproval>
  /** Lightweight per-thread runtime snapshot from workspace-level broadcasts. */
  runtimeSnapshots: Map<string, ThreadRuntimeSnapshot>
  /** Threads that should show "awaiting approval" badge in sidebar. */
  pendingApprovalThreadIds: Set<string>
  /** Threads with pending plan confirmation shortcut in conversation view. */
  pendingPlanConfirmationThreadIds: Set<string>
  /** Threads that completed in background and have not been visited yet. */
  unreadCompletedThreadIds: Set<string>
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
  parkApproval(threadId: string, approval: ParkedApproval): void
  consumeParkedApproval(threadId: string): ParkedApproval | null
  clearParkedApproval(threadId: string): void
  applyRuntimeSnapshot(threadId: string, runtime: ThreadRuntimeSnapshot, options: ApplyRuntimeSnapshotOptions): void
  markPlanConfirmationPending(threadId: string): void
  clearPlanConfirmationPending(threadId: string): void
  markUnreadCompleted(threadId: string): void
  clearUnreadCompleted(threadId: string): void
  reset(): void
}

export interface ThreadStore extends ThreadStoreState, ThreadStoreActions {}

const initialState: ThreadStoreState = {
  threadList: [],
  activeThreadId: null,
  activeThread: null,
  searchQuery: '',
  loading: false,
  runningTurnThreadIds: new Set<string>(),
  parkedApprovals: new Map<string, ParkedApproval>(),
  runtimeSnapshots: new Map<string, ThreadRuntimeSnapshot>(),
  pendingApprovalThreadIds: new Set<string>(),
  pendingPlanConfirmationThreadIds: new Set<string>(),
  unreadCompletedThreadIds: new Set<string>()
}

function filterSetToThreadList(current: Set<string>, ids: Set<string>): Set<string> {
  return new Set([...current].filter((id) => ids.has(id)))
}

function filterMapToThreadList<T>(current: Map<string, T>, ids: Set<string>): Map<string, T> {
  return new Map([...current].filter(([id]) => ids.has(id)))
}

export const useThreadStore = create<ThreadStore>((set, _get) => ({
  ...initialState,

  setThreadList(threads) {
    set((state) => {
      const threadIds = new Set(threads.map((thread) => thread.id))
      const runtimeSnapshots = filterMapToThreadList(state.runtimeSnapshots, threadIds)
      const parkedApprovals = filterMapToThreadList(state.parkedApprovals, threadIds)
      const runningTurnThreadIds = filterSetToThreadList(state.runningTurnThreadIds, threadIds)
      const pendingApprovalThreadIds = filterSetToThreadList(state.pendingApprovalThreadIds, threadIds)
      const pendingPlanConfirmationThreadIds = filterSetToThreadList(
        state.pendingPlanConfirmationThreadIds,
        threadIds
      )
      const unreadCompletedThreadIds = filterSetToThreadList(state.unreadCompletedThreadIds, threadIds)

      for (const thread of threads) {
        const runtime = thread.runtime
        if (!runtime) continue

        const snapshot: ThreadRuntimeSnapshot = {
          running: runtime.running === true,
          waitingOnApproval: runtime.waitingOnApproval === true,
          waitingOnPlanConfirmation: runtime.waitingOnPlanConfirmation === true
        }
        const previous = runtimeSnapshots.get(thread.id)
        const isActive = state.activeThreadId === thread.id
        const isDesktopOrigin = thread.originChannel?.toLowerCase() === 'dotcraft-desktop'
        runtimeSnapshots.set(thread.id, snapshot)

        if (snapshot.running) {
          runningTurnThreadIds.add(thread.id)
          unreadCompletedThreadIds.delete(thread.id)
        } else {
          runningTurnThreadIds.delete(thread.id)
          if (!isActive && previous?.running === true) {
            unreadCompletedThreadIds.add(thread.id)
          }
        }

        if (snapshot.waitingOnApproval && !isActive && isDesktopOrigin) {
          pendingApprovalThreadIds.add(thread.id)
        } else {
          pendingApprovalThreadIds.delete(thread.id)
          if (!snapshot.waitingOnApproval) {
            parkedApprovals.delete(thread.id)
          }
        }

        if (snapshot.waitingOnPlanConfirmation && !isActive && isDesktopOrigin) {
          pendingPlanConfirmationThreadIds.add(thread.id)
        } else {
          pendingPlanConfirmationThreadIds.delete(thread.id)
        }
      }

      return {
        threadList: threads,
        runtimeSnapshots,
        runningTurnThreadIds,
        parkedApprovals,
        pendingApprovalThreadIds,
        pendingPlanConfirmationThreadIds,
        unreadCompletedThreadIds
      }
    })
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
    useViewerTabStore.getState().onThreadDeleted(threadId, {
      onBrowserTabRemoved: (tab) => {
        void window.api.workspace.viewer.browser.destroy({ tabId: tab.id })
      },
      onTerminalTabRemoved: (tab) => {
        void window.api.workspace.viewer.terminal.dispose({ tabId: tab.id })
      }
    })
    set((state) => {
      const parkedApprovals = new Map(state.parkedApprovals)
      parkedApprovals.delete(threadId)
      const runtimeSnapshots = new Map(state.runtimeSnapshots)
      runtimeSnapshots.delete(threadId)
      const pendingApprovalThreadIds = new Set(state.pendingApprovalThreadIds)
      pendingApprovalThreadIds.delete(threadId)
      const pendingPlanConfirmationThreadIds = new Set(state.pendingPlanConfirmationThreadIds)
      pendingPlanConfirmationThreadIds.delete(threadId)
      const unreadCompletedThreadIds = new Set(state.unreadCompletedThreadIds)
      unreadCompletedThreadIds.delete(threadId)
      const runningTurnThreadIds = new Set(state.runningTurnThreadIds)
      runningTurnThreadIds.delete(threadId)
      return {
        threadList: state.threadList.filter((t) => t.id !== threadId),
        activeThreadId:
          state.activeThreadId === threadId ? null : state.activeThreadId,
        activeThread:
          state.activeThread?.id === threadId ? null : state.activeThread,
        runningTurnThreadIds,
        parkedApprovals,
        runtimeSnapshots,
        pendingApprovalThreadIds,
        pendingPlanConfirmationThreadIds,
        unreadCompletedThreadIds
      }
    })
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
    set((state) => {
      if (!id) {
        return { activeThreadId: id }
      }

      const pendingPlanConfirmationThreadIds = new Set(state.pendingPlanConfirmationThreadIds)
      pendingPlanConfirmationThreadIds.delete(id)
      const pendingApprovalThreadIds = new Set(state.pendingApprovalThreadIds)
      pendingApprovalThreadIds.delete(id)
      const unreadCompletedThreadIds = new Set(state.unreadCompletedThreadIds)
      unreadCompletedThreadIds.delete(id)
      return {
        activeThreadId: id,
        pendingApprovalThreadIds,
        pendingPlanConfirmationThreadIds,
        unreadCompletedThreadIds
      }
    })
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

  parkApproval(threadId, approval) {
    set((state) => {
      const parkedApprovals = new Map(state.parkedApprovals)
      parkedApprovals.set(threadId, approval)
      return { parkedApprovals }
    })
  },

  consumeParkedApproval(threadId) {
    const state = _get()
    const approval = state.parkedApprovals.get(threadId) ?? null
    if (!approval) return null
    const parkedApprovals = new Map(state.parkedApprovals)
    parkedApprovals.delete(threadId)
    set({ parkedApprovals })
    return approval
  },

  clearParkedApproval(threadId) {
    set((state) => {
      if (!state.parkedApprovals.has(threadId)) return state
      const parkedApprovals = new Map(state.parkedApprovals)
      parkedApprovals.delete(threadId)
      return { parkedApprovals }
    })
  },

  applyRuntimeSnapshot(threadId, runtime, options) {
    set((state) => {
      const previous = state.runtimeSnapshots.get(threadId)
      const runtimeSnapshots = new Map(state.runtimeSnapshots)
      runtimeSnapshots.set(threadId, runtime)

      const runningTurnThreadIds = new Set(state.runningTurnThreadIds)
      if (runtime.running) {
        runningTurnThreadIds.add(threadId)
      } else {
        runningTurnThreadIds.delete(threadId)
      }

      const parkedApprovals = new Map(state.parkedApprovals)
      if (!runtime.waitingOnApproval) {
        parkedApprovals.delete(threadId)
      }

      const pendingApprovalThreadIds = new Set(state.pendingApprovalThreadIds)
      if (runtime.waitingOnApproval && !options.isActive && options.isDesktopOrigin) {
        pendingApprovalThreadIds.add(threadId)
      } else {
        pendingApprovalThreadIds.delete(threadId)
      }

      const pendingPlanConfirmationThreadIds = new Set(state.pendingPlanConfirmationThreadIds)
      if (runtime.waitingOnPlanConfirmation && !options.isActive && options.isDesktopOrigin) {
        pendingPlanConfirmationThreadIds.add(threadId)
      } else {
        pendingPlanConfirmationThreadIds.delete(threadId)
      }

      const unreadCompletedThreadIds = new Set(state.unreadCompletedThreadIds)
      if (options.isActive || runtime.running) {
        unreadCompletedThreadIds.delete(threadId)
      } else if (previous?.running === true) {
        unreadCompletedThreadIds.add(threadId)
      }

      return {
        runtimeSnapshots,
        runningTurnThreadIds,
        parkedApprovals,
        pendingApprovalThreadIds,
        pendingPlanConfirmationThreadIds,
        unreadCompletedThreadIds
      }
    })
  },

  markPlanConfirmationPending(threadId) {
    set((state) => {
      const pendingPlanConfirmationThreadIds = new Set(state.pendingPlanConfirmationThreadIds)
      pendingPlanConfirmationThreadIds.add(threadId)
      return { pendingPlanConfirmationThreadIds }
    })
  },

  clearPlanConfirmationPending(threadId) {
    set((state) => {
      if (!state.pendingPlanConfirmationThreadIds.has(threadId)) return state
      const pendingPlanConfirmationThreadIds = new Set(state.pendingPlanConfirmationThreadIds)
      pendingPlanConfirmationThreadIds.delete(threadId)
      return { pendingPlanConfirmationThreadIds }
    })
  },

  markUnreadCompleted(threadId) {
    set((state) => {
      const unreadCompletedThreadIds = new Set(state.unreadCompletedThreadIds)
      unreadCompletedThreadIds.add(threadId)
      return { unreadCompletedThreadIds }
    })
  },

  clearUnreadCompleted(threadId) {
    set((state) => {
      if (!state.unreadCompletedThreadIds.has(threadId)) return state
      const unreadCompletedThreadIds = new Set(state.unreadCompletedThreadIds)
      unreadCompletedThreadIds.delete(threadId)
      return { unreadCompletedThreadIds }
    })
  },

  reset() {
    set({
      ...initialState,
      runningTurnThreadIds: new Set<string>(),
      parkedApprovals: new Map<string, ParkedApproval>(),
      runtimeSnapshots: new Map<string, ThreadRuntimeSnapshot>(),
      pendingApprovalThreadIds: new Set<string>(),
      pendingPlanConfirmationThreadIds: new Set<string>(),
      unreadCompletedThreadIds: new Set<string>()
    })
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
