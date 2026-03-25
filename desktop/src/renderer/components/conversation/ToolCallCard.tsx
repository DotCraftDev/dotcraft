import { memo, useState, useEffect } from 'react'
import { translate, type AppLocale } from '../../../shared/locales'
import type { ConversationItem } from '../../types/conversation'
import { useLocale } from '../../contexts/LocaleContext'
import { useConversationStore } from '../../stores/conversationStore'
import {
  CRON_TOOL_NAME,
  formatCronCollapsedLabel,
  formatCronRunningLabel,
  formatCronResultLines
} from '../../utils/cronToolDisplay'
import { InlineDiffView } from './InlineDiffView'

interface ToolCallCardProps {
  item: ConversationItem
  turnId: string
}

/** Tool names treated as file-reading / exploration tools */
const EXPLORE_TOOLS = new Set(['ReadFile', 'GrepFiles', 'FindFiles', 'ListDirectory'])
/** Tool names that write/edit files and show inline diffs */
const FILE_WRITE_TOOLS = new Set(['WriteFile', 'EditFile'])
/** Tool names that execute shell commands */
const SHELL_TOOLS = new Set(['Exec', 'RunCommand', 'BashCommand'])

/**
 * Returns the collapsed-state label for a completed tool call.
 */
function getCollapsedLabel(
  toolName: string,
  args: Record<string, unknown> | undefined,
  locale: AppLocale
): string {
  if (EXPLORE_TOOLS.has(toolName)) {
    const path = (args?.path as string | undefined) ?? (args?.pattern as string | undefined) ?? ''
    const filename = path ? path.split(/[\\/]/).pop() ?? path : ''
    return filename
      ? translate(locale, 'toolCall.explored', { filename })
      : translate(locale, 'toolCall.exploredFiles')
  }
  if (FILE_WRITE_TOOLS.has(toolName)) {
    const path = (args?.path as string | undefined) ?? ''
    const filename = path ? path.split(/[\\/]/).pop() ?? path : ''
    return filename
      ? translate(locale, 'toolCall.edited', { filename })
      : translate(locale, 'toolCall.editedFile')
  }
  if (SHELL_TOOLS.has(toolName)) {
    const cmd = (args?.command as string | undefined) ?? toolName
    const short = cmd.length > 40 ? cmd.slice(0, 40) + '…' : cmd
    return translate(locale, 'toolCall.ran', { cmd: short })
  }
  if (toolName === CRON_TOOL_NAME) {
    return formatCronCollapsedLabel(args, locale)
  }
  return translate(locale, 'toolCall.called', { toolName })
}

/**
 * Collapsible tool call card. Shows a running spinner while the tool is in-flight,
 * then collapses to a one-line summary on completion.
 * Spec §M4-1 through M4-7.
 */
export const ToolCallCard = memo(function ToolCallCard({ item, turnId }: ToolCallCardProps): JSX.Element {
  const locale = useLocale()
  const [expanded, setExpanded] = useState(false)
  const [elapsed, setElapsed] = useState(0)

  const isRunning = item.status !== 'completed'
  const toolName = item.toolName ?? 'tool'
  const args = item.arguments
  const success = item.success !== false // default true when undefined

  // Live elapsed timer while tool is running
  useEffect(() => {
    if (!isRunning) return
    const start = item.createdAt ? new Date(item.createdAt).getTime() : Date.now()
    const interval = setInterval(() => {
      setElapsed(Math.floor((Date.now() - start) / 1000))
    }, 500)
    return () => clearInterval(interval)
  }, [isRunning, item.createdAt])

  // Per-tool-call incremental diff (cumulative file state lives in changedFiles for the Detail Panel)
  const itemDiffs = useConversationStore((s) => s.itemDiffs)
  const fileDiff = FILE_WRITE_TOOLS.has(toolName) ? itemDiffs.get(item.id) : undefined

  function toggleExpand(): void {
    if (!isRunning) setExpanded((v) => !v)
  }

  // ── Running state ────────────────────────────────────────────────────────
  if (isRunning) {
    return (
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: '8px',
          padding: '4px 8px',
          borderRadius: '4px',
          color: 'var(--text-secondary)',
          fontSize: '13px'
        }}
      >
        <Spinner />
        <span>
          {toolName === CRON_TOOL_NAME ? (
            <span style={{ color: 'var(--text-primary)' }}>
              {formatCronRunningLabel(args, locale)}
            </span>
          ) : (
            <>
              {translate(locale, 'toolCall.calling')}{' '}
              <strong style={{ color: 'var(--text-primary)' }}>{toolName}</strong>
            </>
          )}
          {elapsed > 0 && (
            <span style={{ color: 'var(--text-dimmed)', marginLeft: '6px' }}>
              {elapsed}s
            </span>
          )}
        </span>
      </div>
    )
  }

  // ── Completed state ───────────────────────────────────────────────────────
  const label = getCollapsedLabel(toolName, args, locale)

  return (
    <div
      style={{
        borderRadius: '4px',
        overflow: 'hidden',
        border: expanded ? '1px solid var(--border-default)' : 'none'
      }}
    >
      {/* Collapsed header — always shown */}
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
        {/* Status indicator */}
        {success ? (
          <span style={{ color: 'var(--success)', fontSize: '11px', lineHeight: 1 }}>✓</span>
        ) : (
          <span style={{ color: 'var(--error)', fontSize: '11px', lineHeight: 1 }}>✗</span>
        )}

        {/* Label */}
        <span style={{ flex: 1 }}>
          {success ? label : translate(locale, 'toolCall.failed', { label })}
          {!success && item.result && (
            <span
              style={{ color: 'var(--error)', marginLeft: '6px' }}
            >
              — {item.result.slice(0, 80)}{item.result.length > 80 ? '…' : ''}
            </span>
          )}
        </span>

        {/* Duration */}
        {item.duration !== undefined && item.duration > 0 && (
          <span style={{ color: 'var(--text-dimmed)', marginLeft: '8px' }}>
            {item.duration < 1000
              ? `${item.duration}ms`
              : `${(item.duration / 1000).toFixed(1)}s`}
          </span>
        )}

        {/* Chevron */}
        <span
          style={{
            color: 'var(--text-dimmed)',
            fontSize: '10px',
            transform: expanded ? 'rotate(180deg)' : 'rotate(0deg)',
            transition: 'transform 150ms ease'
          }}
        >
          ▾
        </span>
      </button>

      {/* Expanded content */}
      {expanded && (
        <div style={{ background: 'var(--bg-secondary)', padding: '8px' }}>
          <ExpandedContent
            toolName={toolName}
            args={args}
            result={item.result}
            success={success}
            fileDiff={fileDiff ? { diff: fileDiff } : undefined}
            turnId={turnId}
            locale={locale}
          />
        </div>
      )}
    </div>
  )
})

