import { translate, type AppLocale } from '../../shared/locales'
import { isShellToolName } from './shellTools'
import {
  CRON_TOOL_NAME,
  formatCronCollapsedLabel
} from './cronToolDisplay'
import {
  formatInvocationDisplay,
  invocationNeedsCallingPrefix,
  isWebToolName
} from './webToolDisplay'

type ToolArgs = Record<string, unknown> | undefined
type PlanTodoLike = { id?: string; content?: string }

const EXPLORE_TOOLS = new Set(['ReadFile', 'GrepFiles', 'FindFiles', 'ListDirectory'])
export const FILE_WRITE_TOOLS = new Set(['WriteFile', 'EditFile'])

function getFilename(path: string): string {
  return path.split(/[\\/]/).pop() ?? path
}

function toPositiveInt(value: unknown): number | undefined {
  if (typeof value === 'number' && Number.isFinite(value) && value > 0) {
    return Math.floor(value)
  }
  if (typeof value === 'string' && value.trim().length > 0) {
    const parsed = Number.parseInt(value, 10)
    if (Number.isFinite(parsed) && parsed > 0) {
      return parsed
    }
  }
  return undefined
}

function formatReadFileLabel(args: ToolArgs, locale: AppLocale): string | null {
  const path = typeof args?.path === 'string' ? args.path : ''
  if (!path) return null

  const filename = getFilename(path)
  const start = toPositiveInt(args?.offset)
  const limit = toPositiveInt(args?.limit)

  if (start && limit) {
    const end = start + limit - 1
    return translate(locale, 'toolCall.readFile.range', { filename, start, end })
  }

  if (start) {
    return translate(locale, 'toolCall.readFile.from', { filename, start })
  }

  return translate(locale, 'toolCall.readFile.single', { filename })
}

function normalizeStatus(value: unknown): string {
  return typeof value === 'string' ? value.trim().toLowerCase() : ''
}

function normalizeText(value: unknown): string {
  return typeof value === 'string' ? value.trim() : ''
}

function truncateSummary(content: string, maxLen = 28): string {
  const compact = content.replace(/\s+/g, ' ').trim()
  if (compact.length <= maxLen) return compact
  return `${compact.slice(0, maxLen)}...`
}

function asObjectArray(value: unknown): Array<Record<string, unknown>> {
  if (!Array.isArray(value)) return []
  return value.filter((entry): entry is Record<string, unknown> => (
    typeof entry === 'object' && entry != null
  ))
}

function getTodoWriteSummary(args: ToolArgs, preferStarted: boolean): string | null {
  const todos = asObjectArray(args?.todos)
  if (todos.length === 0) return null

  const candidate = preferStarted
    ? todos.find((todo) => normalizeStatus(todo.status) === 'in_progress') ?? todos[0]
    : todos[0]

  const content = typeof candidate.content === 'string' ? candidate.content : ''
  if (!content.trim()) return null
  return truncateSummary(content)
}

function getUpdateTodosSummary(
  args: ToolArgs,
  planTodos: readonly PlanTodoLike[] | undefined,
  preferStarted: boolean
): string | null {
  const updates = asObjectArray(args?.updates)
  if (updates.length === 0 || !planTodos || planTodos.length === 0) return null

  const candidate = preferStarted
    ? updates.find((update) => normalizeStatus(update.status) === 'in_progress') ?? updates[0]
    : updates[0]

  const id = normalizeText(candidate.id)
  if (!id) return null

  const matched = planTodos.find((todo) => normalizeText(todo?.id) === id)
  const content = normalizeText(matched?.content)
  if (!content) return null
  return truncateSummary(content)
}

