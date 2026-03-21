import { useRef } from 'react'
import { useUIStore } from '../../stores/uiStore'
import { useConnectionStore } from '../../stores/connectionStore'
import { useThreadStore } from '../../stores/threadStore'
import type { ThreadSummary } from '../../types/thread'
import { WorkspaceHeader } from '../sidebar/WorkspaceHeader'
import { NewThreadButton } from '../sidebar/NewThreadButton'
import { ThreadSearch } from '../sidebar/ThreadSearch'
import { ThreadList } from '../sidebar/ThreadList'
import { SidebarFooter } from '../sidebar/SidebarFooter'

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
 * 5. Reserved nav items (Automations, Skills)
 * 6. SidebarFooter (connection status, version)
 *
 * Collapsed mode (48px): shows first-letter dots for recent threads.
 * Spec §9.8
 */
export function Sidebar({ workspaceName, workspacePath, onOpenSettings }: SidebarProps): JSX.Element {
  const { sidebarCollapsed, toggleSidebar } = useUIStore()
  const searchRef = useRef<HTMLInputElement>(null)

  // Expose searchRef for Ctrl+K global shortcut (App.tsx reads this via
  // a forwarded ref or an event)
  if (typeof window !== 'undefined') {
    ;(window as Window & { __sidebarSearchFocus?: () => void }).__sidebarSearchFocus = () =>
      searchRef.current?.focus()
  }

  if (sidebarCollapsed) {
    return <CollapsedSidebar onExpand={toggleSidebar} workspacePath={workspacePath} onOpenSettings={onOpenSettings} />
  }

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        height: '100%',
        overflow: 'hidden',
        position: 'relative'
      }}
    >
      <WorkspaceHeader workspaceName={workspaceName} workspacePath={workspacePath} />

      <NewThreadButton workspacePath={workspacePath} />

      <ThreadSearch inputRef={searchRef} />

      {/* Thread list -- fills remaining space */}
      <ThreadList />

      {/* Reserved Phase 2 nav items */}
      <div
        style={{
          borderTop: '1px solid var(--border-default)',
          padding: '4px 0',
          flexShrink: 0
        }}
      >
        {(['Automations', 'Skills'] as const).map((label) => (
          <div
            key={label}
            style={{
              padding: '6px 16px',
              fontSize: '13px',
              color: 'var(--text-secondary)',
              cursor: 'pointer',
              borderRadius: '4px',
              margin: '0 4px'
            }}
            onMouseEnter={(e) => {
              ;(e.currentTarget as HTMLDivElement).style.backgroundColor = 'var(--bg-tertiary)'
            }}
            onMouseLeave={(e) => {
              ;(e.currentTarget as HTMLDivElement).style.backgroundColor = 'transparent'
            }}
          >
            {label}
          </div>
        ))}
      </div>

      <SidebarFooter onOpenSettings={onOpenSettings} />
    </div>
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
  const { status } = useConnectionStore()
  const { threadList, addThread, setActiveThreadId } = useThreadStore()

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
      {/* Expand button */}
      <button
        onClick={onExpand}
        title="Expand sidebar (Ctrl+B)"
        style={iconButtonStyle}
        aria-label="Expand sidebar"
      >
        ›
      </button>

      {/* New thread icon */}
      <button
        title="New Thread (Ctrl+N)"
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
        aria-label="New Thread"
      >
        +
      </button>

      {/* Thread dots: first letter of each recent thread */}
      {recentThreads.map((t) => {
        const letter = (t.displayName ?? 'N')[0].toUpperCase()
        return (
          <button
            key={t.id}
            title={t.displayName ?? 'New conversation'}
            onClick={() => setActiveThreadId(t.id)}
            style={{
              ...iconButtonStyle,
              fontSize: '11px',
              fontWeight: 600,
              backgroundColor: 'var(--bg-tertiary)'
            }}
            aria-label={t.displayName ?? 'New conversation'}
          >
            {letter}
          </button>
        )
      })}

      {/* Spacer */}
      <div style={{ flex: 1 }} />

      {/* Settings icon button */}
      <button
        onClick={onOpenSettings}
        title="Settings (Ctrl+,)"
        aria-label="Open settings"
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
        title={`Status: ${status}`}
        aria-label={`Connection status: ${status}`}
      />
    </div>
  )
}

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
