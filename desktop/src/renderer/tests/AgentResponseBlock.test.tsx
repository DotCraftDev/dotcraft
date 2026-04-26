import { describe, it, expect, beforeEach } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { AgentResponseBlock } from '../components/conversation/AgentResponseBlock'
import type { ConversationItem, ConversationTurn } from '../types/conversation'

function makeToolCallItem(
  id: string,
  toolCallId: string,
  toolName: string,
  createdAt: string
): ConversationItem {
  return {
    id,
    type: 'toolCall',
    status: 'completed',
    toolCallId,
    toolName,
    arguments: {},
    success: true,
    createdAt
  }
}

function renderBlock(turn: ConversationTurn): string {
  const { container } = render(
    <LocaleProvider>
      <AgentResponseBlock turn={turn} />
    </LocaleProvider>
  )
  return container.textContent ?? ''
}

describe('AgentResponseBlock subagent progress placement', () => {
  beforeEach(() => {
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: {
          get: async () => ({ locale: 'en' })
        }
      }
    })
  })

  it('renders completed subagent summary between SpawnSubagent and later tool calls', () => {
    const turn: ConversationTurn = {
      id: 'turn-1',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-04-18T10:00:00.000Z',
      items: [
        makeToolCallItem('tool-1', 'call-1', 'SpawnSubagent', '2026-04-18T10:00:01.000Z'),
        makeToolCallItem('tool-2', 'call-2', 'FollowupTool', '2026-04-18T10:00:02.000Z')
      ],
      subAgentEntries: [
        {
          label: 'planner',
          isCompleted: true,
          currentTool: undefined,
          currentToolDisplay: undefined,
          inputTokens: 1200,
          outputTokens: 450
        }
      ]
    }

    const text = renderBlock(turn)
    const spawnIndex = text.indexOf('Called SpawnSubagent')
    const bubbleIndex = text.indexOf('SubAgent completed')
    const followupIndex = text.indexOf('Called FollowupTool')

    expect(spawnIndex).toBeGreaterThan(-1)
    expect(bubbleIndex).toBeGreaterThan(-1)
    expect(followupIndex).toBeGreaterThan(-1)
    expect(spawnIndex).toBeLessThan(bubbleIndex)
    expect(bubbleIndex).toBeLessThan(followupIndex)
  })

  it('renders completed subagent summary after SpawnSubagent when no follow-up tools exist', () => {
    const turn: ConversationTurn = {
      id: 'turn-2',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-04-18T10:01:00.000Z',
      items: [
        makeToolCallItem('tool-3', 'call-3', 'SpawnSubagent', '2026-04-18T10:01:01.000Z')
      ],
      subAgentEntries: [
        {
          label: 'reviewer',
          isCompleted: true,
          currentTool: undefined,
          currentToolDisplay: undefined,
          inputTokens: 300,
          outputTokens: 200
        }
      ]
    }

    const text = renderBlock(turn)
    const spawnIndex = text.indexOf('Called SpawnSubagent')
    const bubbleIndex = text.indexOf('SubAgent completed')

    expect(spawnIndex).toBeGreaterThan(-1)
    expect(bubbleIndex).toBeGreaterThan(-1)
    expect(spawnIndex).toBeLessThan(bubbleIndex)
  })
})

