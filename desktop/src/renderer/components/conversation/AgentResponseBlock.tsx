import { memo, useState } from 'react'
import type { ConversationItem, ConversationTurn } from '../../types/conversation'
import { isToolLikeItemType } from '../../types/conversation'
import { ThinkingIndicator } from './ThinkingIndicator'
import { ToolCallCard } from './ToolCallCard'
import { AgentMessage } from './AgentMessage'
import { ErrorBlock } from './ErrorBlock'
import { CancelledNotice } from './CancelledNotice'
import { TurnCompletionSummary } from './TurnCompletionSummary'
import { TurnArtifacts } from './TurnArtifacts'
import { ApprovalCard } from './ApprovalCard'
import { SystemNoticeBlock } from './SystemNoticeBlock'
import { UserMessageBlock } from './UserMessageBlock'
import { planToolRunRender } from '../../utils/toolCallAggregation'
import type { AggregatedToolCall } from '../../utils/toolCallAggregation'
import type { ToolGroupCategory } from '../../utils/toolCallAggregation'
import { isToolItemLive } from '../../utils/toolCallAggregation'
import { useConversationStore } from '../../stores/conversationStore'
import { ToolCollapseChevron } from './ToolCollapseChevron'
import { useLocale } from '../../contexts/LocaleContext'
import { formatToolGroupLabel } from '../../utils/toolGroupLabel'
import { TurnCollapsedSummary } from './TurnCollapsedSummary'

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
  activeItemIdOverride
}: AgentResponseBlockProps): JSX.Element {
  const pendingApproval = useConversationStore((s) => s.pendingApproval)
  const activeItemIdFromStore = useConversationStore((s) => s.activeItemId)
  const activeItemId =
    activeItemIdOverride !== undefined ? activeItemIdOverride : activeItemIdFromStore

  // Exclude user messages and toolResult items (toolResults are merged into their
  // parent toolCall items by the store, not rendered independently)
  const renderableItems = turn.items.filter(
    (i) =>
      (i.type !== 'userMessage' || i.deliveryMode === 'guidance')
      && i.type !== 'toolResult'
      && i.type !== 'commandExecution'
  )

  const renderItemSequence = (
    itemsToRender: ConversationItem[],
    keyPrefix = ''
  ): React.ReactNode[] => {
    const nodes: React.ReactNode[] = []
    let i = 0

    while (i < itemsToRender.length) {
      const item = itemsToRender[i]

      if (isToolLikeItemType(item.type)) {
        const toolRun: ConversationItem[] = [item]
        while (
          i + 1 < itemsToRender.length
          && isToolLikeItemType(itemsToRender[i + 1].type)
        ) {
          i++
          toolRun.push(itemsToRender[i])
        }
        const isTrailingRun = i + 1 >= itemsToRender.length
        const { entries } = planToolRunRender(toolRun, { isRunning, isTrailingRun })

        for (const entry of entries) {
          nodes.push(
            renderAggregatedEntry(entry, turn.id, nodes.length, isRunning, keyPrefix)
          )
        }
      } else if (item.type === 'userMessage' && item.deliveryMode === 'guidance') {
        nodes.push(
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
          />
        )
      } else if (item.type === 'reasoningContent') {
        const isLiveStreaming =
          isRunning && item.status === 'streaming' && item.id === activeItemId
        const displayReasoning = isLiveStreaming ? streamingReasoning : (item.reasoning ?? '')
        nodes.push(
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
        nodes.push(
          <AgentMessage key={item.id} text={displayText} streaming={isLiveStreaming} />
        )
      } else if (item.type === 'error') {
        nodes.push(
          <ErrorBlock key={item.id} message={item.text ?? 'Unknown error'} />
        )
      } else if (item.type === 'approvalCard') {
        const isActiveApproval = isActiveTurn && pendingApproval?.itemId === item.id
        nodes.push(
          <ApprovalCard
            key={item.id}
            item={item}
            isActive={isActiveApproval}
          />
        )
      } else if (item.type === 'systemNotice') {
        nodes.push(<SystemNoticeBlock key={item.id} item={item} />)
      }

      i++
    }

    return nodes
  }

  const lastFinalAgentMessageIndex =
    !isRunning && turn.status === 'completed' && !renderableItems.some(isGuidanceUserMessage)
      ? findLastAgentMessageIndex(renderableItems)
      : -1
  const shouldCollapseIntermediate = lastFinalAgentMessageIndex > 0
  const renderNodes: React.ReactNode[] = []

  if (shouldCollapseIntermediate) {
    const pinnedPlanIndex = findLastCreatePlanIndexBefore(renderableItems, lastFinalAgentMessageIndex)
    const pinnedPlanItem = pinnedPlanIndex >= 0 ? renderableItems[pinnedPlanIndex] : null
    const intermediateItems = pinnedPlanItem
      ? [
          ...renderableItems.slice(0, pinnedPlanIndex),
          ...renderableItems.slice(pinnedPlanIndex + 1, lastFinalAgentMessageIndex)
        ]
      : renderableItems.slice(0, lastFinalAgentMessageIndex)
    const trailingItems = renderableItems.slice(lastFinalAgentMessageIndex)
    const intermediateNodes = pinnedPlanItem
      ? [
          ...renderItemSequence(renderableItems.slice(0, pinnedPlanIndex), 'before-pinned-plan'),
          ...renderItemSequence(
            renderableItems.slice(pinnedPlanIndex + 1, lastFinalAgentMessageIndex),
            'after-pinned-plan'
          )
        ]
      : renderItemSequence(intermediateItems)
    const pinnedPlanNodes = pinnedPlanItem
      ? renderItemSequence([pinnedPlanItem], 'pinned-plan')
      : []
    const trailingNodes = renderItemSequence(trailingItems)

    if (intermediateNodes.length > 0) {
      const elapsedMs = getIntermediateElapsedMs(turn, renderableItems[lastFinalAgentMessageIndex])
      renderNodes.push(
        <TurnCollapsedSummary
          key={`turn-collapsed-${turn.id}`}
          elapsedMs={elapsedMs}
        >
          <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
            {intermediateNodes}
          </div>
        </TurnCollapsedSummary>
      )
    }

    renderNodes.push(...pinnedPlanNodes)
    renderNodes.push(...trailingNodes)
  } else {
    renderNodes.push(...renderItemSequence(renderableItems))
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
      {renderNodes}

      {/* Turn-level failure */}
      {turn.status === 'failed' && turn.error && (
        <ErrorBlock message={turn.error} />
      )}

      {/* Cancellation notice */}
      {turn.status === 'cancelled' && (
        <CancelledNotice reason={turn.cancelReason} />
      )}

      {/* Turn completion artifacts and file changes */}
      {turn.status === 'completed' && (
        <>
          <TurnArtifacts turnId={turn.id} />
          <TurnCompletionSummary turnId={turn.id} />
        </>
      )}
    </div>
  )
})

