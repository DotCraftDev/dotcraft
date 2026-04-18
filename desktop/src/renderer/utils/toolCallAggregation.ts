import type { ConversationItem } from '../types/conversation'

const EXPLORE_TOOLS = new Set(['ReadFile', 'GrepFiles', 'FindFiles'])
const WRITE_TOOLS = new Set(['WriteFile', 'EditFile'])
const SHELL_TOOLS = new Set(['Exec', 'RunCommand', 'BashCommand'])

export type ToolGroupCategory = 'explore' | 'write' | 'shell'

export type AggregatedToolCall =
  | { kind: 'single'; item: ConversationItem }
  | { kind: 'group'; category: ToolGroupCategory; items: ConversationItem[] }

function getGroupCategory(toolName: string): ToolGroupCategory | null {
  if (EXPLORE_TOOLS.has(toolName)) return 'explore'
  if (WRITE_TOOLS.has(toolName)) return 'write'
  if (SHELL_TOOLS.has(toolName)) return 'shell'
  return null
}

export function isToolItemLive(item: ConversationItem): boolean {
  const toolName = item.toolName ?? ''
  if (!SHELL_TOOLS.has(toolName)) {
    return item.status !== 'completed'
  }

  if (item.executionStatus != null) {
    if (item.executionStatus === 'inProgress') return true
    // Legacy: wire item lifecycle "started" was mistakenly stored as executionStatus.
    if (String(item.executionStatus) === 'started') return true
    return false
  }

  if (item.status !== 'completed') return true
  return item.result === undefined && item.success === undefined
}

/**
 * Groups consecutive tool calls by category (explore/write/shell), while preserving
 * chronological order. Category transitions close the current group.
 *
 * Example: [ReadFile, ReadFile, WriteFile] → [group(2), single(WriteFile)]
 */
export function aggregateToolCalls(
  items: ConversationItem[]
): AggregatedToolCall[] {
  const result: AggregatedToolCall[] = []
  let i = 0

  while (i < items.length) {
    const item = items[i]
    const toolName = item.toolName ?? ''
    const category = getGroupCategory(toolName)

    if (category == null) {
      result.push({ kind: 'single', item })
      i++
      continue
    }

    // Collect consecutive items in the same category and aggregate only settled
    // stretches. Live items split runs but do not de-aggregate neighbors.
    const run: ConversationItem[] = [item]
    while (i + 1 < items.length) {
      const next = items[i + 1]
      const nextCategory = getGroupCategory(next.toolName ?? '')
      if (nextCategory !== category) break
      run.push(next)
      i++
    }

    let settledBucket: ConversationItem[] = []
    const flushSettledBucket = (): void => {
      if (settledBucket.length === 0) return
      if (settledBucket.length === 1) {
        result.push({ kind: 'single', item: settledBucket[0] })
      } else {
        result.push({
          kind: 'group',
          category,
          items: settledBucket
        })
      }
      settledBucket = []
    }

    for (const runItem of run) {
      const isBreakingItem = isToolItemLive(runItem)
      if (isBreakingItem) {
        flushSettledBucket()
        result.push({ kind: 'single', item: runItem })
      } else {
        settledBucket.push(runItem)
      }
    }
    flushSettledBucket()

    i++
  }

  return result
}

export function planToolRunRender(
  toolRun: ConversationItem[],
  context: { isRunning: boolean; isTrailingRun: boolean; dismissedLingerId?: string }
): { entries: AggregatedToolCall[]; lingerId?: string } {
  if (toolRun.length === 0) {
    return { entries: [] }
  }

  if (context.isRunning && context.isTrailingRun) {
    const lastItem = toolRun[toolRun.length - 1]
    const lingerDismissed = context.dismissedLingerId === lastItem.id
    if (isToolItemLive(lastItem) || lingerDismissed) {
      return { entries: aggregateToolCalls(toolRun) }
    }

    const prefix = toolRun.slice(0, -1)
    const prefixEntries = aggregateToolCalls(prefix)
    return {
      entries: [...prefixEntries, { kind: 'single', item: lastItem }],
      lingerId: lastItem.id
    }
  }

  return { entries: aggregateToolCalls(toolRun) }
}
