import { useT } from '../../contexts/LocaleContext'
import { useUIStore } from '../../stores/uiStore'
import { ConnectionStatusIndicator } from '../ConnectionStatusIndicator'
import { Settings } from 'lucide-react'
import {
  SIDEBAR_NAV_BORDER_INACTIVE,
  SIDEBAR_NAV_ICON_SLOT,
  SIDEBAR_NAV_LABEL,
  SIDEBAR_NAV_ROW_OUTER
} from './sidebarNavRowStyles'

const APP_VERSION = typeof __APP_VERSION__ !== 'undefined' ? __APP_VERSION__ : '0.0.0'

/**
 * Sidebar footer showing settings button, connection status and app version.
 * Spec §9.6
 */
export function SidebarFooter(): JSX.Element {
  const t = useT()
  const { activeMainView, setActiveMainView } = useUIStore()
  const settingsActive = activeMainView === 'settings'
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
        onClick={() => setActiveMainView('settings')}
        title={t('sidebar.settingsShortcut')}
        aria-label={t('sidebar.openSettingsAria')}
        style={{
          ...SIDEBAR_NAV_ROW_OUTER,
          ...(settingsActive ? { borderLeft: '3px solid var(--accent)' } : SIDEBAR_NAV_BORDER_INACTIVE),
          background: settingsActive ? 'var(--bg-tertiary)' : 'transparent',
          color: settingsActive ? 'var(--text-primary)' : 'var(--text-secondary)',
          cursor: 'pointer'
        }}
        onMouseEnter={(e) => {
          if (!settingsActive) {
            ;(e.currentTarget as HTMLButtonElement).style.backgroundColor = 'var(--bg-tertiary)'
            ;(e.currentTarget as HTMLButtonElement).style.color = 'var(--text-primary)'
          }
        }}
        onMouseLeave={(e) => {
          if (!settingsActive) {
            ;(e.currentTarget as HTMLButtonElement).style.backgroundColor = 'transparent'
            ;(e.currentTarget as HTMLButtonElement).style.color = 'var(--text-secondary)'
          }
        }}
      >
        <span style={SIDEBAR_NAV_ICON_SLOT}>
          <Settings size={14} strokeWidth={1.5} aria-hidden style={{ display: 'block', flexShrink: 0 }} />
        </span>
        <span style={SIDEBAR_NAV_LABEL}>{t('sidebarFooter.settings')}</span>
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
