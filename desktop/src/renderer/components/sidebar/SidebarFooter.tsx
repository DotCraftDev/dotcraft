import { ConnectionStatusIndicator } from '../ConnectionStatusIndicator'

const APP_VERSION = '0.1.0'

interface SidebarFooterProps {
  onOpenSettings?: () => void
}

function GearIcon(): JSX.Element {
  return (
    <svg
      width="14"
      height="14"
      viewBox="0 0 16 16"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
      aria-hidden="true"
      style={{ flexShrink: 0 }}
    >
      <path
        d="M8 10.5a2.5 2.5 0 1 0 0-5 2.5 2.5 0 0 0 0 5Z"
        stroke="currentColor"
        strokeWidth="1.25"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
      <path
        d="M13.3 9.4a1.2 1.2 0 0 0 .24 1.32l.04.04a1.45 1.45 0 0 1-2.05 2.05l-.04-.04a1.2 1.2 0 0 0-1.32-.24 1.2 1.2 0 0 0-.73 1.1V14a1.45 1.45 0 0 1-2.9 0v-.06a1.2 1.2 0 0 0-.78-1.1 1.2 1.2 0 0 0-1.32.24l-.04.04a1.45 1.45 0 0 1-2.05-2.05l.04-.04A1.2 1.2 0 0 0 2.73 9.7a1.2 1.2 0 0 0-1.1-.73H1.6a1.45 1.45 0 0 1 0-2.9h.06a1.2 1.2 0 0 0 1.1-.78 1.2 1.2 0 0 0-.24-1.32l-.04-.04a1.45 1.45 0 0 1 2.05-2.05l.04.04a1.2 1.2 0 0 0 1.32.24h.06A1.2 1.2 0 0 0 6.6 1.06V1a1.45 1.45 0 0 1 2.9 0v.06a1.2 1.2 0 0 0 .73 1.1 1.2 1.2 0 0 0 1.32-.24l.04-.04a1.45 1.45 0 0 1 2.05 2.05l-.04.04a1.2 1.2 0 0 0-.24 1.32v.06a1.2 1.2 0 0 0 1.1.73H14a1.45 1.45 0 0 1 0 2.9h-.06a1.2 1.2 0 0 0-1.1.73l-.04-.01Z"
        stroke="currentColor"
        strokeWidth="1.25"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  )
}

/**
 * Sidebar footer showing settings button, connection status and app version.
 * Spec §9.6
 */
export function SidebarFooter({ onOpenSettings }: SidebarFooterProps): JSX.Element {
  return (
    <div
      style={{
        borderTop: '1px solid var(--border-default)',
        padding: '6px 8px 8px',
        flexShrink: 0,
        display: 'flex',
        flexDirection: 'column',
        gap: '2px'
      }}
    >
      {/* Settings button */}
      <button
        onClick={onOpenSettings}
        title="Settings (Ctrl+,)"
        aria-label="Open settings"
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: '8px',
          width: '100%',
          padding: '6px 8px',
          background: 'transparent',
          border: 'none',
          borderRadius: '6px',
          color: 'var(--text-secondary)',
          fontSize: '13px',
          cursor: 'pointer',
          textAlign: 'left'
        }}
        onMouseEnter={(e) => {
          ;(e.currentTarget as HTMLButtonElement).style.backgroundColor = 'var(--bg-tertiary)'
          ;(e.currentTarget as HTMLButtonElement).style.color = 'var(--text-primary)'
        }}
        onMouseLeave={(e) => {
          ;(e.currentTarget as HTMLButtonElement).style.backgroundColor = 'transparent'
          ;(e.currentTarget as HTMLButtonElement).style.color = 'var(--text-secondary)'
        }}
      >
        <GearIcon />
        Settings
      </button>

      <div style={{ padding: '2px 8px', display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
        <ConnectionStatusIndicator />
        <span style={{ fontSize: '11px', color: 'var(--text-dimmed)' }}>v{APP_VERSION}</span>
      </div>
    </div>
  )
}