describe('AgentResponseBlock tail tool aggregation timing', () => {
  beforeEach(() => {
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: {
          get: async () => ({ locale: 'en' })
        }
      }
    })
  })

  it('keeps trailing completed tool run as single cards while the turn is still running', () => {
    const turn: ConversationTurn = {
      id: 'turn-tail-running',
      threadId: 'thread-1',
      status: 'running',
      startedAt: '2026-04-18T11:00:00.000Z',
      items: [
        makeToolCallItem('tool-1', 'call-1', 'ReadFile', '2026-04-18T11:00:01.000Z'),
        makeToolCallItem('tool-2', 'call-2', 'FindFiles', '2026-04-18T11:00:02.000Z')
      ]
    }

    render(
      <LocaleProvider>
        <AgentResponseBlock turn={turn} isRunning />
      </LocaleProvider>
    )

    expect(screen.queryByText('Explored 2 files')).toBeNull()
    expect(screen.getAllByText('Explored files')).toHaveLength(2)
  })

  it('aggregates the same tool run once reasoning starts after it', () => {
    const turn: ConversationTurn = {
      id: 'turn-tail-unlocked',
      threadId: 'thread-1',
      status: 'running',
      startedAt: '2026-04-18T11:05:00.000Z',
      items: [
        makeToolCallItem('tool-1', 'call-1', 'ReadFile', '2026-04-18T11:05:01.000Z'),
        makeToolCallItem('tool-2', 'call-2', 'FindFiles', '2026-04-18T11:05:02.000Z'),
        {
          id: 'reasoning-1',
          type: 'reasoningContent',
          status: 'streaming',
          reasoning: '',
          createdAt: '2026-04-18T11:05:03.000Z'
        }
      ]
    }

    render(
      <LocaleProvider>
        <AgentResponseBlock turn={turn} isRunning activeItemIdOverride="reasoning-1" />
      </LocaleProvider>
    )

    expect(screen.getByText('Explored 2 files')).toBeInTheDocument()
  })

  it('aggregates trailing tool run after the turn completes', () => {
    const turn: ConversationTurn = {
      id: 'turn-tail-completed',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-04-18T11:10:00.000Z',
      items: [
        makeToolCallItem('tool-1', 'call-1', 'ReadFile', '2026-04-18T11:10:01.000Z'),
        makeToolCallItem('tool-2', 'call-2', 'FindFiles', '2026-04-18T11:10:02.000Z')
      ]
    }

    render(
      <LocaleProvider>
        <AgentResponseBlock turn={turn} />
      </LocaleProvider>
    )

    expect(screen.getByText('Explored 2 files')).toBeInTheDocument()
  })

  it('keeps WebSearch child tool headers but removes duplicate expanded copy above the table', () => {
    const makeSearchItem = (
      id: string,
      query: string,
      title: string,
      url: string,
      createdAt: string
    ): ConversationItem => ({
      id,
      type: 'toolCall',
      status: 'completed',
      toolCallId: id,
      toolName: 'WebSearch',
      arguments: { query },
      result: JSON.stringify({
        query,
        results: [{ title, url }]
      }),
      success: true,
      createdAt
    })

    const turn: ConversationTurn = {
      id: 'turn-web-group',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-04-18T11:15:00.000Z',
      items: [
        makeSearchItem('web-1', 'large graph visualization', 'First result', 'https://example.com/first', '2026-04-18T11:15:01.000Z'),
        makeSearchItem('web-2', 'react flow performance', 'Second result', 'https://example.com/second', '2026-04-18T11:15:02.000Z')
      ]
    }

    render(
      <LocaleProvider>
        <AgentResponseBlock turn={turn} />
      </LocaleProvider>
    )

    fireEvent.click(screen.getByRole('button', { name: /Searched web 2 times/ }))

    const firstToolTitle = screen.getByRole('button', { name: 'Searched "large graph visualization"' })
    const secondToolTitle = screen.getByRole('button', { name: 'Searched "react flow performance"' })
    expect(firstToolTitle).toBeInTheDocument()
    expect(secondToolTitle).toBeInTheDocument()
    expect(screen.queryByRole('columnheader', { name: 'Title' })).toBeNull()

    fireEvent.click(firstToolTitle)

    expect(screen.getAllByRole('columnheader', { name: 'Title' })).toHaveLength(1)
    expect(screen.getByRole('button', { name: 'First result' })).toBeInTheDocument()
    expect(screen.queryByText('Web search')).toBeNull()
    expect(screen.getAllByText('Searched "large graph visualization"')).toHaveLength(1)
  })
})