function formatTodoLabel(
  toolName: string,
  args: ToolArgs,
  locale: AppLocale,
  planTodos: readonly PlanTodoLike[] | undefined
): string | null {
  if (toolName !== 'TodoWrite' && toolName !== 'UpdateTodos') return null

  if (toolName === 'TodoWrite') {
    const merge = args?.merge === true
    const todos = asObjectArray(args?.todos)
    const hasInProgress = todos.some((todo) => normalizeStatus(todo.status) === 'in_progress')
    const started = merge && hasInProgress
    const summary = getTodoWriteSummary(args, started)

    if (!merge) {
      return summary
        ? translate(locale, 'toolCall.todo.createWithSummary', { summary })
        : translate(locale, 'toolCall.todo.create')
    }

    if (started) {
      return summary
        ? translate(locale, 'toolCall.todo.startedWithSummary', { summary })
        : translate(locale, 'toolCall.todo.started')
    }

    return summary
      ? translate(locale, 'toolCall.todo.updatedWithSummary', { summary })
      : translate(locale, 'toolCall.todo.updated')
  }

  const updates = asObjectArray(args?.updates)
  const hasInProgress = updates.some((update) => normalizeStatus(update.status) === 'in_progress')
  const summary = getUpdateTodosSummary(args, planTodos, hasInProgress)

  if (hasInProgress) {
    return summary
      ? translate(locale, 'toolCall.todo.startedWithSummary', { summary })
      : translate(locale, 'toolCall.todo.started')
  }

  return summary
    ? translate(locale, 'toolCall.todo.updatedWithSummary', { summary })
    : translate(locale, 'toolCall.todo.updated')
}

export interface ToolCallDisplayOptions {
  planTodos?: readonly PlanTodoLike[]
}

export function formatCollapsedToolLabel(
  toolName: string,
  args: ToolArgs,
  locale: AppLocale,
  options?: ToolCallDisplayOptions
): string {
  if (toolName === 'ReadFile') {
    const readLabel = formatReadFileLabel(args, locale)
    if (readLabel) return readLabel
  }

  const todoLabel = formatTodoLabel(toolName, args, locale, options?.planTodos)
  if (todoLabel) return todoLabel

  if (EXPLORE_TOOLS.has(toolName)) {
    const path = (args?.path as string | undefined) ?? (args?.pattern as string | undefined) ?? ''
    const filename = path ? getFilename(path) : ''
    return filename
      ? translate(locale, 'toolCall.explored', { filename })
      : translate(locale, 'toolCall.exploredFiles')
  }

  if (FILE_WRITE_TOOLS.has(toolName)) {
    const path = (args?.path as string | undefined) ?? ''
    const filename = path ? getFilename(path) : ''
    return filename
      ? translate(locale, 'toolCall.edited', { filename })
      : translate(locale, 'toolCall.editedFile')
  }

  if (isShellToolName(toolName)) {
    const cmd = (args?.command as string | undefined) ?? toolName
    const short = cmd.length > 40 ? cmd.slice(0, 40) + '…' : cmd
    return translate(locale, 'toolCall.ran', { cmd: short })
  }

  if (toolName === CRON_TOOL_NAME) {
    return formatCronCollapsedLabel(args, locale)
  }

  if (isWebToolName(toolName)) {
    const inv = formatInvocationDisplay(toolName, args, locale)
    if (inv) return inv
  }

  return translate(locale, 'toolCall.called', { toolName })
}

function formatGenericInvocation(toolName: string, args: ToolArgs): string | null {
  if (!args || Object.keys(args).length === 0) return null
  const entries = Object.entries(args).map(([k, v]) => `${k}: ${JSON.stringify(v)}`)
  return `${toolName}(${entries.join(', ')})`
}

export function formatExpandedInvocation(
  toolName: string,
  args: ToolArgs,
  locale: AppLocale,
  options?: ToolCallDisplayOptions
): string | null {
  if (toolName === 'ReadFile') {
    return formatReadFileLabel(args, locale) ?? formatGenericInvocation(toolName, args)
  }

  if (toolName === 'TodoWrite' || toolName === 'UpdateTodos') {
    return formatTodoLabel(toolName, args, locale, options?.planTodos)
      ?? formatGenericInvocation(toolName, args)
  }

  if (isWebToolName(toolName) && !invocationNeedsCallingPrefix(toolName, args)) {
    return formatInvocationDisplay(toolName, args, locale)
  }

  return formatGenericInvocation(toolName, args)
}
