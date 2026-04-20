/**
 * Popup menu anchored to the `+` button in the detail panel tab strip.
 * M1: "Open File" → opens the Quick-Open dialog.
 * M2 placeholder: "New Browser Tab" (disabled).
 */
import { useRef, useEffect, type KeyboardEvent } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { useUIStore } from '../../stores/uiStore'
import { FolderOpen, Globe } from 'lucide-react'

interface AddTabPopupProps {
  anchorRef: React.RefObject<HTMLElement | null>
  onClose: () => void
}

export function AddTabPopup({ anchorRef, onClose }: AddTabPopupProps): JSX.Element {
  const t = useT()
  const setQuickOpenVisible = useUIStore((s) => s.setQuickOpenVisible)
  const popupRef = useRef<HTMLDivElement>(null)

  // Close on outside click
  useEffect(() => {
    const handlePointerDown = (e: PointerEvent): void => {
      if (
        popupRef.current &&
        !popupRef.current.contains(e.target as Node) &&
        anchorRef.current &&
        !anchorRef.current.contains(e.target as Node)
      ) {
        onClose()
      }
    }
    document.addEventListener('pointerdown', handlePointerDown)
    return () => document.removeEventListener('pointerdown', handlePointerDown)
  }, [anchorRef, onClose])

  // Close on Escape
  const handleKeyDown = (e: KeyboardEvent<HTMLDivElement>): void => {
    if (e.key === 'Escape') {
      e.preventDefault()
      onClose()
      anchorRef.current?.focus()
    }
  }

  const handleOpenFile = (): void => {
    setQuickOpenVisible(true)
    onClose()
  }

  // Calculate position from anchor
  const anchor = anchorRef.current?.getBoundingClientRect()
  const top = anchor ? anchor.bottom + 4 : 0
  const left = anchor ? anchor.left : 0

  return (
    <div
      ref={popupRef}
      role="menu"
      aria-label={t('detailPanel.addTab')}
      onKeyDown={handleKeyDown}
      style={{
        position: 'fixed',
        top,
        left,
        zIndex: 1000,
        minWidth: '180px',
        backgroundColor: 'var(--bg-elevated, #2a2a2a)',
        border: '1px solid var(--border-default)',
        borderRadius: '6px',
        boxShadow: '0 4px 16px rgba(0,0,0,0.3)',
        padding: '4px 0',
        outline: 'none'
      }}
      tabIndex={-1}
    >
      {/* Open File */}
      <button
        role="menuitem"
        onClick={handleOpenFile}
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: '8px',
          width: '100%',
          padding: '6px 12px',
          border: 'none',
          background: 'transparent',
          color: 'var(--text-primary)',
          fontSize: '13px',
          cursor: 'pointer',
          textAlign: 'left'
        }}
        onMouseEnter={(e) => {
          ;(e.currentTarget as HTMLButtonElement).style.backgroundColor = 'var(--bg-hover, rgba(255,255,255,0.06))'
        }}
        onMouseLeave={(e) => {
          ;(e.currentTarget as HTMLButtonElement).style.backgroundColor = 'transparent'
        }}
      >
        <FolderOpen size={14} aria-hidden style={{ display: 'block', flexShrink: 0 }} />
        {t('detailPanel.addTabOpenFile')}
      </button>

      {/* New Browser Tab — M2 placeholder, disabled */}
      <button
        role="menuitem"
        disabled
        title={t('detailPanel.addTabNewBrowserLater')}
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: '8px',
          width: '100%',
          padding: '6px 12px',
          border: 'none',
          background: 'transparent',
          color: 'var(--text-disabled, rgba(255,255,255,0.3))',
          fontSize: '13px',
          cursor: 'not-allowed',
          textAlign: 'left'
        }}
      >
        <Globe size={14} aria-hidden style={{ display: 'block', flexShrink: 0 }} />
        {t('detailPanel.addTabNewBrowser')}
      </button>
    </div>
  )
}
