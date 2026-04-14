import { useMemo } from 'react'
import { AnsiUp } from 'ansi_up'

const ansiConverter = new AnsiUp()
ansiConverter.escapeForHtml = true

interface TerminalCommandBlockProps {
  command: string
  output: string
  /** Duration in milliseconds */
  duration?: number
  running?: boolean
  exitCode?: number | null
  source?: 'host' | 'sandbox'
}

/**
 * Renders a single shell command block: header + output.
 * ANSI escape codes in the output are converted to colored HTML spans.
 * Spec Section 5.7
 */
export function TerminalCommandBlock({
  command,
  output,
  duration,
  running = false,
  exitCode,
  source
}: TerminalCommandBlockProps): JSX.Element {
  const elapsedLabel = duration !== undefined && duration > 0
    ? `${(duration / 1000).toFixed(1)}s`
    : ''

  const statusLabel = running
    ? 'Running'
    : exitCode !== undefined && exitCode !== null && exitCode !== 0
      ? `Exit ${exitCode}`
      : elapsedLabel

  const statusColor = running
    ? 'var(--warning)'
    : exitCode !== undefined && exitCode !== null && exitCode !== 0
      ? 'var(--danger)'
      : 'var(--text-dimmed)'

  const outputHtml = useMemo(
    () => (output ? ansiConverter.ansi_to_html(output) : ''),
    [output]
  )

  return (
    <div
      style={{
        borderBottom: '1px solid var(--border-default)',
        fontFamily: 'var(--font-mono)',
        fontSize: '12px'
      }}
    >
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          gap: '8px',
          padding: '6px 12px 4px',
          color: 'var(--text-primary)'
        }}
      >
        <div style={{ minWidth: 0, flex: 1 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
            <span style={{ color: 'var(--success)', userSelect: 'none' }}>$</span>
            <span style={{ minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis' }}>{command}</span>
          </div>
          {source && (
            <div style={{ color: 'var(--text-dimmed)', fontSize: '10px', marginTop: '2px' }}>
              {source === 'sandbox' ? 'Sandbox' : 'Host'}
            </div>
          )}
        </div>
        {statusLabel && (
          <span style={{ color: statusColor, fontSize: '11px', flexShrink: 0 }}>
            {running ? '● ' : ''}{statusLabel}
          </span>
        )}
      </div>

      <div
        style={{
          height: '1px',
          background: 'var(--border-default)',
          margin: '0 12px'
        }}
      />

      {output ? (
        <pre
          style={{
            margin: 0,
            padding: '6px 12px',
            color: 'var(--text-secondary)',
            fontSize: '12px',
            lineHeight: 1.5,
            whiteSpace: 'pre-wrap',
            wordBreak: 'break-all',
            overflowX: 'hidden'
          }}
          dangerouslySetInnerHTML={{ __html: outputHtml }}
        />
      ) : (
        <div style={{ padding: '4px 12px 6px', color: 'var(--text-dimmed)', fontSize: '11px' }}>
          {running ? 'Waiting for output...' : '(no output)'}
        </div>
      )}
    </div>
  )
}
