import { useState, useRef, useCallback } from 'react'
import type { ThreadSummary } from '../../types/thread'
import { useThreadStore } from '../../stores/threadStore'
import { useUIStore } from '../../stores/uiStore'
import { useLocale, useT } from '../../contexts/LocaleContext'
import { formatRelativeTime } from '../../utils/relativeTime'
import type { ContextMenuPosition } from '../ui/ContextMenu'

interface ThreadEntryProps {
  thread: ThreadSummary
}

/**
 * Single row in the thread list.
 * Layout: [StatusDot] [DisplayName ...] [RelativeTime]
 * Supports: click to select, right-click context menu, inline rename.
 * Spec §9.5
 */
export function ThreadEntry({ thread }: ThreadEntryProps): JSX.Element {
  const locale = useLocale()
  const t = useT()
  const { activeThreadId, setActiveThreadId, renameThread, runningTurnThreadIds } = useThreadStore()
  const setActiveMainView = useUIStore((s) => s.setActiveMainView)
  const isActive = activeThreadId === thread.id
  const hasRunningTurn = runningTurnThreadIds.has(thread.id) && !isActive

  const [contextMenu, setContextMenu] = useState<ContextMenuPosition | null>(null)
  const [renaming, setRenaming] = useState(false)
  const [renameValue, setRenameValue] = useState(thread.displayName ?? '')
  const [hovered, setHovered] = useState(false)
  const [archiveButtonFocused, setArchiveButtonFocused] = useState(false)
  const renameInputRef = useRef<HTMLInputElement>(null)

  const displayName = thread.displayName ?? t('sidebar.newConversation')
  const relativeTime = formatRelativeTime(thread.lastActiveAt, new Date(), locale)
  const showOriginBadge =
    thread.originChannel.length > 0 &&
    thread.originChannel.toLowerCase() !== 'dotcraft-desktop'
  const showArchiveAction = !renaming && (isActive || hovered || archiveButtonFocused)
  const confirm = useConfirmDialog()

  const archiveThread = useCallback(async (): Promise<void> => {
    const ok = await confirm({
      title: t('threadEntry.archiveTitle'),
      message: t('threadEntry.archiveMessage'),
      confirmLabel: t('threadEntry.archiveConfirm')
    })
    if (!ok) return
    try {
      await window.api.appServer.sendRequest('thread/archive', { threadId: thread.id })
    } catch {
      // Best-effort
    }
    if (activeThreadId === thread.id) setActiveThreadId(null)
    useThreadStore.getState().removeThread(thread.id)
  }, [activeThreadId, confirm, setActiveThreadId, t, thread.id])

  function handleClick(): void {
    if (renaming) return
    setActiveThreadId(thread.id)
    setActiveMainView('conversation')
  }

  function handleContextMenu(e: React.MouseEvent): void {
    e.preventDefault()
    setContextMenu({ x: e.clientX, y: e.clientY })
  }

  function startRename(): void {
    setRenameValue(thread.displayName ?? '')
    setRenaming(true)
    setContextMenu(null)
    // Focus after render
    setTimeout(() => renameInputRef.current?.select(), 0)
  }

  function commitRename(): void {
    const trimmed = renameValue.trim()
    if (trimmed) {
      renameThread(thread.id, trimmed)
      void window.api.appServer
        .sendRequest('thread/rename', { threadId: thread.id, displayName: trimmed })
        .catch((err: unknown) => console.error('thread/rename failed:', err))
    }
    setRenaming(false)
  }

  function cancelRename(): void {
    setRenaming(false)
    setRenameValue(thread.displayName ?? '')
  }

  function handleRenameKeyDown(e: React.KeyboardEvent<HTMLInputElement>): void {
    if (e.key === 'Enter') commitRename()
    if (e.key === 'Escape') cancelRename()
  }

  // Status dot for non-active paused / archived threads
  const showStatusIcon = !isActive && thread.status !== 'active'

  return (
    <>
      <div
        onClick={handleClick}
        onContextMenu={handleContextMenu}
        title={thread.displayName ?? undefined}
        data-testid={`thread-entry-${thread.id}`}
        style={{
          display: 'flex',
          alignItems: 'center',
          padding: '6px 12px 6px 14px',
          cursor: 'pointer',
          borderLeft: isActive ? '2px solid var(--accent)' : '2px solid transparent',
          backgroundColor: isActive ? 'var(--bg-active)' : 'transparent',
          gap: '6px',
          userSelect: 'none',
          transition: 'background-color 100ms ease'
        }}
        onMouseEnter={(e) => {
          setHovered(true)
          if (!isActive) {
            ;(e.currentTarget as HTMLDivElement).style.backgroundColor = 'var(--bg-tertiary)'
          }
        }}
        onMouseLeave={(e) => {
          setHovered(false)
          if (!isActive) {
            ;(e.currentTarget as HTMLDivElement).style.backgroundColor = 'transparent'
          }
        }}
      >
        {/* Activity indicator: pulsing dot when a turn is running in the background */}
        {hasRunningTurn && (
          <span
            aria-label={t('threadEntry.turnRunning')}
            title={t('threadEntry.turnRunning')}
            style={{
              width: '7px',
              height: '7px',
              borderRadius: '50%',
              background: 'var(--accent)',
              flexShrink: 0,
              animation: 'pulse 1.5s ease-in-out infinite'
            }}
          />
        )}

        {/* Status icon */}
        {showStatusIcon && !hasRunningTurn && (
          <span
            title={thread.status}
            style={{ fontSize: '10px', color: 'var(--text-dimmed)', flexShrink: 0 }}
            aria-label={thread.status}
          >
            {thread.status === 'paused' ? '⏸' : '📁'}
          </span>
        )}

        {/* Thread name (or rename input) */}
        {renaming ? (
          <input
            ref={renameInputRef}
            value={renameValue}
            onChange={(e) => setRenameValue(e.target.value)}
            onKeyDown={handleRenameKeyDown}
            onBlur={commitRename}
            autoFocus
            style={{
              flex: 1,
              fontSize: '13px',
              color: 'var(--text-primary)',
              backgroundColor: 'var(--bg-tertiary)',
              border: '1px solid var(--border-active)',
              borderRadius: '4px',
              padding: '1px 4px',
              outline: 'none',
              minWidth: 0
            }}
            onClick={(e) => e.stopPropagation()}
          />
        ) : (
          <>
            <span
              style={{
                flex: 1,
                fontSize: '13px',
                color: 'var(--text-primary)',
                overflow: 'hidden',
                textOverflow: 'ellipsis',
                whiteSpace: 'nowrap',
                minWidth: 0
              }}
            >
              {displayName}
            </span>
            {showOriginBadge && (
              <span
                title={thread.originChannel}
                aria-label={thread.originChannel}
                style={{
                  flexShrink: 0,
                  fontSize: '10px',
                  fontWeight: 600,
                  lineHeight: 1,
                  padding: '2px 5px',
                  borderRadius: '4px',
                  color: 'var(--text-dimmed)',
                  backgroundColor: 'var(--bg-tertiary)',
                  textTransform: 'uppercase',
                  letterSpacing: '0.03em'
                }}
              >
                {thread.originChannel}
              </span>
            )}
          </>
        )}

        {/* Relative time */}
        {!renaming && (
          <div
            style={{
              width: '52px',
              minWidth: '52px',
              marginLeft: '4px',
              flexShrink: 0,
              position: 'relative',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'flex-end'
            }}
          >
            <span
              aria-hidden={showArchiveAction}
              style={{
                fontSize: '12px',
                color: 'var(--text-dimmed)',
                lineHeight: 1,
                whiteSpace: 'nowrap',
                opacity: showArchiveAction ? 0 : 1,
                transition: 'opacity 120ms ease'
              }}
            >
              {relativeTime}
            </span>
            <button
              type="button"
              title={t('threadEntry.archive')}
              aria-label={t('threadEntry.archive')}
              onClick={(e) => {
                e.stopPropagation()
                void archiveThread()
              }}
              onFocus={() => setArchiveButtonFocused(true)}
              onBlur={() => setArchiveButtonFocused(false)}
              style={{
                width: '28px',
                height: '28px',
                padding: 0,
                border: 'none',
                borderRadius: '6px',
                backgroundColor: 'transparent',
                color: isActive ? 'var(--text-secondary)' : 'var(--text-dimmed)',
                display: 'inline-flex',
                alignItems: 'center',
                justifyContent: 'center',
                cursor: showArchiveAction ? 'pointer' : 'default',
                position: 'absolute',
                right: 0,
                opacity: showArchiveAction ? 1 : 0,
                pointerEvents: showArchiveAction ? 'auto' : 'none',
                transition: 'opacity 120ms ease, background-color 120ms ease, color 120ms ease'
              }}
              onMouseEnter={(e) => {
                e.currentTarget.style.backgroundColor = 'var(--bg-secondary)'
                e.currentTarget.style.color = 'var(--error)'
              }}
              onMouseLeave={(e) => {
                e.currentTarget.style.backgroundColor = 'transparent'
                e.currentTarget.style.color = isActive
                  ? 'var(--text-secondary)'
                  : 'var(--text-dimmed)'
              }}
            >
              <ArchiveIcon />
            </button>
          </div>
        )}
      </div>

      {/* Context menu portal */}
      {contextMenu && (
        <ThreadEntryContextMenu
          position={contextMenu}
          onClose={() => setContextMenu(null)}
          onRename={startRename}
          onArchive={archiveThread}
          threadId={thread.id}
        />
      )}
    </>
  )
}

