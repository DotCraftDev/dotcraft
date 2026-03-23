import { useEffect, useRef } from 'react'
import type { ConversationItem, ConversationTurn } from '../../types/conversation'
import { useAutomationsStore } from '../../stores/automationsStore'
import { useReviewPanelStore } from '../../stores/reviewPanelStore'
import type { AutomationTask } from '../../stores/automationsStore'
import { StatusBadge } from './StatusBadge'
import { MarkdownRenderer } from '../conversation/MarkdownRenderer'
import { UserMessageBlock } from '../conversation/UserMessageBlock'
import { AgentResponseBlock } from '../conversation/AgentResponseBlock'
import { ApproveRejectBar } from './ApproveRejectBar'

const PANEL_WIDTH = 480

function SourceBadge({ sourceName }: { sourceName: string }): JSX.Element {
  const label = sourceName === 'github' ? 'GitHub' : 'Local'
  return (
    <span
      style={{
        display: 'inline-block',
        padding: '1px 6px',
        borderRadius: '8px',
        backgroundColor: 'var(--bg-tertiary)',
        color: 'var(--text-secondary)',
        fontSize: '11px',
        fontWeight: 500,
        lineHeight: '16px'
      }}
    >
      {label}
    </span>
  )
}

function ReviewTurnBlock({
  turn,
  activeTurnId,
  turnStatus,
  streamingMessage,
  streamingReasoning,
  activeItemId
}: {
  turn: ConversationTurn
  activeTurnId: string | null
  turnStatus: 'idle' | 'running' | 'waitingApproval'
  streamingMessage: string
  streamingReasoning: string
  activeItemId: string | null
}): JSX.Element {
  const isRunning = turnStatus === 'running' && turn.id === activeTurnId
  const userItems = turn.items.filter((i: ConversationItem) => i.type === 'userMessage')

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
      {userItems.map((item: ConversationItem) => (
        <UserMessageBlock key={item.id} text={item.text ?? ''} />
      ))}
      <AgentResponseBlock
        turn={turn}
        streamingMessage={turn.id === activeTurnId ? streamingMessage : ''}
        streamingReasoning={turn.id === activeTurnId ? streamingReasoning : ''}
        isRunning={isRunning}
        isActiveTurn={turn.id === activeTurnId}
        activeItemIdOverride={isRunning ? activeItemId ?? null : undefined}
      />
    </div>
  )
}

/**
 * Side panel for automation task review: history, live stream, approve/reject (M8).
 */