// ── Expanded content variants ─────────────────────────────────────────────────

interface ExpandedContentProps {
  toolName: string
  args: Record<string, unknown> | undefined
  result: string | undefined
  success: boolean
  fileDiff: { diff: import('../../types/toolCall').FileDiff } | undefined
  turnId: string
  locale: AppLocale
}

function ExpandedContent({
  toolName,
  args,
  result,
  success,
  fileDiff,
  locale
}: ExpandedContentProps): JSX.Element {
  // File diff variant
  if (FILE_WRITE_TOOLS.has(toolName) && fileDiff) {
    return <InlineDiffView diff={fileDiff.diff} />
  }

  // Cron — structured JSON result (aligned with CronToolDisplays.CronResult)
  if (toolName === CRON_TOOL_NAME) {
    const lines = formatCronResultLines(result, locale)
    if (lines && lines.length > 0) {
      const errSample = translate(locale, 'cron.result.errorPrefix', { error: '†' })
      const errMarker = errSample.indexOf('†')
      const errPrefix = errMarker >= 0 ? errSample.slice(0, errMarker) : 'Error: '
      return (
        <div
          className="selectable"
          style={{
            fontSize: '12px',
            lineHeight: 1.5,
            color: 'var(--text-secondary)'
          }}
        >
          <div
            style={{
              color: 'var(--text-dimmed)',
              marginBottom: '6px',
              fontSize: '11px',
              display: 'flex',
              alignItems: 'center',
              gap: '6px'
            }}
          >
            <span aria-hidden>⏰</span>
            <span>Cron</span>
          </div>
          {lines.map((line, i) => (
            <div
              key={i}
              style={{
                color: line.startsWith(errPrefix) ? 'var(--error)' : 'var(--text-secondary)'
              }}
            >
              {line}
            </div>
          ))}
        </div>
      )
    }
    // Unrecognized shape — fall through to general variant below
  }

  // Shell variant
  if (SHELL_TOOLS.has(toolName)) {
    const command = (args?.command as string | undefined) ?? toolName
    const output = result ?? ''
    const outputLines = output.split('\n')
    const preview = outputLines.slice(0, 40)
    const truncated = outputLines.length > 40

    return (
      <div
        className="selectable"
        style={{
          fontFamily: 'var(--font-mono)',
          fontSize: '12px',
          lineHeight: '1.5',
          color: 'var(--text-secondary)'
        }}
      >
        <div style={{ color: 'var(--text-dimmed)', marginBottom: '6px' }}>
          <span style={{ color: 'var(--text-dimmed)' }}>$ </span>
          <span style={{ color: 'var(--text-primary)' }}>{command}</span>
        </div>
        {output && (
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
            {truncated && (
              <span style={{ color: 'var(--text-dimmed)' }}>{'\n'}…</span>
            )}
          </pre>
        )}
      </div>
    )
  }

  // General variant: invocation signature + result preview
  const resultText = result ?? ''
  const resultLines = resultText.split('\n')
  const preview = resultLines.slice(0, 10)
  const truncated = resultLines.length > 10

  return (
    <div
      className="selectable"
      style={{ fontFamily: 'var(--font-mono)', fontSize: '12px', lineHeight: '1.5' }}
    >
      {/* Args summary */}
      {args && Object.keys(args).length > 0 && (
        <div
          style={{
            color: 'var(--text-dimmed)',
            marginBottom: '6px',
            fontSize: '11px',
            overflow: 'hidden',
            textOverflow: 'ellipsis',
            whiteSpace: 'nowrap'
          }}
        >
          {toolName}({Object.entries(args).map(([k, v]) =>
            `${k}: ${JSON.stringify(v)}`
          ).join(', ')})
        </div>
      )}
      {/* Result preview */}
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
          {truncated && (
            <span style={{ color: 'var(--text-dimmed)' }}>{'\n'}…</span>
          )}
        </pre>
      )}
    </div>
  )
}

// ── Spinner ───────────────────────────────────────────────────────────────────

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
