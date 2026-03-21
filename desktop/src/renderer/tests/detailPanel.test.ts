import { describe, it, expect, beforeEach } from 'vitest'
import { useConversationStore } from '../stores/conversationStore'
import { useUIStore } from '../stores/uiStore'
import type { FileDiff } from '../types/toolCall'

const cs = () => useConversationStore.getState()
const ui = () => useUIStore.getState()

function makeDiff(overrides: Partial<FileDiff> = {}): FileDiff {
  return {
    filePath: 'src/test.ts',
    turnId: 'turn-1',
    additions: 10,
    deletions: 2,
    diffHunks: [],
    status: 'written',
    isNewFile: false,
    ...overrides
  }
}

beforeEach(() => {
  cs().reset()
  // Reset UI store state manually
  useUIStore.setState({
    selectedChangedFile: null,
    autoShowTriggeredForTurn: null,
    activeDetailTab: 'changes',
    detailPanelVisible: true
  })
})

// ---------------------------------------------------------------------------
// Commit file filter
// ---------------------------------------------------------------------------

describe('commit file filter', () => {
  it('excludes reverted files from the commit list', () => {
    cs().upsertChangedFile(makeDiff({ filePath: 'src/a.ts', status: 'written' }))
    cs().upsertChangedFile(makeDiff({ filePath: 'src/b.ts', status: 'written' }))
    cs().upsertChangedFile(makeDiff({ filePath: 'src/c.ts', status: 'reverted' }))
    cs().upsertChangedFile(makeDiff({ filePath: 'src/d.ts', status: 'reverted' }))

    const allFiles = Array.from(cs().changedFiles.values())
    const writtenFiles = allFiles.filter((f) => f.status === 'written')

    expect(writtenFiles).toHaveLength(2)
    expect(writtenFiles.map((f) => f.filePath)).toEqual(
      expect.arrayContaining(['src/a.ts', 'src/b.ts'])
    )
    expect(writtenFiles.map((f) => f.filePath)).not.toContain('src/c.ts')
    expect(writtenFiles.map((f) => f.filePath)).not.toContain('src/d.ts')
  })

  it('shows 0 files when all are reverted', () => {
    cs().upsertChangedFile(makeDiff({ filePath: 'src/a.ts', status: 'reverted' }))
    const written = Array.from(cs().changedFiles.values()).filter((f) => f.status === 'written')
    expect(written).toHaveLength(0)
  })

  it('shows all files when none are reverted', () => {
    cs().upsertChangedFile(makeDiff({ filePath: 'src/a.ts' }))
    cs().upsertChangedFile(makeDiff({ filePath: 'src/b.ts' }))
    cs().upsertChangedFile(makeDiff({ filePath: 'src/c.ts' }))
    const written = Array.from(cs().changedFiles.values()).filter((f) => f.status === 'written')
    expect(written).toHaveLength(3)
  })
})

// ---------------------------------------------------------------------------
// Plan todo status mapping
// ---------------------------------------------------------------------------

describe('plan todo status icons', () => {
  const STATUS_ICON: Record<string, string> = {
    pending: '○',
    in_progress: '◉',
    completed: '✓',
    cancelled: '✗'
  }

  it('maps all four statuses to distinct icons', () => {
    const icons = Object.values(STATUS_ICON)
    const unique = new Set(icons)
    expect(unique.size).toBe(4)
  })

  it('pending maps to ○', () => {
    expect(STATUS_ICON.pending).toBe('○')
  })

  it('in_progress maps to ◉', () => {
    expect(STATUS_ICON.in_progress).toBe('◉')
  })

  it('completed maps to ✓', () => {
    expect(STATUS_ICON.completed).toBe('✓')
  })

  it('cancelled maps to ✗', () => {
    expect(STATUS_ICON.cancelled).toBe('✗')
  })
})

// ---------------------------------------------------------------------------
// Auto-show: uiStore.showChangesForFile
// ---------------------------------------------------------------------------

describe('showChangesForFile', () => {
  it('sets detail panel visible, switches to changes tab, selects file', () => {
    useUIStore.setState({ detailPanelVisible: false, activeDetailTab: 'plan', selectedChangedFile: null })

    ui().showChangesForFile('src/foo.ts')

    expect(ui().detailPanelVisible).toBe(true)
    expect(ui().activeDetailTab).toBe('changes')
    expect(ui().selectedChangedFile).toBe('src/foo.ts')
  })

  it('works when panel is already visible', () => {
    useUIStore.setState({ detailPanelVisible: true, activeDetailTab: 'terminal' })

    ui().showChangesForFile('src/bar.ts')

    expect(ui().activeDetailTab).toBe('changes')
    expect(ui().selectedChangedFile).toBe('src/bar.ts')
  })
})

// ---------------------------------------------------------------------------
// Auto-show: markAutoShowForTurn prevents re-trigger
// ---------------------------------------------------------------------------

describe('markAutoShowForTurn', () => {
  it('stores the turn id that triggered auto-show', () => {
    expect(ui().autoShowTriggeredForTurn).toBeNull()

    ui().markAutoShowForTurn('turn-abc')

    expect(ui().autoShowTriggeredForTurn).toBe('turn-abc')
  })

  it('allows override for a different turn', () => {
    ui().markAutoShowForTurn('turn-1')
    ui().markAutoShowForTurn('turn-2')
    expect(ui().autoShowTriggeredForTurn).toBe('turn-2')
  })
})

// ---------------------------------------------------------------------------
// plan/updated notification — store test
// ---------------------------------------------------------------------------

describe('onPlanUpdated via store', () => {
  it('setActiveDetailTab("plan") auto-shows the panel', () => {
    useUIStore.setState({ detailPanelVisible: false })

    ui().setActiveDetailTab('plan')

    expect(ui().detailPanelVisible).toBe(true)
    expect(ui().activeDetailTab).toBe('plan')
  })
})