export function TaskReviewPanel(): JSX.Element {
  const selectedTaskId = useAutomationsStore((s) => s.selectedTaskId)
  const tasks = useAutomationsStore((s) => s.tasks)
  const openReviewPanel = useReviewPanelStore((s) => s.openReviewPanel)
  const destroyReviewPanel = useReviewPanelStore((s) => s.destroyReviewPanel)
  const closeReviewPanel = useReviewPanelStore((s) => s.closeReviewPanel)
  const maybeAdvancePendingThread = useReviewPanelStore((s) => s.maybeAdvancePendingThread)

  const loading = useReviewPanelStore((s) => s.loading)
  const loadError = useReviewPanelStore((s) => s.loadError)
  const taskDetail = useReviewPanelStore((s) => s.taskDetail)
  const reviewThreadId = useReviewPanelStore((s) => s.reviewThreadId)
  const turns = useReviewPanelStore((s) => s.turns)
  const turnStatus = useReviewPanelStore((s) => s.turnStatus)
  const activeTurnId = useReviewPanelStore((s) => s.activeTurnId)
  const streamingMessage = useReviewPanelStore((s) => s.streamingMessage)
  const streamingReasoning = useReviewPanelStore((s) => s.streamingReasoning)
  const activeItemId = useReviewPanelStore((s) => s.activeItemId)

  const scrollRef = useRef<HTMLDivElement>(null)
  const contentKey = turns.length + streamingMessage.length + streamingReasoning.length

  useEffect(() => {
    const el = scrollRef.current
    if (!el) return
    el.scrollTop = el.scrollHeight
  }, [contentKey])

  useEffect(() => {
    if (!selectedTaskId) {
      destroyReviewPanel()
      return
    }
    void openReviewPanel(selectedTaskId)
  }, [selectedTaskId, openReviewPanel, destroyReviewPanel])

  useEffect(() => {
    void maybeAdvancePendingThread()
  }, [tasks, maybeAdvancePendingThread])

  useEffect(() => {
    function handleKeyDown(e: KeyboardEvent): void {
      if (e.key === 'Escape') {
        closeReviewPanel()
      }
    }
    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [closeReviewPanel])

  const listTask = selectedTaskId ? tasks.find((t) => t.id === selectedTaskId) : undefined
  const displayTask: AutomationTask | null =
    listTask && taskDetail
      ? { ...taskDetail, ...listTask }
      : listTask ?? taskDetail

  const showAwaitingActions = displayTask?.status === 'awaiting_review'

  const showWaitingThread =
    !!displayTask &&
    !displayTask.threadId &&
    (displayTask.status === 'pending' ||
      displayTask.status === 'dispatched' ||
      displayTask.status === 'agent_running')

  const showNoActivity =
    !!displayTask &&
    !reviewThreadId &&
    !showWaitingThread &&
    ['awaiting_review', 'approved', 'rejected', 'failed', 'agent_completed'].includes(
      displayTask.status
    )

  return (
    <div
      style={{
        width: PANEL_WIDTH,
        minWidth: PANEL_WIDTH,
        height: '100%',
        display: 'flex',
        flexDirection: 'column',
        borderLeft: '1px solid var(--border-default)',
        backgroundColor: 'var(--bg-primary)',
        flexShrink: 0
      }}
    >
      {/* Header */}
      <div
        style={{
          padding: '12px 14px',
          borderBottom: '1px solid var(--border-default)',
          display: 'flex',
          alignItems: 'flex-start',
          justifyContent: 'space-between',
          gap: '8px',
          flexShrink: 0
        }}
      >
        <div style={{ minWidth: 0, flex: 1 }}>
          <div
            style={{
              fontSize: '14px',
              fontWeight: 600,
              color: 'var(--text-primary)',
              lineHeight: 1.3,
              wordBreak: 'break-word'
            }}
          >
            {displayTask?.title ?? 'Task'}
          </div>
          <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginTop: '6px' }}>
            {displayTask && <StatusBadge status={displayTask.status} />}
            {displayTask && <SourceBadge sourceName={displayTask.sourceName} />}
          </div>
        </div>
        <button
          type="button"
          aria-label="Close review panel"
          onClick={() => closeReviewPanel()}
          style={{
            flexShrink: 0,
            width: '28px',
            height: '28px',
            border: 'none',
            borderRadius: '6px',
            backgroundColor: 'transparent',
            color: 'var(--text-secondary)',
            fontSize: '18px',
            lineHeight: 1,
            cursor: 'pointer'
          }}
        >
          ×
        </button>
      </div>

      {loading && (
        <div style={{ padding: '16px', fontSize: '13px', color: 'var(--text-tertiary)' }}>
          Loading…
        </div>
      )}

      {loadError && !loading && (
        <div style={{ padding: '16px', fontSize: '13px', color: 'var(--error)' }}>{loadError}</div>
      )}

      {!loading && displayTask?.agentSummary && displayTask.agentSummary.trim().length > 0 ? (
        <div
          style={{
            padding: '12px 14px',
            borderBottom: '1px solid var(--border-default)',
            flexShrink: 0
          }}
        >
          <div
            style={{
              fontSize: '11px',
              fontWeight: 600,
              color: 'var(--text-tertiary)',
              textTransform: 'uppercase',
              letterSpacing: '0.04em',
              marginBottom: '8px'
            }}
          >
            Agent summary
          </div>
          <div style={{ fontSize: '13px', color: 'var(--text-primary)' }}>
            <MarkdownRenderer content={displayTask.agentSummary} />
          </div>
        </div>
      ) : null}

      <div
        ref={scrollRef}
        style={{
          flex: 1,
          overflowY: 'auto',
          padding: '12px 14px',
          minHeight: 0
        }}
      >
        <div
          style={{
            fontSize: '11px',
            fontWeight: 600,
            color: 'var(--text-tertiary)',
            textTransform: 'uppercase',
            letterSpacing: '0.04em',
            marginBottom: '10px'
          }}
        >
          Agent activity
        </div>

        {showWaitingThread && (
          <p style={{ margin: 0, fontSize: '13px', color: 'var(--text-secondary)' }}>
            Waiting for agent to start…
          </p>
        )}

        {showNoActivity && (
          <p style={{ margin: 0, fontSize: '13px', color: 'var(--text-secondary)' }}>
            No agent activity recorded.
          </p>
        )}

        {reviewThreadId && turns.length === 0 && !showWaitingThread && !showNoActivity && !loading && (
          <p style={{ margin: 0, fontSize: '13px', color: 'var(--text-secondary)' }}>
            No turns yet.
          </p>
        )}

        {turns.map((turn) => (
          <div key={turn.id} style={{ marginBottom: '16px' }}>
            <ReviewTurnBlock
              turn={turn}
              activeTurnId={activeTurnId}
              turnStatus={turnStatus}
              streamingMessage={streamingMessage}
              streamingReasoning={streamingReasoning}
              activeItemId={activeItemId}
            />
          </div>
        ))}
      </div>

      {showAwaitingActions && displayTask && <ApproveRejectBar task={displayTask} />}
    </div>
  )
}
