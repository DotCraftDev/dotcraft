import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ToolCallCard } from '../components/conversation/ToolCallCard'
import { useConversationStore } from '../stores/conversationStore'
import type { ConversationItem } from '../types/conversation'

function renderWithLocale(node: JSX.Element): void {
  render(<LocaleProvider>{node}</LocaleProvider>)
}

describe('ToolCallCard shell rendering', () => {
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

    fireEvent.click(screen.getByRole('button'))

    expect(screen.getByText((content) => content.includes('line 1'))).toBeInTheDocument()
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
    expect(screen.getByText(/Ran ping -n 10 8\.8\.8\.8/)).toBeInTheDocument()
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

    expect(screen.getByText(/Ran echo hello/)).toBeInTheDocument()
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

  it('auto-expands running tools after threshold and auto-collapses when completed', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-04-13T10:00:00.000Z'))
    const runningItem: ConversationItem = {
      id: 'tool-auto-open',
      type: 'toolCall',
      status: 'started',
      toolName: 'WebFetch',
      toolCallId: 'webfetch-1',
      arguments: { url: 'https://dotcraft.ai' },
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

    expect(screen.getByText('Running...')).toBeInTheDocument()

    rerender(
      <LocaleProvider>
        <ToolCallCard item={completedItem} turnId="turn-1" />
      </LocaleProvider>
    )

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
