import { useEffect, useRef } from 'react'
import { useT } from '../../contexts/LocaleContext'
import type { ConversationTurn } from '../../types/conversation'
import { useAutomationsStore } from '../../stores/automationsStore'
import { useReviewPanelStore } from '../../stores/reviewPanelStore'
import type { AutomationTask } from '../../stores/automationsStore'
import { StatusBadge } from './StatusBadge'
import { MarkdownRenderer } from '../conversation/MarkdownRenderer'
import { AgentResponseBlock } from '../conversation/AgentResponseBlock'
import type { SubAgentEntry } from '../../types/toolCall'
import { ApproveRejectBar } from './ApproveRejectBar'

const PANEL_WIDTH = 480

function SourceBadge({
  sourceName,
  t
}: {
  sourceName: string
  t: ReturnType<typeof useT>
}): JSX.Element {
  const label = sourceName === 'github' ? t('auto.source.github') : t('auto.source.local')
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

function ApprovalPolicyBadge({
  policy,
  t
}: {
  policy?: string | null
  t: ReturnType<typeof useT>
}): JSX.Element {
  const fullAuto =
    policy === 'fullAuto' || policy === 'autoApprove'
  const label = fullAuto ? t('auto.review.fullAuto') : t('auto.review.workspaceScope')
  const title = fullAuto ? t('auto.review.policyFullAuto') : t('auto.review.policyWorkspace')
  return (
    <span
      title={title}
      style={{
        display: 'inline-block',
        padding: '1px 6px',
        borderRadius: '8px',
        backgroundColor: 'var(--bg-tertiary)',
        color: fullAuto ? 'var(--accent)' : 'var(--text-secondary)',
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
  activeItemId,
  subAgentEntriesOverride,
  isLastTurn
}: {
  turn: ConversationTurn
  activeTurnId: string | null
  turnStatus: 'idle' | 'running' | 'waitingApproval'
  streamingMessage: string
  streamingReasoning: string
  activeItemId: string | null
  /** Scoped to the review thread; not the global conversation store. */
  subAgentEntriesOverride: SubAgentEntry[]
  isLastTurn: boolean
}): JSX.Element {
  const isRunning = turnStatus === 'running' && turn.id === activeTurnId

  // Orchestrator-submitted workflow prompt is modeled as userMessage; omit from automation review UI.
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
      <AgentResponseBlock
        turn={turn}
        streamingMessage={turn.id === activeTurnId ? streamingMessage : ''}
        streamingReasoning={turn.id === activeTurnId ? streamingReasoning : ''}
        isRunning={isRunning}
        isActiveTurn={turn.id === activeTurnId}
        activeItemIdOverride={isRunning ? activeItemId ?? null : undefined}
        subAgentEntriesOverride={subAgentEntriesOverride}
        isLastTurn={isLastTurn}
      />
    </div>
  )
}

/**
 * Side panel for automation task review: history, live stream, approve/reject (M8).
 */
export function TaskReviewPanel(): JSX.Element {
  const t = useT()
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
  const subAgentEntries = useReviewPanelStore((s) => s.subAgentEntries)

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
            {displayTask?.title ?? t('auto.taskTitleFallback')}
          </div>
          <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginTop: '6px', flexWrap: 'wrap' }}>
            {displayTask && <StatusBadge status={displayTask.status} />}
            {displayTask && <SourceBadge sourceName={displayTask.sourceName} t={t} />}
            {displayTask?.sourceName === 'local' && (
              <ApprovalPolicyBadge policy={displayTask.approvalPolicy} t={t} />
            )}
          </div>
        </div>
        <button
          type="button"
          aria-label={t('auto.review.panelCloseAria')}
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
          {t('threadList.loading')}
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
            {t('auto.review.agentSummaryHeading')}
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
          {t('auto.review.agentActivityHeading')}
        </div>

        {showWaitingThread && (
          <p style={{ margin: 0, fontSize: '13px', color: 'var(--text-secondary)' }}>
            {t('auto.review.waitingAgent')}
          </p>
        )}

        {showNoActivity && (
          <p style={{ margin: 0, fontSize: '13px', color: 'var(--text-secondary)' }}>
            {t('auto.review.noActivityRecorded')}
          </p>
        )}

        {reviewThreadId && turns.length === 0 && !showWaitingThread && !showNoActivity && !loading && (
          <p style={{ margin: 0, fontSize: '13px', color: 'var(--text-secondary)' }}>
            {t('auto.review.noTurnsYet')}
          </p>
        )}

        {turns.map((turn, idx) => (
          <div key={turn.id} style={{ marginBottom: '16px' }}>
            <ReviewTurnBlock
              turn={turn}
              activeTurnId={activeTurnId}
              turnStatus={turnStatus}
              streamingMessage={streamingMessage}
              streamingReasoning={streamingReasoning}
              activeItemId={activeItemId}
              subAgentEntriesOverride={subAgentEntries}
              isLastTurn={idx === turns.length - 1}
            />
          </div>
        ))}
      </div>

      {showAwaitingActions && displayTask && <ApproveRejectBar task={displayTask} />}
    </div>
  )
}
