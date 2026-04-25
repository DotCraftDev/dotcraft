import { useEffect, useMemo, useRef, useState, type CSSProperties } from 'react'
import { Check, ChevronDown, ChevronUp, Copy } from 'lucide-react'
import { translate, type AppLocale } from '../../../shared/locales'
import type { ConversationItem } from '../../types/conversation'
import { addToast } from '../../stores/toastStore'
import { extractPartialJsonStringValue } from '../../utils/toolCallDisplay'
import { MarkdownRenderer } from './MarkdownRenderer'
import { ActionTooltip } from '../ui/ActionTooltip'

interface CreatePlanCardProps {
  item: ConversationItem
  locale: AppLocale
}

interface PlanTodo {
  id: string
  content: string
  status: 'pending' | 'in_progress' | 'completed' | 'cancelled'
}

interface ParsedCreatePlan {
  title: string
  overview: string
  content: string
  todos: PlanTodo[]
}

const STATUS_ICON: Record<PlanTodo['status'], string> = {
  pending: '○',
  in_progress: '◉',
  completed: '✓',
  cancelled: '✗'
}

export function hasCreatePlanDisplayData(item: ConversationItem): boolean {
  const parsed = parseCreatePlanData(item)
  return (
    parsed.title.trim().length > 0
    || parsed.overview.trim().length > 0
    || parsed.content.trim().length > 0
    || parsed.todos.length > 0
  )
}

