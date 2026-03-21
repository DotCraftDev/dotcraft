import { create } from 'zustand'

const SIDEBAR_DEFAULT_WIDTH = 240
const SIDEBAR_MIN_WIDTH = 200
const SIDEBAR_COLLAPSED_WIDTH = 48

const DETAIL_DEFAULT_WIDTH = 400
const DETAIL_MIN_WIDTH = 300

export type DetailPanelTab = 'changes' | 'plan' | 'terminal'

export interface UIState {
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
  /** Text to pre-fill into the InputComposer when it next mounts. */
  composerPrefill: string | null
}

interface UIStore extends UIState {
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
}

export const useUIStore = create<UIStore>((set, get) => ({
  sidebarCollapsed: false,
  sidebarWidth: SIDEBAR_DEFAULT_WIDTH,
  detailPanelVisible: true,
  detailPanelWidth: DETAIL_DEFAULT_WIDTH,
  activeDetailTab: 'changes',
  selectedChangedFile: null,
  autoShowTriggeredForTurn: null,
  composerPrefill: null,

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
  }
}))

export { SIDEBAR_DEFAULT_WIDTH, SIDEBAR_MIN_WIDTH, SIDEBAR_COLLAPSED_WIDTH, DETAIL_DEFAULT_WIDTH, DETAIL_MIN_WIDTH }
