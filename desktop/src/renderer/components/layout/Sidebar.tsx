import { useRef, useState } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { connectionStatusLabel } from '../../utils/connectionStatusLabel'
import { useUIStore } from '../../stores/uiStore'
import { useConnectionStore } from '../../stores/connectionStore'
import { useThreadStore } from '../../stores/threadStore'
import type { ThreadSummary } from '../../types/thread'
import { WorkspaceHeader } from '../sidebar/WorkspaceHeader'
import { NewThreadButton } from '../sidebar/NewThreadButton'
import { ThreadSearch } from '../sidebar/ThreadSearch'
import { ThreadList } from '../sidebar/ThreadList'
import { SidebarFooter } from '../sidebar/SidebarFooter'
import {
  SIDEBAR_NAV_BORDER_INACTIVE,
  SIDEBAR_NAV_ICON_SLOT,
  SIDEBAR_NAV_LABEL,
  SIDEBAR_NAV_ROW_OUTER
} from '../sidebar/sidebarNavRowStyles'
import { DotCraftLogo } from '../ui/DotCraftLogo'

interface SidebarProps {
  workspaceName: string
  workspacePath: string
  onOpenSettings?: () => void
}

/**
 * Main sidebar panel — M2: fully functional thread list.
 *
 * Structure:
 * 1. WorkspaceHeader (name, path, dropdown)
 * 2. NewThreadButton (Ctrl+N, disabled when disconnected)
 * 3. ThreadSearch (Ctrl+K, debounced)
 * 4. ThreadList (grouped, scrollable)
 * 5. Reserved nav items (DashBoard link, Automations, Skills)
 * 6. SidebarFooter (connection status, version)
 *
 * Collapsed mode (48px): shows first-letter dots for recent threads.
 * Spec §9.8
 */
export function Sidebar({ workspaceName, workspacePath, onOpenSettings }: SidebarProps): JSX.Element {
  const t = useT()
  const { sidebarCollapsed, toggleSidebar, activeMainView, setActiveMainView } = useUIStore()
  const capabilities = useConnectionStore((s) => s.capabilities)
  const connectionStatus = useConnectionStore((s) => s.status)
  const dashboardUrl = useConnectionStore((s) => s.dashboardUrl)
  const dashboardOpenEnabled = connectionStatus === 'connected' && Boolean(dashboardUrl)
  const dashboardDisabledTitle = !dashboardOpenEnabled
    ? connectionStatus !== 'connected'
      ? t('sidebar.dashboardDisabledConnect')
      : t('sidebar.dashboardDisabledNoUrl')
    : undefined
  const dashboardHoverTitle = dashboardOpenEnabled
    ? `${t('sidebar.dashboardOpenTitle')}${dashboardUrl ? ` — ${dashboardUrl}` : ''}`
    : dashboardDisabledTitle

  const automationsAvailable =
    capabilities?.automations === true || capabilities?.cronManagement === true
  const automationsDisabledTitle =
    !automationsAvailable ? t('sidebar.automationsDisabled') : undefined
  const searchRef = useRef<HTMLInputElement>(null)

  // Expose searchRef for Ctrl+K global shortcut (App.tsx reads this via
  // a forwarded ref or an event)
  if (typeof window !== 'undefined') {
    ;(window as Window & { __sidebarSearchFocus?: () => void }).__sidebarSearchFocus = () =>
      searchRef.current?.focus()
  }

  if (sidebarCollapsed) {
    return (
      <CollapsedSidebar
        onExpand={toggleSidebar}
        workspacePath={workspacePath}
        onOpenSettings={onOpenSettings}
      />
    )
  }

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        height: '100%',
        overflow: 'visible',
        position: 'relative'
      }}
    >
      <LogoHeader onCollapse={toggleSidebar} />
      <WorkspaceHeader workspaceName={workspaceName} workspacePath={workspacePath} />

      <NewThreadButton workspacePath={workspacePath} />

      <ThreadSearch inputRef={searchRef} />

      {/* Thread list -- fills remaining space */}
      <ThreadList />

      {/* Phase 2 nav: Automations, Skills — spacing instead of a divider line */}
      <div
        style={{
          marginTop: '8px',
          paddingTop: '8px',
          paddingBottom: '4px',
          flexShrink: 0
        }}
      >
        <SidebarNavRow
          label={t('sidebar.dashboard')}
          active={false}
          onClick={() => {
            if (dashboardUrl) void window.api.shell.openExternal(dashboardUrl)
          }}
          icon={<DashboardIcon />}
          disabled={!dashboardOpenEnabled}
          title={dashboardHoverTitle}
          externalLink
        />
        <SidebarNavRow
          label={t('sidebar.automations')}
          active={activeMainView === 'automations'}
          onClick={() => setActiveMainView('automations')}
          icon={<AutomationsIcon />}
          disabled={!automationsAvailable}
          title={automationsDisabledTitle}
        />
        <SidebarNavRow
          label={t('sidebar.skills')}
          active={activeMainView === 'skills'}
          onClick={() => setActiveMainView('skills')}
          icon={<SkillsIcon />}
        />
      </div>

      <SidebarFooter onOpenSettings={onOpenSettings} />
    </div>
  )
}