export function CreatePlanCard({ item, locale }: CreatePlanCardProps): JSX.Element {
  const [expanded, setExpanded] = useState(false)
  const [copied, setCopied] = useState(false)
  const [elapsedMs, setElapsedMs] = useState(0)
  const copyResetTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  const parsed = useMemo(() => parseCreatePlanData(item), [item])
  const isRunning = item.status !== 'completed'

  useEffect(() => {
    const start = item.createdAt ? new Date(item.createdAt).getTime() : Date.now()
    if (!isRunning) {
      setElapsedMs(Math.max(0, Date.now() - start))
      return
    }
    setElapsedMs(Math.max(0, Date.now() - start))
    const interval = setInterval(() => {
      setElapsedMs(Math.max(0, Date.now() - start))
    }, 100)
    return () => clearInterval(interval)
  }, [isRunning, item.createdAt])

  const runningElapsedLabel = `${(elapsedMs / 1000).toFixed(1)}s`
  const title =
    parsed.title.trim().length > 0
      ? parsed.title
      : translate(locale, 'toolCall.plan.collapsedLabelFallback')
  const copyContent = useMemo(() => buildCopyContent(parsed, title), [parsed, title])
  const hasContent = parsed.content.trim().length > 0
  const hasTodos = parsed.todos.length > 0
  const canExpand = hasContent || hasTodos

  useEffect(() => {
    return () => {
      if (copyResetTimerRef.current != null) {
        clearTimeout(copyResetTimerRef.current)
        copyResetTimerRef.current = null
      }
    }
  }, [])

  useEffect(() => {
    if (copyResetTimerRef.current != null) {
      clearTimeout(copyResetTimerRef.current)
      copyResetTimerRef.current = null
    }
    setCopied(false)
  }, [item.id])

  async function handleCopy(): Promise<void> {
    if (!copyContent) return
    try {
      await navigator.clipboard.writeText(copyContent)
      setCopied(true)
      addToast(translate(locale, 'toast.copied'), 'success', 2000)
      if (copyResetTimerRef.current != null) {
        clearTimeout(copyResetTimerRef.current)
      }
      copyResetTimerRef.current = setTimeout(() => {
        setCopied(false)
        copyResetTimerRef.current = null
      }, 1500)
    } catch {
      // Ignore clipboard failures silently.
    }
  }

  const copyButton = copyContent ? (
    <IconButton
      icon={copied ? <Check size={14} aria-hidden /> : <Copy size={14} aria-hidden />}
      ariaLabel={translate(locale, copied ? 'toolCall.plan.copiedAria' : 'toolCall.plan.copyAria')}
      active={copied}
      onClick={() => {
        void handleCopy()
      }}
    />
  ) : null

  const expandButton = canExpand ? (
    <IconButton
      icon={expanded ? <ChevronUp size={14} aria-hidden /> : <ChevronDown size={14} aria-hidden />}
      ariaLabel={translate(locale, expanded ? 'toolCall.plan.collapseAria' : 'toolCall.plan.expandAria')}
      onClick={() => {
        setExpanded((v) => !v)
      }}
    />
  ) : null

  return (
    <div
      style={{
        border: '1px solid var(--border-default)',
        borderRadius: '8px',
        background: 'var(--bg-secondary)',
        padding: '10px',
        position: 'relative'
      }}
    >
      <div
        style={{
          position: 'absolute',
          top: '8px',
          right: '10px',
          display: 'flex',
          alignItems: 'center',
          gap: '6px'
        }}
      >
        {isRunning && (
          <div style={{ display: 'flex', alignItems: 'center', gap: '6px', color: 'var(--text-dimmed)', fontSize: '11px' }}>
            <span className="animate-spin-custom" style={spinnerStyle} />
            <span>{runningElapsedLabel}</span>
          </div>
        )}
        {copyButton}
        {expandButton}
      </div>

      <div
        style={{
          display: 'inline-flex',
          alignItems: 'center',
          padding: '2px 8px',
          borderRadius: '999px',
          background: 'var(--bg-tertiary)',
          color: 'var(--text-dimmed)',
          fontSize: '11px',
          marginBottom: '8px'
        }}
      >
        {translate(locale, 'toolCall.plan.previewBadge')}
      </div>

      <h3 style={{ margin: '0 0 8px', color: 'var(--text-primary)', fontSize: '18px', fontWeight: 700 }}>
        {title}
      </h3>

      {parsed.overview.trim().length > 0 && (
        <p style={{ margin: '0 0 10px', color: 'var(--text-secondary)', whiteSpace: 'pre-wrap', lineHeight: 1.5 }}>
          {parsed.overview}
        </p>
      )}

      {hasContent && (
        <div style={{ position: 'relative', paddingBottom: expanded ? 0 : '28px' }}>
          <div
            style={expanded ? undefined : {
              maxHeight: '220px',
              overflow: 'hidden',
              maskImage: 'linear-gradient(to bottom, #000 65%, transparent)',
              WebkitMaskImage: 'linear-gradient(to bottom, #000 65%, transparent)'
            }}
          >
            <MarkdownRenderer content={parsed.content} />
          </div>
          {!expanded && (
            <button
              type="button"
              onClick={() => {
                setExpanded(true)
              }}
              style={{
                ...expandToggleStyle,
                position: 'absolute',
                left: '50%',
                transform: 'translateX(-50%)',
                bottom: 0
              }}
            >
              {translate(locale, 'toolCall.plan.expandButton')}
            </button>
          )}
        </div>
      )}

      {expanded && hasTodos && (
        <ul
          style={{
            margin: hasContent ? '12px 0 0' : 0,
            padding: 0,
            listStyle: 'none',
            display: 'grid',
            gap: '6px',
            color: 'var(--text-primary)',
            fontSize: '14px',
            lineHeight: 1.6
          }}
        >
          {parsed.todos.map((todo) => {
            const isCancelled = todo.status === 'cancelled'
            return (
              <li key={todo.id} style={{ display: 'flex', alignItems: 'flex-start', gap: '8px' }}>
                <span
                  style={{
                    width: '16px',
                    textAlign: 'center',
                    flexShrink: 0,
                    color: todoStatusColor(todo.status)
                  }}
                >
                  {STATUS_ICON[todo.status]}
                </span>
                <span
                  style={{
                    textDecoration: isCancelled ? 'line-through' : 'none',
                    color: isCancelled ? 'var(--text-dimmed)' : 'var(--text-primary)'
                  }}
                >
                  {todo.content}
                </span>
              </li>
            )
          })}
        </ul>
      )}
    </div>
  )
}

