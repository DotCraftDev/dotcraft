import { StrictMode } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { MessageStream } from '../components/conversation/MessageStream'
import { useConversationStore } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import { ACCEPT_PLAN_SENTINEL_EN } from '../utils/planAcceptSentinel'

function renderWithLocale(node: JSX.Element): void {
  render(<LocaleProvider>{node}</LocaleProvider>)
}

function makeRunningTurn(): ReturnType<typeof useConversationStore.getState>['turns'][number] {
  return {
    id: 'turn-1',
    threadId: 'thread-1',
    status: 'running',
    startedAt: new Date().toISOString(),
    items: [
      {
        id: 'u1',
        type: 'userMessage',
        status: 'completed',
        text: 'Fetch this page',
        createdAt: new Date().toISOString()
      }
    ]
  }
}

describe('MessageStream plan-accept sentinel filtering', () => {
  beforeEach(() => {
    useConversationStore.getState().reset()
    useThreadStore.getState().reset()
    useThreadStore.setState({ activeThreadId: 'thread-1' })
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: {
          get: async () => ({ locale: 'en' })
        }
      }
    })
  })

  it('hides the plan-accept sentinel user message from conversation view', () => {
    useConversationStore.setState({
      turns: [{
        id: 'turn-1',
        threadId: 'thread-1',
        status: 'completed',
        startedAt: new Date().toISOString(),
        completedAt: new Date().toISOString(),
        items: [
          {
            id: 'u1',
            type: 'userMessage',
            status: 'completed',
            text: ACCEPT_PLAN_SENTINEL_EN,
            createdAt: new Date().toISOString()
          },
          {
            id: 'a1',
            type: 'agentMessage',
            status: 'completed',
            text: 'Executing accepted plan now.',
            createdAt: new Date().toISOString()
          }
        ]
      }]
    })

    renderWithLocale(<MessageStream />)

    expect(screen.queryByText(ACCEPT_PLAN_SENTINEL_EN)).toBeNull()
    expect(screen.getByText('Executing accepted plan now.')).toBeInTheDocument()
  })

  it('keeps running tool auto-scroll stable under StrictMode streaming updates', () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {})
    useConversationStore.setState({
      turns: [makeRunningTurn()],
      turnStatus: 'running',
      activeTurnId: 'turn-1',
      turnStartedAt: Date.now()
    })

    renderWithLocale(
      <StrictMode>
        <MessageStream />
      </StrictMode>
    )

    const stream = screen.getByTestId('message-stream')
    Object.defineProperty(stream, 'clientHeight', { configurable: true, value: 100 })
    Object.defineProperty(stream, 'scrollHeight', { configurable: true, value: 300 })
    stream.scrollTop = 180

    act(() => {
      fireEvent.scroll(stream)
      useConversationStore.getState().onToolCallArgumentsDelta({
        threadId: 'thread-1',
        turnId: 'turn-1',
        itemId: 'tool-1',
        toolName: 'WebFetch',
        callId: 'webfetch-1',
        delta: '{"url":"https://dotcraft.ai"'
      })
    })

    expect(screen.getByText('Fetching https://dotcraft.ai...')).toHaveClass('tool-running-gradient-text')
    const maxDepthErrors = consoleError.mock.calls.filter((call) =>
      call.some((part) => String(part).includes('Maximum update depth exceeded'))
    )
    expect(maxDepthErrors).toHaveLength(0)
    consoleError.mockRestore()
  })
})
