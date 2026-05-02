import { useCallback, useEffect, useRef, useState } from 'react'
import { useConversationStore } from '../../stores/conversationStore'
import { useThreadStore } from '../../stores/threadStore'
import { addToast } from '../../stores/toastStore'
import { useT } from '../../contexts/LocaleContext'
import { useAutoScroll } from '../../hooks/useAutoScroll'
import { UserMessageBlock } from './UserMessageBlock'
import { AgentResponseBlock } from './AgentResponseBlock'
import { ScrollToBottomButton } from './ScrollToBottomButton'
import { wireTurnToConversationTurn } from '../../types/conversation'
import type { ConversationItem, ConversationTurn } from '../../types/conversation'
import type { ContextUsageSnapshotWire, Thread } from '../../types/thread'
import { isAcceptPlanSentinel } from '../../utils/planAcceptSentinel'
import { startTurnWithOptimisticUI } from '../../utils/startTurn'

/** Module-level scroll position cache — ephemeral, not persisted to storage. */
const scrollPositionCache = new Map<string, number>()

const NEAR_BOTTOM_THRESHOLD = 50

interface InlineEditState {
  threadId: string
  turnId: string
  itemId: string
  draftText: string
  submitting: boolean
  rollbackPending: boolean
}

interface RollbackThreadResult {
  thread?: {
    turns?: Array<Record<string, unknown>>
    contextUsage?: ContextUsageSnapshotWire | null
    [key: string]: unknown
  }
}

function isTextOnlyEditableUserMessage(item: ConversationItem): boolean {
  if (item.type !== 'userMessage') return false
  if ((item.images?.length ?? 0) > 0 || (item.imageDataUrls?.length ?? 0) > 0) return false
  if (!item.nativeInputParts || item.nativeInputParts.length === 0) return true
  return item.nativeInputParts.every((part) => part.type === 'text')
}

function editableUserText(item: ConversationItem): string {
  if (item.nativeInputParts && item.nativeInputParts.length > 0) {
    return item.nativeInputParts
      .filter((part) => part.type === 'text')
      .map((part) => part.text)
      .join('')
  }
  return item.text ?? ''
}

function lastUserItem(turn: ConversationTurn): ConversationItem | undefined {
  return [...turn.items].reverse().find(
    (item) =>
      item.type === 'userMessage' &&
      item.deliveryMode !== 'guidance' &&
      !isAcceptPlanSentinel(item.text ?? '')
  )
}

/**
 * Scrollable container that renders the full turn history and live streaming content.
 * Spec §10.3.3. Handles scroll position restoration.
 */
