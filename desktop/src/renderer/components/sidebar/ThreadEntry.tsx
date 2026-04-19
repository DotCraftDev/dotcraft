import { useState, useRef, useCallback } from 'react'
import type { ThreadSummary } from '../../types/thread'
import { useThreadStore } from '../../stores/threadStore'
import { useUIStore } from '../../stores/uiStore'
import { useLocale, useT } from '../../contexts/LocaleContext'
import { formatRelativeTime } from '../../utils/relativeTime'
import type { ContextMenuPosition } from '../ui/ContextMenu'
import { ContextMenu } from '../ui/ContextMenu'
import { useConfirmDialog } from '../ui/ConfirmDialog'
import { RunningSpinner } from '../ui/RunningSpinner'
import { ChannelIconBadge } from '../ui/channelMeta'
import { Archive } from 'lucide-react'

interface ThreadEntryProps {
  thread: ThreadSummary
}

/**
 * Single row in the thread list.
 * Layout: [StatusSlot] [DisplayName ...] [RelativeTime]
 * Supports: click to select, right-click context menu, inline rename.
 * Spec 搂9.5
 */
export function ThreadEntry({ thread }: ThreadEntryProps): JSX.Element {
  const locale = useLocale()
  const t = useT()
  const { activeThreadId, setActiveThreadId, renameThread, runningTurnThreadIds } = useThreadStore()
  const setActiveMainView = useUIStore((s) => s.setActiveMainView)
  const isActive = activeThreadId === thread.id
  const hasRunningTurn = runningTurnThreadIds.has(thread.id)

  const [contextMenu, setContextMenu] = useState<ContextMenuPosition | null>(null)
  const [renaming, setRenaming] = useState(false)
  const [renameValue, setRenameValue] = useState(thread.displayName ?? '')
  const [hovered, setHovered] = useState(false)
  const [archiveButtonFocused, setArchiveButtonFocused] = useState(false)
  const [archiveConfirming, setArchiveConfirming] = useState(false)
  const renameInputRef = useRef<HTMLInputElement>(null)
  const actionSlotRef = useRef<HTMLDivElement>(null)

  const displayName = thread.displayName ?? t('sidebar.newConversation')
  const relativeTime = formatRelativeTime(thread.lastActiveAt, new Date(), locale)
  const showOriginBadge =
    thread.originChannel.length > 0 &&
    thread.originChannel.toLowerCase() !== 'dotcraft-desktop'
  const showArchiveAction = !renaming && (hovered || archiveButtonFocused)
  const showArchiveConfirm = showArchiveAction && archiveConfirming
  const confirm = useConfirmDialog()

  const performArchiveThread = useCallback(async (): Promise<void> => {
    try {
      await window.api.appServer.sendRequest('thread/archive', { threadId: thread.id })
    } catch {
      // Best-effort
    }
    if (activeThreadId === thread.id) setActiveThreadId(null)
    useThreadStore.getState().removeThread(thread.id)
  }, [activeThreadId, confirm, setActiveThreadId, t, thread.id])

  const archiveThreadWithDialog = useCallback(async (): Promise<void> => {
    const ok = await confirm({
      title: t('threadEntry.archiveTitle'),
      message: t('threadEntry.archiveMessage'),
      confirmLabel: t('threadEntry.archiveConfirm')
    })
    if (!ok) return
    await performArchiveThread()
  }, [confirm, performArchiveThread, t])

  const beginInlineArchiveConfirm = useCallback((): void => {
    setArchiveConfirming(true)
  }, [])

  const resetArchiveActionState = useCallback((): void => {
    setArchiveButtonFocused(false)
    setArchiveConfirming(false)
  }, [])

  function handleClick(): void {
    if (renaming) return
    setActiveThreadId(thread.id)
    setActiveMainView('conversation')
  }

  function handleContextMenu(e: React.MouseEvent): void {
    e.preventDefault()
    setArchiveConfirming(false)
    setContextMenu({ x: e.clientX, y: e.clientY })
  }

  function startRename(): void {
    setRenameValue(thread.displayName ?? '')
    setRenaming(true)
    setArchiveConfirming(false)
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
          setArchiveConfirming(false)
          if (!isActive) {
            ;(e.currentTarget as HTMLDivElement).style.backgroundColor = 'transparent'
          }
        }}
      >
        <span
          style={{
            width: '12px',
            minWidth: '12px',
            display: 'inline-flex',
            alignItems: 'center',
            justifyContent: 'center',
            flexShrink: 0
          }}
        >
          {hasRunningTurn ? (
            <RunningSpinner
              title={t('threadEntry.turnRunning')}
              testId={`thread-running-indicator-${thread.id}`}
            />
          ) : showStatusIcon ? (
            <span
              title={thread.status}
              style={{ fontSize: '10px', color: 'var(--text-dimmed)', flexShrink: 0 }}
              aria-label={thread.status}
            >
              {thread.status === 'paused' ? '⏸' : '🗄'}
            </span>
          ) : null}
        </span>

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
          </>
        )}

        {!renaming && (
          <div
            ref={actionSlotRef}
            style={{
              width: '96px',
              minWidth: '96px',
              marginLeft: '4px',
              flexShrink: 0,
              position: 'relative',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'flex-end'
            }}
            onBlurCapture={(e) => {
              const nextTarget = e.relatedTarget as Node | null
              if (nextTarget && actionSlotRef.current?.contains(nextTarget)) return
              resetArchiveActionState()
            }}
          >
            {showOriginBadge && (
              <span
                aria-hidden={showArchiveConfirm}
                style={{
                  marginRight: '6px',
                  display: 'inline-flex',
                  alignItems: 'center',
                  opacity: showArchiveConfirm ? 0 : 1,
                  pointerEvents: showArchiveConfirm ? 'none' : 'auto',
                  transition: 'opacity 120ms ease'
                }}
              >
                <ChannelIconBadge
                  channelName={thread.originChannel}
                  tooltip={t('threadEntry.originChannel', { channel: thread.originChannel })}
                  muted={!isActive}
                  size={20}
                />
              </span>
            )}
            <span
              aria-hidden={showArchiveAction}
              style={{
                fontSize: '12px',
                color: 'var(--text-dimmed)',
                lineHeight: 1,
                whiteSpace: 'nowrap',
                display: 'inline-flex',
                alignItems: 'center',
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
                beginInlineArchiveConfirm()
              }}
              onFocus={() => setArchiveButtonFocused(true)}
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
                cursor: showArchiveAction && !showArchiveConfirm ? 'pointer' : 'default',
                position: 'absolute',
                right: 0,
                opacity: showArchiveAction && !showArchiveConfirm ? 1 : 0,
                pointerEvents: showArchiveAction && !showArchiveConfirm ? 'auto' : 'none',
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
              <Archive size={14} strokeWidth={2} aria-hidden="true" />
            </button>
            <button
              type="button"
              tabIndex={showArchiveConfirm ? 0 : -1}
              title={t('threadEntry.archiveConfirm')}
              aria-label={t('threadEntry.archiveConfirm')}
              onClick={(e) => {
                e.stopPropagation()
                void performArchiveThread()
              }}
              onFocus={() => setArchiveButtonFocused(true)}
              style={{
                height: '24px',
                padding: '0 8px',
                border: '1px solid rgba(248,81,73,0.35)',
                borderRadius: '999px',
                backgroundColor: 'rgba(248,81,73,0.10)',
                color: 'var(--error)',
                fontSize: '11px',
                fontWeight: 600,
                display: 'inline-flex',
                alignItems: 'center',
                justifyContent: 'center',
                cursor: showArchiveConfirm ? 'pointer' : 'default',
                position: 'absolute',
                right: 0,
                opacity: showArchiveConfirm ? 1 : 0,
                pointerEvents: showArchiveConfirm ? 'auto' : 'none',
                transition: 'opacity 120ms ease, background-color 120ms ease'
              }}
              onMouseEnter={(e) => {
                e.currentTarget.style.backgroundColor = 'rgba(248,81,73,0.18)'
              }}
              onMouseLeave={(e) => {
                e.currentTarget.style.backgroundColor = 'rgba(248,81,73,0.10)'
              }}
            >
              {t('threadEntry.archiveConfirm')}
            </button>
          </div>
        )}
      </div>

      {contextMenu && (
        <ThreadEntryContextMenu
          position={contextMenu}
          onClose={() => setContextMenu(null)}
          onRename={startRename}
          onArchive={archiveThreadWithDialog}
          threadId={thread.id}
        />
      )}
    </>
  )
}

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
  const confirm = useConfirmDialog()
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
