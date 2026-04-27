import { beforeEach, describe, expect, it } from 'vitest'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'

describe('uiStore defaults', () => {
  it('starts with the detail panel hidden', () => {
    expect(useUIStore.getState().detailPanelPreferredVisible).toBe(false)
    expect(useUIStore.getState().detailPanelVisible).toBe(false)
  })
})

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
      selectedChangedFile: null,
      autoShowReasons: new Set<string>()
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

  it('records one-shot auto-show reasons', () => {
    useUIStore.getState().setDetailPanelVisible(false)
    const first = useUIStore.getState().maybeAutoShowForReason('link:thread-1:item-2')
    const second = useUIStore.getState().maybeAutoShowForReason('link:thread-1:item-2')
    expect(first).toBe(true)
    expect(second).toBe(false)
    expect(useUIStore.getState().detailPanelPreferredVisible).toBe(true)
  })

  it('clears one-shot auto-show reasons', () => {
    useUIStore.getState().maybeAutoShowForReason('plan:auto')
    expect(useUIStore.getState().autoShowReasons.size).toBe(1)
    useUIStore.getState().resetAutoShowReasons()
    expect(useUIStore.getState().autoShowReasons.size).toBe(0)
  })

  it('can switch system detail tabs without revealing the panel', () => {
    useUIStore.getState().setDetailPanelVisible(false)

    useUIStore.getState().setActiveDetailTab('plan', { reveal: false })

    expect(useUIStore.getState().activeDetailTab).toEqual({ kind: 'system', id: 'plan' })
    expect(useUIStore.getState().lastActiveSystemTab).toBe('plan')
    expect(useUIStore.getState().detailPanelPreferredVisible).toBe(false)
    expect(useUIStore.getState().detailPanelVisible).toBe(false)
  })

  it('can switch viewer tabs without revealing the panel', () => {
    useUIStore.getState().setDetailPanelVisible(false)

    useUIStore.getState().setActiveViewerTab('vtab-hidden', { reveal: false })

    expect(useUIStore.getState().activeDetailTab).toEqual({ kind: 'viewer', id: 'vtab-hidden' })
    expect(useUIStore.getState().detailPanelPreferredVisible).toBe(false)
    expect(useUIStore.getState().detailPanelVisible).toBe(false)
  })

  it('reveals the panel by default when switching detail tabs explicitly', () => {
    useUIStore.getState().setDetailPanelVisible(false)

    useUIStore.getState().setActiveDetailTab('plan')

    expect(useUIStore.getState().detailPanelPreferredVisible).toBe(true)
    expect(useUIStore.getState().detailPanelVisible).toBe(true)
  })
})
