import { create } from 'zustand'
import type { ComposerFileAttachment, ImageAttachment, ThreadMode } from '../types/conversation'
import type { ComposerDraftSegment } from '../types/composerDraft'
import { useThreadStore } from './threadStore'

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

export interface WelcomeDraft {
  text: string
  segments?: ComposerDraftSegment[]
  images: ImageAttachment[]
  files?: ComposerFileAttachment[]
  mode: ThreadMode
  model: string
  updatedAt: number
}

export interface UIState {
  /** Which primary view fills the center column (conversation panel slot). */
  activeMainView: ActiveMainView
  /** Active tab inside Automations view (spec §21.1). */
  automationsTab: AutomationsTab
  /** User preference for whether the sidebar is collapsed when width allows it. */
  sidebarPreferredCollapsed: boolean
  sidebarCollapsed: boolean
  sidebarWidth: number
  /** User preference for whether the detail panel is visible when width allows it. */
  detailPanelPreferredVisible: boolean
  detailPanelVisible: boolean
  detailPanelWidth: number
  /** Current responsive layout classification used to constrain panel visibility. */
  responsiveLayout: 'full' | 'no-detail' | 'collapsed'
  activeDetailTab: DetailPanelTab
  /** Currently selected file path in the Changes tab */
  selectedChangedFile: string | null
  /**
   * Tracks the turn ID for which the detail panel was auto-shown.
   * Prevents re-triggering after the user manually hides the panel.
   */
  autoShowTriggeredForTurn: string | null
  /**
   * Tracks the streaming CreatePlan item ID for which the Plan tab auto-switch
   * has already been triggered.
   */
  autoShowPlanForItem: string | null
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
    files?: ComposerFileAttachment[]
    /** Agent/plan chosen on Welcome before thread exists; applied after thread/read. */
    mode?: ThreadMode
    /** Model chosen on Welcome before thread exists; applied after thread/read. */
    model?: string
    createdAt: number
  } | null
  /** Unsent draft on ConversationWelcome, preserved across thread navigation. */
  welcomeDraft: WelcomeDraft | null
  /** Per-turn dismissal marker for the plan approval composer. */
  planApprovalDismissed: Record<string, boolean>
}

interface UIStore extends UIState {
  setActiveMainView(view: ActiveMainView): void
  /** Deselect current thread and open Welcome composer in conversation view. */
  goToNewChat(): void
  setAutomationsTab(tab: AutomationsTab): void
  toggleSidebar(): void
  setSidebarCollapsed(collapsed: boolean): void
  setSidebarWidth(width: number): void
  toggleDetailPanel(): void
  setDetailPanelVisible(visible: boolean): void
  setResponsiveLayout(layout: 'full' | 'no-detail' | 'collapsed'): void
  setDetailPanelWidth(width: number): void
  setActiveDetailTab(tab: DetailPanelTab): void
  selectChangedFile(filePath: string | null): void
  /** Open detail panel, switch to Changes tab, select the given file */
  showChangesForFile(filePath: string): void
  /** Mark auto-show as triggered for a given turn (prevents re-trigger) */
  markAutoShowForTurn(turnId: string): void
  /** Mark plan auto-switch as triggered for a given CreatePlan item. */
  markAutoShowPlanForItem(itemId: string): void
  /** Set text to be picked up by InputComposer on its next mount. */
  setComposerPrefill(text: string): void
  /** Read and clear the prefill text atomically. */
  consumeComposerPrefill(): string | null
  /** Queue first turn for a thread created from the welcome composer. */
  setPendingWelcomeTurn(
    payload: {
      threadId: string
      text: string
      images?: ImageAttachment[]
      files?: ComposerFileAttachment[]
      mode?: ThreadMode
      model?: string
    } | null
  ): void
  /** If pending matches threadId, return payload and clear; otherwise return null. */
  consumePendingWelcomeTurnIfMatch(
    threadId: string
  ): {
    text: string
    images?: ImageAttachment[]
    files?: ComposerFileAttachment[]
    mode?: ThreadMode
    model?: string
  } | null
  /** Clear pending welcome turn when it targets the given thread (e.g. thread/read failed). */
  cancelPendingWelcomeTurnForThread(threadId: string): void
  setWelcomeDraft(draft: Omit<WelcomeDraft, 'updatedAt'> | null): void
  clearWelcomeDraft(): void
  dismissPlanApproval(turnId: string): void
  resetPlanApprovalDismissed(): void
}

/** Internal state not exposed in UIState but used for timeout management */
interface InternalState {
  _pendingWelcomeTimer: ReturnType<typeof setTimeout> | null
}

export function resolveResponsivePanels(
  layout: UIState['responsiveLayout'],
  sidebarPreferredCollapsed: boolean,
  detailPanelPreferredVisible: boolean
): Pick<UIState, 'sidebarCollapsed' | 'detailPanelVisible'> {
  switch (layout) {
    case 'collapsed':
      return {
        sidebarCollapsed: true,
        detailPanelVisible: false
      }
    case 'no-detail':
      return {
        sidebarCollapsed: sidebarPreferredCollapsed,
        detailPanelVisible: false
      }
    case 'full':
    default:
      return {
        sidebarCollapsed: sidebarPreferredCollapsed,
        detailPanelVisible: detailPanelPreferredVisible
      }
  }
}

