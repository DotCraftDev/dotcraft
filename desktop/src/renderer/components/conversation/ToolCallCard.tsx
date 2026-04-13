import { memo, useEffect, useState } from 'react'
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
import {
  formatInvocationDisplay,
  formatResultSummary,
  getWebToolIcon,
  getWebToolSectionLabel,
  invocationNeedsCallingPrefix,
  isWebToolName
} from '../../utils/webToolDisplay'
import { InlineDiffView } from './InlineDiffView'

interface ToolCallCardProps {
  item: ConversationItem
  turnId: string
}

const EXPLORE_TOOLS = new Set(['ReadFile', 'GrepFiles', 'FindFiles', 'ListDirectory'])
const FILE_WRITE_TOOLS = new Set(['WriteFile', 'EditFile'])
const SHELL_TOOLS = new Set(['Exec', 'RunCommand', 'BashCommand'])

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
  if (isWebToolName(toolName)) {
    const inv = formatInvocationDisplay(toolName, args, locale)
    if (inv) return inv
  }
  return translate(locale, 'toolCall.called', { toolName })
}

export const ToolCallCard = memo(function ToolCallCard({ item, turnId }: ToolCallCardProps): JSX.Element {
  const locale = useLocale()
  const [expanded, setExpanded] = useState(false)
  const [elapsed, setElapsed] = useState(0)

  const toolName = item.toolName ?? 'tool'
  const args = item.arguments
  const isShellTool = SHELL_TOOLS.has(toolName)
  const isRunning = item.status !== 'completed'
  const shellOutput = item.aggregatedOutput ?? item.result ?? ''
  const shellFailed = item.executionStatus === 'failed'
    || item.executionStatus === 'cancelled'
    || (item.exitCode != null && item.exitCode !== 0)
  const success = item.success !== false && !shellFailed

  useEffect(() => {
    if (!isRunning) return
    const start = item.createdAt ? new Date(item.createdAt).getTime() : Date.now()
    const interval = setInterval(() => {
      setElapsed(Math.floor((Date.now() - start) / 1000))
    }, 500)
    return () => clearInterval(interval)
  }, [isRunning, item.createdAt])

  const itemDiffs = useConversationStore((s) => s.itemDiffs)
  const fileDiff = FILE_WRITE_TOOLS.has(toolName) ? itemDiffs.get(item.id) : undefined

  function toggleExpand(): void {
    if (!isRunning || isShellTool) {
      setExpanded((v) => !v)
    }
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
            cursor: isShellTool ? 'pointer' : 'default'
          }}
        >
          <Spinner />
          <span style={{ flex: 1 }}>
            {toolName === CRON_TOOL_NAME ? (
              <span style={{ color: 'var(--text-primary)' }}>
                {formatCronRunningLabel(args, locale)}
              </span>
            ) : isWebToolName(toolName) && !invocationNeedsCallingPrefix(toolName, args) ? (
              <span style={{ color: 'var(--text-primary)' }}>
                {formatInvocationDisplay(toolName, args, locale)}
              </span>
            ) : (
              <>
                {translate(locale, 'toolCall.calling')}{' '}
                <strong style={{ color: 'var(--text-primary)' }}>{toolName}</strong>
              </>
            )}
          </span>
          {elapsed > 0 && (
            <span style={{ color: 'var(--text-dimmed)', marginLeft: '8px', flexShrink: 0 }}>
              {elapsed}s
            </span>
          )}
          {isShellTool && <Chevron expanded={expanded} />}
        </button>

        {expanded && isShellTool && (
          <div style={{ background: 'var(--bg-secondary)', padding: '8px' }}>
            <ExpandedContent
              toolName={toolName}
              args={args}
              result={shellOutput}
              success={!shellFailed}
              fileDiff={undefined}
              locale={locale}
            />
          </div>
        )}
      </div>
    )
  }

  const label = getCollapsedLabel(toolName, args, locale)

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

        <Chevron expanded={expanded} />
      </button>

      {expanded && (
        <div style={{ background: 'var(--bg-secondary)', padding: '8px' }}>
          <ExpandedContent
            toolName={toolName}
            args={args}
            result={isShellTool ? shellOutput : item.result}
            success={success}
            fileDiff={fileDiff ? { diff: fileDiff } : undefined}
            locale={locale}
          />
        </div>
      )}
    </div>
  )
})

interface ExpandedContentProps {
  toolName: string
  args: Record<string, unknown> | undefined
  result: string | undefined
  success: boolean
  fileDiff: { diff: import('../../types/toolCall').FileDiff } | undefined
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

  if (SHELL_TOOLS.has(toolName)) {
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

  return (
    <div className="selectable" style={{ fontFamily: 'var(--font-mono)', fontSize: '12px', lineHeight: '1.5' }}>
      {args && Object.keys(args).length > 0 && (
        <div style={{ color: 'var(--text-dimmed)', marginBottom: '6px', fontSize: '11px', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
          {toolName}({Object.entries(args).map(([k, v]) => `${k}: ${JSON.stringify(v)}`).join(', ')})
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

function Chevron({ expanded }: { expanded: boolean }): JSX.Element {
  return (
    <span
      style={{
        color: 'var(--text-dimmed)',
        fontSize: '10px',
        transform: expanded ? 'rotate(180deg)' : 'rotate(0deg)',
        transition: 'transform 150ms ease'
      }}
    >
      ▼
    </span>
  )
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