// ── Render helpers ────────────────────────────────────────────────────────────

function renderAggregatedEntry(
  entry: AggregatedToolCall,
  turnId: string,
  offset: number,
  turnRunning: boolean,
  keyPrefix = ''
): React.ReactNode {
  if (entry.kind === 'single') {
    return (
      <ToolCallCard
        key={entry.item.id}
        item={entry.item}
        turnId={turnId}
        turnRunning={turnRunning}
      />
    )
  }
  return (
    <GroupedToolCallRow
      key={`group-${keyPrefix}-${turnId}-${offset}`}
      category={entry.category}
      items={entry.items}
      turnId={turnId}
      turnRunning={turnRunning}
    />
  )
}

// ── Grouped tool call row ─────────────────────────────────────────────────────

interface GroupedToolCallRowProps {
  category: ToolGroupCategory
  items: ConversationItem[]
  turnId: string
  turnRunning: boolean
}

/**
 * Collapsed summary row for a group of consecutive aggregated tool calls.
 * Expandable to show each individual child tool card.
 */
function GroupedToolCallRow({ category, items, turnId, turnRunning }: GroupedToolCallRowProps): JSX.Element {
  const locale = useLocale()
  const changedFiles = useConversationStore((s) => s.changedFiles)
  const label = formatToolGroupLabel(category, items, locale, changedFiles)
  const hasFailedItems = items.some(isGroupedItemFailed)
  const [expanded, setExpanded] = useState(false)
  const [hovered, setHovered] = useState(false)
  const rowColor = hovered || expanded ? 'var(--text-secondary)' : 'var(--text-dimmed)'

  return (
    <div>
      <button
        onClick={() => setExpanded((v) => !v)}
        onMouseEnter={() => setHovered(true)}
        onMouseLeave={() => setHovered(false)}
        onFocus={() => setHovered(true)}
        onBlur={() => setHovered(false)}
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: '6px',
          width: '100%',
          padding: '3px 6px',
          background: 'transparent',
          border: 'none',
          cursor: 'pointer',
          color: hasFailedItems ? 'var(--error)' : rowColor,
          fontSize: '12px',
          textAlign: 'left',
          borderRadius: '4px'
        }}
      >
        <span
          data-testid="tool-row-title-group"
          style={{
            display: 'inline-flex',
            alignItems: 'center',
            gap: '3px',
            flex: '0 1 auto',
            minWidth: 0,
            maxWidth: '100%',
            color: hasFailedItems ? 'var(--error)' : rowColor
          }}
        >
          <span style={{ minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
            {label}
          </span>
          <ToolCollapseChevron expanded={expanded} visible={hovered || expanded} />
        </span>
      </button>
      {expanded && (
        <div style={{ paddingLeft: '16px' }}>
          {items.map((item) => (
            <ToolCallCard key={item.id} item={item} turnId={turnId} turnRunning={turnRunning} />
          ))}
        </div>
      )}
    </div>
  )
}

