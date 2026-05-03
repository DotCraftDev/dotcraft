import { useEffect, useMemo } from 'react'
import type { CSSProperties } from 'react'
import { Bot, ChevronDown, ExternalLink, Square } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import {
  isSubAgentChildRunning,
  useSubAgentStore,
  type SubAgentChild
} from '../../stores/subAgentStore'
import { useThreadStore } from '../../stores/threadStore'
import { useUIStore } from '../../stores/uiStore'
import { addToast } from '../../stores/toastStore'
import { RunningSpinner } from '../ui/RunningSpinner'
import { ActionTooltip } from '../ui/ActionTooltip'

interface SubAgentDockProps {
  parentThreadId: string
}

export function SubAgentDock({ parentThreadId }: SubAgentDockProps): JSX.Element | null {
  const t = useT()
  const children = useSubAgentStore((s) => s.childrenByParent.get(parentThreadId) ?? EMPTY_SUB_AGENT_CHILDREN)
  const collapsed = useSubAgentStore((s) => s.collapsedByParent.get(parentThreadId) === true)
  const setParentCollapsed = useSubAgentStore((s) => s.setParentCollapsed)
  const fetchChildren = useSubAgentStore((s) => s.fetchChildren)

  const runningChildren = useMemo(
    () => children.filter(isSubAgentChildRunning),
    [children]
  )

  useEffect(() => {
    if (!parentThreadId) return
    void fetchChildren(parentThreadId)
  }, [fetchChildren, parentThreadId])

  if (children.length === 0) return null

  const closeableRunning = runningChildren.filter((child) => child.supportsClose)
  const contentMaxHeight = Math.min(children.length * 28 + 8, 180)

  const stopAll = async (): Promise<void> => {
    try {
      await Promise.all(closeableRunning.map((child) => closeSubAgent(parentThreadId, child.childThreadId)))
      await fetchChildren(parentThreadId)
    } catch (err) {
      addToast(err instanceof Error ? err.message : String(err), 'error')
    }
  }

  return (
    <div data-testid="subagent-dock" style={dockFrameStyle}>
      <div style={dockHeaderStyle}>
        <span style={titleStyle}>
          <Bot size={13} aria-hidden="true" style={{ color: 'var(--text-dimmed)' }} />
          <span>{t('subAgentDock.title', { count: children.length })}</span>
          {collapsed && runningChildren.length > 0 && (
            <span style={runningSummaryStyle}>
              {t('subAgentDock.runningSummary', { count: runningChildren.length })}
            </span>
          )}
        </span>
        <span style={{ display: 'inline-flex', alignItems: 'center', gap: '4px', flexShrink: 0 }}>
          {closeableRunning.length > 0 && (
            <ActionTooltip label={t('subAgentDock.stopAll')} placement="top">
              <button
                type="button"
                aria-label={t('subAgentDock.stopAll')}
                onClick={() => { void stopAll() }}
                style={iconButtonStyle}
              >
                <Square size={12} fill="currentColor" aria-hidden="true" />
              </button>
            </ActionTooltip>
          )}
          <ActionTooltip
            label={collapsed ? t('subAgentDock.expand') : t('subAgentDock.collapse')}
            placement="top"
          >
            <button
              type="button"
              aria-label={collapsed ? t('subAgentDock.expand') : t('subAgentDock.collapse')}
              onClick={() => setParentCollapsed(parentThreadId, !collapsed)}
              style={iconButtonStyle}
            >
              <ChevronDown
                size={14}
                aria-hidden="true"
                style={{
                  transform: collapsed ? 'rotate(-90deg)' : 'none',
                  transition: 'transform 120ms ease'
                }}
              />
            </button>
          </ActionTooltip>
        </span>
      </div>

      <div
        aria-hidden={collapsed}
        data-testid="subagent-dock-rows"
        style={rowsViewportStyle(collapsed, contentMaxHeight)}
      >
        <div style={rowsStyle}>
          {children.map((child, index) => (
            <SubAgentDockRow
              key={child.childThreadId}
              child={child}
              parentThreadId={parentThreadId}
              color={rowColors[index % rowColors.length]}
              onRefresh={() => { void fetchChildren(parentThreadId) }}
            />
          ))}
        </div>
      </div>
    </div>
  )
}

const EMPTY_SUB_AGENT_CHILDREN: SubAgentChild[] = []

