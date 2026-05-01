// @vitest-environment jsdom
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ChangesTab } from '../components/detail/ChangesTab'
import { DiffViewer } from '../components/detail/DiffViewer'
import { useConversationStore } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import type { FileDiff } from '../types/toolCall'

const cs = () => useConversationStore.getState()
const ui = () => useUIStore.getState()

function makeDiff(overrides: Partial<FileDiff> = {}): FileDiff {
  return {
    filePath: 'src/a.ts',
    turnId: 'turn-1',
    turnIds: ['turn-1'],
    additions: 1,
    deletions: 1,
    diffHunks: [
      {
        oldStart: 3,
        oldLines: 2,
        newStart: 3,
        newLines: 2,
        lines: [
          { type: 'context', content: 'shared line' },
          { type: 'remove', content: 'old line' },
          { type: 'add', content: 'new line' }
        ]
      }
    ],
    status: 'written',
    isNewFile: false,
    ...overrides
  }
}

function Harness({ workspacePath = 'F:\\work' }: { workspacePath?: string }): JSX.Element {
  return (
    <LocaleProvider>
      <ChangesTab workspacePath={workspacePath} />
    </LocaleProvider>
  )
}

beforeEach(() => {
  cs().reset()
  useThreadStore.setState({ activeThreadId: 'thread-1' })
  useUIStore.setState({
    selectedChangedFile: null,
    changesDiffModeByThread: {},
    activeDetailTab: { kind: 'system', id: 'changes' },
    detailPanelVisible: true,
    detailPanelPreferredVisible: true
  })
  Object.defineProperty(window, 'api', {
    configurable: true,
    value: {
      settings: {
        get: async () => ({ locale: 'en' })
      },
      shell: {
        launchEditor: vi.fn(async () => {}),
        showItemInFolder: vi.fn(async () => {})
      }
    }
  })
})

describe('ChangesTab diff stream', () => {
  it('expands the first file by default and keeps the rest collapsed', async () => {
    cs().upsertChangedFile(makeDiff({ filePath: 'src/a.ts' }))
    cs().upsertChangedFile(makeDiff({
      filePath: 'src/b.ts',
      diffHunks: [
        {
          oldStart: 1,
          oldLines: 1,
          newStart: 1,
          newLines: 1,
          lines: [
            { type: 'remove', content: 'second old' },
            { type: 'add', content: 'second new' }
          ]
        }
      ]
    }))

    render(<Harness />)

    await waitFor(() => {
      expect(screen.getByText('old line')).toBeInTheDocument()
    })
    expect(screen.queryByText('second old')).toBeNull()
  })

  it('toggles a file section by clicking its summary row', async () => {
    cs().upsertChangedFile(makeDiff({ filePath: 'src/a.ts' }))

    render(<Harness />)

    await waitFor(() => {
      expect(screen.getByText('old line')).toBeInTheDocument()
    })

    fireEvent.click(screen.getByText('src/a.ts'))

    await waitFor(() => {
      expect(screen.queryByText('old line')).toBeNull()
    })
  })

  it('stores split diff mode per thread', async () => {
    cs().upsertChangedFile(makeDiff({ filePath: 'src/a.ts' }))

    render(<Harness />)

    fireEvent.click(screen.getByLabelText('Show split diff'))

    expect(ui().getChangesDiffMode('thread-1')).toBe('split')

    act(() => {
      useThreadStore.setState({ activeThreadId: 'thread-2' })
    })

    await waitFor(() => {
      expect(ui().getChangesDiffMode('thread-2')).toBe('inline')
    })
  })

  it('opens the changed file parent directory from the hover action', async () => {
    cs().upsertChangedFile(makeDiff({ filePath: 'src/a.ts' }))

    render(<Harness workspacePath={'F:\\work'} />)

    fireEvent.click(screen.getByLabelText('Open containing folder'))

    await waitFor(() => {
      expect(window.api.shell.launchEditor).toHaveBeenCalledWith('explorer', 'F:\\work\\src')
    })
  })
})

describe('DiffViewer split mode', () => {
  it('renders paired remove/add rows and unchanged dividers', () => {
    render(
      <LocaleProvider>
        <DiffViewer diff={makeDiff()} workspacePath="F:\\work" mode="split" />
      </LocaleProvider>
    )

    expect(screen.getByTestId('split-diff-body')).toBeInTheDocument()
    expect(screen.getByText('2 unchanged lines')).toBeInTheDocument()
    expect(screen.getByText('old line')).toBeInTheDocument()
    expect(screen.getByText('new line')).toBeInTheDocument()
    expect(screen.getAllByText('shared line')).toHaveLength(2)
  })
})
