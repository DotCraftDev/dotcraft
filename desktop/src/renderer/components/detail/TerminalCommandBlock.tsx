import { useMemo } from 'react'
import { AnsiUp } from 'ansi_up'

const ansiConverter = new AnsiUp()
ansiConverter.escapeForHtml = true

interface TerminalCommandBlockProps {
  command: string
  output: string
  /** Duration in milliseconds */
  duration?: number
}

/**
 * Renders a single shell command block: header + output.
 * ANSI escape codes in the output are converted to colored HTML spans.
 * Spec §11.5
 */
export function TerminalCommandBlock({ command, output, duration }: TerminalCommandBlockProps): JSX.Element {
  const elapsedLabel = duration !== undefined && duration > 0
    ? `(${(duration / 1000).toFixed(1)}s)`
    : ''

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
      {/* Command header */}
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          padding: '6px 12px 4px',
          color: 'var(--text-primary)'
        }}
      >
        <span>
          <span style={{ color: 'var(--success)', userSelect: 'none' }}>$ </span>
          {command}
        </span>
        {elapsedLabel && (
          <span style={{ color: 'var(--text-dimmed)', fontSize: '11px', flexShrink: 0 }}>
            {elapsedLabel}
          </span>
        )}
      </div>

      {/* Separator */}
      <div
        style={{
          height: '1px',
          background: 'var(--border-default)',
          margin: '0 12px'
        }}
      />

      {/* Output with ANSI color support */}
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
          (no output)
        </div>
      )}
    </div>
  )
}
