import { useState, useEffect } from 'react'
import { useT } from '../../contexts/LocaleContext'

interface Props {
  onConfirm(reason: string): void
  onCancel(): void
}

/**
 * Optional rejection reason for automation task reject flow (M8).
 */
export function RejectDialog({ onConfirm, onCancel }: Props): JSX.Element {
  const t = useT()
  const [reason, setReason] = useState('')

  useEffect(() => {
    function handleKeyDown(e: KeyboardEvent): void {
      if (e.key === 'Escape') onCancel()
    }
    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [onCancel])

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby="reject-title"
      style={{
        position: 'fixed',
        inset: 0,
        zIndex: 10001,
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        backgroundColor: 'rgba(0,0,0,0.5)'
      }}
      onMouseDown={(e) => {
        if (e.target === e.currentTarget) onCancel()
      }}
    >
      <div
        style={{
          width: '420px',
          maxHeight: '80vh',
          backgroundColor: 'var(--bg-primary)',
          borderRadius: '12px',
          border: '1px solid var(--border-default)',
          display: 'flex',
          flexDirection: 'column',
          overflow: 'hidden',
          boxShadow: '0 8px 32px rgba(0,0,0,0.3)'
        }}
        onMouseDown={(e) => e.stopPropagation()}
      >
        <div
          id="reject-title"
          style={{
            padding: '16px 20px',
            borderBottom: '1px solid var(--border-default)',
            fontSize: '15px',
            fontWeight: 600,
            color: 'var(--text-primary)'
          }}
        >
          {t('auto.rejectDialogTitle')}
        </div>
        <div style={{ padding: '16px 20px' }}>
          <label style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
            <span style={{ fontSize: '12px', color: 'var(--text-secondary)' }}>
              {t('auto.rejectReasonLabel')}
            </span>
            <textarea
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              rows={4}
              placeholder={t('auto.rejectPlaceholder')}
              style={{
                padding: '8px 10px',
                borderRadius: '6px',
                border: '1px solid var(--border-default)',
                backgroundColor: 'var(--bg-secondary)',
                color: 'var(--text-primary)',
                fontSize: '13px',
                resize: 'vertical',
                fontFamily: 'inherit'
              }}
            />
          </label>
        </div>
        <div
          style={{
            padding: '12px 20px',
            borderTop: '1px solid var(--border-default)',
            display: 'flex',
            justifyContent: 'flex-end',
            gap: '8px'
          }}
        >
          <button
            type="button"
            onClick={onCancel}
            style={{
              padding: '6px 14px',
              borderRadius: '6px',
              border: '1px solid var(--border-default)',
              backgroundColor: 'transparent',
              color: 'var(--text-secondary)',
              fontSize: '13px',
              cursor: 'pointer'
            }}
          >
            {t('common.cancel')}
          </button>
          <button
            type="button"
            onClick={() => onConfirm(reason.trim())}
            style={{
              padding: '6px 14px',
              borderRadius: '6px',
              border: 'none',
              backgroundColor: 'var(--error)',
              color: '#fff',
              fontSize: '13px',
              fontWeight: 600,
              cursor: 'pointer'
            }}
          >
            {t('auto.reject')}
          </button>
        </div>
      </div>
    </div>
  )
}
