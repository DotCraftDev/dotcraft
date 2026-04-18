import { describe, it, expect } from 'vitest'
import { aggregateToolCalls } from '../utils/toolCallAggregation'
import type { ConversationItem } from '../types/conversation'

function makeItem(
  toolName: string,
  id: string,
  overrides: Partial<ConversationItem> = {}
): ConversationItem {
  return {
    id,
    type: 'toolCall',
    status: 'completed',
    toolName,
    toolCallId: id,
    createdAt: new Date().toISOString(),
    ...overrides
  }
}

describe('aggregateToolCalls', () => {
  it('returns empty array for empty input', () => {
    expect(aggregateToolCalls([])).toHaveLength(0)
  })

  it('keeps a single ReadFile as individual card', () => {
    const items = [makeItem('ReadFile', '1')]
    const result = aggregateToolCalls(items)
    expect(result).toHaveLength(1)
    expect(result[0].kind).toBe('single')
    if (result[0].kind === 'single') {
      expect(result[0].item.toolName).toBe('ReadFile')
    }
  })

  it('groups three consecutive ReadFile calls into one group', () => {
    const items = [
      makeItem('ReadFile', '1'),
      makeItem('ReadFile', '2'),
      makeItem('ReadFile', '3')
    ]
    const result = aggregateToolCalls(items)
    expect(result).toHaveLength(1)
    expect(result[0].kind).toBe('group')
    if (result[0].kind === 'group') {
      expect(result[0].items).toHaveLength(3)
      expect(result[0].category).toBe('explore')
    }
  })

  it('groups consecutive explore tools into one group', () => {
    const items = [
      makeItem('ReadFile', '1'),
      makeItem('GrepFiles', '2'),
      makeItem('FindFiles', '3')
    ]
    const result = aggregateToolCalls(items)
    expect(result).toHaveLength(1)
    expect(result[0].kind).toBe('group')
    if (result[0].kind === 'group') {
      expect(result[0].items).toHaveLength(3)
      expect(result[0].category).toBe('explore')
    }
  })

  it('groups consecutive write tools into one group', () => {
    const items = [
      makeItem('WriteFile', '1'),
      makeItem('EditFile', '2')
    ]
    const result = aggregateToolCalls(items)
    expect(result).toHaveLength(1)
    expect(result[0].kind).toBe('group')
    if (result[0].kind === 'group') {
      expect(result[0].category).toBe('write')
      expect(result[0].items).toHaveLength(2)
    }
  })

  it('handles mixed sequences: [ReadFile, WriteFile, ReadFile, ReadFile]', () => {
    const items = [
      makeItem('ReadFile', '1'),
      makeItem('WriteFile', '2'),
      makeItem('ReadFile', '3'),
      makeItem('ReadFile', '4')
    ]
    const result = aggregateToolCalls(items)
    expect(result).toHaveLength(3)
    // First: single ReadFile
    expect(result[0].kind).toBe('single')
    if (result[0].kind === 'single') {
      expect(result[0].item.toolName).toBe('ReadFile')
    }
    // Second: single WriteFile
    expect(result[1].kind).toBe('single')
    if (result[1].kind === 'single') {
      expect(result[1].item.toolName).toBe('WriteFile')
    }
    // Third: group of 2 ReadFiles
    expect(result[2].kind).toBe('group')
    if (result[2].kind === 'group') {
      expect(result[2].category).toBe('explore')
    }
  })

  it('groups consecutive shell tools into one group', () => {
    const items = [
      makeItem('Exec', '1', { result: 'ok', success: true }),
      makeItem('RunCommand', '2', { result: 'ok', success: true }),
      makeItem('BashCommand', '3', { result: 'ok', success: true })
    ]
    const result = aggregateToolCalls(items)
    expect(result).toHaveLength(1)
    expect(result[0].kind).toBe('group')
    if (result[0].kind === 'group') {
      expect(result[0].category).toBe('shell')
      expect(result[0].items).toHaveLength(3)
    }
  })

  it('keeps non-aggregatable tools as individual cards', () => {
    const items = [
      makeItem('SpawnSubagent', '1'),
      makeItem('SpawnSubagent', '2')
    ]
    const result = aggregateToolCalls(items)
    expect(result).toHaveLength(2)
    expect(result[0].kind).toBe('single')
    expect(result[1].kind).toBe('single')
  })

  it('preserves order of non-aggregatable items', () => {
    const items = [
      makeItem('Exec', '1'),
      makeItem('ReadFile', '2'),
      makeItem('GrepFiles', '3'),
      makeItem('WriteFile', '4')
    ]
    const result = aggregateToolCalls(items)
    expect(result).toHaveLength(3)
    if (result[0].kind === 'single') expect(result[0].item.toolName).toBe('Exec')
    if (result[1].kind === 'group') expect(result[1].category).toBe('explore')
    if (result[2].kind === 'single') expect(result[2].item.toolName).toBe('WriteFile')
  })

  it('does not aggregate across category transitions', () => {
    const items = [
      makeItem('ReadFile', '1'),
      makeItem('WriteFile', '2'),
      makeItem('Exec', '3')
    ]
    const result = aggregateToolCalls(items)
    expect(result).toHaveLength(3)
    for (const entry of result) {
      expect(entry.kind).toBe('single')
    }
  })

  it('groups each category independently in a mixed run', () => {
    const items = [
      makeItem('ReadFile', '1'),
      makeItem('FindFiles', '2'),
      makeItem('WriteFile', '3'),
      makeItem('EditFile', '4'),
      makeItem('Exec', '5', { result: 'ok', success: true }),
      makeItem('RunCommand', '6', { result: 'ok', success: true })
    ]
    const result = aggregateToolCalls(items)
    expect(result).toHaveLength(3)
    expect(result[0].kind).toBe('group')
    expect(result[1].kind).toBe('group')
    expect(result[2].kind).toBe('group')
    if (result[0].kind === 'group') expect(result[0].category).toBe('explore')
    if (result[1].kind === 'group') expect(result[1].category).toBe('write')
    if (result[2].kind === 'group') expect(result[2].category).toBe('shell')
  })

  it('keeps settled write prefix grouped when trailing write item is live', () => {
    const items = [
      makeItem('WriteFile', '1'),
      makeItem('EditFile', '2'),
      makeItem('WriteFile', '3', { status: 'streaming' })
    ]
    const result = aggregateToolCalls(items)
    expect(result).toHaveLength(2)
    expect(result[0].kind).toBe('group')
    if (result[0].kind === 'group') {
      expect(result[0].category).toBe('write')
      expect(result[0].items.map((item) => item.id)).toEqual(['1', '2'])
    }
    expect(result[1].kind).toBe('single')
    if (result[1].kind === 'single') {
      expect(result[1].item.id).toBe('3')
    }
  })

  it('keeps settled shell prefix grouped when trailing shell execution is live', () => {
    const items = [
      makeItem('Exec', '1', {
        status: 'completed',
        executionStatus: 'completed',
        result: 'done',
        success: true
      }),
      makeItem('RunCommand', '2', {
        status: 'completed',
        executionStatus: 'completed',
        result: 'done',
        success: true
      }),
      makeItem('BashCommand', '3', {
        status: 'completed',
        executionStatus: 'inProgress'
      })
    ]
    const result = aggregateToolCalls(items)
    expect(result).toHaveLength(2)
    expect(result[0].kind).toBe('group')
    if (result[0].kind === 'group') {
      expect(result[0].category).toBe('shell')
      expect(result[0].items.map((item) => item.id)).toEqual(['1', '2'])
    }
    expect(result[1].kind).toBe('single')
    if (result[1].kind === 'single') {
      expect(result[1].item.id).toBe('3')
    }
  })

  it('does not de-aggregate settled prefix and suffix around a live item', () => {
    const items = [
      makeItem('ReadFile', '1'),
      makeItem('FindFiles', '2'),
      makeItem('ReadFile', '3', { status: 'streaming' }),
      makeItem('GrepFiles', '4'),
      makeItem('ReadFile', '5')
    ]
    const result = aggregateToolCalls(items)
    expect(result).toHaveLength(3)
    expect(result[0].kind).toBe('group')
    if (result[0].kind === 'group') {
      expect(result[0].items.map((item) => item.id)).toEqual(['1', '2'])
    }
    expect(result[1].kind).toBe('single')
    if (result[1].kind === 'single') {
      expect(result[1].item.id).toBe('3')
    }
    expect(result[2].kind).toBe('group')
    if (result[2].kind === 'group') {
      expect(result[2].items.map((item) => item.id)).toEqual(['4', '5'])
    }
  })
})