function todoStatusColor(status: PlanTodo['status']): string {
  if (status === 'completed') return 'var(--success)'
  if (status === 'in_progress') return 'var(--accent)'
  if (status === 'cancelled') return 'var(--text-dimmed)'
  return 'var(--text-secondary)'
}

function buildCopyContent(parsed: ParsedCreatePlan, fallbackTitle: string): string {
  if (parsed.content.trim().length > 0) {
    return parsed.content
  }
  const blocks: string[] = []
  if (fallbackTitle.trim().length > 0) {
    blocks.push(`# ${fallbackTitle.trim()}`)
  }
  if (parsed.overview.trim().length > 0) {
    blocks.push(parsed.overview.trim())
  }
  if (parsed.todos.length > 0) {
    blocks.push(parsed.todos.map((todo) => `- ${todo.content}`).join('\n'))
  }
  return blocks.join('\n\n').trim()
}

function parseCreatePlanData(item: ConversationItem): ParsedCreatePlan {
  const title = typeof item.arguments?.title === 'string'
    ? item.arguments.title
    : (extractPartialJsonStringValue(item.argumentsPreview ?? '', 'title') ?? '')
  const overview = typeof item.arguments?.overview === 'string'
    ? item.arguments.overview
    : (extractPartialJsonStringValue(item.argumentsPreview ?? '', 'overview') ?? '')
  const content = typeof item.arguments?.plan === 'string'
    ? item.arguments.plan
    : (extractPartialJsonStringValue(item.argumentsPreview ?? '', 'plan') ?? '')
  const todos = Array.isArray(item.arguments?.todos)
    ? item.arguments.todos
      .filter((entry): entry is Record<string, unknown> => typeof entry === 'object' && entry != null)
      .map((entry, index) => ({
        id: typeof entry.id === 'string' && entry.id.trim().length > 0 ? entry.id : `todo-${index}`,
        content: typeof entry.content === 'string' ? entry.content : '',
        status: normalizeTodoStatus(entry.status)
      }))
      .filter((todo) => todo.content.trim().length > 0)
    : []

  return { title, overview, content, todos }
}

function normalizeTodoStatus(value: unknown): PlanTodo['status'] {
  if (value === 'in_progress' || value === 'completed' || value === 'cancelled') {
    return value
  }
  return 'pending'
}

const expandToggleStyle: CSSProperties = {
  border: '1px solid var(--border-default)',
  borderRadius: '999px',
  padding: '4px 10px',
  background: 'var(--bg-primary)',
  color: 'var(--text-secondary)',
  cursor: 'pointer',
  fontSize: '12px'
}

const spinnerStyle: CSSProperties = {
  display: 'inline-block',
  width: '10px',
  height: '10px',
  borderRadius: '50%',
  border: '2px solid var(--border-active)',
  borderTopColor: 'var(--accent)'
}

function IconButton(
  {
    icon,
    ariaLabel,
    active = false,
    onClick
  }: {
    icon: JSX.Element
    ariaLabel: string
    active?: boolean
    onClick: () => void
  }
): JSX.Element {
  return (
    <ActionTooltip label={ariaLabel} placement="top">
      <button
        type="button"
        aria-label={ariaLabel}
        onClick={onClick}
        style={{
        width: '24px',
        height: '24px',
        borderRadius: '6px',
        border: '1px solid var(--border-default)',
        background: 'var(--bg-secondary)',
        color: active ? 'var(--success)' : 'var(--text-secondary)',
        display: 'inline-flex',
        alignItems: 'center',
        justifyContent: 'center',
        cursor: 'pointer'
      }}
    >
      {icon}
      </button>
    </ActionTooltip>
  )
}