function isGroupedItemFailed(item: ConversationItem): boolean {
  if (isToolItemLive(item)) return false
  const executionFailed = item.executionStatus === 'failed'
    || item.executionStatus === 'cancelled'
    || (item.exitCode != null && item.exitCode !== 0)
  return item.success === false || executionFailed
}

function findLastAgentMessageIndex(items: ConversationItem[]): number {
  for (let i = items.length - 1; i >= 0; i--) {
    if (items[i].type === 'agentMessage') {
      return i
    }
  }
  return -1
}

function findLastCreatePlanIndexBefore(items: ConversationItem[], beforeIndex: number): number {
  for (let i = beforeIndex - 1; i >= 0; i--) {
    const item = items[i]
    const isToolCall = isToolLikeItemType(item.type)
    if (
      isToolCall
      && item.toolName === 'CreatePlan'
      && item.status === 'completed'
      && item.success !== false
    ) {
      return i
    }
  }
  return -1
}

function isGuidanceUserMessage(item: ConversationItem): boolean {
  return item.type === 'userMessage' && item.deliveryMode === 'guidance'
}

function getIntermediateElapsedMs(
  turn: ConversationTurn,
  finalAgentMessage: ConversationItem | undefined
): number {
  const turnStartMs = Date.parse(turn.startedAt)
  if (!Number.isFinite(turnStartMs)) return 0

  const finalStartMs = finalAgentMessage?.createdAt ? Date.parse(finalAgentMessage.createdAt) : Number.NaN
  if (Number.isFinite(finalStartMs) && finalStartMs >= turnStartMs) {
    return finalStartMs - turnStartMs
  }

  const turnCompletedMs = turn.completedAt ? Date.parse(turn.completedAt) : Number.NaN
  if (Number.isFinite(turnCompletedMs) && turnCompletedMs >= turnStartMs) {
    return turnCompletedMs - turnStartMs
  }

  return 0
}
