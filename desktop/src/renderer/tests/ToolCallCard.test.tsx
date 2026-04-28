import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ToolCallCard } from '../components/conversation/ToolCallCard'
import { useConversationStore } from '../stores/conversationStore'
import { useViewerTabStore } from '../stores/viewerTabStore'
import type { ConversationItem } from '../types/conversation'
import type { FileDiff } from '../types/toolCall'

function renderWithLocale(node: JSX.Element): void {
  render(<LocaleProvider>{node}</LocaleProvider>)
}

function expectRunningGradientText(text: string | RegExp): HTMLElement {
  const label = screen.getByText(text)
  expect(label).toHaveClass('tool-running-gradient-text')
  return label
}

const collapseAnimationMs = 200

describe('ToolCallCard shell rendering', () => {
  beforeEach(() => {
    useConversationStore.getState().reset()
    useViewerTabStore.setState({
      byThread: new Map(),
      currentThreadId: null,
      currentWorkspacePath: null
    })
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: {
          get: async () => ({ locale: 'en' })
        }
      }
    })
  })

  it('keeps running Exec collapsed by default and reveals live output when expanded', () => {
    const item: ConversationItem = {
      id: 'tool-1',
      type: 'toolCall',
      status: 'started',
      toolName: 'Exec',
      toolCallId: 'exec-1',
      arguments: { command: 'npm test' },
      aggregatedOutput: 'line 1\nline 2\n',
      executionStatus: 'inProgress',
      createdAt: new Date().toISOString()
    }

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    expect(screen.queryByText('line 1')).toBeNull()
    expectRunningGradientText(/Ran npm test/)

    fireEvent.click(screen.getByRole('button'))

    expect(screen.getByText((content) => content.includes('line 1'))).toBeInTheDocument()
  })

  it('renders ANSI shell output without raw escape markers', () => {
    const item: ConversationItem = {
      id: 'tool-ansi-shell',
      type: 'toolCall',
      status: 'completed',
      toolName: 'Exec',
      toolCallId: 'exec-ansi-1',
      arguments: { command: 'pnpm test' },
      aggregatedOutput: '\u001b[1;46m RUN \u001b[0m\u001b[36mv3.2.4\u001b[0m',
      result: '\u001b[1;46m RUN \u001b[0m\u001b[36mv3.2.4\u001b[0m',
      success: true,
      createdAt: new Date().toISOString()
    }

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)
    fireEvent.click(screen.getByRole('button'))

    const pre = document.querySelector('pre')
    expect(pre?.textContent).toContain(' RUN v3.2.4')
    expect(pre?.textContent).not.toContain('\u001b')
  })

  it('renders streaming InlineDiffView for running WriteFile tool calls', () => {
    const item: ConversationItem = {
      id: 'tool-write-streaming',
      type: 'toolCall',
      status: 'started',
      toolName: 'WriteFile',
      toolCallId: 'write-streaming-1',
      createdAt: new Date().toISOString()
    }
    const streamingDiff: FileDiff = {
      filePath: 'src/live.ts',
      turnId: 'turn-1',
      turnIds: ['turn-1'],
      additions: 1,
      deletions: 0,
      diffHunks: [
        {
          oldStart: 0,
          oldLines: 0,
          newStart: 1,
          newLines: 1,
          lines: [{ type: 'add', content: 'const live = true' }]
        }
      ],
      status: 'written',
      isNewFile: true,
      originalContent: '',
      currentContent: 'const live = true'
    }
    useConversationStore.setState({
      streamingItemDiffs: new Map([[item.id, streamingDiff]])
    })

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)
    fireEvent.click(screen.getByRole('button'))

    const filename = screen.getByText('live.ts')
    expect(filename).toBeInTheDocument()
    expect(filename).toHaveAttribute('title', 'src/live.ts')
    expect(screen.getByText(/Created live\.ts \+1/)).toBeInTheDocument()
    expect(screen.queryByText('src/live.ts')).toBeNull()
    expect(screen.queryByText('streaming')).toBeNull()
    expect(screen.queryByText('Waiting for content...')).toBeNull()
  })

  it('renders completed file diffs embedded with compact filename and stats', () => {
    const item: ConversationItem = {
      id: 'tool-edit-completed',
      type: 'toolCall',
      status: 'completed',
      toolName: 'EditFile',
      toolCallId: 'edit-completed-1',
      arguments: { path: 'src/Target.cs', oldText: 'old', newText: 'new' },
      result: 'Successfully edited src/Target.cs',
      success: true,
      createdAt: new Date().toISOString()
    }
    const diff: FileDiff = {
      filePath: 'src/Target.cs',
      turnId: 'turn-1',
      turnIds: ['turn-1'],
      additions: 1,
      deletions: 1,
      diffHunks: [
        {
          oldStart: 1,
          oldLines: 1,
          newStart: 1,
          newLines: 1,
          lines: [
            { type: 'remove', content: 'old' },
            { type: 'add', content: 'new' }
          ]
        }
      ],
      status: 'written',
      isNewFile: false,
      originalContent: 'old',
      currentContent: 'new'
    }
    useConversationStore.setState({
      itemDiffs: new Map([[item.id, diff]])
    })

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    fireEvent.click(screen.getByRole('button', { name: /Edited Target\.cs \+1 -1/ }))

    expect(screen.getByTestId('tool-expanded-content')).toHaveStyle({ padding: '0px' })
    expect(screen.getByTestId('inline-diff-view').style.borderStyle).toBe('none')
    const filename = screen.getByText('Target.cs')
    expect(filename).toHaveAttribute('title', 'src/Target.cs')
    expect(screen.queryByText('src/Target.cs')).toBeNull()
  })

  it('keeps showing the running timer for Exec after the toolCall item is completed but command execution is still in progress', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-04-13T10:00:01.500Z'))

    const item: ConversationItem = {
      id: 'tool-2',
      type: 'toolCall',
      status: 'completed',
      toolName: 'Exec',
      toolCallId: 'exec-2',
      arguments: { command: 'ping -n 10 8.8.8.8' },
      executionStatus: 'inProgress',
      createdAt: '2026-04-13T10:00:00.000Z'
    }

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    expect(screen.getByText('1.5s')).toBeInTheDocument()
    expectRunningGradientText(/Ran ping -n 10 8\.8\.8\.8/)
    expect(screen.queryByText('Calling')).not.toBeInTheDocument()

    vi.useRealTimers()
  })

  it('shows running timer when toolCall is completed but toolResult has not merged yet (no executionStatus)', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-04-13T10:00:02.000Z'))

    const item: ConversationItem = {
      id: 'tool-3',
      type: 'toolCall',
      status: 'completed',
      toolName: 'Exec',
      toolCallId: 'exec-3',
      arguments: { command: 'slow-cmd' },
      createdAt: '2026-04-13T10:00:00.000Z'
    }

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    expect(screen.getByText('2.0s')).toBeInTheDocument()

    vi.useRealTimers()
  })

  it('shows Ran + command while shell is running (same style as completed, not Calling Exec)', () => {
    const item: ConversationItem = {
      id: 'tool-ran',
      type: 'toolCall',
      status: 'started',
      toolName: 'Exec',
      toolCallId: 'exec-ran',
      arguments: { command: 'echo hello' },
      executionStatus: 'inProgress',
      createdAt: new Date().toISOString()
    }

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    expectRunningGradientText(/Ran echo hello/)
    expect(screen.queryByText('Calling')).not.toBeInTheDocument()
  })

  it('treats legacy executionStatus started as running (mis-mapped wire lifecycle)', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-04-13T10:00:01.500Z'))

    const item = {
      id: 'tool-legacy',
      type: 'toolCall' as const,
      status: 'completed' as const,
      toolName: 'Exec',
      toolCallId: 'exec-legacy',
      arguments: { command: 'ping' },
      executionStatus: 'started' as ConversationItem['executionStatus'],
      createdAt: '2026-04-13T10:00:00.000Z'
    } as ConversationItem

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    expect(screen.getByText('1.5s')).toBeInTheDocument()

    vi.useRealTimers()
  })

  it('auto-expands eligible running tools after threshold and auto-collapses after the collapse animation completes', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-04-13T10:00:00.000Z'))
    const runningItem: ConversationItem = {
      id: 'tool-auto-open',
      type: 'toolCall',
      status: 'started',
      toolName: 'Exec',
      toolCallId: 'exec-auto-open-1',
      arguments: { command: 'sleep 10' },
      createdAt: '2026-04-13T10:00:00.000Z'
    }
    const completedItem: ConversationItem = {
      ...runningItem,
      status: 'completed',
      result: 'ok',
      success: true,
      duration: 820
    }

    const { rerender } = render(
      <LocaleProvider>
        <ToolCallCard item={runningItem} turnId="turn-1" />
      </LocaleProvider>
    )

    expect(screen.queryByText('Running...')).toBeNull()

    act(() => {
      vi.advanceTimersByTime(450)
    })

    expect(screen.getByText('Waiting for output...')).toBeInTheDocument()

    rerender(
      <LocaleProvider>
        <ToolCallCard item={completedItem} turnId="turn-1" />
      </LocaleProvider>
    )

    expect(screen.getByText('ok')).toBeInTheDocument()

    act(() => {
      vi.advanceTimersByTime(collapseAnimationMs)
    })

    expect(screen.queryByText('ok')).toBeNull()
    vi.useRealTimers()
  })

  it('does not auto-expand non-eligible tools while running', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-04-13T10:00:00.000Z'))
    const runningItem: ConversationItem = {
      id: 'tool-no-auto-open',
      type: 'toolCall',
      status: 'started',
      toolName: 'WebFetch',
      toolCallId: 'webfetch-2',
      arguments: { url: 'https://dotcraft.ai' },
      createdAt: '2026-04-13T10:00:00.000Z'
    }

    renderWithLocale(<ToolCallCard item={runningItem} turnId="turn-1" />)

    expect(screen.getByText('0.0s')).toBeInTheDocument()
    expectRunningGradientText('Fetched https://dotcraft.ai')

    act(() => {
      vi.advanceTimersByTime(450)
    })

    expect(screen.queryByText('Running...')).toBeNull()
    vi.useRealTimers()
  })

  it('keeps user-selected expansion state after running completes', () => {
    vi.useFakeTimers()
    const runningItem: ConversationItem = {
      id: 'tool-user-open',
      type: 'toolCall',
      status: 'started',
      toolName: 'WebSearch',
      toolCallId: 'websearch-1',
      arguments: { query: 'dotcraft' },
      createdAt: '2026-04-13T10:00:00.000Z'
    }
    const completedItem: ConversationItem = {
      ...runningItem,
      status: 'completed',
      result: 'done',
      success: true,
      duration: 500
    }

    const { rerender } = render(
      <LocaleProvider>
        <ToolCallCard item={runningItem} turnId="turn-1" />
      </LocaleProvider>
    )

    fireEvent.click(screen.getByRole('button'))
    expect(screen.getByText('Running...')).toBeInTheDocument()

    rerender(
      <LocaleProvider>
        <ToolCallCard item={completedItem} turnId="turn-1" />
      </LocaleProvider>
    )

    expect(screen.getAllByText('Searched "dotcraft"').length).toBeGreaterThan(0)
    vi.useRealTimers()
  })

  it('renders completed WebSearch results as a clickable table that opens the internal browser', () => {
    const item: ConversationItem = {
      id: 'tool-web-search-table',
      type: 'toolCall',
      status: 'completed',
      toolName: 'WebSearch',
      toolCallId: 'websearch-table-1',
      arguments: { query: 'dotcraft docs' },
      result: JSON.stringify({
        query: 'dotcraft docs',
        provider: 'exa',
        results: [
          { title: 'DotCraft Docs', url: 'https://docs.dotcraft.ai/start', snippet: 'Guide' },
          { title: 'GitHub', url: 'https://github.com/DotHarness/dotcraft' }
        ]
      }),
      success: true,
      createdAt: new Date().toISOString()
    }

    useConversationStore.setState({ workspacePath: 'F:\\dotcraft' })
    useViewerTabStore.getState().onThreadSwitched('thread-1')
    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    fireEvent.click(screen.getByRole('button', { name: /Searched "dotcraft docs"/ }))

    expect(screen.getByRole('columnheader', { name: 'Title' })).toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: 'Link' })).toBeInTheDocument()
    expect(screen.queryByText('Web search')).toBeNull()
    expect(screen.getAllByText('Searched "dotcraft docs"')).toHaveLength(1)
    expect(screen.getByTestId('tool-expanded-content')).toHaveStyle({ padding: '0px' })
    expect(screen.getByRole('button', { name: 'DotCraft Docs' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'docs.dotcraft.ai' })).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'DotCraft Docs' }))

    const threadState = useViewerTabStore.getState().getThreadState('thread-1')
    expect(threadState.tabs).toHaveLength(1)
    expect(threadState.tabs[0]).toMatchObject({
      kind: 'browser',
      currentUrl: 'https://docs.dotcraft.ai/start'
    })
    expect(threadState.activeTabId).toBe(threadState.tabs[0]?.id)
  })

  it('renders completed WebFetch as a non-expandable title row', () => {
    const item: ConversationItem = {
      id: 'tool-web-fetch-summary',
      type: 'toolCall',
      status: 'completed',
      toolName: 'WebFetch',
      toolCallId: 'webfetch-summary-1',
      arguments: { url: 'https://dotcraft.ai' },
      result: JSON.stringify({
        status: 200,
        length: 12345,
        extractor: 'readability',
        truncated: true
      }),
      success: true,
      createdAt: new Date().toISOString()
    }

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)
    const row = screen.getByRole('button', { name: /Fetched https:\/\/dotcraft\.ai/ })

    expect(screen.queryByText('▼')).toBeNull()
    fireEvent.click(row)

    expect(screen.queryByText('200 · 12,345 chars · readability · truncated')).toBeNull()
    expect(screen.queryByTestId('tool-expanded-content')).toBeNull()
  })

  it('keeps content mounted during manual collapse animation before removing it', () => {
    vi.useFakeTimers()
    const completedItem: ConversationItem = {
      id: 'tool-manual-collapse',
      type: 'toolCall',
      status: 'completed',
      toolName: 'Exec',
      toolCallId: 'exec-manual-collapse-1',
      arguments: { command: 'echo hello' },
      result: 'hello',
      success: true,
      duration: 120,
      createdAt: '2026-04-13T10:00:00.000Z'
    }

    renderWithLocale(<ToolCallCard item={completedItem} turnId="turn-1" />)

    fireEvent.click(screen.getByRole('button'))
    expect(screen.getByText('hello')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button'))
    expect(screen.getByText('hello')).toBeInTheDocument()

    act(() => {
      vi.advanceTimersByTime(collapseAnimationMs)
    })

    expect(screen.queryByText('hello')).toBeNull()
    vi.useRealTimers()
  })

  it('hides success glyph and duration for completed rows, and only shows chevron on hover', () => {
    const item: ConversationItem = {
      id: 'tool-style-completed',
      type: 'toolCall',
      status: 'completed',
      toolName: 'ReadFile',
      toolCallId: 'call-style-1',
      arguments: { path: 'src/main.ts' },
      result: 'ok',
      success: true,
      duration: 350,
      createdAt: '2026-04-13T10:00:00.000Z'
    }

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    expect(screen.getByText('Read main.ts')).toBeInTheDocument()
    expect(document.querySelector('.tool-running-gradient-text')).toBeNull()
    expect(screen.queryByText('✓')).toBeNull()
    expect(screen.queryByText('350ms')).toBeNull()

    const button = screen.getByRole('button')
    const wrapper = button.parentElement as HTMLElement
    const chevron = screen.getByText('▼')
    expect(chevron).toHaveStyle({ opacity: '0' })

    fireEvent.mouseEnter(wrapper)
    expect(chevron).toHaveStyle({ opacity: '1' })
  })

})

