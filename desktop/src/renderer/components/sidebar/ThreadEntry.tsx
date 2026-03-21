import { useState, useRef } from 'react'
import type { ThreadSummary } from '../../types/thread'
import { useThreadStore } from '../../stores/threadStore'
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
  const { activeThreadId, setActiveThreadId, renameThread, runningTurnThreadIds } = useThreadStore()
  const isActive = activeThreadId === thread.id
  const hasRunningTurn = runningTurnThreadIds.has(thread.id) && !isActive

  const [contextMenu, setContextMenu] = useState<ContextMenuPosition | null>(null)
  const [renaming, setRenaming] = useState(false)
  const [renameValue, setRenameValue] = useState(thread.displayName ?? '')
  const renameInputRef = useRef<HTMLInputElement>(null)

  const displayName = thread.displayName ?? 'New conversation'
  const relativeTime = formatRelativeTime(thread.lastActiveAt)

  function handleClick(): void {
    if (renaming) return
    setActiveThreadId(thread.id)
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
          if (!isActive) {
            ;(e.currentTarget as HTMLDivElement).style.backgroundColor = 'var(--bg-tertiary)'
          }
        }}
        onMouseLeave={(e) => {
          if (!isActive) {
            ;(e.currentTarget as HTMLDivElement).style.backgroundColor = 'transparent'
          }
        }}
      >
        {/* Activity indicator: pulsing dot when a turn is running in the background */}
        {hasRunningTurn && (
          <span
            aria-label="Turn running"
            title="Turn running"
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
        )}

        {/* Relative time */}
        {!renaming && (
          <span
            style={{
              fontSize: '12px',
              color: 'var(--text-dimmed)',
              flexShrink: 0,
              marginLeft: '4px'
            }}
          >
            {relativeTime}
          </span>
        )}
      </div>

      {/* Context menu portal */}
      {contextMenu && (
        <ThreadEntryContextMenu
          position={contextMenu}
          onClose={() => setContextMenu(null)}
          onRename={startRename}
          threadId={thread.id}
        />
      )}
    </>
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
  threadId: string
}

function ThreadEntryContextMenu({
  position,
  onClose,
  onRename,
  threadId
}: ThreadEntryContextMenuProps): JSX.Element {
  const { removeThread, activeThreadId, setActiveThreadId } = useThreadStore()
  const confirm = useConfirmDialog()

  async function handleArchive(): Promise<void> {
    onClose()
    const ok = await confirm({
      title: 'Archive conversation?',
      message: 'This conversation will be moved to your archive and removed from this list.',
      confirmLabel: 'Archive'
    })
    if (!ok) return
    try {
      await window.api.appServer.sendRequest('thread/archive', { threadId })
    } catch {
      // Best-effort
    }
    if (activeThreadId === threadId) setActiveThreadId(null)
    removeThread(threadId)
  }

  async function handleDelete(): Promise<void> {
    onClose()
    const ok = await confirm({
      title: 'Delete conversation?',
      message: 'This conversation will be permanently deleted. This cannot be undone.',
      confirmLabel: 'Delete',
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
        { label: 'Rename', onClick: onRename },
        { label: 'Archive', onClick: handleArchive },
        { label: 'Delete', onClick: handleDelete, danger: true }
      ]}
    />
  )
}
