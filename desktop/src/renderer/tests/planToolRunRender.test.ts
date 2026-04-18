import { describe, expect, it } from 'vitest'
import type { ConversationItem } from '../types/conversation'
import { aggregateToolCalls, planToolRunRender } from '../utils/toolCallAggregation'

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

describe('planToolRunRender', () => {
  it('keeps two-item trailing run as singles and marks last completed item as lingering', () => {
    const run = [
      makeItem('WriteFile', '1'),
      makeItem('EditFile', '2')
    ]

    const result = planToolRunRender(run, { isRunning: true, isTrailingRun: true })
    expect(result.entries).toHaveLength(2)
    expect(result.entries.every((entry) => entry.kind === 'single')).toBe(true)
    expect(result.lingerId).toBe('2')
  })

  it('aggregates settled prefix and keeps last completed item as lingering single', () => {
    const run = [
      makeItem('WriteFile', '1'),
      makeItem('EditFile', '2'),
      makeItem('EditFile', '3')
    ]

    const result = planToolRunRender(run, { isRunning: true, isTrailingRun: true })
    expect(result.entries).toHaveLength(2)
    expect(result.entries[0]).toEqual({
      kind: 'group',
      category: 'write',
      items: [run[0], run[1]]
    })
    expect(result.entries[1]).toEqual({ kind: 'single', item: run[2] })
    expect(result.lingerId).toBe('3')
  })

  it('aggregates longer settled prefix and keeps final completed item as lingering single', () => {
    const run = [
      makeItem('WriteFile', '1'),
      makeItem('EditFile', '2'),
      makeItem('EditFile', '3'),
      makeItem('EditFile', '4')
    ]

    const result = planToolRunRender(run, { isRunning: true, isTrailingRun: true })
    expect(result.entries).toHaveLength(2)
    expect(result.entries[0]).toEqual({
      kind: 'group',
      category: 'write',
      items: [run[0], run[1], run[2]]
    })
    expect(result.entries[1]).toEqual({ kind: 'single', item: run[3] })
    expect(result.lingerId).toBe('4')
  })

  it('aggregates all entries without lingering when last item is live', () => {
    const run = [
      makeItem('WriteFile', '1'),
      makeItem('EditFile', '2', { status: 'streaming' })
    ]

    const result = planToolRunRender(run, { isRunning: true, isTrailingRun: true })
    expect(result.entries).toEqual(aggregateToolCalls(run))
    expect(result.lingerId).toBeUndefined()
  })

  it('aggregates all entries after linger has been dismissed', () => {
    const run = [
      makeItem('WriteFile', '1'),
      makeItem('EditFile', '2'),
      makeItem('EditFile', '3')
    ]

    const result = planToolRunRender(run, {
      isRunning: true,
      isTrailingRun: true,
      dismissedLingerId: '3'
    })
    expect(result.entries).toEqual(aggregateToolCalls(run))
    expect(result.lingerId).toBeUndefined()
  })

  it('aggregates non-trailing run while still running', () => {
    const run = [
      makeItem('ReadFile', '1'),
      makeItem('FindFiles', '2')
    ]

    const result = planToolRunRender(run, { isRunning: true, isTrailingRun: false })
    expect(result.entries).toEqual(aggregateToolCalls(run))
    expect(result.lingerId).toBeUndefined()
  })

  it('aggregates trailing run when turn is not running', () => {
    const run = [
      makeItem('Exec', '1', { result: 'ok', success: true }),
      makeItem('RunCommand', '2', { result: 'ok', success: true })
    ]

    const result = planToolRunRender(run, { isRunning: false, isTrailingRun: true })
    expect(result.entries).toEqual(aggregateToolCalls(run))
    expect(result.lingerId).toBeUndefined()
  })
})
