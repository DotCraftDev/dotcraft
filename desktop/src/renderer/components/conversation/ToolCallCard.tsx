import { memo, useEffect, useRef, useState } from 'react'
import { translate, type AppLocale } from '../../../shared/locales'
import type { ConversationItem } from '../../types/conversation'
import { useLocale } from '../../contexts/LocaleContext'
import { useConversationStore } from '../../stores/conversationStore'
import {
  CRON_TOOL_NAME,
  formatCronRunningLabel,
  formatCronResultLines
} from '../../utils/cronToolDisplay'
import {
  formatInvocationDisplay,
  formatResultSummary,
  getWebToolIcon,
  getWebToolSectionLabel,
  invocationNeedsCallingPrefix,
  isWebToolName
} from '../../utils/webToolDisplay'
import { InlineDiffView } from './InlineDiffView'
import { isShellToolName } from '../../utils/shellTools'
import {
  FILE_WRITE_TOOLS,
  extractPartialJsonStringValue,
  formatCollapsedToolLabel,
  formatExpandedInvocation,
  getStreamingToolDisplay
} from '../../utils/toolCallDisplay'
import { PlanToolOutput } from './PlanToolOutput'
import { CreatePlanCard, hasCreatePlanDisplayData } from './CreatePlanCard'
import { ToolCollapseChevron } from './ToolCollapseChevron'

interface ToolCallCardProps {
  item: ConversationItem
  turnId: string
}

const COLLAPSIBLE_TRANSITION_MS = 200

function isShellExecutionRunning(item: ConversationItem, isShellTool: boolean): boolean {
  if (!isShellTool) return false
  if (item.executionStatus != null) {
    if (item.executionStatus === 'inProgress') return true
    // Legacy: wire item lifecycle "started" was mistakenly stored as executionStatus
    if (String(item.executionStatus) === 'started') return true
    return false
  }
  if (item.status !== 'completed') return true
  // ToolCall item/completed marks invocation done before the shell finishes; keep the live
  // timer until toolResult merges (result + success), or until executionStatus is merged.
  const toolResultPending = item.result === undefined && item.success === undefined
  return toolResultPending
}

