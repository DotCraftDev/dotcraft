import { memo, useState } from 'react'
import type { ConversationItem, ConversationTurn } from '../../types/conversation'
import { ThinkingIndicator } from './ThinkingIndicator'
import { ToolCallCard } from './ToolCallCard'
import { AgentMessage } from './AgentMessage'
import { ErrorBlock } from './ErrorBlock'
import { CancelledNotice } from './CancelledNotice'
import { SubAgentProgressBlock } from './SubAgentProgressBlock'
import { TurnCompletionSummary } from './TurnCompletionSummary'
import { ApprovalCard } from './ApprovalCard'
import { aggregateToolCalls } from '../../utils/toolCallAggregation'
import type { AggregatedToolCall } from '../../utils/toolCallAggregation'
import { useConversationStore } from '../../stores/conversationStore'
import type { SubAgentEntry } from '../../types/toolCall'

interface AgentResponseBlockProps {
  turn: ConversationTurn
  /** Live streaming text (only set for the active turn while running) */
  streamingMessage?: string
  /** Live reasoning text (only set for the active turn while reasoning) */
  streamingReasoning?: string
  /** Whether this is the currently running turn */
  isRunning?: boolean
  /** Whether this is the active turn that may be in waitingApproval */
  isActiveTurn?: boolean
  /**
   * When set, used for streaming item highlight instead of the main conversation store
   * (e.g. automation task review panel).
   */
  activeItemIdOverride?: string | null
  /**
   * When set, SubAgent table uses this data instead of the global conversation store
   * (e.g. automation review scoped to reviewThreadId).
   */
  subAgentEntriesOverride?: SubAgentEntry[]
  /**
   * Thread-level SubAgent snapshot must render at most once. Show on the active turn
   * while streaming, and on the last turn for the collapsed completed summary.
   */
  isLastTurn?: boolean
}

/**
 * Renders agent-side content for a single turn in **chronological item order**.
 *
 * Each item type is rendered inline as it appears in `turn.items`:
 *   reasoningContent → ThinkingIndicator
 *   toolCall (consecutive runs aggregated) → ToolCallCard / GroupedToolCallRow
 *   agentMessage → AgentMessage
 *   error → ErrorBlock
 *
 * Streaming agentMessage / reasoningContent items are represented as placeholder
 * rows in `turn.items` (status `streaming`) and rendered inline using the live
 * buffers so order matches committed items (e.g. tool calls after streaming text).
 *
 * Spec §10.3.3
 */
