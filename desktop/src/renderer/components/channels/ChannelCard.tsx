export type ChannelConnectionState = 'connected' | 'enabledNotConnected' | 'notConfigured'

interface ChannelCardProps {
  logoPath?: string
  label: string
  statusLabel: string
  status: ChannelConnectionState
  active: boolean
  onClick: () => void
}

function stateColor(status: ChannelConnectionState): string {
  if (status === 'connected') return 'var(--success)'
  if (status === 'enabledNotConnected') return 'var(--warning)'
  return 'var(--text-dimmed)'
}

export function ChannelCard({
  logoPath,
  label,
  status,
  statusLabel,
  active,
  onClick
}: ChannelCardProps): JSX.Element {
  return (
    <button
      type="button"
      onClick={onClick}
      style={{
        width: '100%',
        border: active ? '1px solid var(--accent)' : '1px solid var(--border-default)',
        backgroundColor: active ? 'var(--bg-tertiary)' : 'var(--bg-primary)',
        borderRadius: '8px',
        padding: '10px',
        cursor: 'pointer',
        textAlign: 'left',
        display: 'flex',
        flexDirection: 'column',
        gap: '8px'
      }}
    >
      <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
        {logoPath ? (
          <img
            src={logoPath}
            alt={label}
            width={24}
            height={24}
            style={{ borderRadius: '6px', flexShrink: 0, backgroundColor: 'var(--bg-secondary)' }}
          />
        ) : (
          <div
            aria-hidden
            style={{
              width: 24,
              height: 24,
              borderRadius: '6px',
              flexShrink: 0,
              backgroundColor: 'var(--bg-secondary)',
              color: 'var(--text-secondary)',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              fontSize: '11px',
              fontWeight: 700
            }}
          >
            {label.slice(0, 1).toUpperCase()}
          </div>
        )}
        <span style={{ color: 'var(--text-primary)', fontSize: '13px', fontWeight: 600 }}>{label}</span>
      </div>
      <div style={{ display: 'inline-flex', alignItems: 'center', gap: '6px', color: 'var(--text-secondary)' }}>
        <span
          aria-hidden
          style={{
            width: '7px',
            height: '7px',
            borderRadius: '50%',
            backgroundColor: stateColor(status),
            display: 'inline-block'
          }}
        />
        <span style={{ fontSize: '11px' }}>{statusLabel}</span>
      </div>
    </button>
  )
}