// ---------------------------------------------------------------------------
// Phase 2 sidebar rows (Skills, Automations)
// ---------------------------------------------------------------------------

interface SidebarNavRowProps {
  label: string
  active: boolean
  onClick: () => void
  icon: JSX.Element
  disabled?: boolean
  title?: string
  /** When set, shows a small external-link affordance (opens browser / external URL). */
  externalLink?: boolean
}

function SidebarNavRow({
  label,
  active,
  onClick,
  icon,
  disabled,
  title,
  externalLink
}: SidebarNavRowProps): JSX.Element {
  return (
    <button
      type="button"
      onClick={disabled ? undefined : onClick}
      disabled={disabled}
      title={title}
      style={{
        ...SIDEBAR_NAV_ROW_OUTER,
        cursor: disabled ? 'default' : 'pointer',
        backgroundColor: active ? 'var(--bg-tertiary)' : 'transparent',
        ...(active ? { borderLeft: '3px solid var(--accent)' } : SIDEBAR_NAV_BORDER_INACTIVE),
        color: disabled ? 'var(--text-tertiary)' : active ? 'var(--text-primary)' : 'var(--text-secondary)',
        opacity: disabled ? 0.5 : 1,
        transition: 'background-color 120ms ease, color 120ms ease'
      }}
      onMouseEnter={(e) => {
        if (!active && !disabled) (e.currentTarget as HTMLButtonElement).style.backgroundColor = 'var(--bg-tertiary)'
      }}
      onMouseLeave={(e) => {
        if (!active && !disabled) (e.currentTarget as HTMLButtonElement).style.backgroundColor = 'transparent'
      }}
    >
      <span style={SIDEBAR_NAV_ICON_SLOT}>{icon}</span>
      <span style={{ ...SIDEBAR_NAV_LABEL, display: 'flex', alignItems: 'center', gap: '6px', minWidth: 0 }}>
        <span style={{ overflow: 'hidden', textOverflow: 'ellipsis' }}>{label}</span>
        {externalLink ? (
          <span style={{ flexShrink: 0, opacity: 0.65, display: 'flex' }} aria-hidden>
            <ExternalLinkGlyph />
          </span>
        ) : null}
      </span>
    </button>
  )
}

function ExternalLinkGlyph(): JSX.Element {
  return (
    <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden>
      <path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6" />
      <polyline points="15 3 21 3 21 9" />
      <line x1="10" y1="14" x2="21" y2="3" />
    </svg>
  )
}

function DashboardIcon(): JSX.Element {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden style={{ display: 'block' }}>
      <rect x="3" y="3" width="7" height="7" rx="1" />
      <rect x="14" y="3" width="7" height="7" rx="1" />
      <rect x="3" y="14" width="7" height="7" rx="1" />
      <rect x="14" y="14" width="7" height="7" rx="1" />
    </svg>
  )
}

