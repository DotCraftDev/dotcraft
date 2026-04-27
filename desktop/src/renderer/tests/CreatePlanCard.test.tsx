import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import type { ConversationItem } from '../types/conversation'
import { CreatePlanCard } from '../components/conversation/CreatePlanCard'

function renderWithLocale(node: JSX.Element): void {
  render(<LocaleProvider>{node}</LocaleProvider>)
}

describe('CreatePlanCard', () => {
  let writeTextMock: ReturnType<typeof vi.fn>

  beforeEach(() => {
    writeTextMock = vi.fn().mockResolvedValue(undefined)
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: {
          get: async () => ({ locale: 'en' })
        }
      }
    })
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: {
        writeText: writeTextMock
      }
    })
  })

  it('renders markdown preview while CreatePlan is streaming', () => {
    const item: ConversationItem = {
      id: 'plan-streaming',
      type: 'toolCall',
      status: 'started',
      toolName: 'CreatePlan',
      toolCallId: 'call-1',
      argumentsPreview: '{"title":"Streaming Plan","overview":"Live draft","plan":"# DraftTitle\\n\\n- item one"}',
      createdAt: new Date().toISOString()
    }

    renderWithLocale(<CreatePlanCard item={item} locale="en" />)

    const badge = screen.getByText('Planning')
    expect(badge).toHaveClass('tool-running-gradient-text')
    expect(screen.getByText('Streaming Plan')).toBeInTheDocument()
    expect(screen.getByText('DraftTitle')).toBeInTheDocument()
    expect(screen.getByText('item one')).toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: 'Expand plan' }).length).toBeGreaterThan(0)
  })

  it('expands full plan output and collapses back to preview', () => {
    const item: ConversationItem = {
      id: 'plan-complete',
      type: 'toolCall',
      status: 'completed',
      toolName: 'CreatePlan',
      toolCallId: 'call-2',
      arguments: {
        title: 'Ship Plan',
        overview: 'Two stages',
        plan: '# Heading\n\n- step a',
        todos: [{ id: 'a', content: 'Stage A', status: 'pending' }]
      },
      success: true,
      createdAt: new Date().toISOString()
    }

    renderWithLocale(<CreatePlanCard item={item} locale="en" />)

    const badge = screen.getByText('Plan')
    expect(badge).not.toHaveClass('tool-running-gradient-text')
    expect(screen.queryByText('Stage A')).toBeNull()
    fireEvent.click(screen.getAllByRole('button', { name: 'Expand plan' })[0])
    expect(screen.queryAllByRole('button', { name: 'Expand plan' }).length).toBe(0)
    expect(screen.getByText('Stage A')).toBeInTheDocument()
    expect(screen.queryByText('Overview')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Collapse plan' }))
    expect(screen.queryByText('Stage A')).toBeNull()
    expect(screen.getByText('Ship Plan')).toBeInTheDocument()
  })

  it('toggles using icon expand/collapse buttons', () => {
    const item: ConversationItem = {
      id: 'plan-toggle',
      type: 'toolCall',
      status: 'completed',
      toolName: 'CreatePlan',
      toolCallId: 'call-3',
      arguments: {
        title: 'Toggle Plan',
        overview: 'Preview first',
        plan: '# Toggle\n\n- step'
      },
      success: true,
      createdAt: new Date().toISOString()
    }

    renderWithLocale(<CreatePlanCard item={item} locale="en" />)

    fireEvent.click(screen.getAllByRole('button', { name: 'Expand plan' })[0])
    expect(screen.getByRole('button', { name: 'Collapse plan' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Collapse plan' }))
    expect(screen.getAllByRole('button', { name: 'Expand plan' }).length).toBeGreaterThan(0)
  })

  it('copies parsed content then falls back to generated markdown', async () => {
    const withContent: ConversationItem = {
      id: 'plan-copy-content',
      type: 'toolCall',
      status: 'completed',
      toolName: 'CreatePlan',
      toolCallId: 'call-4',
      arguments: {
        title: 'Copy Plan',
        plan: '# Body\n\n- one'
      },
      success: true,
      createdAt: new Date().toISOString()
    }

    const fallbackOnly: ConversationItem = {
      id: 'plan-copy-fallback',
      type: 'toolCall',
      status: 'completed',
      toolName: 'CreatePlan',
      toolCallId: 'call-5',
      arguments: {
        title: 'Fallback Title',
        overview: 'Fallback overview',
        todos: [{ id: 'todo-1', content: 'First task', status: 'pending' }]
      },
      success: true,
      createdAt: new Date().toISOString()
    }

    const { rerender } = render(
      <LocaleProvider>
        <CreatePlanCard item={withContent} locale="en" />
      </LocaleProvider>
    )

    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Copy plan' }))
      await Promise.resolve()
    })
    expect(writeTextMock).toHaveBeenCalledWith('# Body\n\n- one')

    rerender(
      <LocaleProvider>
        <CreatePlanCard item={fallbackOnly} locale="en" />
      </LocaleProvider>
    )

    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Copy plan' }))
      await Promise.resolve()
    })
    expect(writeTextMock).toHaveBeenLastCalledWith('# Fallback Title\n\nFallback overview\n\n- First task')
  })
})
