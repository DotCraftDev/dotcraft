import { useT } from '../../contexts/LocaleContext'
import { useUIStore } from '../../stores/uiStore'
import { ConnectionStatusIndicator } from '../ConnectionStatusIndicator'
import {
  SIDEBAR_NAV_BORDER_INACTIVE,
  SIDEBAR_NAV_ICON_SLOT,
  SIDEBAR_NAV_LABEL,
  SIDEBAR_NAV_ROW_OUTER
} from './sidebarNavRowStyles'
import { SettingsIcon } from '../ui/AppIcons'
import { ActionTooltip } from '../ui/ActionTooltip'
import { ACTION_SHORTCUTS } from '../ui/shortcutKeys'

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
      <ActionTooltip
        label={t('sidebar.openSettingsAria')}
        shortcut={ACTION_SHORTCUTS.settings}
        wrapperStyle={{ display: 'block', width: '100%' }}
      >
        <button
          type="button"
          onClick={() => setActiveMainView('settings')}
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
            <span style={{ display: 'block', flexShrink: 0 }}>
              <SettingsIcon />
            </span>
          </span>
          <span style={SIDEBAR_NAV_LABEL}>{t('sidebarFooter.settings')}</span>
        </button>
      </ActionTooltip>

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
        <span style={{
          fontSize: 'var(--type-secondary-size)',
          lineHeight: 'var(--type-secondary-line-height)',
          color: 'var(--text-dimmed)',
          flexShrink: 0
        }}>
          v{APP_VERSION}
        </span>
      </div>
    </div>
  )
}
