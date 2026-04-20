/**
 * Per-thread viewer tab state store (M1).
 *
 * Each thread independently manages its own list of viewer tabs.
 * Thread lifecycle:
 *  - `openFile`            — opens (or focuses) a file tab in the active thread.
 *  - `closeTab`            — removes a tab; nearest-neighbor fallback for active tab.
 *  - `setActiveTab`        — sets active tab within a thread.
 *  - `onThreadSwitched`    — records the new active thread ID.
 *  - `onThreadDeleted`     — discards all tabs for the deleted thread.
 *  - `onWorkspaceSwitched` — clears all tab state (new workspace = fresh start).
 *
 * Label collision resolution (§5.4):
 *   When multiple open tabs share the same basename, we walk backward up the
 *   relative path, appending parent directory segments, until all labels are unique.
 */
import { create } from 'zustand'
import type { ViewerTab, ViewerContentClass, ViewerKind, PerThreadViewerState } from '../../shared/viewer/types'

// ─── Label deduplication helpers ────────────────────────────────────────────

function computeLabels(tabs: ViewerTab[]): ViewerTab[] {
  if (tabs.length === 0) return tabs

  // Build a map: basename → [tab indices with that basename]
  const basenameMap = new Map<string, number[]>()
  for (let i = 0; i < tabs.length; i++) {
    const tab = tabs[i]!
    // Normalize separators for consistent splitting
    const parts = tab.relativePath.replace(/\\/g, '/').split('/')
    const base = parts[parts.length - 1] ?? tab.relativePath
    const existing = basenameMap.get(base)
    if (existing) {
      existing.push(i)
    } else {
      basenameMap.set(base, [i])
    }
  }

  // For non-colliding tabs, the label is simply the basename.
  // For colliding tabs, we extend with parent path segments.
  const labels = tabs.map((tab) => {
    const parts = tab.relativePath.replace(/\\/g, '/').split('/')
    return parts[parts.length - 1] ?? tab.relativePath
  })

  for (const [, indices] of basenameMap.entries()) {
    if (indices.length <= 1) continue

    // Resolve collision: keep adding parent segments until all are unique
    let depth = 1 // 0 = basename, 1 = parent/basename, ...
    const maxDepth = Math.max(
      ...indices.map((i) => tabs[i]!.relativePath.replace(/\\/g, '/').split('/').length - 1)
    )

    while (depth <= maxDepth) {
      const candidates = indices.map((i) => {
        const parts = tabs[i]!.relativePath.replace(/\\/g, '/').split('/')
        const start = Math.max(0, parts.length - 1 - depth)
        return parts.slice(start).join('/')
      })

      const unique = new Set(candidates).size === candidates.length
      if (unique) {
        for (let j = 0; j < indices.length; j++) {
          labels[indices[j]!] = candidates[j]!
        }
        break
      }
      depth++
    }

    // Fallback: use the full relative path if still not unique after max depth
    if (depth > maxDepth) {
      for (const i of indices) {
        labels[i] = tabs[i]!.relativePath
      }
    }
  }

  return tabs.map((tab, i) => ({ ...tab, label: labels[i] ?? tab.relativePath }))
}

// ─── Store interface ────────────────────────────────────────────────────────

interface ViewerTabStoreState {
  /** Map from threadId → per-thread viewer state. */
  byThread: Map<string, PerThreadViewerState>
  /** Currently active thread ID (mirrors threadStore.activeThreadId). */
  currentThreadId: string | null
  /** Current workspace path — used to scope tab identity. */
  currentWorkspacePath: string | null
}

interface ViewerTabStoreActions {
  /**
   * Opens a file tab in the given thread.
   * If an identical tab (same absolutePath) already exists, focuses it and returns its id.
   * Otherwise, creates a new tab and activates it.
   * Returns the tab ID.
   */
  openFile(params: {
    threadId: string
    absolutePath: string
    relativePath: string
    contentClass: ViewerContentClass
    sizeBytes?: number
    kind?: ViewerKind
  }): string

  /** Closes the tab with `tabId` in `threadId` and selects the nearest neighbor. */
  closeTab(threadId: string, tabId: string): void

  /** Activates an existing tab in `threadId`. */
  setActiveTab(threadId: string, tabId: string): void

  /** Sets the active thread (does not alter tab state). */
  onThreadSwitched(newThreadId: string | null): void

  /** Removes all viewer tabs for the given thread (e.g., thread deleted). */
  onThreadDeleted(threadId: string): void

  /** Clears all per-thread state when the workspace changes. */
  onWorkspaceSwitched(newWorkspacePath: string): void

