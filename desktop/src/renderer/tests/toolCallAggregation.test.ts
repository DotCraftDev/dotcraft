import { describe, it, expect } from 'vitest'
import { aggregateToolCalls } from '../utils/toolCallAggregation'
import type { ConversationItem } from '../types/conversation'

function makeItem(toolName: string, id: string): ConversationItem {
  return {
    id,
    type: 'toolCall',
    status: 'completed',
    toolName,
    toolCallId: id,
    createdAt: new Date().toISOString()
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
      expect(result[0].label).toBe('Explored 3 files')
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
      expect(result[0].label).toBe('Explored 3 files')
    }
  })

  it('does not aggregate non-aggregatable tools', () => {
    const items = [
      makeItem('WriteFile', '1'),
      makeItem('WriteFile', '2')
    ]
    const result = aggregateToolCalls(items)
    expect(result).toHaveLength(2)
    expect(result[0].kind).toBe('single')
    expect(result[1].kind).toBe('single')
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
      expect(result[2].label).toBe('Explored 2 files')
    }
  })

  it('keeps Exec as individual cards even if consecutive', () => {
    const items = [
      makeItem('Exec', '1'),
      makeItem('Exec', '2')
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
    if (result[1].kind === 'group') expect(result[1].label).toBe('Explored 2 files')
    if (result[2].kind === 'single') expect(result[2].item.toolName).toBe('WriteFile')
  })
})
