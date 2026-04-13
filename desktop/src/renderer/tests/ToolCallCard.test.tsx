import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
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
})
