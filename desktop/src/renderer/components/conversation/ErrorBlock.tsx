interface ErrorBlockProps {
  message: string
}

/**
 * Red-tinted error block for turn/failed or error items.
 * Spec §10.3.3 / §18.3
 */
export function ErrorBlock({ message }: ErrorBlockProps): JSX.Element {
  return (
    <div
      role="alert"
      style={{
        backgroundColor: 'rgba(239, 68, 68, 0.1)',
        border: '1px solid var(--error)',
        borderRadius: '6px',
        padding: '10px 14px',
        color: 'var(--error)',
        fontSize: '13px',
        lineHeight: 1.5,
        marginTop: '4px'
      }}
    >
      <strong style={{ display: 'block', marginBottom: '4px', fontWeight: 600 }}>Error</strong>
      <span>{message}</span>
    </div>
  )
}
