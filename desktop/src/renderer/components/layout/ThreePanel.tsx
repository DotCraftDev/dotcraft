import { type ReactNode, useCallback } from 'react'
import { useUIStore, SIDEBAR_COLLAPSED_WIDTH, DETAIL_MIN_WIDTH } from '../../stores/uiStore'
import { useResponsiveLayout } from '../../hooks/useResponsiveLayout'
import { useThreadStore } from '../../stores/threadStore'
import { DragHandle } from './DragHandle'

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
    detailPanelWidth,
    activeMainView
  } = useUIStore()
  const activeThreadId = useThreadStore((s) => s.activeThreadId)
  const isWelcomeState = activeMainView === 'conversation' && !activeThreadId

  const effectiveDetailPanelVisible =
    activeMainView === 'settings' ||
    activeMainView === 'channels' ||
    activeMainView === 'skills' ||
    activeMainView === 'automations' ||
    isWelcomeState
      ? false
      : detailPanelVisible

  const effectiveSidebarWidth = sidebarCollapsed ? SIDEBAR_COLLAPSED_WIDTH : sidebarWidth
  const handleDetailDrag = useCallback((delta: number) => {
    const state = useUIStore.getState()
    const sidebar = state.sidebarCollapsed ? SIDEBAR_COLLAPSED_WIDTH : state.sidebarWidth
    const maxDetailWidth = Math.max(DETAIL_MIN_WIDTH, window.innerWidth - 400 - sidebar)
    const nextWidth = Math.min(maxDetailWidth, state.detailPanelWidth - delta)
    state.setDetailPanelWidth(nextWidth)
  }, [])

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
          // Soft edge without a hard vertical border (Codex-style separation by contrast)
          boxShadow: '1px 0 0 0 rgba(0, 0, 0, 0.12)',
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

      {effectiveDetailPanelVisible && <DragHandle onDrag={handleDetailDrag} />}

      {/* Detail panel — same background as conversation so it reads as an
          embedded extension. A 1px left border in the same token as the
          header bottom line visually forms a continuous "L" divider and
          marks the boundary with the conversation column. */}
      <div
        style={{
          width: effectiveDetailPanelVisible ? `${detailPanelWidth}px` : '0px',
          minWidth: effectiveDetailPanelVisible ? `${DETAIL_MIN_WIDTH}px` : '0px',
          flexShrink: 0,
          overflow: 'hidden',
          transition: 'width 200ms ease-out, min-width 200ms ease-out',
          backgroundColor: 'var(--bg-primary)',
          boxShadow: effectiveDetailPanelVisible
            ? '-1px 0 0 0 var(--border-default)'
            : 'none',
          display: 'flex',
          flexDirection: 'column'
        }}
      >
        {effectiveDetailPanelVisible && detail}
      </div>
    </div>
  )
}
