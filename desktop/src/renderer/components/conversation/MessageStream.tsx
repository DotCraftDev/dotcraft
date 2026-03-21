import { useEffect, useRef } from 'react'
import { useConversationStore } from '../../stores/conversationStore'
import { useThreadStore } from '../../stores/threadStore'
import { useAutoScroll } from '../../hooks/useAutoScroll'
import { UserMessageBlock } from './UserMessageBlock'
import { AgentResponseBlock } from './AgentResponseBlock'
import { ScrollToBottomButton } from './ScrollToBottomButton'
import type { ConversationItem, ConversationTurn } from '../../types/conversation'

/** Module-level scroll position cache — ephemeral, not persisted to storage. */
const scrollPositionCache = new Map<string, number>()

const NEAR_BOTTOM_THRESHOLD = 50

/**
 * Scrollable container that renders the full turn history and live streaming content.
 * Spec §10.3.3, M7-14 (scroll position restoration)
 */
export function MessageStream(): JSX.Element {
  const turns = useConversationStore((s) => s.turns)
  const turnStatus = useConversationStore((s) => s.turnStatus)
  const activeTurnId = useConversationStore((s) => s.activeTurnId)
  const streamingMessage = useConversationStore((s) => s.streamingMessage)
  const streamingReasoning = useConversationStore((s) => s.streamingReasoning)
  const activeThreadId = useThreadStore((s) => s.activeThreadId)
  const prevThreadIdRef = useRef<string | null>(null)

  // Use total character count + turn count as a proxy for content size changes
  const contentLength = turns.reduce((acc, t) => acc + t.items.length, 0) +
    streamingMessage.length + streamingReasoning.length

  const { scrollRef, showScrollButton, scrollToBottom } = useAutoScroll(contentLength)

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
          padding: '16px 20px',
          display: 'flex',
          flexDirection: 'column',
          gap: '16px'
        }}
      >
        {turns.map((turn) => (
          <TurnBlock
            key={turn.id}
            turn={turn}
            streamingMessage={turn.id === activeTurnId ? streamingMessage : ''}
            streamingReasoning={turn.id === activeTurnId ? streamingReasoning : ''}
            isRunning={turnStatus === 'running' && turn.id === activeTurnId}
            isActiveTurn={turn.id === activeTurnId}
          />
        ))}

        {/* Bottom anchor for auto-scroll */}
        <div />
      </div>

      {showScrollButton && <ScrollToBottomButton onClick={scrollToBottom} />}
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
}

function TurnBlock({
  turn,
  streamingMessage,
  streamingReasoning,
  isRunning,
  isActiveTurn
}: TurnBlockProps): JSX.Element {
  // Separate user-input items from agent items
  const userItems = turn.items.filter((i: ConversationItem) => i.type === 'userMessage')

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
      {/* User messages */}
      {userItems.map((item: ConversationItem) => (
        <UserMessageBlock key={item.id} text={item.text ?? ''} />
      ))}

      {/* Agent response */}
      <AgentResponseBlock
        turn={turn}
        streamingMessage={streamingMessage}
        streamingReasoning={streamingReasoning}
        isRunning={isRunning}
        isActiveTurn={isActiveTurn}
      />
    </div>
  )
}