export const useUIStore = create<UIStore & InternalState>((set, get) => ({
  activeMainView: 'conversation',
  automationsTab: 'tasks',
  sidebarPreferredCollapsed: false,
  sidebarCollapsed: false,
  sidebarWidth: SIDEBAR_DEFAULT_WIDTH,
  detailPanelPreferredVisible: true,
  detailPanelVisible: true,
  detailPanelWidth: DETAIL_DEFAULT_WIDTH,
  responsiveLayout: 'full',
  activeDetailTab: 'changes',
  selectedChangedFile: null,
  autoShowTriggeredForTurn: null,
  autoShowPlanForItem: null,
  composerPrefill: null,
  pendingWelcomeTurn: null,
  welcomeDraft: null,
  planApprovalDismissed: {},

  setActiveMainView(view) {
    set({ activeMainView: view })
  },

  goToNewChat() {
    useThreadStore.getState().setActiveThreadId(null)
    set({ activeMainView: 'conversation', planApprovalDismissed: {} })
  },

  setAutomationsTab(tab) {
    set({ automationsTab: tab })
  },

  toggleSidebar() {
    set((state) => {
      const sidebarPreferredCollapsed = !state.sidebarPreferredCollapsed
      return {
        sidebarPreferredCollapsed,
        ...resolveResponsivePanels(
          state.responsiveLayout,
          sidebarPreferredCollapsed,
          state.detailPanelPreferredVisible
        )
      }
    })
  },

  setSidebarCollapsed(collapsed: boolean) {
    set((state) => ({
      sidebarPreferredCollapsed: collapsed,
      ...resolveResponsivePanels(
        state.responsiveLayout,
        collapsed,
        state.detailPanelPreferredVisible
      )
    }))
  },

  setSidebarWidth(width: number) {
    const clamped = Math.max(SIDEBAR_MIN_WIDTH, width)
    set({ sidebarWidth: clamped })
  },

  toggleDetailPanel() {
    set((state) => {
      const detailPanelPreferredVisible = !state.detailPanelPreferredVisible
      return {
        detailPanelPreferredVisible,
        ...resolveResponsivePanels(
          state.responsiveLayout,
          state.sidebarPreferredCollapsed,
          detailPanelPreferredVisible
        )
      }
    })
  },

  setDetailPanelVisible(visible: boolean) {
    set((state) => ({
      detailPanelPreferredVisible: visible,
      ...resolveResponsivePanels(
        state.responsiveLayout,
        state.sidebarPreferredCollapsed,
        visible
      )
    }))
  },

  setResponsiveLayout(layout) {
    set((state) => ({
      responsiveLayout: layout,
      ...resolveResponsivePanels(
        layout,
        state.sidebarPreferredCollapsed,
        state.detailPanelPreferredVisible
      )
    }))
  },

  setDetailPanelWidth(width: number) {
    const clamped = Math.max(DETAIL_MIN_WIDTH, width)
    set({ detailPanelWidth: clamped })
  },

  setActiveDetailTab(tab: DetailPanelTab) {
    const state = get()
    const detailPanelPreferredVisible = true
    set({
      activeDetailTab: tab,
      detailPanelPreferredVisible,
      ...resolveResponsivePanels(
        state.responsiveLayout,
        state.sidebarPreferredCollapsed,
        detailPanelPreferredVisible
      )
    })
  },

  selectChangedFile(filePath) {
    set({ selectedChangedFile: filePath })
  },

  showChangesForFile(filePath) {
    const state = get()
    const detailPanelPreferredVisible = true
    set({
      activeDetailTab: 'changes',
      selectedChangedFile: filePath,
      detailPanelPreferredVisible,
      ...resolveResponsivePanels(
        state.responsiveLayout,
        state.sidebarPreferredCollapsed,
        detailPanelPreferredVisible
      )
    })
  },

  markAutoShowForTurn(turnId) {
    set({ autoShowTriggeredForTurn: turnId })
  },

  markAutoShowPlanForItem(itemId) {
    set({ autoShowPlanForItem: itemId })
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
      const { text, images, files, mode, model } = p
      return {
        text,
        ...(images !== undefined ? { images } : {}),
        ...(files !== undefined ? { files } : {}),
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

  setWelcomeDraft(draft) {
    if (draft == null) {
      set({ welcomeDraft: null })
      return
    }
    set({
      welcomeDraft: {
        ...draft,
        images: [...draft.images],
        files: draft.files ? [...draft.files] : [],
        segments: draft.segments ? [...draft.segments] : undefined,
        updatedAt: Date.now()
      }
    })
  },

  clearWelcomeDraft() {
    set({ welcomeDraft: null })
  },

  dismissPlanApproval(turnId) {
    if (!turnId) return
    set((state) => ({
      planApprovalDismissed: {
        ...state.planApprovalDismissed,
        [turnId]: true
      }
    }))
  },

  resetPlanApprovalDismissed() {
    set({ planApprovalDismissed: {} })
  },

  // Internal state for timeout timer (not exposed in UIState interface)
  _pendingWelcomeTimer: null
}))

export { SIDEBAR_DEFAULT_WIDTH, SIDEBAR_MIN_WIDTH, SIDEBAR_COLLAPSED_WIDTH, DETAIL_DEFAULT_WIDTH, DETAIL_MIN_WIDTH }
