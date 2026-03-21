interface ScrollToBottomButtonProps {
  onClick: () => void
}

/**
 * Floating button shown when the user has scrolled up from the bottom.
 * Clicking it jumps back to the latest messages.
 */
export function ScrollToBottomButton({ onClick }: ScrollToBottomButtonProps): JSX.Element {
  return (
    <button
      onClick={onClick}
      aria-label="Scroll to bottom"
      title="Scroll to bottom"
      style={{
        position: 'absolute',
        bottom: '12px',
        right: '12px',
        width: '32px',
        height: '32px',
        borderRadius: '50%',
        backgroundColor: 'var(--bg-elevated)',
        border: '1px solid var(--border-default)',
        color: 'var(--text-secondary)',
        cursor: 'pointer',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        fontSize: '14px',
        boxShadow: 'var(--shadow-md)',
        zIndex: 10,
        transition: 'background-color 100ms ease'
      }}
    >
      ↓
    </button>
  )
}