export const ToolCallCard = memo(function ToolCallCard({
  item,
  turnId
}: ToolCallCardProps): JSX.Element {
  const locale = useLocale()
  const [expanded, setExpanded] = useState(false)
  const [renderExpanded, setRenderExpanded] = useState(false)
  const [autoExpanded, setAutoExpanded] = useState(false)
  const [userInteracted, setUserInteracted] = useState(false)
  const [elapsedMs, setElapsedMs] = useState(0)
  const autoExpandTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)

  const toolName = item.toolName ?? 'tool'
  const args = item.arguments
  const isShellTool = isShellToolName(toolName)
  const isStreamingFileTool = FILE_WRITE_TOOLS.has(toolName)
  const autoExpandEligible = isShellTool || isStreamingFileTool
  const canExpandWhileRunning = true
  const streamingDisplay = getStreamingToolDisplay(
    toolName,
    item.argumentsPreview ?? null,
    locale
  )
  const shellExecutionRunning = isShellExecutionRunning(item, isShellTool)
  const isRunning = isShellTool ? shellExecutionRunning : item.status !== 'completed'
  const shellOutput = item.aggregatedOutput ?? item.result ?? ''
  const shellFailed = item.executionStatus === 'failed'
    || item.executionStatus === 'cancelled'
    || (item.exitCode != null && item.exitCode !== 0)
  const success = item.success !== false && !shellFailed

  useEffect(() => {
    if (expanded) {
      setRenderExpanded(true)
    }
  }, [expanded])

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

  const itemDiffs = useConversationStore((s) => s.itemDiffs)
  const streamingItemDiffs = useConversationStore((s) => s.streamingItemDiffs)
  const plan = useConversationStore((s) => s.plan)
  const planTodos = plan?.todos
  const fileDiff = FILE_WRITE_TOOLS.has(toolName) ? itemDiffs.get(item.id) : undefined
  const streamingFileDiff = FILE_WRITE_TOOLS.has(toolName) ? streamingItemDiffs.get(item.id) : undefined

  function toggleExpand(): void {
    if (!isRunning || canExpandWhileRunning) {
      setUserInteracted(true)
      if (autoExpandTimerRef.current != null) {
        clearTimeout(autoExpandTimerRef.current)
        autoExpandTimerRef.current = null
      }
      setAutoExpanded(false)
      setExpanded((v) => !v)
    }
  }

  useEffect(() => {
    if (!autoExpandEligible) {
      if (autoExpandTimerRef.current != null) {
        clearTimeout(autoExpandTimerRef.current)
        autoExpandTimerRef.current = null
      }
      if (autoExpanded) {
        setAutoExpanded(false)
      }
      return
    }

    if (isRunning) {
      if (!userInteracted && !expanded && autoExpandTimerRef.current == null) {
        autoExpandTimerRef.current = setTimeout(() => {
          setExpanded(true)
          setAutoExpanded(true)
          autoExpandTimerRef.current = null
        }, 400)
      }
      return
    }

    if (autoExpandTimerRef.current != null) {
      clearTimeout(autoExpandTimerRef.current)
      autoExpandTimerRef.current = null
    }

    const shouldAutoCollapse = !userInteracted && expanded && autoExpanded
    if (shouldAutoCollapse) {
      setExpanded(false)
      setAutoExpanded(false)
      return
    }

    if (autoExpanded) {
      setAutoExpanded(false)
    }
  }, [autoExpandEligible, autoExpanded, expanded, isRunning, userInteracted])

  useEffect(() => {
    return () => {
      if (autoExpandTimerRef.current != null) {
        clearTimeout(autoExpandTimerRef.current)
        autoExpandTimerRef.current = null
      }
    }
  }, [])

  if (toolName === 'CreatePlan' && hasCreatePlanDisplayData(item)) {
    return <CreatePlanCard item={item} locale={locale} />
  }

  if (isRunning) {
    return (
      <div
        style={{
          borderRadius: '4px',
          overflow: 'hidden',
          border: expanded ? '1px solid var(--border-default)' : 'none'
        }}
      >
        <button
          onClick={toggleExpand}
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: '8px',
            width: '100%',
            padding: '4px 8px',
            background: expanded ? 'var(--bg-tertiary)' : 'transparent',
            border: 'none',
            borderBottom: expanded ? '1px solid var(--border-default)' : 'none',
            borderRadius: expanded ? '4px 4px 0 0' : '4px',
            color: 'var(--text-secondary)',
            fontSize: '13px',
            textAlign: 'left',
            cursor: canExpandWhileRunning ? 'pointer' : 'default'
          }}
        >
          <Spinner />
          <span style={{ flex: 1 }}>
            {isShellTool && args ? (
              <span style={{ color: 'var(--text-primary)' }}>
                {formatCollapsedToolLabel(toolName, args, locale, { planTodos })}
              </span>
            ) : toolName === CRON_TOOL_NAME && args ? (
              <span style={{ color: 'var(--text-primary)' }}>
                {formatCronRunningLabel(args, locale)}
              </span>
            ) : isWebToolName(toolName) && args && !invocationNeedsCallingPrefix(toolName, args) ? (
              <span style={{ color: 'var(--text-primary)' }}>
                {formatInvocationDisplay(toolName, args, locale)}
              </span>
            ) : (
              <span style={{ color: 'var(--text-primary)' }}>
                {streamingDisplay.label}
              </span>
            )}
          </span>
          <span style={{ color: 'var(--text-dimmed)', marginLeft: '8px', flexShrink: 0 }}>
            {runningElapsedLabel}
          </span>
          {canExpandWhileRunning && <ToolCollapseChevron expanded={expanded} />}
        </button>

        <CollapsibleContent
          expanded={expanded && canExpandWhileRunning}
          renderExpanded={renderExpanded && canExpandWhileRunning}
          setRenderExpanded={setRenderExpanded}
        >
          <div style={{ background: 'var(--bg-secondary)', padding: '8px' }}>
            {isShellTool ? (
              <ExpandedContent
                itemId={item.id}
                toolName={toolName}
                args={args}
                result={shellOutput}
                success={!shellFailed}
                fileDiff={undefined}
                locale={locale}
                planTodos={planTodos}
              />
            ) : isStreamingFileTool ? (
              streamingFileDiff ? (
                <InlineDiffView diff={streamingFileDiff} streaming />
              ) : (
                <RunningFileToolPreview
                  item={item}
                  locale={locale}
                  streamingPath={streamingDisplay.parsedPreview?.path ?? null}
                />
              )
            ) : (
              <RunningGenericToolPreview
                toolName={toolName}
                args={args}
                locale={locale}
                planTodos={planTodos}
              />
            )}
          </div>
        </CollapsibleContent>
      </div>
    )
  }

  const label = formatCollapsedToolLabel(toolName, args, locale, { planTodos })

  return (
    <div
      style={{
        borderRadius: '4px',
        overflow: 'hidden',
        border: expanded ? '1px solid var(--border-default)' : 'none'
      }}
    >
      <button
        onClick={toggleExpand}
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: '6px',
          width: '100%',
          padding: '3px 6px',
          background: expanded ? 'var(--bg-tertiary)' : 'transparent',
          border: 'none',
          borderBottom: expanded ? '1px solid var(--border-default)' : 'none',
          cursor: 'pointer',
          color: 'var(--text-secondary)',
          fontSize: '12px',
          textAlign: 'left',
          borderRadius: expanded ? '4px 4px 0 0' : '4px'
        }}
      >
        {success ? (
          <span style={{ color: 'var(--success)', fontSize: '11px', lineHeight: 1 }}>✓</span>
        ) : (
          <span style={{ color: 'var(--error)', fontSize: '11px', lineHeight: 1 }}>✕</span>
        )}

        <span style={{ flex: 1 }}>
          {success ? label : translate(locale, 'toolCall.failed', { label })}
          {!success && (item.result || shellOutput) && (
            <span style={{ color: 'var(--error)', marginLeft: '6px' }}>
              - {(item.result ?? shellOutput).slice(0, 80)}{(item.result ?? shellOutput).length > 80 ? '…' : ''}
            </span>
          )}
        </span>

        {item.duration !== undefined && item.duration > 0 && (
          <span style={{ color: 'var(--text-dimmed)', marginLeft: '8px' }}>
            {item.duration < 1000
              ? `${item.duration}ms`
              : `${(item.duration / 1000).toFixed(1)}s`}
          </span>
        )}

        <ToolCollapseChevron expanded={expanded} />
      </button>

      <CollapsibleContent
        expanded={expanded}
        renderExpanded={renderExpanded}
        setRenderExpanded={setRenderExpanded}
      >
        <div style={{ background: 'var(--bg-secondary)', padding: '8px' }}>
          <ExpandedContent
            itemId={item.id}
            toolName={toolName}
            args={args}
            result={isShellTool ? shellOutput : item.result}
            success={success}
            fileDiff={fileDiff ? { diff: fileDiff } : undefined}
            locale={locale}
            planTodos={planTodos}
          />
        </div>
      </CollapsibleContent>
    </div>
  )
})

