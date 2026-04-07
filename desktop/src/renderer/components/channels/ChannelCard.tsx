import type { ChannelDefinition } from './channelDefs'

export type ChannelConnectionState = 'connected' | 'enabledNotConnected' | 'notConfigured'

interface ChannelCardProps {
  channel: ChannelDefinition
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
  channel,
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
        <img
          src={channel.logoPath}
          alt={label}
          width={24}
          height={24}
          style={{ borderRadius: '6px', flexShrink: 0, backgroundColor: 'var(--bg-secondary)' }}
        />
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
