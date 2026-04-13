import { beforeEach, describe, expect, it } from 'vitest'
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
})
