import type { ConversationItem } from '../types/conversation'

/** Tool names that can be collapsed into an "Explored N files" group */
const AGGREGATABLE_TOOLS = new Set(['ReadFile', 'GrepFiles', 'FindFiles', 'ListDirectory'])

export type AggregatedToolCall =
  | { kind: 'single'; item: ConversationItem }
  | { kind: 'group'; items: ConversationItem[]; label: string }

/**
 * Groups consecutive aggregatable tool calls (ReadFile, GrepFiles, FindFiles, ListDirectory)
 * into a single summary card. Non-aggregatable tools are kept as individual entries.
 *
 * Example: [ReadFile, ReadFile, WriteFile] → [group(2), single(WriteFile)]
 */
export function aggregateToolCalls(items: ConversationItem[]): AggregatedToolCall[] {
  const result: AggregatedToolCall[] = []
  let i = 0

  while (i < items.length) {
    const item = items[i]
    const toolName = item.toolName ?? ''

    if (AGGREGATABLE_TOOLS.has(toolName)) {
      // Collect consecutive aggregatable items
      const group: ConversationItem[] = [item]
      while (i + 1 < items.length) {
        const next = items[i + 1]
        if (AGGREGATABLE_TOOLS.has(next.toolName ?? '')) {
          group.push(next)
          i++
        } else {
          break
        }
      }

      if (group.length === 1) {
        // Single explore tool — emit as individual card with explore label
        result.push({ kind: 'single', item: group[0] })
      } else {
        result.push({
          kind: 'group',
          items: group,
          label: `Explored ${group.length} files`
        })
      }
    } else {
      result.push({ kind: 'single', item })
    }

    i++
  }

  return result
}