  /** Returns the per-thread viewer state for the given thread (lazy-initialised). */
  getThreadState(threadId: string): PerThreadViewerState

  /** Returns current thread's tabs (convenience selector for UI components). */
  getCurrentTabs(): ViewerTab[]

  /** Returns current thread's active tab ID. */
  getCurrentActiveTabId(): string | null
}

type ViewerTabStore = ViewerTabStoreState & ViewerTabStoreActions

// Stable empty references for selectors (avoid new object/array per read).
const EMPTY_TABS: ViewerTab[] = Object.freeze([]) as unknown as ViewerTab[]
const EMPTY_THREAD_STATE: PerThreadViewerState = Object.freeze({
  tabs: EMPTY_TABS,
  activeTabId: null
}) as PerThreadViewerState

// ─── Counter for unique IDs ───────────────────────────────────────────────────

let _tabIdCounter = 0
function nextTabId(): string {
  return `vtab-${Date.now()}-${++_tabIdCounter}`
}

// ─── Store implementation ────────────────────────────────────────────────────

export const useViewerTabStore = create<ViewerTabStore>((set, get) => ({
  byThread: new Map(),
  currentThreadId: null,
  currentWorkspacePath: null,

  openFile({ threadId, absolutePath, relativePath, contentClass, sizeBytes, kind = 'file' }) {
    const state = get()
    const threadState = state.getThreadState(threadId)

    // Deduplication: if a tab with the same absolutePath already exists, focus it
    const existing = threadState.tabs.find(
      (t) => t.kind === kind && t.absolutePath === absolutePath
    )
    if (existing) {
      set((s) => {
        const next = new Map(s.byThread)
        next.set(threadId, { ...threadState, activeTabId: existing.id })
        return { byThread: next }
      })
      return existing.id
    }

    // Create new tab
    const newTab: ViewerTab = {
      id: nextTabId(),
      kind,
      absolutePath,
      relativePath,
      label: relativePath, // will be recomputed by computeLabels
      contentClass,
      ...(sizeBytes !== undefined ? { sizeBytes } : {})
    }

    const newTabs = computeLabels([...threadState.tabs, newTab])
    set((s) => {
      const next = new Map(s.byThread)
      next.set(threadId, { tabs: newTabs, activeTabId: newTab.id })
      return { byThread: next }
    })

    return newTab.id
  },

  closeTab(threadId, tabId) {
    const state = get()
    const threadState = state.getThreadState(threadId)
    const tabs = threadState.tabs
    const idx = tabs.findIndex((t) => t.id === tabId)
    if (idx === -1) return

    const newTabs = computeLabels(tabs.filter((_, i) => i !== idx))

    let newActiveTabId: string | null = threadState.activeTabId

    if (threadState.activeTabId === tabId) {
      // Nearest-neighbor: prefer left, then right, then fall back to null
      if (idx > 0) {
        newActiveTabId = tabs[idx - 1]!.id
      } else if (idx < tabs.length - 1) {
        newActiveTabId = tabs[idx + 1]!.id
      } else {
        // No more viewer tabs — signal caller to return to last system tab
        newActiveTabId = null
      }
    }

    set((s) => {
      const next = new Map(s.byThread)
      next.set(threadId, { tabs: newTabs, activeTabId: newActiveTabId })
      return { byThread: next }
    })
  },

  setActiveTab(threadId, tabId) {
    const state = get()
    const threadState = state.getThreadState(threadId)
    if (!threadState.tabs.find((t) => t.id === tabId)) return

    set((s) => {
      const next = new Map(s.byThread)
      next.set(threadId, { ...threadState, activeTabId: tabId })
      return { byThread: next }
    })
  },

  onThreadSwitched(newThreadId) {
    set({ currentThreadId: newThreadId })
  },

  onThreadDeleted(threadId) {
    set((s) => {
      const next = new Map(s.byThread)
      next.delete(threadId)
      return { byThread: next }
    })
  },

  onWorkspaceSwitched(newWorkspacePath) {
    set({
      byThread: new Map(),
      currentWorkspacePath: newWorkspacePath
    })
  },

  getThreadState(threadId) {
    const existing = get().byThread.get(threadId)
    if (existing) return existing
    return EMPTY_THREAD_STATE
  },

  getCurrentTabs() {
    const { currentThreadId } = get()
    if (!currentThreadId) return EMPTY_TABS
    return get().getThreadState(currentThreadId).tabs
  },

  getCurrentActiveTabId() {
    const { currentThreadId } = get()
    if (!currentThreadId) return null
    return get().getThreadState(currentThreadId).activeTabId
  }
}))
