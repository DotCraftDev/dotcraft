import { translate, type AppLocale } from '../../shared/locales'
import type { ConversationItem } from '../types/conversation'
import type { FileDiff } from '../types/toolCall'
import type { ToolGroupCategory } from './toolCallAggregation'

function getPathArgument(item: ConversationItem): string {
  const args = item.arguments
  return typeof args?.path === 'string' ? args.path : ''
}

function lookupChangedFile(path: string, changedFiles: Map<string, FileDiff>): FileDiff | undefined {
  return (
    changedFiles.get(path)
    ?? changedFiles.get(path.replace(/\\/g, '/'))
    ?? changedFiles.get(path.replace(/\//g, '\\'))
  )
}

function getWriteCounts(items: ConversationItem[], changedFiles: Map<string, FileDiff>): {
  createdCount: number
  modifiedCount: number
} {
  let createdCount = 0
  let modifiedCount = 0

  for (const item of items) {
    if (item.toolName === 'EditFile') {
      modifiedCount += 1
      continue
    }

    if (item.toolName === 'WriteFile') {
      const path = getPathArgument(item)
      const diff = path ? lookupChangedFile(path, changedFiles) : undefined
      if (diff?.isNewFile === true) {
        createdCount += 1
      } else {
        modifiedCount += 1
      }
      continue
    }

    modifiedCount += 1
  }

  return { createdCount, modifiedCount }
}

export function formatToolGroupLabel(
  category: ToolGroupCategory,
  items: ConversationItem[],
  locale: AppLocale,
  changedFiles: Map<string, FileDiff>
): string {
  const count = items.length

  if (category === 'explore') {
    return translate(locale, 'toolCall.group.explored', { count })
  }

  if (category === 'shell') {
    return translate(locale, 'toolCall.group.ran', { count })
  }

  const { createdCount, modifiedCount } = getWriteCounts(items, changedFiles)
  if (createdCount > 0 && modifiedCount > 0) {
    return translate(locale, 'toolCall.group.createdAndModified', {
      created: createdCount,
      modified: modifiedCount,
      count
    })
  }
  if (createdCount > 0) {
    return translate(locale, 'toolCall.group.created', { count: createdCount })
  }
  return translate(locale, 'toolCall.group.modified', { count: modifiedCount || count })
}