describe('ToolCallCard todo rendering safety', () => {
  beforeEach(() => {
    useConversationStore.getState().reset()
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: {
          get: async () => ({ locale: 'en' })
        }
      }
    })
  })

  it('renders TodoWrite without crashing when plan is null', () => {
    const item: ConversationItem = {
      id: 'todo-write-1',
      type: 'toolCall',
      status: 'completed',
      toolName: 'TodoWrite',
      toolCallId: 'todo-write-call-1',
      arguments: {
        merge: false,
        todos: [{ id: 't1', content: 'Next step is ABCDEFGHIJKLMNOPQRSTUVWXYZ', status: 'pending' }]
      },
      result: 'Created task list with 1 item(s).',
      success: true,
      createdAt: new Date().toISOString()
    }

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    expect(screen.getByText(/Create to-do/)).toBeInTheDocument()
  })

  it('renders UpdateTodos fallback label when plan is unavailable', () => {
    const item: ConversationItem = {
      id: 'todo-update-1',
      type: 'toolCall',
      status: 'completed',
      toolName: 'UpdateTodos',
      toolCallId: 'todo-update-call-1',
      arguments: {
        updates: [{ id: 't1', status: 'completed' }]
      },
      result: 'Updated plan tasks',
      success: true,
      createdAt: new Date().toISOString()
    }

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    expect(screen.getByText('Updated to-do')).toBeInTheDocument()
  })

  it('does not throw when plan todo ids are non-string values', () => {
    useConversationStore.getState().onPlanUpdated({
      title: 'Plan',
      overview: '',
      todos: [{ id: 123 as unknown as string, content: 'Bad data shape', status: 'pending' as const }]
    })

    const item: ConversationItem = {
      id: 'todo-update-2',
      type: 'toolCall',
      status: 'completed',
      toolName: 'UpdateTodos',
      toolCallId: 'todo-update-call-2',
      arguments: {
        updates: [{ id: '123', status: 'in_progress' }]
      },
      result: 'Updated plan tasks',
      success: true,
      createdAt: new Date().toISOString()
    }

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    expect(screen.getByText('Started to-do')).toBeInTheDocument()
  })
})

