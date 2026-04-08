import { create } from 'zustand'
import type { ImageAttachment, ThreadMode } from '../types/conversation'

const SIDEBAR_DEFAULT_WIDTH = 240
const SIDEBAR_MIN_WIDTH = 200
const SIDEBAR_COLLAPSED_WIDTH = 48

const DETAIL_DEFAULT_WIDTH = 400
const DETAIL_MIN_WIDTH = 300

/** Timeout for pending welcome turn to prevent permanent residue */
const PENDING_WELCOME_TIMEOUT_MS = 30_000

export type DetailPanelTab = 'changes' | 'plan' | 'terminal'

/** Main content area: conversation vs auxiliary surfaces (Skills, Automations, Settings). */
export type ActiveMainView = 'conversation' | 'skills' | 'automations' | 'settings' | 'channels'

/** Automations view: Tasks (orchestrator) vs Cron (scheduled jobs). */
export type AutomationsTab = 'tasks' | 'cron'

export interface UIState {
  /** Which primary view fills the center column (conversation panel slot). */
  activeMainView: ActiveMainView
  /** Active tab inside Automations view (spec §21.1). */
  automationsTab: AutomationsTab
  sidebarCollapsed: boolean
  sidebarWidth: number
  detailPanelVisible: boolean
  detailPanelWidth: number
  activeDetailTab: DetailPanelTab
  /** Currently selected file path in the Changes tab */
  selectedChangedFile: string | null
  /**
   * Tracks the turn ID for which the detail panel was auto-shown.
   * Prevents re-triggering after the user manually hides the panel.
   */
  autoShowTriggeredForTurn: string | null
  /** Text to pre-fill into the InputComposer when its next mounts. */
  composerPrefill: string | null
  /**
   * First message to send after thread/read completes for a thread created from the
   * welcome screen (avoids optimistic UI being cleared by conversation reset).
   */
  pendingWelcomeTurn: {
    threadId: string
    text: string
    images?: ImageAttachment[]
    /** Agent/plan chosen on Welcome before thread exists; applied after thread/read. */
    mode?: ThreadMode
    /** Model chosen on Welcome before thread exists; applied after thread/read. */
    model?: string
    createdAt: number
  } | null
}

interface UIStore extends UIState {
  setActiveMainView(view: ActiveMainView): void
  setAutomationsTab(tab: AutomationsTab): void
  toggleSidebar(): void
  setSidebarCollapsed(collapsed: boolean): void
  setSidebarWidth(width: number): void
  toggleDetailPanel(): void
  setDetailPanelVisible(visible: boolean): void
  setDetailPanelWidth(width: number): void
  setActiveDetailTab(tab: DetailPanelTab): void
  selectChangedFile(filePath: string | null): void
  /** Open detail panel, switch to Changes tab, select the given file */
  showChangesForFile(filePath: string): void
  /** Mark auto-show as triggered for a given turn (prevents re-trigger) */
  markAutoShowForTurn(turnId: string): void
  /** Set text to be picked up by InputComposer on its next mount. */
  setComposerPrefill(text: string): void
  /** Read and clear the prefill text atomically. */
  consumeComposerPrefill(): string | null
  /** Queue first turn for a thread created from the welcome composer. */
  setPendingWelcomeTurn(
    payload: { threadId: string; text: string; images?: ImageAttachment[]; mode?: ThreadMode; model?: string } | null
  ): void
  /** If pending matches threadId, return payload and clear; otherwise return null. */
  consumePendingWelcomeTurnIfMatch(
    threadId: string
  ): { text: string; images?: ImageAttachment[]; mode?: ThreadMode; model?: string } | null
  /** Clear pending welcome turn when it targets the given thread (e.g. thread/read failed). */
  cancelPendingWelcomeTurnForThread(threadId: string): void
}

/** Internal state not exposed in UIState but used for timeout management */
interface InternalState {
  _pendingWelcomeTimer: ReturnType<typeof setTimeout> | null
}

