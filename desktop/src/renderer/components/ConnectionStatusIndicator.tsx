import { useConnectionStore } from '../stores/connectionStore'
import { SIDEBAR_NAV_ICON_SLOT } from './sidebar/sidebarNavRowStyles'

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
        gap: '8px',
        minWidth: 0,
        flex: 1
      }}
      title={label}
    >
      <span style={SIDEBAR_NAV_ICON_SLOT}>
        <span
          style={{
            display: 'block',
            width: '8px',
            height: '8px',
            borderRadius: '50%',
            backgroundColor: config.color,
            flexShrink: 0,
            animation: config.pulse ? 'pulse 2s ease-in-out infinite' : 'none'
          }}
          aria-hidden="true"
        />
      </span>
      <span
        style={{
          fontSize: '12px',
          lineHeight: 1.2,
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