describe('ToolCallCard CreatePlan rendering', () => {
  beforeEach(() => {
    useConversationStore.getState().reset()
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: {
          get: async () => ({ locale: 'en' })
        }
      }
    })
  })

  it('renders completed CreatePlan as preview card and expands on demand', () => {
    const item: ConversationItem = {
      id: 'create-plan-1',
      type: 'toolCall',
      status: 'completed',
      toolName: 'CreatePlan',
      toolCallId: 'create-plan-call-1',
      arguments: {
        title: 'Release Plan',
        overview: 'Ship the feature in two phases.',
        plan: '# Final heading\n\n- add tests\n- run smoke checks',
        todos: [
          { id: 'tests', content: 'Add tests', status: 'in_progress' },
          { id: 'smoke', content: 'Run smoke checks', status: 'pending' }
        ]
      },
      success: true,
      createdAt: new Date().toISOString()
    }

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    expect(screen.getByText('Plan')).toBeInTheDocument()
    expect(screen.getByText('Release Plan')).toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: /Expand plan/ }).length).toBeGreaterThan(0)
    fireEvent.click(screen.getAllByRole('button', { name: /Expand plan/ })[0])

    expect(screen.getByText('Final heading')).toBeInTheDocument()
    expect(screen.getByText('add tests')).toBeInTheDocument()
    expect(screen.getByText('Add tests')).toBeInTheDocument()
    expect(screen.getByText('Run smoke checks')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Collapse/ })).toBeInTheDocument()
  })

  it('keeps preview mode from streaming to completed until user expands', () => {
    const startedItem: ConversationItem = {
      id: 'create-plan-2',
      type: 'toolCall',
      status: 'started',
      toolName: 'CreatePlan',
      toolCallId: 'create-plan-call-2',
      argumentsPreview: '{"title":"Migration","overview":"Rolling update","plan":"# Draft heading\\n\\n- step 1"}',
      createdAt: new Date().toISOString()
    }

    const completedItem: ConversationItem = {
      ...startedItem,
      status: 'completed',
      arguments: {
        title: 'Migration',
        overview: 'Rolling update',
        plan: '# Done plan\n\nMove traffic in batches.',
        todos: [{ id: 'rollout', content: 'Roll out by cluster', status: 'completed' }]
      },
      success: true,
      result: 'Plan created.'
    }

    const { rerender } = render(
      <LocaleProvider>
        <ToolCallCard item={startedItem} turnId="turn-1" />
      </LocaleProvider>
    )

    expect(screen.getByText('Migration')).toBeInTheDocument()
    expect(screen.getByText('Draft heading')).toBeInTheDocument()

    rerender(
      <LocaleProvider>
        <ToolCallCard item={completedItem} turnId="turn-1" />
      </LocaleProvider>
    )

    expect(screen.getByText('Migration')).toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: /Expand plan/ }).length).toBeGreaterThan(0)
    fireEvent.click(screen.getAllByRole('button', { name: /Expand plan/ })[0])
    expect(screen.getByText('Done plan')).toBeInTheDocument()
    expect(screen.getByText('Roll out by cluster')).toBeInTheDocument()
  })
})