export const useUIStore = create<UIStore & InternalState>((set, get) => ({
  activeMainView: 'conversation',
  automationsTab: 'tasks',
  sidebarCollapsed: false,
  sidebarWidth: SIDEBAR_DEFAULT_WIDTH,
  detailPanelVisible: true,
  detailPanelWidth: DETAIL_DEFAULT_WIDTH,
  activeDetailTab: 'changes',
  selectedChangedFile: null,
  autoShowTriggeredForTurn: null,
  composerPrefill: null,
  pendingWelcomeTurn: null,

  setActiveMainView(view) {
    set({ activeMainView: view })
  },

  setAutomationsTab(tab) {
    set({ automationsTab: tab })
  },

  toggleSidebar() {
    set((state) => ({ sidebarCollapsed: !state.sidebarCollapsed }))
  },

  setSidebarCollapsed(collapsed: boolean) {
    set({ sidebarCollapsed: collapsed })
  },

  setSidebarWidth(width: number) {
    const clamped = Math.max(SIDEBAR_MIN_WIDTH, width)
    set({ sidebarWidth: clamped })
  },

  toggleDetailPanel() {
    set((state) => ({ detailPanelVisible: !state.detailPanelVisible }))
  },

  setDetailPanelVisible(visible: boolean) {
    set({ detailPanelVisible: visible })
  },

  setDetailPanelWidth(width: number) {
    const clamped = Math.max(DETAIL_MIN_WIDTH, width)
    set({ detailPanelWidth: clamped })
  },

  setActiveDetailTab(tab: DetailPanelTab) {
    set({ activeDetailTab: tab })
    // Auto-show the panel when switching to a tab
    if (!get().detailPanelVisible) {
      set({ detailPanelVisible: true })
    }
  },

  selectChangedFile(filePath) {
    set({ selectedChangedFile: filePath })
  },

  showChangesForFile(filePath) {
    set({ activeDetailTab: 'changes', detailPanelVisible: true, selectedChangedFile: filePath })
  },

  markAutoShowForTurn(turnId) {
    set({ autoShowTriggeredForTurn: turnId })
  },

  setComposerPrefill(text) {
    set({ composerPrefill: text })
  },

  consumeComposerPrefill() {
    const text = get().composerPrefill
    set({ composerPrefill: null })
    return text
  },

  setPendingWelcomeTurn(payload) {
    const existing = get()._pendingWelcomeTimer
    if (existing != null) {
      clearTimeout(existing)
    }

    if (payload == null) {
      set({ pendingWelcomeTurn: null, _pendingWelcomeTimer: null })
      return
    }

    const timer = setTimeout(() => {
      const current = get().pendingWelcomeTurn
      if (current != null) {
        console.warn('pendingWelcomeTurn timed out, clearing')
        set({ pendingWelcomeTurn: null, _pendingWelcomeTimer: null })
      }
    }, PENDING_WELCOME_TIMEOUT_MS)

    set({
      pendingWelcomeTurn: { ...payload, createdAt: Date.now() },
      _pendingWelcomeTimer: timer
    })
  },

  consumePendingWelcomeTurnIfMatch(threadId) {
    const p = get().pendingWelcomeTurn
    if (p && p.threadId === threadId) {
      // Clear the timeout timer
      const timer = get()._pendingWelcomeTimer
      if (timer != null) {
        clearTimeout(timer)
      }
      set({ pendingWelcomeTurn: null, _pendingWelcomeTimer: null })
      const { text, images, mode, model } = p
      return {
        text,
        ...(images !== undefined ? { images } : {}),
        ...(mode !== undefined ? { mode } : {}),
        ...(model !== undefined ? { model } : {})
      }
    }
    return null
  },

  cancelPendingWelcomeTurnForThread(threadId) {
    const p = get().pendingWelcomeTurn
    if (p?.threadId === threadId) {
      const timer = get()._pendingWelcomeTimer
      if (timer != null) {
        clearTimeout(timer)
      }
      set({ pendingWelcomeTurn: null, _pendingWelcomeTimer: null })
    }
  },

  // Internal state for timeout timer (not exposed in UIState interface)
  _pendingWelcomeTimer: null
}))

export { SIDEBAR_DEFAULT_WIDTH, SIDEBAR_MIN_WIDTH, SIDEBAR_COLLAPSED_WIDTH, DETAIL_DEFAULT_WIDTH, DETAIL_MIN_WIDTH }