describe('AgentResponseBlock completed turn folding', () => {
  beforeEach(() => {
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: {
          get: async () => ({ locale: 'en' })
        }
      }
    })
  })

  it('collapses intermediate items into processed summary and keeps final message visible', () => {
    const turn: ConversationTurn = {
      id: 'turn-folded',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-04-18T11:20:00.000Z',
      completedAt: '2026-04-18T11:20:10.000Z',
      items: [
        {
          id: 'reasoning-1',
          type: 'reasoningContent',
          status: 'completed',
          reasoning: 'intermediate reasoning',
          elapsedSeconds: 2,
          createdAt: '2026-04-18T11:20:01.000Z'
        },
        {
          id: 'tool-1',
          type: 'toolCall',
          status: 'completed',
          toolCallId: 'call-1',
          toolName: 'ReadFile',
          arguments: { path: 'src/main.ts' },
          success: true,
          createdAt: '2026-04-18T11:20:02.000Z'
        },
        {
          id: 'assistant-final',
          type: 'agentMessage',
          status: 'completed',
          text: 'final response',
          createdAt: '2026-04-18T11:20:05.000Z'
        }
      ]
    }

    render(
      <LocaleProvider>
        <AgentResponseBlock turn={turn} />
      </LocaleProvider>
    )

    expect(screen.getByText('Processed in 5s')).toBeInTheDocument()
    expect(screen.getByText('final response')).toBeInTheDocument()
    expect(screen.queryByText('Read main.ts')).toBeNull()

    fireEvent.click(screen.getByRole('button', { name: /Processed in 5s/ }))

    expect(screen.getByText('Read main.ts')).toBeInTheDocument()
  })
})

describe('AgentResponseBlock guidance user messages', () => {
  beforeEach(() => {
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: {
          get: async () => ({ locale: 'en' })
        }
      }
    })
  })

  it('renders guidance user messages inline after the preceding tool call', () => {
    const turn: ConversationTurn = {
      id: 'turn-guidance',
      threadId: 'thread-1',
      status: 'running',
      startedAt: '2026-04-25T10:00:00.000Z',
      items: [
        {
          id: 'initial-user',
          type: 'userMessage',
          status: 'completed',
          text: 'initial request',
          createdAt: '2026-04-25T10:00:00.000Z'
        },
        makeToolCallItem('tool-1', 'call-1', 'FollowupTool', '2026-04-25T10:00:01.000Z'),
        {
          id: 'guidance-user',
          type: 'userMessage',
          status: 'completed',
          deliveryMode: 'guidance',
          text: 'guide the active turn',
          createdAt: '2026-04-25T10:00:02.000Z'
        },
        {
          id: 'assistant-1',
          type: 'agentMessage',
          status: 'completed',
          text: 'continuing after guidance',
          createdAt: '2026-04-25T10:00:03.000Z'
        }
      ]
    }

    const text = renderBlock(turn)
    const initialIndex = text.indexOf('initial request')
    const toolIndex = text.indexOf('Called FollowupTool')
    const guidanceIndex = text.indexOf('guide the active turn')
    const assistantIndex = text.indexOf('continuing after guidance')

    expect(initialIndex).toBe(-1)
    expect(toolIndex).toBeGreaterThan(-1)
    expect(guidanceIndex).toBeGreaterThan(-1)
    expect(assistantIndex).toBeGreaterThan(-1)
    expect(toolIndex).toBeLessThan(guidanceIndex)
    expect(guidanceIndex).toBeLessThan(assistantIndex)
  })

  it('does not fold completed turns that contain guidance user messages', () => {
    const turn: ConversationTurn = {
      id: 'turn-guidance-completed',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-04-25T10:00:00.000Z',
      completedAt: '2026-04-25T10:00:10.000Z',
      items: [
        makeToolCallItem('tool-1', 'call-1', 'FollowupTool', '2026-04-25T10:00:01.000Z'),
        {
          id: 'guidance-user',
          type: 'userMessage',
          status: 'completed',
          deliveryMode: 'guidance',
          text: 'guide the active turn',
          createdAt: '2026-04-25T10:00:02.000Z'
        },
        {
          id: 'assistant-final',
          type: 'agentMessage',
          status: 'completed',
          text: 'final response',
          createdAt: '2026-04-25T10:00:05.000Z'
        }
      ]
    }

    render(
      <LocaleProvider>
        <AgentResponseBlock turn={turn} />
      </LocaleProvider>
    )

    expect(screen.queryByText(/Processed in/)).toBeNull()
    expect(screen.getByText('Called FollowupTool')).toBeInTheDocument()
    expect(screen.getByText('guide the active turn')).toBeInTheDocument()
    expect(screen.getByText('final response')).toBeInTheDocument()
  })
})