export const AgentResponseBlock = memo(function AgentResponseBlock({
  turn,
  streamingMessage = '',
  streamingReasoning = '',
  isRunning = false,
  isActiveTurn = false,
  activeItemIdOverride,
  subAgentEntriesOverride,
  isLastTurn = false
}: AgentResponseBlockProps): JSX.Element {
  const pendingApproval = useConversationStore((s) => s.pendingApproval)
  const activeItemIdFromStore = useConversationStore((s) => s.activeItemId)
  const activeItemId =
    activeItemIdOverride !== undefined ? activeItemIdOverride : activeItemIdFromStore

  // Exclude user messages and toolResult items (toolResults are merged into their
  // parent toolCall items by the store, not rendered independently)
  const renderableItems = turn.items.filter(
    (i) => i.type !== 'userMessage' && i.type !== 'toolResult'
  )

  // Build the ordered render list by walking items in sequence.
  // Consecutive toolCall items are grouped so the aggregation utility can merge
  // "Explored N files" runs, while respecting the chronological position.
  const renderNodes: React.ReactNode[] = []
  let i = 0

  while (i < renderableItems.length) {
    const item = renderableItems[i]

    if (item.type === 'toolCall') {
      // Collect a consecutive run of toolCall items starting at this position
      const toolRun: ConversationItem[] = [item]
      while (i + 1 < renderableItems.length && renderableItems[i + 1].type === 'toolCall') {
        i++
        toolRun.push(renderableItems[i])
      }
      // Aggregate consecutive explore-tools within this run
      const aggregated = aggregateToolCalls(toolRun)
      for (const entry of aggregated) {
        renderNodes.push(
          renderAggregatedEntry(entry, turn.id, renderNodes.length)
        )
      }
    } else if (item.type === 'reasoningContent') {
      const isLiveStreaming =
        isRunning && item.status === 'streaming' && item.id === activeItemId
      const displayReasoning = isLiveStreaming ? streamingReasoning : (item.reasoning ?? '')
      renderNodes.push(
        <ThinkingIndicator
          key={item.id}
          elapsedSeconds={item.elapsedSeconds}
          reasoning={displayReasoning}
          streaming={isLiveStreaming}
        />
      )
    } else if (item.type === 'agentMessage') {
      const isLiveStreaming =
        isRunning && item.status === 'streaming' && item.id === activeItemId
      const displayText = isLiveStreaming ? streamingMessage : (item.text ?? '')
      renderNodes.push(
        <AgentMessage key={item.id} text={displayText} streaming={isLiveStreaming} />
      )
    } else if (item.type === 'error') {
      renderNodes.push(
        <ErrorBlock key={item.id} message={item.text ?? 'Unknown error'} />
      )
    } else if (item.type === 'approvalCard') {
      // Active approval card: pendingApproval.itemId matches this item
      const isActiveApproval = isActiveTurn && pendingApproval?.itemId === item.id
      renderNodes.push(
        <ApprovalCard
          key={item.id}
          item={item}
          isActive={isActiveApproval}
        />
      )
    }

    i++
  }

  const showSubAgentProgress = isActiveTurn || isLastTurn

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
      {renderNodes}

      {/* SubAgent progress — thread-level snapshot; one mount per stream (active or last turn) */}
      {showSubAgentProgress ? (
        <SubAgentProgressBlock entries={subAgentEntriesOverride} />
      ) : null}

      {/* Turn-level failure */}
      {turn.status === 'failed' && turn.error && (
        <ErrorBlock message={turn.error} />
      )}

      {/* Cancellation notice */}
      {turn.status === 'cancelled' && (
        <CancelledNotice reason={turn.cancelReason} />
      )}

      {/* Turn completion summary (file changes) */}
      {turn.status === 'completed' && (
        <TurnCompletionSummary turnId={turn.id} />
      )}
    </div>
  )
})

// ── Render helpers ────────────────────────────────────────────────────────────

function renderAggregatedEntry(
  entry: AggregatedToolCall,
  turnId: string,
  offset: number
): React.ReactNode {
  if (entry.kind === 'single') {
    return (
      <ToolCallCard
        key={entry.item.id}
        item={entry.item}
        turnId={turnId}
      />
    )
  }
  return (
    <GroupedToolCallRow
      key={`group-${turnId}-${offset}`}
      label={entry.label}
      items={entry.items}
    />
  )
}

// ── Grouped tool call row ─────────────────────────────────────────────────────

interface GroupedToolCallRowProps {
  label: string
  items: ConversationItem[]
}

/**
 * Collapsed summary row for a group of consecutive aggregatable tool calls
 * (e.g., "Explored 3 files"). Expandable to show each individual item.
 */
function GroupedToolCallRow({ label, items }: GroupedToolCallRowProps): JSX.Element {
  const [expanded, setExpanded] = useState(false)
  const allCompleted = items.every((i) => i.status === 'completed')

  return (
    <div>
      <button
        onClick={() => setExpanded((v) => !v)}
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: '6px',
          width: '100%',
          padding: '3px 6px',
          background: 'transparent',
          border: 'none',
          cursor: 'pointer',
          color: 'var(--text-secondary)',
          fontSize: '12px',
          textAlign: 'left',
          borderRadius: '4px'
        }}
      >
        <span style={{ color: allCompleted ? 'var(--success)' : 'var(--text-dimmed)', fontSize: '11px' }}>
          {allCompleted ? '✓' : '…'}
        </span>
        <span style={{ flex: 1 }}>{label}</span>
        <span
          style={{
            color: 'var(--text-dimmed)',
            fontSize: '10px',
            transform: expanded ? 'rotate(180deg)' : 'rotate(0deg)',
            transition: 'transform 150ms ease'
          }}
        >
          ▾
        </span>
      </button>
      {expanded && (
        <div style={{ paddingLeft: '16px' }}>
          {items.map((item) => (
            <ToolCallCard key={item.id} item={item} turnId={''} />
          ))}
        </div>
      )}
    </div>
  )
}
