import { useRef, useState } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { connectionStatusLabel } from '../../utils/connectionStatusLabel'
import { useUIStore } from '../../stores/uiStore'
import { useConnectionStore } from '../../stores/connectionStore'
import { useThreadStore } from '../../stores/threadStore'
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
import { SettingsIcon } from '../ui/AppIcons'
import { DotCraftLogo } from '../ui/DotCraftLogo'
import { MessageSquare, PanelLeftClose, PanelLeftOpen, Sun } from 'lucide-react'

interface SidebarProps {
  workspaceName: string
  workspacePath: string
}

/**
 * Main sidebar panel — M2: fully functional thread list.
 *
 * Structure:
 * 1. WorkspaceHeader (name, path, dropdown)
 * 2. NewThreadButton (Ctrl+N, disabled when disconnected)
 * 3. ThreadSearch (Ctrl+K, debounced)
 * 4. ThreadList (grouped, scrollable)
 * 5. Reserved nav items (Channels, Automations, Skills)
 * 6. SidebarFooter (connection status, version)
 *
 * Collapsed mode (48px): shows first-letter dots for recent threads.
 * Spec §9.8
 */
export function Sidebar({ workspaceName, workspacePath }: SidebarProps): JSX.Element {
  const t = useT()
  const { sidebarCollapsed, toggleSidebar, activeMainView, setActiveMainView } = useUIStore()
  const capabilities = useConnectionStore((s) => s.capabilities)

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

      <NewThreadButton />

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
          label={t('sidebar.channels')}
          active={activeMainView === 'channels'}
          onClick={() => setActiveMainView('channels')}
          icon={<ChannelsIcon />}
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

      <SidebarFooter />
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
}

function SidebarNavRow({
  label,
  active,
  onClick,
  icon,
  disabled,
  title
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
      <span style={{ ...SIDEBAR_NAV_LABEL, overflow: 'hidden', textOverflow: 'ellipsis' }}>{label}</span>
    </button>
  )
}

function SkillsIcon(): JSX.Element {
  return <Sun size={16} strokeWidth={2} aria-hidden style={{ display: 'block' }} />
}

function ChannelsIcon(): JSX.Element {
  return <MessageSquare size={16} strokeWidth={2} aria-hidden style={{ display: 'block' }} />
}

function AutomationsIcon(): JSX.Element {
  return (
    <svg
      width="16"
      height="16"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden
      style={{ display: 'block' }}
    >
      <circle cx="12" cy="12" r="9" />
      <line x1="12" y1="12" x2="12" y2="8" />
      <line x1="12" y1="12" x2="16" y2="12" />
    </svg>
  )
}

// ---------------------------------------------------------------------------
// Collapsed sidebar (48px wide)
// ---------------------------------------------------------------------------

interface CollapsedSidebarProps {
  onExpand: () => void
}

function CollapsedSidebar({ onExpand }: CollapsedSidebarProps): JSX.Element {
  const t = useT()
  const [logoHovered, setLogoHovered] = useState(false)
  const { status, errorMessage, capabilities: collapsedCaps } = useConnectionStore()
  const { threadList, setActiveThreadId } = useThreadStore()
  const { activeMainView, setActiveMainView, goToNewChat } = useUIStore()
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

  function handleNewThread(): void {
    if (status !== 'connected') return
    goToNewChat()
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
        title={t('sidebar.channels')}
        onClick={() => setActiveMainView('channels')}
        style={{
          ...iconButtonStyle,
          backgroundColor: activeMainView === 'channels' ? 'var(--bg-tertiary)' : 'transparent',
          color: activeMainView === 'channels' ? 'var(--accent)' : 'var(--text-secondary)'
        }}
        aria-label={t('sidebar.channels')}
      >
        <ChannelsIcon />
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
        onClick={() => setActiveMainView('settings')}
        title={t('sidebar.settingsShortcut')}
        aria-label={t('sidebar.openSettingsAria')}
        style={{
          ...iconButtonStyle,
          backgroundColor: activeMainView === 'settings' ? 'var(--bg-tertiary)' : 'transparent',
          color: activeMainView === 'settings' ? 'var(--accent)' : 'var(--text-secondary)'
        }}
      >
        <SettingsIcon />
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
  return <PanelLeftClose size={18} strokeWidth={2} aria-hidden="true" />
}

function PanelLeftOpenIcon(): JSX.Element {
  return <PanelLeftOpen size={18} strokeWidth={2} aria-hidden="true" />
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
