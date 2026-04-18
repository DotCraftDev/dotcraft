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
export function aggregateToolCalls(items: ConversationItem[]): AggregatedToolCall[] {
  const result: AggregatedToolCall[] = []
  let i = 0

  while (i < items.length) {
    const item = items[i]
    const toolName = item.toolName ?? ''
    const category = getGroupCategory(toolName)

    if (category != null) {
      // Collect consecutive items in the same category
      const group: ConversationItem[] = [item]
      while (i + 1 < items.length) {
        const next = items[i + 1]
        const nextCategory = getGroupCategory(next.toolName ?? '')
        if (nextCategory === category) {
          group.push(next)
          i++
        } else {
          break
        }
      }

      if (group.length === 1) {
        // Keep single items as normal tool cards.
        result.push({ kind: 'single', item: group[0] })
      } else if (group.some(isToolItemLive)) {
        for (const groupItem of group) {
          result.push({ kind: 'single', item: groupItem })
        }
      } else {
        result.push({
          kind: 'group',
          category,
          items: group
        })
      }
    } else {
      result.push({ kind: 'single', item })
    }

    i++
  }

  return result
}