export function MessageStream(): JSX.Element {
  const t = useT()
  const turns = useConversationStore((s) => s.turns)
  const turnStatus = useConversationStore((s) => s.turnStatus)
  const activeTurnId = useConversationStore((s) => s.activeTurnId)
  const streamingMessage = useConversationStore((s) => s.streamingMessage)
  const streamingReasoning = useConversationStore((s) => s.streamingReasoning)
  const systemLabel = useConversationStore((s) => s.systemLabel)
  const workspacePath = useConversationStore((s) => s.workspacePath)
  const activeThreadId = useThreadStore((s) => s.activeThreadId)
  const activeThread = useThreadStore((s) => s.activeThread)
  const [editing, setEditing] = useState<InlineEditState | null>(null)
  const prevThreadIdRef = useRef<string | null>(null)

  // Use total character count + turn count as a proxy for content size changes
  const contentLength = turns.reduce((acc, t) => acc + t.items.length, 0) +
    streamingMessage.length + streamingReasoning.length + (systemLabel?.length ?? 0)

  const { scrollRef, showScrollButton, scrollToBottom } = useAutoScroll(contentLength)

  useEffect(() => {
    setEditing(null)
  }, [activeThreadId])

  const submitInlineEdit = useCallback(async (): Promise<void> => {
    const current = editing
    if (!current || current.submitting) return
    const draftText = current.draftText.trim()
    if (!draftText) return

    setEditing({ ...current, submitting: true })
    let rollbackPending = current.rollbackPending
    try {
      if (rollbackPending) {
        const state = useConversationStore.getState()
        const latestTurn = state.turns[state.turns.length - 1]
        const latestUser = latestTurn ? lastUserItem(latestTurn) : undefined
        if (!latestTurn || latestTurn.id !== current.turnId || latestUser?.id !== current.itemId) {
          setEditing(null)
          addToast(t('conversation.editStale'), 'warning')
          return
        }

        const rollbackResult = await window.api.appServer.sendRequest('thread/rollback', {
          threadId: current.threadId,
          numTurns: 1
        }) as RollbackThreadResult
        rollbackPending = false
        if (rollbackResult.thread) {
          useConversationStore.getState().setTurns((rollbackResult.thread.turns ?? []).map(wireTurnToConversationTurn))
          useConversationStore.getState().setContextUsage(rollbackResult.thread.contextUsage ?? null)
          useThreadStore.getState().setActiveThread(rollbackResult.thread as Thread)
        }
      }

      await startTurnWithOptimisticUI({
        threadId: current.threadId,
        workspacePath: workspacePath || activeThread?.workspacePath || '',
        text: draftText,
        fallbackThreadName: t('toast.imageMessage'),
        fileFallbackThreadName: t('toast.fileReferenceMessage'),
        attachmentFallbackThreadName: t('toast.attachmentMessage'),
        renameThreadFromText: false,
        throwOnStartError: true
      })
      setEditing(null)
    } catch (err) {
      console.error('inline edit retry failed:', err)
      setEditing((prev) =>
        prev && prev.turnId === current.turnId && prev.itemId === current.itemId
          ? { ...prev, submitting: false, rollbackPending }
          : prev
      )
      addToast(err instanceof Error ? err.message : String(err), 'error')
    }
  }, [activeThread?.workspacePath, editing, t, workspacePath])

  // Save scroll position on thread switch; restore on switch-in
  useEffect(() => {
    const el = scrollRef.current
    const prev = prevThreadIdRef.current
    const curr = activeThreadId

    if (prev && prev !== curr && el) {
      // Save the departing thread's scroll position
      scrollPositionCache.set(prev, el.scrollTop)
    }

    if (curr && curr !== prev && el) {
      // Restore the arriving thread's scroll position (after content renders)
      requestAnimationFrame(() => {
        const saved = scrollPositionCache.get(curr)
        if (saved === undefined) {
          // Never visited: scroll to bottom
          el.scrollTop = el.scrollHeight
        } else {
          const atBottom = el.scrollHeight - saved - el.clientHeight <= NEAR_BOTTOM_THRESHOLD
          el.scrollTop = atBottom ? el.scrollHeight : saved
        }
      })
    }

    prevThreadIdRef.current = curr
  }, [activeThreadId, scrollRef])

  return (
    <div style={{ position: 'relative', flex: 1, overflow: 'hidden' }}>
      <div
        ref={scrollRef}
        data-testid="message-stream"
        aria-live="polite"
        aria-atomic="false"
        aria-label="Conversation messages"
        role="log"
        style={{
          height: '100%',
          overflowY: 'auto',
          padding: '24px 28px',
          display: 'flex',
          flexDirection: 'column',
          gap: '16px'
        }}
      >
        {turns.map((turn, idx) => (
          <TurnBlock
            key={turn.id}
            turn={turn}
            streamingMessage={turn.id === activeTurnId ? streamingMessage : ''}
            streamingReasoning={turn.id === activeTurnId ? streamingReasoning : ''}
            isRunning={turnStatus === 'running' && turn.id === activeTurnId}
            isActiveTurn={turn.id === activeTurnId}
            isLastTurn={idx === turns.length - 1}
            isIdle={turnStatus === 'idle'}
            editing={editing}
            onStartEdit={(item) => {
              setEditing({
                threadId: turn.threadId,
                turnId: turn.id,
                itemId: item.id,
                draftText: editableUserText(item),
                submitting: false,
                rollbackPending: true
              })
            }}
            onDraftChange={(draftText) => {
              setEditing((prev) => prev ? { ...prev, draftText } : prev)
            }}
            onCancelEdit={() => {
              setEditing(null)
            }}
            onSubmitEdit={() => {
              void submitInlineEdit()
            }}
          />
        ))}

        {editing && !turns.some((turn) =>
          turn.id === editing.turnId && turn.items.some((item) => item.id === editing.itemId)
        ) && (
          <UserMessageBlock
            text={editing.draftText}
            editing
            editText={editing.draftText}
            editSubmitting={editing.submitting}
            editSubmitDisabled={
              turnStatus !== 'idle' || editing.submitting || editing.draftText.trim().length === 0
            }
            onEditTextChange={(draftText) => {
              setEditing((prev) => prev ? { ...prev, draftText } : prev)
            }}
            onCancelEdit={() => {
              setEditing(null)
            }}
            onSubmitEdit={() => {
              void submitInlineEdit()
            }}
          />
        )}

        {systemLabel && <SystemStatusDivider labelKey={systemLabel} />}

        {/* Bottom anchor for auto-scroll */}
        <div />
      </div>

      {/* Soft fade so messages blend into the input area (no hard divider line) */}
      <div
        style={{
          position: 'absolute',
          bottom: 0,
          left: 0,
          right: 0,
          height: '40px',
          background: 'linear-gradient(transparent, var(--bg-primary))',
          pointerEvents: 'none',
          zIndex: 1
        }}
      />

      {showScrollButton && <ScrollToBottomButton onClick={scrollToBottom} />}
    </div>
  )
}

