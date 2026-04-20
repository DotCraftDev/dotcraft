import { beforeEach, describe, expect, it } from 'vitest'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'

describe('uiStore goToNewChat', () => {
  beforeEach(() => {
    useThreadStore.getState().reset()
    useUIStore.setState({
      activeMainView: 'settings',
      welcomeDraft: null,
      sidebarPreferredCollapsed: false,
      sidebarCollapsed: false,
      detailPanelPreferredVisible: true,
      detailPanelVisible: true,
      responsiveLayout: 'full'
    })
  })

  it('clears active thread and routes to conversation view', () => {
    useThreadStore.getState().setActiveThreadId('thread-123')

    useUIStore.getState().goToNewChat()

    expect(useThreadStore.getState().activeThreadId).toBeNull()
    expect(useUIStore.getState().activeMainView).toBe('conversation')
  })
})

describe('uiStore responsive panel preferences', () => {
  beforeEach(() => {
    useUIStore.setState({
      sidebarPreferredCollapsed: false,
      sidebarCollapsed: false,
      detailPanelPreferredVisible: true,
      detailPanelVisible: true,
      responsiveLayout: 'full',
      activeDetailTab: { kind: 'system', id: 'changes' },
      lastActiveSystemTab: 'changes',
      selectedChangedFile: null
    })
  })

  it('preserves a manually hidden detail panel when layout stays full', () => {
    useUIStore.getState().setDetailPanelVisible(false)

    expect(useUIStore.getState().detailPanelPreferredVisible).toBe(false)
    expect(useUIStore.getState().detailPanelVisible).toBe(false)

    useUIStore.getState().setResponsiveLayout('full')

    expect(useUIStore.getState().detailPanelVisible).toBe(false)
  })

  it('restores the preferred detail visibility after leaving a narrow breakpoint', () => {
    useUIStore.getState().setDetailPanelVisible(false)
    useUIStore.getState().setResponsiveLayout('collapsed')

    expect(useUIStore.getState().detailPanelVisible).toBe(false)

    useUIStore.getState().setResponsiveLayout('full')

    expect(useUIStore.getState().detailPanelPreferredVisible).toBe(false)
    expect(useUIStore.getState().detailPanelVisible).toBe(false)
  })

  it('restores the preferred sidebar expansion after leaving the collapsed breakpoint', () => {
    useUIStore.getState().setSidebarCollapsed(false)
    useUIStore.getState().setResponsiveLayout('collapsed')

    expect(useUIStore.getState().sidebarCollapsed).toBe(true)

    useUIStore.getState().setResponsiveLayout('full')

    expect(useUIStore.getState().sidebarPreferredCollapsed).toBe(false)
    expect(useUIStore.getState().sidebarCollapsed).toBe(false)
  })

  it('auto-opening the detail panel updates the stored preference', () => {
    useUIStore.getState().setDetailPanelVisible(false)
    useUIStore.getState().setResponsiveLayout('no-detail')

    useUIStore.getState().showChangesForFile('src/foo.ts')

    expect(useUIStore.getState().detailPanelPreferredVisible).toBe(true)
    expect(useUIStore.getState().detailPanelVisible).toBe(false)

    useUIStore.getState().setResponsiveLayout('full')

    expect(useUIStore.getState().detailPanelVisible).toBe(true)
    expect(useUIStore.getState().selectedChangedFile).toBe('src/foo.ts')
  })
})
