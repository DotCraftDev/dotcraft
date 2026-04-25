import { useEffect, useId, useRef, useState, type JSX } from 'react'
import { FileText, ImagePlus, Plus } from 'lucide-react'
import { ActionTooltip } from '../ui/ActionTooltip'

interface ComposerAttachmentMenuProps {
  title: string
  ariaLabel: string
  attachImageLabel: string
  referenceFileLabel: string
  onAttachImages: (files: File[]) => void
  onReferenceFiles: () => void
  disabled?: boolean
}

export function ComposerAttachmentMenu({
  title,
  ariaLabel,
  attachImageLabel,
  referenceFileLabel,
  onAttachImages,
  onReferenceFiles,
  disabled = false
}: ComposerAttachmentMenuProps): JSX.Element {
  const [open, setOpen] = useState(false)
  const wrapRef = useRef<HTMLDivElement>(null)
  const fileInputRef = useRef<HTMLInputElement>(null)
  const menuId = useId()

  useEffect(() => {
    if (!open) return

    const handlePointerDown = (event: MouseEvent): void => {
      if (!wrapRef.current?.contains(event.target as Node)) {
        setOpen(false)
      }
    }

    const handleKeyDown = (event: KeyboardEvent): void => {
      if (event.key === 'Escape') {
        event.preventDefault()
        setOpen(false)
      }
    }

    window.addEventListener('mousedown', handlePointerDown, true)
    window.addEventListener('keydown', handleKeyDown, true)
    return () => {
      window.removeEventListener('mousedown', handlePointerDown, true)
      window.removeEventListener('keydown', handleKeyDown, true)
    }
  }, [open])

  return (
    <div ref={wrapRef} style={{ position: 'relative', flexShrink: 0 }}>
      <input
        ref={fileInputRef}
        type="file"
        accept="image/*"
        multiple
        tabIndex={-1}
        aria-hidden
        style={{ display: 'none' }}
        onChange={(event) => {
          const files = Array.from(event.currentTarget.files ?? [])
          event.currentTarget.value = ''
          if (files.length === 0) return
          onAttachImages(files)
        }}
      />

      <ActionTooltip label={title} placement="top">
        <button
          type="button"
          aria-label={ariaLabel}
          aria-haspopup="menu"
          aria-expanded={open}
          aria-controls={open ? menuId : undefined}
          disabled={disabled}
          onClick={() => {
            if (disabled) return
            setOpen((current) => !current)
          }}
          style={{
            display: 'inline-flex',
            alignItems: 'center',
            justifyContent: 'center',
            padding: '2px',
            borderRadius: '8px',
            border: 'none',
            background: 'transparent',
            color: disabled ? 'var(--text-dimmed)' : 'var(--text-secondary)',
            cursor: disabled ? 'default' : 'pointer',
            lineHeight: 1
          }}
      >
          <Plus size={16} strokeWidth={2} aria-hidden />
        </button>
      </ActionTooltip>

      {open && !disabled && (
        <div
          id={menuId}
          role="menu"
          aria-label={ariaLabel}
          style={{
            position: 'absolute',
            left: 0,
            bottom: 'calc(100% + 8px)',
            minWidth: '180px',
            zIndex: 70,
            border: '1px solid var(--border-default)',
            borderRadius: '12px',
            background: 'var(--bg-secondary)',
            boxShadow: '0 12px 30px rgba(0, 0, 0, 0.26)',
            padding: '6px'
          }}
        >
          <button
            type="button"
            role="menuitem"
            onClick={() => {
              setOpen(false)
              fileInputRef.current?.click()
            }}
            style={menuItemStyle}
          >
            <ImagePlus size={14} aria-hidden />
            <span>{attachImageLabel}</span>
          </button>
          <button
            type="button"
            role="menuitem"
            onClick={() => {
              setOpen(false)
              onReferenceFiles()
            }}
            style={menuItemStyle}
          >
            <FileText size={14} aria-hidden />
            <span>{referenceFileLabel}</span>
          </button>
        </div>
      )}
    </div>
  )
}

const menuItemStyle = {
  width: '100%',
  display: 'flex',
  alignItems: 'center',
  gap: '8px',
  border: 'none',
  borderRadius: '10px',
  padding: '8px 10px',
  background: 'transparent',
  color: 'var(--text-secondary)',
  cursor: 'pointer',
  textAlign: 'left',
  fontSize: 'var(--type-secondary-size)',
  lineHeight: 'var(--type-secondary-line-height)'
} as const
