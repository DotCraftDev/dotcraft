import { useRef, useEffect, useState } from 'react'
import { useThreadStore } from '../../stores/threadStore'

interface ThreadSearchProps {
  /** Ref for external focus control (e.g. Ctrl+K shortcut) */
  inputRef?: React.RefObject<HTMLInputElement | null>
}

/**
 * Thread search input with 150ms debounce and Escape-to-clear.
 * Spec §9.4
 */
export function ThreadSearch({ inputRef: externalRef }: ThreadSearchProps): JSX.Element {
  const { searchQuery, setSearchQuery } = useThreadStore()
  const internalRef = useRef<HTMLInputElement>(null)
  const ref = externalRef ?? internalRef
  const [localValue, setLocalValue] = useState(searchQuery)
  const debounceTimer = useRef<ReturnType<typeof setTimeout> | null>(null)

  // Sync external searchQuery reset back to local value
  useEffect(() => {
    setLocalValue(searchQuery)
  }, [searchQuery])

  function handleChange(e: React.ChangeEvent<HTMLInputElement>): void {
    const value = e.target.value
    setLocalValue(value)
    if (debounceTimer.current) clearTimeout(debounceTimer.current)
    debounceTimer.current = setTimeout(() => {
      setSearchQuery(value)
    }, 150)
  }

  function handleKeyDown(e: React.KeyboardEvent<HTMLInputElement>): void {
    if (e.key === 'Escape') {
      setLocalValue('')
      setSearchQuery('')
      ;(e.currentTarget as HTMLInputElement).blur()
    }
  }

  // Cleanup debounce on unmount
  useEffect(() => {
    return () => {
      if (debounceTimer.current) clearTimeout(debounceTimer.current)
    }
  }, [])

  return (
    <div
      style={{
        padding: '6px 12px',
        borderBottom: '1px solid var(--border-default)',
        flexShrink: 0
      }}
    >
      <div style={{ position: 'relative' }}>
        <span
          style={{
            position: 'absolute',
            left: '8px',
            top: '50%',
            transform: 'translateY(-50%)',
            color: 'var(--text-dimmed)',
            lineHeight: 0,
            pointerEvents: 'none',
            display: 'flex',
            alignItems: 'center'
          }}
          aria-hidden="true"
        >
          <svg width="13" height="13" viewBox="0 0 16 16" fill="none" xmlns="http://www.w3.org/2000/svg">
            <circle cx="7" cy="7" r="4.5" stroke="currentColor" strokeWidth="1.5" />
            <line x1="10.2" y1="10.2" x2="14" y2="14" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" />
          </svg>
        </span>
        <input
          ref={ref as React.RefObject<HTMLInputElement>}
          type="text"
          value={localValue}
          onChange={handleChange}
          onKeyDown={handleKeyDown}
          placeholder="Search threads..."
          aria-label="Search conversations"
          style={{
            width: '100%',
            padding: '5px 28px 5px 26px',
            backgroundColor: 'var(--bg-tertiary)',
            border: '1px solid var(--border-default)',
            borderRadius: '6px',
            color: 'var(--text-primary)',
            fontSize: '13px',
            outline: 'none',
            boxSizing: 'border-box',
            transition: 'border-color 100ms ease'
          }}
          onFocus={(e) => {
            ;(e.currentTarget as HTMLInputElement).style.borderColor = 'var(--border-active)'
          }}
          onBlur={(e) => {
            ;(e.currentTarget as HTMLInputElement).style.borderColor = 'var(--border-default)'
          }}
        />
        {localValue && (
          <button
            onClick={() => {
              setLocalValue('')
              setSearchQuery('')
              ref.current?.focus()
            }}
            style={{
              position: 'absolute',
              right: '6px',
              top: '50%',
              transform: 'translateY(-50%)',
              background: 'none',
              border: 'none',
              cursor: 'pointer',
              color: 'var(--text-dimmed)',
              fontSize: '14px',
              lineHeight: 1,
              padding: '2px'
            }}
            aria-label="Clear search"
          >
            ×
          </button>
        )}
      </div>
    </div>
  )
}