function SkillsIcon(): JSX.Element {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden style={{ display: 'block' }}>
      <path d="M12 2v4M12 18v4M4.93 4.93l2.83 2.83M16.24 16.24l2.83 2.83M2 12h4M18 12h4M4.93 19.07l2.83-2.83M16.24 7.76l2.83-2.83" />
      <circle cx="12" cy="12" r="3" />
    </svg>
  )
}

function AutomationsIcon(): JSX.Element {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden style={{ display: 'block' }}>
      <rect x="3" y="4" width="18" height="16" rx="2" />
      <path d="M3 10h18" />
    </svg>
  )
}

// ---------------------------------------------------------------------------
// Collapsed sidebar (48px wide)
// ---------------------------------------------------------------------------

interface CollapsedSidebarProps {
  onExpand: () => void
  workspacePath: string
  onOpenSettings?: () => void
}

function CollapsedSidebar({ onExpand, workspacePath, onOpenSettings }: CollapsedSidebarProps): JSX.Element {
  const t = useT()
  const [logoHovered, setLogoHovered] = useState(false)
  const { status, errorMessage, capabilities: collapsedCaps, dashboardUrl: collapsedDashboardUrl } =
    useConnectionStore()
  const { threadList, addThread, setActiveThreadId } = useThreadStore()
  const { activeMainView, setActiveMainView } = useUIStore()
  const collapsedDashboardOpen = status === 'connected' && Boolean(collapsedDashboardUrl)
  const collapsedDashboardTitle = !collapsedDashboardOpen
    ? status !== 'connected'
      ? t('sidebar.dashboardDisabledConnect')
      : t('sidebar.dashboardDisabledNoUrl')
    : `${t('sidebar.dashboardOpenTitle')}${collapsedDashboardUrl ? ` — ${collapsedDashboardUrl}` : ''}`
  const collapsedAutomationsAvailable =
    collapsedCaps?.automations === true || collapsedCaps?.cronManagement === true

  const colorMap: Record<string, string> = {
    connecting: 'var(--warning)',
    connected: 'var(--success)',
    disconnected: 'var(--error)',
    error: 'var(--error)'
  }

  // Show up to 5 recent thread dots in collapsed mode
  const recentThreads = threadList.slice(0, 5)

  async function handleNewThread(): Promise<void> {
    if (status !== 'connected') return
    try {
      const result = await window.api.appServer.sendRequest('thread/start', {
        identity: {
          channelName: 'dotcraft-desktop',
          userId: 'local',
          channelContext: `workspace:${workspacePath}`,
          workspacePath
        },
        historyMode: 'server'
      }) as { thread: ThreadSummary }
      addThread(result.thread)
      setActiveThreadId(result.thread.id)
      setActiveMainView('conversation')
    } catch (err) {
      console.error('Failed to create thread:', err)
    }
  }

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        height: '100%',
        padding: '8px 0',
        gap: '6px'
      }}
    >
      {/* Logo doubles as expand button — swaps to panel-open icon on hover */}
      <button
        onClick={onExpand}
        title={t('sidebar.expand')}
        aria-label={t('sidebar.expand')}
        onMouseEnter={() => setLogoHovered(true)}
        onMouseLeave={() => setLogoHovered(false)}
        style={{
          ...iconButtonStyle,
          width: '36px',
          height: '36px',
          borderRadius: '8px',
          marginBottom: '2px',
          color: logoHovered ? 'var(--text-primary)' : 'var(--text-secondary)'
        }}
      >
        {logoHovered ? <PanelLeftOpenIcon /> : <DotCraftLogo size={24} />}
      </button>

      {/* New thread icon */}
      <button
        title={t('sidebar.newThread')}
        onClick={handleNewThread}
        disabled={status !== 'connected'}
        style={{
          ...iconButtonStyle,
          backgroundColor: 'var(--accent)',
          color: '#ffffff',
          fontSize: '18px',
          fontWeight: 700,
          opacity: status !== 'connected' ? 0.5 : 1
        }}
        aria-label={t('sidebar.newThread')}
      >
        +
      </button>

      {/* Thread dots: first letter of each recent thread */}
      {recentThreads.map((thread) => {
        const letter = (thread.displayName ?? 'N')[0].toUpperCase()
        return (
          <button
            key={thread.id}
            title={thread.displayName ?? t('sidebar.newConversation')}
            onClick={() => {
              setActiveThreadId(thread.id)
              setActiveMainView('conversation')
            }}
            style={{
              ...iconButtonStyle,
              fontSize: '11px',
              fontWeight: 600,
              backgroundColor: 'var(--bg-tertiary)'
            }}
            aria-label={thread.displayName ?? t('sidebar.newConversation')}
          >
            {letter}
          </button>
        )
      })}

      {/* Spacer */}
      <div style={{ flex: 1 }} />

      <button
        type="button"
        title={collapsedDashboardTitle}
        onClick={
          collapsedDashboardOpen && collapsedDashboardUrl
            ? () => void window.api.shell.openExternal(collapsedDashboardUrl)
            : undefined
        }
        disabled={!collapsedDashboardOpen}
        style={{
          ...iconButtonStyle,
          backgroundColor: 'transparent',
          color: collapsedDashboardOpen ? 'var(--accent)' : 'var(--text-secondary)',
          opacity: collapsedDashboardOpen ? 1 : 0.4
        }}
        aria-label={t('sidebar.dashboard')}
      >
        <DashboardIcon />
      </button>
      <button
        type="button"
        title={
          collapsedAutomationsAvailable
            ? t('sidebar.automations')
            : t('sidebar.automationsDisabled')
        }
        onClick={collapsedAutomationsAvailable ? () => setActiveMainView('automations') : undefined}
        disabled={!collapsedAutomationsAvailable}
        style={{
          ...iconButtonStyle,
          backgroundColor: activeMainView === 'automations' ? 'var(--bg-tertiary)' : 'transparent',
          color: activeMainView === 'automations' ? 'var(--accent)' : 'var(--text-secondary)',
          opacity: collapsedAutomationsAvailable ? 1 : 0.4
        }}
        aria-label={t('sidebar.automations')}
      >
        <AutomationsIcon />
      </button>
      <button
        type="button"
        title={t('sidebar.skills')}
        onClick={() => setActiveMainView('skills')}
        style={{
          ...iconButtonStyle,
          backgroundColor: activeMainView === 'skills' ? 'var(--bg-tertiary)' : 'transparent',
          color: activeMainView === 'skills' ? 'var(--accent)' : 'var(--text-secondary)'
        }}
        aria-label={t('sidebar.skills')}
      >
        <SkillsIcon />
      </button>

      {/* Settings icon button */}
      <button
        onClick={onOpenSettings}
        title={t('sidebar.settingsShortcut')}
        aria-label={t('sidebar.openSettingsAria')}
        style={iconButtonStyle}
      >
        <svg width="15" height="15" viewBox="0 0 16 16" fill="none" xmlns="http://www.w3.org/2000/svg" aria-hidden="true">
          <path d="M8 10.5a2.5 2.5 0 1 0 0-5 2.5 2.5 0 0 0 0 5Z" stroke="currentColor" strokeWidth="1.25" strokeLinecap="round" strokeLinejoin="round" />
          <path d="M13.3 9.4a1.2 1.2 0 0 0 .24 1.32l.04.04a1.45 1.45 0 0 1-2.05 2.05l-.04-.04a1.2 1.2 0 0 0-1.32-.24 1.2 1.2 0 0 0-.73 1.1V14a1.45 1.45 0 0 1-2.9 0v-.06a1.2 1.2 0 0 0-.78-1.1 1.2 1.2 0 0 0-1.32.24l-.04.04a1.45 1.45 0 0 1-2.05-2.05l.04-.04A1.2 1.2 0 0 0 2.73 9.7a1.2 1.2 0 0 0-1.1-.73H1.6a1.45 1.45 0 0 1 0-2.9h.06a1.2 1.2 0 0 0 1.1-.78 1.2 1.2 0 0 0-.24-1.32l-.04-.04a1.45 1.45 0 0 1 2.05-2.05l.04.04a1.2 1.2 0 0 0 1.32.24h.06A1.2 1.2 0 0 0 6.6 1.06V1a1.45 1.45 0 0 1 2.9 0v.06a1.2 1.2 0 0 0 .73 1.1 1.2 1.2 0 0 0 1.32-.24l.04-.04a1.45 1.45 0 0 1 2.05 2.05l-.04.04a1.2 1.2 0 0 0-.24 1.32v.06a1.2 1.2 0 0 0 1.1.73H14a1.45 1.45 0 0 1 0 2.9h-.06a1.2 1.2 0 0 0-1.1.73l-.04-.01Z" stroke="currentColor" strokeWidth="1.25" strokeLinecap="round" strokeLinejoin="round" />
        </svg>
      </button>

      {/* Connection status dot */}
      <div
        style={{
          width: '8px',
          height: '8px',
          borderRadius: '50%',
          backgroundColor: colorMap[status] ?? 'var(--text-dimmed)',
          marginBottom: '8px'
        }}
        title={t('connection.statusTitle', {
          status: connectionStatusLabel(status, errorMessage, t)
        })}
        aria-label={t('connection.statusTitle', {
          status: connectionStatusLabel(status, errorMessage, t)
        })}
      />
    </div>
  )
}

