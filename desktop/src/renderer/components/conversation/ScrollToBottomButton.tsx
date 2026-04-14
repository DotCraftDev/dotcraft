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
        lineHeight: 0,
        boxShadow: 'var(--shadow-md)',
        zIndex: 10,
        transition: 'background-color 100ms ease'
      }}
    >
      <svg
        width="16"
        height="16"
        viewBox="0 0 16 16"
        fill="none"
        stroke="currentColor"
        strokeWidth="1.9"
        strokeLinecap="round"
        strokeLinejoin="round"
        aria-hidden="true"
      >
        <path d="M8 2.5v7" />
        <path d="M4.5 7.5L8 11l3.5-3.5" />
        <path d="M4 13h8" />
      </svg>
    </button>
  )
}