interface CollapsibleContentProps {
  expanded: boolean
  renderExpanded: boolean
  setRenderExpanded: (value: boolean) => void
  children: JSX.Element
}

function CollapsibleContent({
  expanded,
  renderExpanded,
  setRenderExpanded,
  children
}: CollapsibleContentProps): JSX.Element | null {
  const contentRef = useRef<HTMLDivElement | null>(null)
  const animationFrameRef = useRef<number | null>(null)
  const transitionTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  const [height, setHeight] = useState<string>('0px')
  const [opacity, setOpacity] = useState(0)
  const [translateY, setTranslateY] = useState('-2px')

  useEffect(() => {
    if (animationFrameRef.current != null) {
      cancelAnimationFrame(animationFrameRef.current)
      animationFrameRef.current = null
    }
    if (transitionTimerRef.current != null) {
      clearTimeout(transitionTimerRef.current)
      transitionTimerRef.current = null
    }

    if (!renderExpanded) {
      setHeight('0px')
      setOpacity(0)
      setTranslateY('-2px')
      return
    }

    const measuredHeight = contentRef.current?.scrollHeight ?? 0

    if (expanded) {
      setHeight('0px')
      setOpacity(0)
      setTranslateY('-2px')
      animationFrameRef.current = requestAnimationFrame(() => {
        setHeight(`${measuredHeight}px`)
        setOpacity(1)
        setTranslateY('0px')
        animationFrameRef.current = null
      })
      transitionTimerRef.current = setTimeout(() => {
        setHeight('auto')
        transitionTimerRef.current = null
      }, COLLAPSIBLE_TRANSITION_MS)
      return
    }

    setHeight(`${measuredHeight}px`)
    setOpacity(1)
    setTranslateY('0px')
    animationFrameRef.current = requestAnimationFrame(() => {
      setHeight('0px')
      setOpacity(0)
      setTranslateY('-2px')
      animationFrameRef.current = null
    })
    transitionTimerRef.current = setTimeout(() => {
      setRenderExpanded(false)
      transitionTimerRef.current = null
    }, COLLAPSIBLE_TRANSITION_MS)

    return () => {
      if (animationFrameRef.current != null) {
        cancelAnimationFrame(animationFrameRef.current)
        animationFrameRef.current = null
      }
      if (transitionTimerRef.current != null) {
        clearTimeout(transitionTimerRef.current)
        transitionTimerRef.current = null
      }
    }
  }, [expanded, renderExpanded, setRenderExpanded])

  useEffect(() => {
    return () => {
      if (animationFrameRef.current != null) {
        cancelAnimationFrame(animationFrameRef.current)
        animationFrameRef.current = null
      }
      if (transitionTimerRef.current != null) {
        clearTimeout(transitionTimerRef.current)
        transitionTimerRef.current = null
      }
    }
  }, [])

  if (!renderExpanded) {
    return null
  }

  return (
    <div
      aria-hidden={!expanded}
      style={{
        overflow: 'hidden',
        height,
        opacity,
        transform: `translateY(${translateY})`,
        transition: `height ${COLLAPSIBLE_TRANSITION_MS}ms ease-out, opacity ${COLLAPSIBLE_TRANSITION_MS}ms ease-out, transform ${COLLAPSIBLE_TRANSITION_MS}ms ease-out`
      }}
    >
      <div ref={contentRef}>
        {children}
      </div>
    </div>
  )
}

