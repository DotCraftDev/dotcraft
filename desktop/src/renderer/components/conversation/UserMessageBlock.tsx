interface UserMessageBlockProps {
  text: string
}

/**
 * Renders a user message with a subtle background tint.
 * Plain text only — no Markdown. Spec §10.3.2
 */
export function UserMessageBlock({ text }: UserMessageBlockProps): JSX.Element {
  return (
    <div
      style={{
        backgroundColor: 'var(--user-message-bg)',
        borderRadius: '8px',
        padding: '10px 14px',
        fontSize: '14px',
        lineHeight: 1.6,
        color: 'var(--text-primary)',
        whiteSpace: 'pre-wrap',
        wordBreak: 'break-word',
        alignSelf: 'flex-end',
        maxWidth: '85%'
      }}
    >
      {text}
    </div>
  )
}
