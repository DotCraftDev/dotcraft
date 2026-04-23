import { useState, useEffect } from 'react'
import { useConversationStore } from '../../stores/conversationStore'
import { RunningSpinner } from '../ui/RunningSpinner'

/**
 * Shown above the InputComposer while a turn is running.
 * Displays: spinner + "Working..." (or systemLabel) + elapsed time.
 * Spec §10.3.5
 */
export function TurnStatusIndicator(): JSX.Element | null {
  const turnStatus = useConversationStore((s) => s.turnStatus)
  const turnStartedAt = useConversationStore((s) => s.turnStartedAt)
  const systemLabel = useConversationStore((s) => s.systemLabel)

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
      <RunningSpinner title="Working" />

      {/* Label */}
      <span style={{ flex: 1 }}>
        {label}
        {elapsedLabel && (
          <span style={{ color: 'var(--text-dimmed)', marginLeft: '4px' }}>{elapsedLabel}</span>
        )}
      </span>

    </div>
  )
}
