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
