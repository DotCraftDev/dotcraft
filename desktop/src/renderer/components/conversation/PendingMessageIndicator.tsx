interface PendingMessageIndicatorProps {
  message: string
}

/**
 * Shows a queued follow-up message below the input composer.
 * Only rendered when pendingMessage !== null in conversationStore.
 */
export function PendingMessageIndicator({ message }: PendingMessageIndicatorProps): JSX.Element {
  // Truncate long messages in the indicator
  const displayText = message.length > 80 ? `${message.slice(0, 80)}…` : message

  return (
    <div
      style={{
        display: 'flex',
        alignItems: 'center',
        gap: '6px',
        padding: '4px 12px',
        fontSize: '11px',
        color: 'var(--text-dimmed)'
      }}
    >
      <span
        style={{
          display: 'inline-block',
          width: '6px',
          height: '6px',
          borderRadius: '50%',
          backgroundColor: 'var(--warning)',
          flexShrink: 0
        }}
      />
      <span>Queued: &ldquo;{displayText}&rdquo;</span>
    </div>
  )
}