function SystemStatusDivider({ labelKey }: { labelKey: string }): JSX.Element {
  const t = useT()
  const label = t(labelKey)

  return (
    <div
      role="status"
      aria-live="polite"
      aria-label={label}
      style={{
        display: 'flex',
        alignItems: 'center',
        gap: 8,
        padding: '14px 4px',
        color: 'var(--text-secondary, #8a8a8a)',
        fontSize: 11,
        lineHeight: 1.4,
        userSelect: 'none'
      }}
    >
      <span
        aria-hidden
        style={{
          flex: 1,
          height: 1,
          background: 'var(--border-color, rgba(127,127,127,0.25))'
        }}
      />
      <span
        className="tool-running-gradient-text"
        style={{
          display: 'inline-flex',
          alignItems: 'center',
          gap: 6,
          fontWeight: 600,
          whiteSpace: 'nowrap'
        }}
      >
        {label}
      </span>
      <span
        aria-hidden
        style={{
          flex: 1,
          height: 1,
          background: 'var(--border-color, rgba(127,127,127,0.25))'
        }}
      />
    </div>
  )
}

// ---------------------------------------------------------------------------
// Single turn renderer
// ---------------------------------------------------------------------------

interface TurnBlockProps {
  turn: ConversationTurn
  streamingMessage: string
  streamingReasoning: string
  isRunning: boolean
  isActiveTurn: boolean
  isLastTurn: boolean
  isIdle: boolean
  editing: InlineEditState | null
  onStartEdit: (item: ConversationItem) => void
  onDraftChange: (draftText: string) => void
  onCancelEdit: () => void
  onSubmitEdit: () => void
}

function TurnBlock({
  turn,
  streamingMessage,
  streamingReasoning,
  isRunning,
  isActiveTurn,
  isLastTurn,
  isIdle,
  editing,
  onStartEdit,
  onDraftChange,
  onCancelEdit,
  onSubmitEdit
}: TurnBlockProps): JSX.Element {
  // Separate user-input items from agent items
  const userItems = turn.items.filter(
    (i: ConversationItem) =>
      i.type === 'userMessage' &&
      i.deliveryMode !== 'guidance' &&
      !isAcceptPlanSentinel(i.text ?? '')
  )
  const canEditUserMessage = isLastTurn && !isActiveTurn && userItems.length > 0

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
      {/* User messages */}
      {userItems.map((item: ConversationItem, idx) => (
        <UserMessageBlock
          key={item.id}
          text={item.text ?? ''}
          nativeInputParts={item.nativeInputParts}
          imageDataUrls={item.imageDataUrls}
          images={item.images}
          createdAt={item.createdAt}
          triggerKind={item.triggerKind}
          triggerLabel={item.triggerLabel}
          triggerRefId={item.triggerRefId}
          editable={canEditUserMessage && idx === userItems.length - 1 && isIdle && isTextOnlyEditableUserMessage(item)}
          onEdit={() => onStartEdit(item)}
          editing={editing?.turnId === turn.id && editing.itemId === item.id}
          editText={editing?.turnId === turn.id && editing.itemId === item.id ? editing.draftText : undefined}
          editSubmitting={editing?.turnId === turn.id && editing.itemId === item.id ? editing.submitting : false}
          editSubmitDisabled={
            !isIdle ||
            (editing?.turnId === turn.id && editing.itemId === item.id
              ? editing.submitting || editing.draftText.trim().length === 0
              : false)
          }
          onEditTextChange={onDraftChange}
          onCancelEdit={onCancelEdit}
          onSubmitEdit={onSubmitEdit}
        />
      ))}

      {/* Agent response */}
      <AgentResponseBlock
        turn={turn}
        streamingMessage={streamingMessage}
        streamingReasoning={streamingReasoning}
        isRunning={isRunning}
        isActiveTurn={isActiveTurn}
        isLastTurn={isLastTurn}
      />
    </div>
  )
}