function ArchiveIcon(): JSX.Element {
  return (
    <svg
      width="14"
      height="14"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <path d="M21 8v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8" />
      <path d="M1 3h22v5H1z" />
      <path d="M10 12h4" />
    </svg>
  )
}

// Deferred import to avoid circular dependency with ContextMenu/ConfirmDialog
// These are injected via the ThreadEntryContextMenu local component below
import { ContextMenu } from '../ui/ContextMenu'
import { useConfirmDialog } from '../ui/ConfirmDialog'

interface ThreadEntryContextMenuProps {
  position: ContextMenuPosition
  onClose: () => void
  onRename: () => void
  onArchive: () => Promise<void>
  threadId: string
}

function ThreadEntryContextMenu({
  position,
  onClose,
  onRename,
  onArchive,
  threadId
}: ThreadEntryContextMenuProps): JSX.Element {
  const t = useT()
  const { removeThread, activeThreadId, setActiveThreadId } = useThreadStore()

  async function handleDelete(): Promise<void> {
    onClose()
    const ok = await confirm({
      title: t('threadEntry.deleteTitle'),
      message: t('threadEntry.deleteMessage'),
      confirmLabel: t('threadEntry.delete'),
      danger: true
    })
    if (!ok) return
    try {
      await window.api.appServer.sendRequest('thread/delete', { threadId })
    } catch {
      // Best-effort
    }
    if (activeThreadId === threadId) setActiveThreadId(null)
    removeThread(threadId)
  }

  return (
    <ContextMenu
      position={position}
      onClose={onClose}
      items={[
        { label: t('threadEntry.rename'), onClick: onRename },
        {
          label: t('threadEntry.archive'),
          onClick: async () => {
            onClose()
            await onArchive()
          }
        },
        { label: t('threadEntry.delete'), onClick: handleDelete, danger: true }
      ]}
    />
  )
}
