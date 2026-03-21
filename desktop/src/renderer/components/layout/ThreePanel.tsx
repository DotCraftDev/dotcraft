import { type ReactNode } from 'react'
import { useUIStore, SIDEBAR_COLLAPSED_WIDTH, DETAIL_MIN_WIDTH } from '../../stores/uiStore'
import { useResponsiveLayout } from '../../hooks/useResponsiveLayout'

interface ThreePanelProps {
  sidebar: ReactNode
  conversation: ReactNode
  detail: ReactNode
}

/**
 * Three-panel horizontal layout: Sidebar | Conversation | Detail
 *
 * Spec §8.1 dimensions:
 * - Sidebar: 240px default, 200px min, 48px collapsed
 * - Conversation: flex:1, 400px min, always visible
 * - Detail: 400px default, 300px min, collapsible
 *
 * Spec §8.3 drag handles: 4px, transparent, highlight on hover
 * Spec §8.2 responsive breakpoints applied via useResponsiveLayout
 * Spec §15.5 transitions: 200ms ease-out for panel collapse
 */
export function ThreePanel({ sidebar, conversation, detail }: ThreePanelProps): JSX.Element {
  useResponsiveLayout()

  const {
    sidebarCollapsed,
    sidebarWidth,
    detailPanelVisible,
    detailPanelWidth
  } = useUIStore()

  const effectiveSidebarWidth = sidebarCollapsed ? SIDEBAR_COLLAPSED_WIDTH : sidebarWidth

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'row',
        height: '100%',
        width: '100%',
        overflow: 'hidden',
        backgroundColor: 'var(--bg-primary)'
      }}
    >
      {/* Sidebar */}
      <div
        style={{
          width: `${effectiveSidebarWidth}px`,
          minWidth: `${effectiveSidebarWidth}px`,
          flexShrink: 0,
          overflow: 'visible',
          position: 'relative',
          zIndex: 2,
          transition: 'width 200ms ease-out, min-width 200ms ease-out',
          backgroundColor: 'var(--bg-secondary)',
          borderRight: '1px solid var(--border-default)',
          display: 'flex',
          flexDirection: 'column'
        }}
      >
        {sidebar}
      </div>

      {/* Conversation panel (always visible, fills remaining space) */}
      <div
        style={{
          flex: 1,
          minWidth: '400px',
          overflow: 'hidden',
          display: 'flex',
          flexDirection: 'column',
          backgroundColor: 'var(--bg-primary)'
        }}
      >
        {conversation}
      </div>

      {/* Detail panel */}
      <div
        style={{
          width: detailPanelVisible ? `${detailPanelWidth}px` : '0px',
          minWidth: detailPanelVisible ? `${DETAIL_MIN_WIDTH}px` : '0px',
          flexShrink: 0,
          overflow: 'hidden',
          transition: 'width 200ms ease-out, min-width 200ms ease-out',
          backgroundColor: 'var(--bg-secondary)',
          borderLeft: detailPanelVisible ? '1px solid var(--border-default)' : 'none',
          display: 'flex',
          flexDirection: 'column'
        }}
      >
        {detailPanelVisible && detail}
      </div>
    </div>
  )
}
