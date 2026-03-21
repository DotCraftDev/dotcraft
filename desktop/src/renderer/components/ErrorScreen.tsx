import { useConnectionStore } from '../stores/connectionStore'

interface ErrorScreenProps {
  onOpenSettings?: () => void
}

/**
 * Full-screen error display for fatal AppServer connection errors.
 * Spec §18.1, M7-15, M7-16
 *
 * Shown when:
 * - AppServer binary not found (errorType: 'binary-not-found')
 * - Initialize handshake timeout (errorType: 'handshake-timeout')
 */
export function ErrorScreen({ onOpenSettings }: ErrorScreenProps = {}): JSX.Element | null {
  const { status, errorMessage, errorType } = useConnectionStore()

  if (status !== 'error') return null

  const isBinaryNotFound = errorType === 'binary-not-found'
  const isHandshakeTimeout = errorType === 'handshake-timeout'

  const title = isBinaryNotFound
    ? 'DotCraft AppServer Not Found'
    : isHandshakeTimeout
      ? 'AppServer Not Responding'
      : 'Connection Error'

  const description = isBinaryNotFound
    ? 'Please install DotCraft or configure the binary path in Settings.'
    : isHandshakeTimeout
      ? 'The AppServer did not respond within 10 seconds.'
      : (errorMessage ?? 'An unexpected error occurred.')

  const actionLabel = isBinaryNotFound
    ? 'Open Settings'
    : isHandshakeTimeout
      ? 'Restart'
      : 'Retry'

  function handleAction(): void {
    if (isBinaryNotFound) {
      onOpenSettings?.()
    } else {
      // Reload the window to trigger reconnection
      window.location.reload()
    }
  }

  return (
    <div
      style={{
        position: 'fixed',
        inset: 0,
        backgroundColor: 'var(--bg-primary)',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        zIndex: 9999,
        padding: '32px'
      }}
      role="alert"
      aria-live="assertive"
    >
      <div
        style={{
          textAlign: 'center',
          maxWidth: '480px'
        }}
      >
        {/* Error icon */}
        <div
          style={{
            width: '56px',
            height: '56px',
            borderRadius: '50%',
            backgroundColor: 'rgba(239, 68, 68, 0.15)',
            border: '2px solid var(--error)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            margin: '0 auto 20px',
            fontSize: '24px',
            color: 'var(--error)'
          }}
          aria-hidden="true"
        >
          ✕
        </div>

        {/* Title */}
        <h1
          style={{
            fontSize: '20px',
            fontWeight: 600,
            color: 'var(--text-primary)',
            marginBottom: '12px'
          }}
        >
          {title}
        </h1>

        {/* Description */}
        <p
          style={{
            fontSize: '14px',
            color: 'var(--text-secondary)',
            lineHeight: 1.6,
            marginBottom: '28px'
          }}
        >
          {description}
        </p>

        {/* Action button */}
        <button
          onClick={handleAction}
          style={{
            padding: '10px 24px',
            backgroundColor: 'var(--accent)',
            color: '#ffffff',
            border: 'none',
            borderRadius: '8px',
            fontSize: '14px',
            fontWeight: 500,
            cursor: 'pointer',
            transition: 'background-color 150ms ease',
            boxShadow: 'var(--shadow-level-1)'
          }}
          onMouseEnter={(e) => {
            ;(e.currentTarget as HTMLButtonElement).style.backgroundColor = 'var(--accent-hover)'
          }}
          onMouseLeave={(e) => {
            ;(e.currentTarget as HTMLButtonElement).style.backgroundColor = 'var(--accent)'
          }}
        >
          {actionLabel}
        </button>

        {/* Error details (collapsible for debugging) */}
        {errorMessage && !isBinaryNotFound && !isHandshakeTimeout && (
          <details
            style={{
              marginTop: '20px',
              textAlign: 'left'
            }}
          >
            <summary
              style={{
                fontSize: '12px',
                color: 'var(--text-dimmed)',
                cursor: 'pointer',
                marginBottom: '8px'
              }}
            >
              Error details
            </summary>
            <pre
              style={{
                fontSize: '11px',
                color: 'var(--error)',
                backgroundColor: 'var(--bg-secondary)',
                padding: '12px',
                borderRadius: '6px',
                overflow: 'auto',
                whiteSpace: 'pre-wrap',
                wordBreak: 'break-all',
                border: '1px solid var(--border-default)'
              }}
            >
              {errorMessage}
            </pre>
          </details>
        )}
      </div>
    </div>
  )
}
