import { useState, useEffect, useCallback } from 'react'
import { useConversationStore } from '../../stores/conversationStore'
import { TokenUsageDisplay } from './TokenUsageDisplay'

interface TurnStatusIndicatorProps {
  threadId: string
}

/**
 * Shown above the InputComposer while a turn is running.
 * Displays: spinner + "Working..." (or systemLabel) + elapsed time + token usage + "esc to cancel"
 * Spec §10.3.5
 */
export function TurnStatusIndicator({ threadId }: TurnStatusIndicatorProps): JSX.Element | null {
  const turnStatus = useConversationStore((s) => s.turnStatus)
  const turnStartedAt = useConversationStore((s) => s.turnStartedAt)
  const systemLabel = useConversationStore((s) => s.systemLabel)
  const inputTokens = useConversationStore((s) => s.inputTokens)
  const outputTokens = useConversationStore((s) => s.outputTokens)
  const activeTurnId = useConversationStore((s) => s.activeTurnId)

  const [elapsed, setElapsed] = useState(0)

  useEffect(() => {
    if (turnStatus !== 'running' || !turnStartedAt) {
      setElapsed(0)
      return
    }
    // Tick every second
    setElapsed(Math.floor((Date.now() - turnStartedAt) / 1000))
    const id = setInterval(() => {
      setElapsed(Math.floor((Date.now() - turnStartedAt) / 1000))
    }, 1000)
    return () => clearInterval(id)
  }, [turnStatus, turnStartedAt])

  const handleCancel = useCallback(async () => {
    // Don't send interrupt if we only have a local optimistic ID (server hasn't confirmed yet)
    if (!activeTurnId || activeTurnId.startsWith('local-turn-')) return
    try {
      await window.api.appServer.sendRequest('turn/interrupt', { threadId, turnId: activeTurnId })
    } catch (err) {
      console.error('turn/interrupt failed:', err)
    }
  }, [threadId, activeTurnId])

  if (turnStatus !== 'running') return null

  const label = systemLabel ?? 'Working...'
  const elapsedLabel = elapsed > 0 ? `${elapsed}s` : ''

  return (
    <div
      role="status"
      aria-live="polite"
      aria-label={`${label}${elapsedLabel ? ' ' + elapsedLabel : ''}`}
      style={{
        display: 'flex',
        alignItems: 'center',
        gap: '8px',
        padding: '6px 16px',
        fontSize: '12px',
        color: 'var(--text-secondary)',
        flexShrink: 0
      }}
    >
      {/* Spinner */}
      <span
        aria-label="Working"
        style={{
          display: 'inline-block',
          width: '12px',
          height: '12px',
          border: '2px solid var(--text-dimmed)',
          borderTopColor: 'var(--accent)',
          borderRadius: '50%',
          animation: 'spin 1s linear infinite',
          flexShrink: 0
        }}
      />

      {/* Label */}
      <span style={{ flex: 1 }}>
        {label}
        {elapsedLabel && (
          <span style={{ color: 'var(--text-dimmed)', marginLeft: '4px' }}>{elapsedLabel}</span>
        )}
      </span>

      {/* Token usage */}
      <TokenUsageDisplay inputTokens={inputTokens} outputTokens={outputTokens} />

      {/* Cancel hint */}
      <button
        onClick={handleCancel}
        title="Cancel turn (Esc)"
        style={{
          background: 'none',
          border: 'none',
          cursor: 'pointer',
          padding: '0 2px',
          fontSize: '11px',
          color: 'var(--text-dimmed)',
          textDecoration: 'underline',
          flexShrink: 0
        }}
      >
        esc to cancel
      </button>
    </div>
  )
}
