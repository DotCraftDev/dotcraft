import { useState, useRef, useCallback, useEffect } from 'react'
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
import { AUTOMATION_TASK_DRAG_MIME } from '../automations/TaskCard'
import { useAutomationsStore } from '../../stores/automationsStore'
import { useDragDropStore } from '../../stores/dragDropStore'
import { addToast } from '../../stores/toastStore'

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
  const {
    activeThreadId,
    setActiveThreadId,
    renameThread,
    runningTurnThreadIds,
    pendingApprovalThreadIds,
    pendingPlanConfirmationThreadIds,
    unreadCompletedThreadIds
  } = useThreadStore()
  const setActiveMainView = useUIStore((s) => s.setActiveMainView)
  const isActive = activeThreadId === thread.id
  const hasRunningTurn = runningTurnThreadIds.has(thread.id)
  const hasPendingApproval = pendingApprovalThreadIds.has(thread.id)
  const hasPendingPlanConfirmation = pendingPlanConfirmationThreadIds.has(thread.id)
  const hasUnreadCompleted = unreadCompletedThreadIds.has(thread.id)

  const [contextMenu, setContextMenu] = useState<ContextMenuPosition | null>(null)
  const [renaming, setRenaming] = useState(false)
  const [renameValue, setRenameValue] = useState(thread.displayName ?? '')
  const [hovered, setHovered] = useState(false)
  const [archiveButtonFocused, setArchiveButtonFocused] = useState(false)
  const [archiveConfirming, setArchiveConfirming] = useState(false)
  const [dropActive, setDropActive] = useState(false)
  // `anim` drives the two transient post-drop animations. `success` plays
  // `dropSuccessPulse` on the row + `slideInBadge` on the inline bound icon;
  // `fail` plays `shake` on the row. Clears itself after the animation window.
  const [anim, setAnim] = useState<'success' | 'fail' | null>(null)
  const renameInputRef = useRef<HTMLInputElement>(null)
  const actionSlotRef = useRef<HTMLDivElement>(null)

  // Subscribe to the global drag session so we can dim archived threads and
  // mark the thread that's already the bound target of the dragged task.
  const dragActive = useDragDropStore((s) => s.active)
  const dragKind = dragActive?.kind ?? null
  const alreadyBound =
    dragKind === 'automation-task' &&
    dragActive!.alreadyBoundThreadId === thread.id
  const dimmedTarget =
    dragKind === 'automation-task' && thread.status !== 'active'

  useEffect(() => {
    if (!anim) return
    const timeout = anim === 'success' ? 700 : 360
    const t = setTimeout(() => setAnim(null), timeout)
    return () => clearTimeout(t)
  }, [anim])

  const displayName = thread.displayName ?? t('sidebar.newConversation')
  const relativeTime = formatRelativeTime(thread.lastActiveAt, new Date(), locale)
  const showOriginBadge =
    thread.originChannel.length > 0 &&
    thread.originChannel.toLowerCase() !== 'dotcraft-desktop'
  // Hide the archive action during a drag session so the right side stays
  // clean while the drop-hint / already-bound pill is shown.
  const showArchiveAction =
    !renaming && !dragKind && (hovered || archiveButtonFocused)
  const showArchiveConfirm = showArchiveAction && archiveConfirming
  const confirm = useConfirmDialog()
  const showPendingApprovalBadge = !isActive && hasPendingApproval
  const showPendingPlanBadge = !isActive && !showPendingApprovalBadge && hasPendingPlanConfirmation
  const showUnreadCompletedDot =
    !isActive
    && !hasRunningTurn
    && thread.status === 'active'
    && hasUnreadCompleted

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

  function isAutomationDrag(e: React.DragEvent): boolean {
    const types = e.dataTransfer?.types
    if (!types) return false
    // DataTransferItemList doesn't implement Array.includes
    for (let i = 0; i < types.length; i++) {
      if (types[i] === AUTOMATION_TASK_DRAG_MIME) return true
    }
    return false
  }

  function handleDragOver(e: React.DragEvent): void {
    if (!isAutomationDrag(e)) return
    // Reject drops onto the already-bound thread (no-op) and onto threads that
    // can't host a bound automation run (archived, paused). This keeps the
    // drop ring from lighting up on non-actionable targets.
    if (alreadyBound || dimmedTarget) {
      e.dataTransfer.dropEffect = 'none'
      if (dropActive) setDropActive(false)
      return
    }
    e.preventDefault()
    e.dataTransfer.dropEffect = 'link'
    if (!dropActive) setDropActive(true)
  }

  function handleDragLeave(e: React.DragEvent): void {
    if (!isAutomationDrag(e)) return
    // Only clear when leaving the row, not when crossing child boundaries.
    const related = e.relatedTarget as Node | null
    if (related && (e.currentTarget as Node).contains(related)) return
    setDropActive(false)
  }

  async function handleDrop(e: React.DragEvent): Promise<void> {
    if (!isAutomationDrag(e)) return
    e.preventDefault()
    setDropActive(false)
    // Safety net: onDragEnd on the source fires slightly after drop in some
    // browsers. Clear the session eagerly so other rows stop dimming.
    useDragDropStore.getState().end()

    if (alreadyBound || dimmedTarget) return

    const raw = e.dataTransfer.getData(AUTOMATION_TASK_DRAG_MIME)
    const title = e.dataTransfer.getData('text/plain')
    const [sourceName, taskId] = raw.split('::')
    if (!sourceName || !taskId) return
    const state = useAutomationsStore.getState()
    const task = state.tasks.find((t) => t.sourceName === sourceName && t.id === taskId)
    if (!task) {
      addToast(t('auto.dnd.bindFailed', { error: taskId }), 'error')
      setAnim('fail')
      return
    }
    try {
      await state.updateBinding(task, { threadId: thread.id, mode: 'run-in-thread' })
      addToast(
        t('auto.dnd.bindSuccess', { task: title || task.title, thread: displayName }),
        'success'
      )
      setAnim('success')
    } catch (err: unknown) {
      addToast(
        t('auto.dnd.bindFailed', { error: err instanceof Error ? err.message : String(err) }),
        'error'
      )
      setAnim('fail')
    }
  }

  return (
    <>
      <div
        onClick={handleClick}
        onContextMenu={handleContextMenu}
        onDragOver={handleDragOver}
        onDragLeave={handleDragLeave}
        onDrop={(e) => void handleDrop(e)}
        title={
          dimmedTarget
            ? t('auto.dnd.archivedCannotBind')
            : thread.displayName ?? undefined
        }
        data-testid={`thread-entry-${thread.id}`}
        style={{
          display: 'flex',
          alignItems: 'center',
          position: 'relative',
          padding: '6px 12px 6px 14px',
          cursor: dimmedTarget ? 'not-allowed' : 'pointer',
          borderLeft:
            !dragKind && isActive
              ? '2px solid var(--accent)'
              : '2px solid transparent',
          backgroundColor: dropActive
            ? 'color-mix(in srgb, var(--accent) 14%, transparent)'
            : isActive
              ? 'var(--bg-active)'
              : 'transparent',
          // Single-effect drop/target ring replaces the older 3-effect combo
          // (left-border + tinted-bg + dashed-outline). dropActive = hovered
          // valid target; alreadyBound = inset outline marking the existing
          // binding; otherwise we defer to the success pulse keyframe.
          boxShadow: dropActive
            ? '0 0 0 2px color-mix(in srgb, var(--accent) 55%, transparent)'
            : alreadyBound
              ? 'inset 0 0 0 1px color-mix(in srgb, var(--accent) 40%, transparent)'
              : 'none',
          transform: dropActive ? 'scale(1.01)' : 'none',
          opacity: dimmedTarget ? 0.42 : 1,
          filter: dimmedTarget ? 'saturate(0.7)' : 'none',
          pointerEvents: dimmedTarget ? 'none' : 'auto',
          gap: '6px',
          userSelect: 'none',
          transition:
            'background-color 100ms ease, box-shadow 140ms ease, transform 140ms ease, opacity 140ms ease',
          animation:
            anim === 'success'
              ? 'dropSuccessPulse 700ms ease-out'
              : anim === 'fail'
                ? 'shake 320ms cubic-bezier(0.3, 0.7, 0.4, 1)'
                : undefined
        }}
        onMouseEnter={(e) => {
          setHovered(true)
          if (!isActive && !dropActive && !alreadyBound && !dragKind) {
            ;(e.currentTarget as HTMLDivElement).style.backgroundColor = 'var(--bg-tertiary)'
          }
        }}
        onMouseLeave={(e) => {
          setHovered(false)
          setArchiveConfirming(false)
          if (!isActive && !dropActive && !alreadyBound && !dragKind) {
            ;(e.currentTarget as HTMLDivElement).style.backgroundColor = 'transparent'
          }
        }}
      >
        <span
          style={{
            width: '16px',
            minWidth: '16px',
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
          ) : showUnreadCompletedDot ? (
            <span
              aria-label={t('threadEntry.unreadCompleted')}
              title={t('threadEntry.unreadCompleted')}
              data-testid={`thread-unread-completed-${thread.id}`}
              style={{
                width: '6px',
                height: '6px',
                borderRadius: '999px',
                backgroundColor: 'var(--success)',
                display: 'inline-block'
              }}
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
        {showOriginBadge && (
          <span
            style={{
              display: 'inline-flex',
              alignItems: 'center',
              marginRight: '2px',
              flexShrink: 0
            }}
          >
            <ChannelIconBadge
              channelName={thread.originChannel}
              tooltip={t('threadEntry.originChannel', { channel: thread.originChannel })}
              muted={!isActive}
              size={18}
            />
          </span>
        )}

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
            {anim === 'success' && (
              <span
                aria-hidden="true"
                style={{
                  display: 'inline-flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  marginLeft: '4px',
                  fontSize: '13px',
                  color: 'var(--accent)',
                  flexShrink: 0,
                  animation: 'slideInBadge 450ms ease-out'
                }}
              >
                💬
              </span>
            )}
            {(dropActive || alreadyBound) && (
              <span
                data-testid={
                  dropActive
                    ? `thread-drop-hint-${thread.id}`
                    : `thread-already-bound-${thread.id}`
                }
                style={{
                  display: 'inline-flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  height: '18px',
                  padding: '2px 8px',
                  borderRadius: '999px',
                  border:
                    '1px solid color-mix(in srgb, var(--accent) 40%, transparent)',
                  backgroundColor: dropActive
                    ? 'color-mix(in srgb, var(--accent) 22%, transparent)'
                    : 'color-mix(in srgb, var(--accent) 10%, transparent)',
                  color: 'var(--accent)',
                  fontSize: '11px',
                  fontWeight: 600,
                  whiteSpace: 'nowrap',
                  flexShrink: 0,
                  marginLeft: '4px'
                }}
              >
                {dropActive
                  ? t('auto.dnd.dropHere')
                  : t('auto.dnd.alreadyBoundBadge')}
              </span>
            )}
            {(showPendingApprovalBadge || showPendingPlanBadge) && (
              <span
                data-testid={
                  showPendingApprovalBadge
                    ? `thread-pending-approval-${thread.id}`
                    : `thread-pending-confirmation-${thread.id}`
                }
                style={{
                  display: 'inline-flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  height: '18px',
                  padding: '2px 8px',
                  borderRadius: '999px',
                  border: showPendingApprovalBadge
                    ? '1px solid color-mix(in srgb, #d4a33b 45%, transparent)'
                    : '1px solid color-mix(in srgb, var(--accent) 40%, transparent)',
                  backgroundColor: showPendingApprovalBadge
                    ? 'color-mix(in srgb, #d4a33b 18%, transparent)'
                    : 'color-mix(in srgb, var(--accent) 12%, transparent)',
                  color: showPendingApprovalBadge ? '#d4a33b' : 'var(--accent)',
                  fontSize: '11px',
                  fontWeight: 600,
                  whiteSpace: 'nowrap',
                  flexShrink: 0
                }}
              >
                {showPendingApprovalBadge
                  ? t('threadEntry.pendingApproval')
                  : t('threadEntry.pendingPlanConfirmation')}
              </span>
            )}
          </>
        )}

        {!renaming && (
          <div
            ref={actionSlotRef}
            style={{
              width: '56px',
              minWidth: '56px',
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
            <span
              aria-hidden={showArchiveAction}
              style={{
                fontSize: '12px',
                color: 'var(--text-dimmed)',
                lineHeight: 1,
                whiteSpace: 'nowrap',
                display: 'inline-flex',
                alignItems: 'center',
                justifyContent: 'flex-end',
                width: '100%',
                overflow: 'hidden',
                textOverflow: 'ellipsis',
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
                padding: '0 6px',
                border: '1px solid rgba(248,81,73,0.35)',
                borderRadius: '999px',
                backgroundColor: 'rgba(248,81,73,0.10)',
                color: 'var(--error)',
                fontSize: '10px',
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
      if (activeThreadId === threadId) setActiveThreadId(null)
      removeThread(threadId)
    } catch {
      // Keep local state unchanged when the backend delete fails.
    }
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
