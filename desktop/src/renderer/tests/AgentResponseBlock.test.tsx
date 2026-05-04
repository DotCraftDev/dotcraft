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

function makeCreatePlanItem(
  id: string,
  title: string,
  createdAt: string
): ConversationItem {
  return {
    id,
    type: 'toolCall',
    status: 'completed',
    toolCallId: `${id}-call`,
    toolName: 'CreatePlan',
    arguments: {
      title,
      overview: `${title} overview`,
      plan: '- keep visible'
    },
    success: true,
    createdAt
  }
}

function renderBlock(turn: ConversationTurn, options: { isRunning?: boolean } = {}): string {
  const { container } = render(
    <LocaleProvider>
      <AgentResponseBlock turn={turn} isRunning={options.isRunning} />
    </LocaleProvider>
  )
  return container.textContent ?? ''
}

function expectDisclosureInsideTitleGroup(container: HTMLElement): HTMLElement {
  const titleGroup = container.querySelector('[data-testid="tool-row-title-group"]') as HTMLElement
  const disclosureIcon = container.querySelector('[data-testid="tool-disclosure-icon"]') as HTMLElement
  expect(titleGroup).toBeTruthy()
  expect(disclosureIcon).toBeTruthy()
  expect(titleGroup).toContainElement(disclosureIcon)
  expect(titleGroup.style.display).toBe('inline-flex')
  expect(titleGroup.style.flex).toBe('0 1 auto')
  return disclosureIcon
}

