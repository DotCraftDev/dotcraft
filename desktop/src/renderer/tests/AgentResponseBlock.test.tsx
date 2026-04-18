import { describe, it, expect, beforeEach } from 'vitest'
import { render } from '@testing-library/react'
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
