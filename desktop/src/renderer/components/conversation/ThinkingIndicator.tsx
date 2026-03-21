import { useState } from 'react'

interface ThinkingIndicatorProps {
  /** Elapsed reasoning time in seconds */
  elapsedSeconds?: number
  /** Full reasoning text — shown when expanded */
  reasoning?: string
  /** True while the agent is still reasoning (live streaming) */
  streaming?: boolean
}

/**
 * Collapsible "Thought Xs" indicator for agent reasoning.
 * Collapsed by default; click chevron to show/hide full text.
 * Spec §10.3.3
 */
export function ThinkingIndicator({
  elapsedSeconds,
  reasoning,
  streaming = false
}: ThinkingIndicatorProps): JSX.Element {
  const [expanded, setExpanded] = useState(false)

  const label = streaming
    ? 'Thinking...'
    : `Thought ${elapsedSeconds ?? 0}s`

  return (
    <div style={{ marginBottom: '6px' }}>
      {/* Summary line */}
      <button
        onClick={() => !streaming && setExpanded((v) => !v)}
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: '4px',
          background: 'none',
          border: 'none',
          cursor: streaming ? 'default' : 'pointer',
          padding: '2px 0',
          color: 'var(--text-dimmed)',
          fontSize: '12px'
        }}
        aria-expanded={expanded}
        title={streaming ? 'Agent is thinking...' : 'Click to expand reasoning'}
      >
        <span
          style={{
            display: 'inline-block',
            fontSize: '10px',
            transform: expanded ? 'rotate(180deg)' : 'rotate(0deg)',
            transition: 'transform 150ms ease',
            opacity: streaming ? 0 : 1
          }}
          aria-hidden="true"
        >
          ▾
        </span>
        <span>{label}</span>
        {streaming && (
          <span
            style={{
              display: 'inline-block',
              width: '12px',
              height: '12px',
              border: '2px solid var(--text-dimmed)',
              borderTopColor: 'transparent',
              borderRadius: '50%',
              animation: 'spin 1s linear infinite',
              marginLeft: '4px'
            }}
            aria-hidden="true"
          />
        )}
      </button>

      {/* Expanded reasoning text */}
      {expanded && reasoning && (
        <div
          style={{
            marginTop: '4px',
            padding: '8px 12px',
            borderLeft: '2px solid var(--border-default)',
            color: 'var(--text-dimmed)',
            fontStyle: 'italic',
            fontSize: '13px',
            lineHeight: 1.6,
            whiteSpace: 'pre-wrap',
            wordBreak: 'break-word'
          }}
        >
          {reasoning}
        </div>
      )}
    </div>
  )
}