describe('AgentResponseBlock subagent transcript rendering', () => {
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

  it('does not render the old inline subagent progress summary between SpawnAgent and later tool calls', () => {
    const turn: ConversationTurn = {
      id: 'turn-1',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-04-18T10:00:00.000Z',
      items: [
        makeToolCallItem('tool-1', 'call-1', 'SpawnAgent', '2026-04-18T10:00:01.000Z'),
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
    const spawnIndex = text.indexOf('Started agent')
    const followupIndex = text.indexOf('Called FollowupTool')

    expect(spawnIndex).toBeGreaterThan(-1)
    expect(followupIndex).toBeGreaterThan(-1)
    expect(spawnIndex).toBeLessThan(followupIndex)
    expect(text).not.toContain('SubAgent completed')
  })

  it('keeps SpawnAgent output compact when no follow-up tools exist', () => {
    const turn: ConversationTurn = {
      id: 'turn-2',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-04-18T10:01:00.000Z',
      items: [
        makeToolCallItem('tool-3', 'call-3', 'SpawnAgent', '2026-04-18T10:01:01.000Z')
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
    expect(text).toContain('Started agent')
    expect(text).not.toContain('SubAgent completed')
  })

  it('keeps WaitAgent in running state after toolCall completion while waiting for toolResult', () => {
    const turn: ConversationTurn = {
      id: 'turn-wait-running',
      threadId: 'thread-1',
      status: 'running',
      startedAt: '2026-05-03T10:00:00.000Z',
      items: [
        {
          id: 'tool-wait',
          type: 'toolCall',
          status: 'completed',
          toolCallId: 'call-wait',
          toolName: 'WaitAgent',
          arguments: { childThreadId: 'thread_child', agentNickname: 'Reviewer' },
          createdAt: '2026-05-03T10:00:01.000Z'
        }
      ]
    }

    const text = renderBlock(turn, { isRunning: true })

    expect(text).toContain('Waiting for Reviewer')
    expect(text).not.toContain('Received result from Reviewer')
    expect(text).not.toContain('thread_child')
  })

  it('does not keep historical WaitAgent calls running when toolResult is missing', () => {
    const turn: ConversationTurn = {
      id: 'turn-wait-history',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-05-03T10:00:00.000Z',
      items: [
        {
          id: 'tool-wait-history',
          type: 'toolCall',
          status: 'completed',
          toolCallId: 'call-wait-history',
          toolName: 'WaitAgent',
          arguments: { childThreadId: 'thread_child', agentNickname: 'Reviewer' },
          createdAt: '2026-05-03T10:00:01.000Z'
        }
      ]
    }

    const text = renderBlock(turn)

    expect(text).not.toContain('Waiting for Reviewer')
    expect(text).not.toContain('Received result from Reviewer')
  })

  it('renders pluginFunctionCall items in the tool run', () => {
    const turn: ConversationTurn = {
      id: 'turn-plugin',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-04-18T10:02:00.000Z',
      items: [
        {
          id: 'plugin-tool-1',
          type: 'pluginFunctionCall',
          status: 'completed',
          toolCallId: 'plugin-call-1',
          toolName: 'NodeReplJs',
          arguments: { code: '1 + 1' },
          result: '2',
          success: true,
          createdAt: '2026-04-18T10:02:01.000Z'
        }
      ]
    }

    const text = renderBlock(turn)

    expect(text).toContain('Called NodeReplJs')
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

  it('renders completed parallel tool results as settled while unmatched tools stay running', () => {
    const turn: ConversationTurn = {
      id: 'turn-parallel-mixed',
      threadId: 'thread-1',
      status: 'running',
      startedAt: '2026-04-18T11:12:00.000Z',
      items: [
        {
          id: 'tool-done',
          type: 'toolCall',
          status: 'completed',
          toolCallId: 'call-done',
          toolName: 'FollowupTool',
          arguments: {},
          createdAt: '2026-04-18T11:12:01.000Z'
        },
        {
          id: 'tool-pending',
          type: 'toolCall',
          status: 'completed',
          toolCallId: 'call-pending',
          toolName: 'PendingTool',
          arguments: {},
          createdAt: '2026-04-18T11:12:02.000Z'
        },
        {
          id: 'result-done',
          type: 'toolResult',
          status: 'completed',
          toolCallId: 'call-done',
          result: 'done',
          success: true,
          createdAt: '2026-04-18T11:12:03.000Z',
          completedAt: '2026-04-18T11:12:03.000Z'
        }
      ]
    }

    render(
      <LocaleProvider>
        <AgentResponseBlock turn={turn} isRunning />
      </LocaleProvider>
    )

    const completedLabel = screen.getByText('Called FollowupTool')
    expect(completedLabel).not.toHaveClass('tool-running-gradient-text')
    expect(screen.getByText(/PendingTool/)).toHaveClass('tool-running-gradient-text')
    expect(screen.queryByText('done')).toBeNull()
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

describe('AgentResponseBlock reasoning timeline rendering', () => {
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

  it('renders reasoning items as separate timeline rows around tool output', () => {
    const turn: ConversationTurn = {
      id: 'turn-reasoning-timeline',
      threadId: 'thread-1',
      status: 'running',
      startedAt: '2026-04-18T11:18:00.000Z',
      items: [
        {
          id: 'reasoning-before',
          type: 'reasoningContent',
          status: 'completed',
          reasoning: 'first thought',
          elapsedSeconds: 3,
          createdAt: '2026-04-18T11:18:01.000Z'
        },
        {
          id: 'tool-between',
          type: 'toolCall',
          status: 'completed',
          toolCallId: 'call-between',
          toolName: 'ReadFile',
          arguments: { path: 'src/main.ts' },
          success: true,
          createdAt: '2026-04-18T11:18:02.000Z'
        },
        {
          id: 'reasoning-after',
          type: 'reasoningContent',
          status: 'completed',
          reasoning: 'second thought',
          elapsedSeconds: 5,
          createdAt: '2026-04-18T11:18:03.000Z'
        },
        {
          id: 'assistant-after',
          type: 'agentMessage',
          status: 'completed',
          text: 'final response',
          createdAt: '2026-04-18T11:18:04.000Z'
        }
      ]
    }

    const text = renderBlock(turn, { isRunning: true })
    const firstThought = text.indexOf('Thought 3s')
    const tool = text.indexOf('Read main.ts')
    const secondThought = text.indexOf('Thought 5s')
    const finalMessage = text.indexOf('final response')

    expect(firstThought).toBeGreaterThan(-1)
    expect(tool).toBeGreaterThan(-1)
    expect(secondThought).toBeGreaterThan(-1)
    expect(finalMessage).toBeGreaterThan(-1)
    expect(firstThought).toBeLessThan(tool)
    expect(tool).toBeLessThan(secondThought)
    expect(secondThought).toBeLessThan(finalMessage)
  })

  it('uses the shared disclosure icon and keeps expanded reasoning as italic quote content', () => {
    const turn: ConversationTurn = {
      id: 'turn-reasoning-style',
      threadId: 'thread-1',
      status: 'running',
      startedAt: '2026-04-18T11:19:00.000Z',
      items: [
        {
          id: 'reasoning-style',
          type: 'reasoningContent',
          status: 'completed',
          reasoning: 'quoted reasoning',
          elapsedSeconds: 7,
          createdAt: '2026-04-18T11:19:01.000Z'
        }
      ]
    }

    const { container } = render(
      <LocaleProvider>
        <AgentResponseBlock turn={turn} isRunning />
      </LocaleProvider>
    )
    const button = screen.getByRole('button', { name: 'Thought 7s' })
    const disclosureIcon = expectDisclosureInsideTitleGroup(container)

    expect(screen.getByText('Thought 7s')).not.toHaveClass('tool-running-gradient-text')
    expect(button).toHaveStyle({ color: 'var(--text-dimmed)' })
    expect(disclosureIcon).toHaveStyle({ opacity: '0' })
    fireEvent.mouseEnter(button)
    expect(button).toHaveStyle({ color: 'var(--text-secondary)' })
    expect(disclosureIcon).toHaveStyle({ opacity: '1' })

    fireEvent.click(button)

    const expanded = screen.getByText('quoted reasoning')
    expect(expanded).toHaveStyle({
      fontStyle: 'italic',
      whiteSpace: 'pre-wrap',
      background: 'transparent'
    })
    expect(expanded.style.borderLeft).toBe('2px solid var(--border-default)')
  })

  it('allows streaming reasoning with text to expand using the same row layout', () => {
    const turn: ConversationTurn = {
      id: 'turn-reasoning-streaming',
      threadId: 'thread-1',
      status: 'running',
      startedAt: '2026-04-18T11:19:30.000Z',
      items: [
        {
          id: 'reasoning-streaming',
          type: 'reasoningContent',
          status: 'streaming',
          reasoning: '',
          createdAt: '2026-04-18T11:19:31.000Z'
        }
      ]
    }

    const { container } = render(
      <LocaleProvider>
        <AgentResponseBlock
          turn={turn}
          isRunning
          activeItemIdOverride="reasoning-streaming"
          streamingReasoning="live reasoning"
        />
      </LocaleProvider>
    )

    const button = screen.getByRole('button', { name: 'Thinking...' })
    expect(screen.getByText('Thinking...')).toHaveClass('tool-running-gradient-text')
    expectDisclosureInsideTitleGroup(container)
    fireEvent.click(button)

    expect(screen.getByText('live reasoning')).toBeInTheDocument()
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

    const { container } = render(
      <LocaleProvider>
        <AgentResponseBlock turn={turn} />
      </LocaleProvider>
    )

    expect(screen.getByText('Processed in 5s')).toBeInTheDocument()
    expect(screen.getByText('final response')).toBeInTheDocument()
    expect(screen.queryByText('Read main.ts')).toBeNull()

    fireEvent.click(screen.getByRole('button', { name: /Processed in 5s/ }))

    expect(screen.getByText('Thought 2s')).toBeInTheDocument()
    expect(screen.getByText('Read main.ts')).toBeInTheDocument()
    const expandedText = container.textContent ?? ''
    expect(expandedText.indexOf('Thought 2s')).toBeLessThan(expandedText.indexOf('Read main.ts'))
  })

  it('keeps the final CreatePlan visible while folding earlier intermediate work', () => {
    const turn: ConversationTurn = {
      id: 'turn-folded-plan',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-04-18T11:30:00.000Z',
      completedAt: '2026-04-18T11:30:10.000Z',
      items: [
        {
          id: 'reasoning-1',
          type: 'reasoningContent',
          status: 'completed',
          reasoning: 'intermediate reasoning',
          elapsedSeconds: 2,
          createdAt: '2026-04-18T11:30:01.000Z'
        },
        {
          id: 'tool-1',
          type: 'toolCall',
          status: 'completed',
          toolCallId: 'call-1',
          toolName: 'ReadFile',
          arguments: { path: 'src/main.ts' },
          success: true,
          createdAt: '2026-04-18T11:30:02.000Z'
        },
        makeCreatePlanItem('plan-final', 'Visible Plan', '2026-04-18T11:30:04.000Z'),
        {
          id: 'assistant-final',
          type: 'agentMessage',
          status: 'completed',
          text: 'final response after plan',
          createdAt: '2026-04-18T11:30:06.000Z'
        }
      ]
    }

    render(
      <LocaleProvider>
        <AgentResponseBlock turn={turn} />
      </LocaleProvider>
    )

    expect(screen.getByText('Processed in 6s')).toBeInTheDocument()
    expect(screen.getByText('Visible Plan')).toBeInTheDocument()
    expect(screen.getByText('final response after plan')).toBeInTheDocument()
    expect(screen.queryByText('Read main.ts')).toBeNull()

    fireEvent.click(screen.getByRole('button', { name: /Processed in 6s/ }))

    expect(screen.getByText('Read main.ts')).toBeInTheDocument()
  })

  it('pins only the latest CreatePlan before the final message', () => {
    const turn: ConversationTurn = {
      id: 'turn-folded-two-plans',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-04-18T11:40:00.000Z',
      completedAt: '2026-04-18T11:40:12.000Z',
      items: [
        makeCreatePlanItem('plan-first', 'First Plan', '2026-04-18T11:40:01.000Z'),
        {
          id: 'tool-1',
          type: 'toolCall',
          status: 'completed',
          toolCallId: 'call-1',
          toolName: 'ReadFile',
          arguments: { path: 'src/main.ts' },
          success: true,
          createdAt: '2026-04-18T11:40:02.000Z'
        },
        makeCreatePlanItem('plan-latest', 'Latest Plan', '2026-04-18T11:40:05.000Z'),
        {
          id: 'assistant-final',
          type: 'agentMessage',
          status: 'completed',
          text: 'final response after latest plan',
          createdAt: '2026-04-18T11:40:08.000Z'
        }
      ]
    }

    render(
      <LocaleProvider>
        <AgentResponseBlock turn={turn} />
      </LocaleProvider>
    )

    expect(screen.getByText('Latest Plan')).toBeInTheDocument()
    expect(screen.queryByText('First Plan')).toBeNull()
    expect(screen.getByText('final response after latest plan')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: /Processed in 8s/ }))

    expect(screen.getByText('First Plan')).toBeInTheDocument()
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