interface ExpandedContentProps {
  itemId: string
  toolName: string
  args: Record<string, unknown> | undefined
  result: string | undefined
  success: boolean
  fileDiff: { diff: import('../../types/toolCall').FileDiff } | undefined
  locale: AppLocale
  planTodos?: Array<{ id: string; content: string }>
}

function ExpandedContent({
  itemId,
  toolName,
  args,
  result,
  success,
  fileDiff,
  locale,
  planTodos
}: ExpandedContentProps): JSX.Element {
  if (toolName === 'CreatePlan') {
    const parsedPlan = parseCompletedCreatePlanArgs(args)
    return (
      <PlanToolOutput
        itemId={itemId}
        title={parsedPlan.title}
        overview={parsedPlan.overview}
        content={parsedPlan.content}
        todos={parsedPlan.todos}
        locale={locale}
      />
    )
  }

  if (FILE_WRITE_TOOLS.has(toolName) && fileDiff) {
    return <InlineDiffView diff={fileDiff.diff} />
  }

  if (toolName === CRON_TOOL_NAME) {
    const lines = formatCronResultLines(result, locale)
    if (lines && lines.length > 0) {
      const errSample = translate(locale, 'cron.result.errorPrefix', { error: 'x' })
      const errMarker = errSample.indexOf('x')
      const errPrefix = errMarker >= 0 ? errSample.slice(0, errMarker) : 'Error: '
      return (
        <div className="selectable" style={{ fontSize: '12px', lineHeight: 1.5, color: 'var(--text-secondary)' }}>
          <div style={{ color: 'var(--text-dimmed)', marginBottom: '6px', fontSize: '11px', display: 'flex', alignItems: 'center', gap: '6px' }}>
            <span aria-hidden>⏰</span>
            <span>Cron</span>
          </div>
          {lines.map((line, i) => (
            <div key={i} style={{ color: line.startsWith(errPrefix) ? 'var(--error)' : 'var(--text-secondary)' }}>
              {line}
            </div>
          ))}
        </div>
      )
    }
  }

  if (isWebToolName(toolName)) {
    const lines = formatResultSummary(toolName, result)
    const inv = formatInvocationDisplay(toolName, args, locale)
    const section = getWebToolSectionLabel(toolName, locale)
    const icon = getWebToolIcon(toolName)
    const errPrefix = 'Error: '

    if (lines && lines.length > 0) {
      return (
        <div className="selectable" style={{ fontSize: '12px', lineHeight: 1.5, color: 'var(--text-secondary)' }}>
          <div style={{ color: 'var(--text-dimmed)', marginBottom: '6px', fontSize: '11px', display: 'flex', alignItems: 'center', gap: '6px' }}>
            <span aria-hidden>{icon}</span>
            <span>{section}</span>
          </div>
          {inv && (
            <div style={{ color: 'var(--text-dimmed)', marginBottom: '8px', fontSize: '11px', lineHeight: 1.4 }}>
              {inv}
            </div>
          )}
          {lines.map((line, i) => (
            <div key={i} style={{ color: line.startsWith(errPrefix) ? 'var(--error)' : 'var(--text-secondary)', fontFamily: 'var(--font-mono)', whiteSpace: 'pre-wrap', wordBreak: 'break-word' }}>
              {line}
            </div>
          ))}
        </div>
      )
    }
  }

  if (isShellToolName(toolName)) {
    const command = (args?.command as string | undefined) ?? toolName
    const output = result ?? ''
    const outputLines = output.split('\n')
    const preview = outputLines.slice(0, 40)
    const truncated = outputLines.length > 40

    return (
      <div className="selectable" style={{ fontFamily: 'var(--font-mono)', fontSize: '12px', lineHeight: '1.5', color: 'var(--text-secondary)' }}>
        <div style={{ color: 'var(--text-dimmed)', marginBottom: '6px' }}>
          <span style={{ color: 'var(--text-dimmed)' }}>$ </span>
          <span style={{ color: 'var(--text-primary)' }}>{command}</span>
        </div>
        {output ? (
          <pre
            style={{
              margin: 0,
              whiteSpace: 'pre-wrap',
              wordBreak: 'break-all',
              color: success ? 'var(--text-secondary)' : 'var(--error)',
              maxHeight: '200px',
              overflow: 'auto'
            }}
          >
            {preview.join('\n')}
            {truncated && <span style={{ color: 'var(--text-dimmed)' }}>{'\n'}…</span>}
          </pre>
        ) : (
          <div style={{ color: 'var(--text-dimmed)', fontSize: '11px' }}>Waiting for output...</div>
        )}
      </div>
    )
  }

  const resultText = result ?? ''
  const resultLines = resultText.split('\n')
  const preview = resultLines.slice(0, 10)
  const truncated = resultLines.length > 10
  const invocation = formatExpandedInvocation(toolName, args, locale, { planTodos })

  return (
    <div className="selectable" style={{ fontFamily: 'var(--font-mono)', fontSize: '12px', lineHeight: '1.5' }}>
      {invocation && (
        <div style={{ color: 'var(--text-dimmed)', marginBottom: '6px', fontSize: '11px', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
          {invocation}
        </div>
      )}
      {resultText && (
        <pre
          style={{
            margin: 0,
            whiteSpace: 'pre-wrap',
            wordBreak: 'break-all',
            color: success ? 'var(--text-secondary)' : 'var(--error)',
            maxHeight: '160px',
            overflow: 'auto'
          }}
        >
          {preview.join('\n')}
          {truncated && <span style={{ color: 'var(--text-dimmed)' }}>{'\n'}…</span>}
        </pre>
      )}
    </div>
  )
}

function parseCompletedCreatePlanArgs(args: Record<string, unknown> | undefined): {
  title: string
  overview: string
  content: string
  todos: Array<{ id: string; content: string; status: 'pending' | 'in_progress' | 'completed' | 'cancelled' }>
} {
  const title = typeof args?.title === 'string' ? args.title : ''
  const overview = typeof args?.overview === 'string' ? args.overview : ''
  const content = typeof args?.plan === 'string' ? args.plan : ''
  const todos = Array.isArray(args?.todos)
    ? args.todos
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

function normalizeTodoStatus(value: unknown): 'pending' | 'in_progress' | 'completed' | 'cancelled' {
  if (value === 'in_progress' || value === 'completed' || value === 'cancelled') {
    return value
  }
  return 'pending'
}

function Spinner(): JSX.Element {
  return (
    <span
      className="animate-spin-custom"
      style={{
        display: 'inline-block',
        width: '12px',
        height: '12px',
        borderRadius: '50%',
        border: '2px solid var(--border-active)',
        borderTopColor: 'var(--accent)',
        flexShrink: 0
      }}
    />
  )
}

function RunningFileToolPreview(
  {
    item,
    locale,
    streamingPath
  }: {
    item: ConversationItem
    locale: AppLocale
    streamingPath: string | null
  }
): JSX.Element {
  const pathPreview = streamingPath
    ?? extractPartialJsonStringValue(item.argumentsPreview ?? '', 'path')
  const fileName = pathPreview ? pathPreview.split(/[\\/]/).pop() ?? pathPreview : ''
  const isEditFile = item.toolName === 'EditFile'
  const tip = fileName
    ? translate(locale, isEditFile ? 'toolCall.editingFile' : 'toolCall.writingFile', { filename: fileName })
    : translate(locale, isEditFile ? 'toolCall.editingGeneric' : 'toolCall.writingGeneric')

  return (
    <div className="selectable" style={{ fontSize: '12px', lineHeight: '1.5' }}>
      <div style={{ color: 'var(--text-dimmed)', marginBottom: '6px' }}>
        {tip}
      </div>
      <div style={{ color: 'var(--text-dimmed)', fontSize: '11px' }}>
        Waiting for content...
      </div>
    </div>
  )
}

function RunningGenericToolPreview(
  {
    toolName,
    args,
    locale,
    planTodos
  }: {
    toolName: string
    args: Record<string, unknown> | undefined
    locale: AppLocale
    planTodos?: Array<{ id: string; content: string }>
  }
): JSX.Element {
  const invocation = formatExpandedInvocation(toolName, args, locale, { planTodos })
  return (
    <div className="selectable" style={{ fontSize: '12px', lineHeight: '1.5' }}>
      {invocation && (
        <div
          style={{
            color: 'var(--text-dimmed)',
            marginBottom: '6px',
            fontFamily: 'var(--font-mono)',
            whiteSpace: 'pre-wrap',
            wordBreak: 'break-word'
          }}
        >
          {invocation}
        </div>
      )}
      <div style={{ color: 'var(--text-dimmed)', fontSize: '11px' }}>
        {translate(locale, 'toolCall.genericRunning')}
      </div>
    </div>
  )
}
