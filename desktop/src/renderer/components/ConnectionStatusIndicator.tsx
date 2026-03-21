import { useConnectionStore } from '../stores/connectionStore'

const STATUS_CONFIG = {
  connecting: {
    color: 'var(--warning)',
    label: 'Connecting...',
    pulse: true
  },
  connected: {
    color: 'var(--success)',
    label: 'Connected',
    pulse: false
  },
  disconnected: {
    color: 'var(--error)',
    label: 'Disconnected \u2014 Reconnecting...',
    pulse: true
  },
  error: {
    color: 'var(--error)',
    label: null, // uses errorMessage from store
    pulse: false
  }
} as const

/**
 * Connection status indicator for the sidebar footer.
 * Shows a colored dot and label reflecting the current AppServer connection state.
 * Spec §5.3 and spec §9.7
 */
export function ConnectionStatusIndicator(): JSX.Element {
  const { status, errorMessage } = useConnectionStore()
  const config = STATUS_CONFIG[status]
  const label = config.label ?? errorMessage ?? 'Unknown error'

  return (
    <div
      style={{
        display: 'flex',
        alignItems: 'center',
        gap: '6px',
        padding: '4px 0'
      }}
      title={label}
    >
      {/* Status dot */}
      <span
        style={{
          display: 'inline-block',
          width: '8px',
          height: '8px',
          borderRadius: '50%',
          backgroundColor: config.color,
          flexShrink: 0,
          animation: config.pulse ? 'pulse 2s ease-in-out infinite' : 'none'
        }}
        aria-hidden="true"
      />
      {/* Label */}
      <span
        style={{
          fontSize: '12px',
          color: 'var(--text-secondary)',
          overflow: 'hidden',
          textOverflow: 'ellipsis',
          whiteSpace: 'nowrap'
        }}
      >
        {label}
      </span>

      <style>{`
        @keyframes pulse {
          0%, 100% { opacity: 1; }
          50% { opacity: 0.4; }
        }
      `}</style>
    </div>
  )
}