function SubAgentDockRow({
  child,
  parentThreadId,
  color,
  onRefresh
}: {
  child: SubAgentChild
  parentThreadId: string
  color: string
  onRefresh: () => void
}): JSX.Element {
  const t = useT()
  const running = isSubAgentChildRunning(child)
  const statusLabel = running
    ? child.lastToolDisplay?.trim() || t('subAgentDock.running')
    : formatSubAgentStatus(child, t)
  const canOpen = child.isPlaceholder !== true

  const openThread = (): void => {
    useThreadStore.getState().setActiveThreadId(child.childThreadId)
    useUIStore.getState().setActiveMainView('conversation')
  }

  const stop = async (): Promise<void> => {
    try {
      await closeSubAgent(parentThreadId, child.childThreadId)
      onRefresh()
    } catch (err) {
      addToast(err instanceof Error ? err.message : String(err), 'error')
    }
  }

  return (
    <div style={rowStyle}>
      <span style={statusSlotStyle}>
        {running ? (
          <RunningSpinner
            title={t('subAgentDock.running')}
            testId={`subagent-dock-running-${child.childThreadId}`}
          />
        ) : (
          <span aria-hidden style={{ width: 6, height: 6, borderRadius: 999, background: 'var(--text-dimmed)', opacity: 0.58 }} />
        )}
      </span>
      <span style={{ ...nicknameStyle, color }} title={child.nickname}>{child.nickname}</span>
      <span
        className={running ? 'tool-running-gradient-text' : undefined}
        style={descriptionStyle}
        title={statusLabel}
      >
        {statusLabel}
      </span>
      {canOpen ? (
        <button type="button" onClick={openThread} style={textButtonStyle}>
          <ExternalLink size={12} aria-hidden="true" />
          <span>{t('subAgentDock.open')}</span>
        </button>
      ) : (
        <span aria-hidden style={{ width: 1 }} />
      )}
      {child.supportsClose && running && canOpen && (
        <ActionTooltip label={t('subAgentDock.stop')} placement="top">
          <button
            type="button"
            aria-label={t('subAgentDock.stopAria', { name: child.nickname })}
            onClick={() => { void stop() }}
            style={iconButtonStyle}
          >
            <Square size={11} fill="currentColor" aria-hidden="true" />
          </button>
        </ActionTooltip>
      )}
    </div>
  )
}

function formatSubAgentStatus(
  child: SubAgentChild,
  t: (key: string, vars?: Record<string, string | number>) => string
): string {
  const normalized = child.status.trim().toLowerCase()
  if (normalized === 'closed' || normalized === 'completed') return t('subAgentDock.completed')
  if (normalized === 'failed') return t('subAgentDock.failed')
  if (normalized === 'cancelled' || normalized === 'canceled') return t('subAgentDock.cancelled')
  if (child.isCompleted) return t('subAgentDock.completed')
  return t('subAgentDock.idle')
}

async function closeSubAgent(parentThreadId: string, childThreadId: string): Promise<void> {
  await window.api.appServer.sendRequest('subagent/close', {
    parentThreadId,
    childThreadId
  })
}

const rowColors = ['#ff6b7a', '#0ea5ff', '#f59e0b', '#22c55e', '#a78bfa']

const dockFrameStyle: CSSProperties = {
  width: '100%',
  margin: '0 0 -1px',
  border: '1px solid color-mix(in srgb, var(--border-default) 82%, transparent)',
  borderRadius: '14px 14px 0 0',
  background: 'color-mix(in srgb, var(--bg-secondary) 94%, var(--bg-primary))',
  overflow: 'hidden'
}

const dockHeaderStyle: CSSProperties = {
  minHeight: '28px',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'space-between',
  gap: '8px',
  padding: '4px 10px 3px',
  color: 'var(--text-secondary)',
  fontSize: '12px'
}

const titleStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  gap: '6px',
  minWidth: 0,
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap'
}

const runningSummaryStyle: CSSProperties = {
  flexShrink: 0,
  color: 'var(--text-dimmed)'
}

function rowsViewportStyle(collapsed: boolean, maxHeight: number): CSSProperties {
  return {
    maxHeight: collapsed ? 0 : maxHeight,
    opacity: collapsed ? 0 : 1,
    transform: collapsed ? 'translateY(-4px)' : 'translateY(0)',
    overflow: 'hidden',
    visibility: collapsed ? 'hidden' : 'visible',
    pointerEvents: collapsed ? 'none' : 'auto',
    transition: collapsed
      ? 'max-height 170ms ease, opacity 150ms ease, transform 170ms ease, visibility 0ms linear 170ms'
      : 'max-height 170ms ease, opacity 150ms ease, transform 170ms ease'
  }
}

const rowsStyle: CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  gap: '3px',
  padding: '0 10px 7px'
}

const rowStyle: CSSProperties = {
  minHeight: '24px',
  display: 'grid',
  gridTemplateColumns: '16px minmax(54px, max-content) minmax(0, 1fr) auto auto',
  alignItems: 'center',
  gap: '6px',
  fontSize: '13px'
}

const statusSlotStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  width: 16
}

const nicknameStyle: CSSProperties = {
  minWidth: 0,
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap',
  fontWeight: 600
}

const descriptionStyle: CSSProperties = {
  minWidth: 0,
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap',
  color: 'var(--text-secondary)'
}

const iconButtonStyle: CSSProperties = {
  width: 24,
  height: 24,
  padding: 0,
  border: 'none',
  borderRadius: 6,
  background: 'transparent',
  color: 'var(--text-dimmed)',
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  cursor: 'pointer'
}

const textButtonStyle: CSSProperties = {
  border: 'none',
  background: 'transparent',
  color: 'var(--text-dimmed)',
  display: 'inline-flex',
  alignItems: 'center',
  gap: '4px',
  padding: '2px 4px',
  fontSize: '12px',
  cursor: 'pointer'
}
