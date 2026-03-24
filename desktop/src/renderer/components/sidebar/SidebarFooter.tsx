import { ConnectionStatusIndicator } from '../ConnectionStatusIndicator'
import {
  SIDEBAR_NAV_BORDER_INACTIVE,
  SIDEBAR_NAV_ICON_SLOT,
  SIDEBAR_NAV_LABEL,
  SIDEBAR_NAV_ROW_OUTER
} from './sidebarNavRowStyles'

const APP_VERSION = typeof __APP_VERSION__ !== 'undefined' ? __APP_VERSION__ : '0.0.0'

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
      style={{ display: 'block', flexShrink: 0 }}
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
        marginTop: '8px',
        padding: '8px 0',
        flexShrink: 0,
        display: 'flex',
        flexDirection: 'column',
        gap: '2px'
      }}
    >
      <button
        type="button"
        onClick={onOpenSettings}
        title="Settings (Ctrl+,)"
        aria-label="Open settings"
        style={{
          ...SIDEBAR_NAV_ROW_OUTER,
          ...SIDEBAR_NAV_BORDER_INACTIVE,
          background: 'transparent',
          color: 'var(--text-secondary)',
          cursor: 'pointer'
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
        <span style={SIDEBAR_NAV_ICON_SLOT}>
          <GearIcon />
        </span>
        <span style={SIDEBAR_NAV_LABEL}>Settings</span>
      </button>

      <div
        style={{
          ...SIDEBAR_NAV_ROW_OUTER,
          ...SIDEBAR_NAV_BORDER_INACTIVE,
          backgroundColor: 'transparent',
          cursor: 'default',
          justifyContent: 'space-between',
          gap: '8px'
        }}
      >
        <ConnectionStatusIndicator />
        <span style={{ fontSize: '11px', color: 'var(--text-dimmed)', flexShrink: 0, lineHeight: 1.2 }}>
          v{APP_VERSION}
        </span>
      </div>
    </div>
  )
}