// ---------------------------------------------------------------------------
// Panel toggle icons (Lucide-style, inline SVG)
// ---------------------------------------------------------------------------

function PanelLeftCloseIcon(): JSX.Element {
  return (
    <svg
      width="18"
      height="18"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <rect x="3" y="3" width="18" height="18" rx="2" />
      <path d="M9 3v18" />
      <path d="m16 15-3-3 3-3" />
    </svg>
  )
}

function PanelLeftOpenIcon(): JSX.Element {
  return (
    <svg
      width="18"
      height="18"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <rect x="3" y="3" width="18" height="18" rx="2" />
      <path d="M9 3v18" />
      <path d="m14 9 3 3-3 3" />
    </svg>
  )
}

// ---------------------------------------------------------------------------
// Logo header (expanded sidebar top-left)
// ---------------------------------------------------------------------------

interface LogoHeaderProps {
  onCollapse: () => void
}

function LogoHeader({ onCollapse }: LogoHeaderProps): JSX.Element {
  const t = useT()
  const [hovered, setHovered] = useState(false)

  return (
    <button
      type="button"
      onClick={onCollapse}
      title={t('sidebar.collapseTitle')}
      aria-label={t('sidebar.collapseAria')}
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      style={{
        display: 'flex',
        alignItems: 'center',
        gap: '8px',
        padding: '10px 14px',
        background: 'transparent',
        border: 'none',
        width: '100%',
        cursor: 'pointer',
        flexShrink: 0,
        color: hovered ? 'var(--text-primary)' : 'var(--text-secondary)',
        transition: 'color 120ms ease'
      }}
    >
      {/* On hover, swap logo for panel-close icon; logo stays otherwise */}
      <span style={{ flexShrink: 0, display: 'flex', alignItems: 'center', width: 24, height: 24, justifyContent: 'center' }}>
        {hovered ? <PanelLeftCloseIcon /> : <DotCraftLogo size={24} />}
      </span>
      <span
        style={{
          fontSize: '14px',
          fontWeight: 700,
          color: 'var(--text-primary)',
          letterSpacing: '-0.3px',
          flex: 1,
          textAlign: 'left'
        }}
      >
        DotCraft
      </span>
    </button>
  )
}

// ---------------------------------------------------------------------------

const iconButtonStyle: React.CSSProperties = {
  width: '32px',
  height: '32px',
  borderRadius: '6px',
  backgroundColor: 'transparent',
  border: 'none',
  color: 'var(--text-secondary)',
  cursor: 'pointer',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  fontSize: '14px'
}
