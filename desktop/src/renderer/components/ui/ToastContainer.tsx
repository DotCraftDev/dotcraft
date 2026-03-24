import { useEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { useToastStore, type Toast, type ToastType } from '../../stores/toastStore'
import { MarkdownRenderer } from '../conversation/MarkdownRenderer'

/**
 * Stacked toast notification container, fixed to the top-right corner.
 * Toasts auto-dismiss and can be click-dismissed.
 * Spec §6.9, M7
 */
export function ToastContainer(): JSX.Element {
  const toasts = useToastStore((s) => s.toasts)
  const removeToast = useToastStore((s) => s.removeToast)

  const isMac = window.api.platform === 'darwin'
  const topPx = isMac ? 16 : window.api.titleBarOverlayHeight + 16
  const rightPx = isMac ? 16 : 16 + window.api.titleBarOverlayRightReserve

  return createPortal(
    <div
      aria-live="polite"
      aria-atomic="false"
      style={{
        position: 'fixed',
        top: `${topPx}px`,
        right: `${rightPx}px`,
        zIndex: 30000,
        display: 'flex',
        flexDirection: 'column',
        gap: '8px',
        pointerEvents: 'none',
        maxWidth: '360px',
        width: '100%'
      }}
    >
      {toasts.map((toast) => (
        <ToastItem key={toast.id} toast={toast} onDismiss={() => removeToast(toast.id)} />
      ))}
    </div>,
    document.body
  ) as JSX.Element
}

interface ToastItemProps {
  toast: Toast
  onDismiss: () => void
}

function ToastItem({ toast, onDismiss }: ToastItemProps): JSX.Element {
  const [visible, setVisible] = useState(false)
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null)

  // Trigger fade-in on mount
  useEffect(() => {
    const t = setTimeout(() => setVisible(true), 10)
    return () => clearTimeout(t)
  }, [])

  function handleDismiss(): void {
    setVisible(false)
    timerRef.current = setTimeout(onDismiss, 300)
  }

  useEffect(() => {
    return () => {
      if (timerRef.current) clearTimeout(timerRef.current)
    }
  }, [])

  const borderColor = typeToColor(toast.type)

  return (
    <div
      role="alert"
      onClick={handleDismiss}
      style={{
        pointerEvents: 'auto',
        padding: '10px 14px',
        borderRadius: '8px',
        background: 'var(--bg-secondary)',
        border: `1px solid ${borderColor}`,
        boxShadow: 'var(--shadow-level-2)',
        fontSize: '13px',
        color: 'var(--text-primary)',
        cursor: 'pointer',
        userSelect: 'none',
        display: 'flex',
        alignItems: 'flex-start',
        gap: '8px',
        opacity: visible ? 1 : 0,
        transform: visible ? 'translateY(0)' : 'translateY(-8px)',
        transition: 'opacity 250ms ease, transform 250ms ease',
        maxWidth: '100%',
        wordBreak: 'break-word'
      }}
      title="Click to dismiss"
    >
      <span style={{ color: borderColor, flexShrink: 0, fontSize: '14px', marginTop: '1px' }}>
        {typeToIcon(toast.type)}
      </span>
      {toast.markdown ? (
        <div
          style={{
            flex: 1,
            lineHeight: 1.4,
            maxHeight: 120,
            overflow: 'auto',
            fontSize: '13px'
          }}
        >
          <MarkdownRenderer content={toast.message} />
        </div>
      ) : (
        <span style={{ flex: 1, lineHeight: 1.4 }}>{toast.message}</span>
      )}
    </div>
  )
}

function typeToColor(type: ToastType): string {
  switch (type) {
    case 'success': return 'var(--success)'
    case 'warning': return 'var(--warning)'
    case 'error':   return 'var(--error)'
    default:        return 'var(--border-default)'
  }
}

function typeToIcon(type: ToastType): string {
  switch (type) {
    case 'success': return '✓'
    case 'warning': return '⚠'
    case 'error':   return '✕'
    default:        return 'ℹ'
  }
}
