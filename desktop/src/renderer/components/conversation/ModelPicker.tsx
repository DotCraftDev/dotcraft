import { useEffect, useId, useMemo, useRef, useState, type CSSProperties, type JSX } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { ActionTooltip } from '../ui/ActionTooltip'
import type { ShortcutSpec } from '../ui/shortcutKeys'

interface ModelPickerProps {
  modelName: string
  modelOptions: string[]
  disabled?: boolean
  loading?: boolean
  unsupported?: boolean
  onChange?: (model: string) => void
  shortcut?: ShortcutSpec
  triggerStyle: CSSProperties
}

export function ModelPicker({
  modelName,
  modelOptions,
  disabled = false,
  loading = false,
  unsupported = false,
  onChange,
  shortcut,
  triggerStyle
}: ModelPickerProps): JSX.Element {
  const t = useT()
  const [open, setOpen] = useState(false)
  const [highlight, setHighlight] = useState(0)
  const wrapRef = useRef<HTMLDivElement>(null)
  const listId = useId()

  const options = useMemo(() => {
    const withDefault = ['Default', ...modelOptions.filter((option) => option !== 'Default')]
    if (!modelName || modelName === 'Default') return withDefault
    if (withDefault.includes(modelName)) return withDefault
    return [modelName, ...withDefault]
  }, [modelName, modelOptions])

  const interactive = !disabled && !loading && !unsupported && options.length > 0
  const selectedIndex = Math.max(0, options.findIndex((option) => option === modelName))

  useEffect(() => {
    setHighlight(selectedIndex)
  }, [selectedIndex])

  useEffect(() => {
    if (!shortcut) return
    const handleShortcut = (event: KeyboardEvent): void => {
      const mod = event.ctrlKey || event.metaKey
      if (
        !mod ||
        !event.shiftKey ||
        event.altKey ||
        event.isComposing ||
        event.key.toLowerCase() !== 'm'
      ) {
        return
      }
      if (!interactive) return
      event.preventDefault()
      event.stopPropagation()
      setHighlight(selectedIndex)
      setOpen(true)
    }

    window.addEventListener('keydown', handleShortcut, true)
    return () => {
      window.removeEventListener('keydown', handleShortcut, true)
    }
  }, [interactive, selectedIndex, shortcut])

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
        return
      }
      if (!interactive || options.length === 0) return
      if (event.key === 'ArrowDown') {
        event.preventDefault()
        setHighlight((current) => Math.min(options.length - 1, current + 1))
        return
      }
      if (event.key === 'ArrowUp') {
        event.preventDefault()
        setHighlight((current) => Math.max(0, current - 1))
        return
      }
      if (event.key === 'Enter') {
        event.preventDefault()
        const next = options[highlight]
        if (next) {
          onChange?.(next)
          setOpen(false)
        }
      }
    }
    window.addEventListener('mousedown', handlePointerDown, true)
    window.addEventListener('keydown', handleKeyDown, true)
    return () => {
      window.removeEventListener('mousedown', handlePointerDown, true)
      window.removeEventListener('keydown', handleKeyDown, true)
    }
  }, [highlight, interactive, onChange, open, options])

  const displayLabel = loading
    ? t('composer.modelListLoading')
    : modelName === 'Default'
      ? t('composer.defaultModel')
      : modelName
  const tooltipLabel = t('composer.selectModelTitle')
  const disabledReason = loading
    ? t('composer.modelListLoading')
    : unsupported
      ? t('composer.modelListUnsupportedTitle')
      : undefined

  return (
    <div ref={wrapRef} style={{ position: 'relative', minWidth: 0 }}>
      <ActionTooltip
        label={tooltipLabel}
        shortcut={shortcut}
        disabledReason={disabledReason}
        placement="top"
        wrapperStyle={{ minWidth: 0 }}
      >
        <button
          type="button"
          aria-label={tooltipLabel}
          aria-haspopup={interactive ? 'listbox' : undefined}
          aria-expanded={interactive ? open : undefined}
          aria-controls={interactive && open ? listId : undefined}
          disabled={!interactive}
          onClick={() => {
            if (!interactive) return
            setOpen((current) => !current)
          }}
          style={{
            ...triggerStyle,
            cursor: interactive ? 'pointer' : 'default'
          }}
        >
        <span
          style={{
            minWidth: 0,
            overflow: 'hidden',
            textOverflow: 'ellipsis',
            whiteSpace: 'nowrap'
          }}
        >
          {displayLabel}
        </span>
        {interactive && (
          <span
            aria-hidden
            style={{
              display: 'inline-flex',
              alignItems: 'center',
              justifyContent: 'center',
              width: '14px',
              height: '14px',
              flexShrink: 0,
              transform: open ? 'rotate(180deg)' : 'none',
              transition: 'transform 120ms ease'
            }}
          >
            <svg width="10" height="10" viewBox="0 0 12 12" fill="none">
              <path
                d="M3 4.5L6 7.5L9 4.5"
                stroke="currentColor"
                strokeWidth="1.7"
                strokeLinecap="round"
                strokeLinejoin="round"
              />
            </svg>
          </span>
        )}
        </button>
      </ActionTooltip>

      {interactive && open && (
        <div
          id={listId}
          role="listbox"
          aria-label={t('composer.selectModelTitle')}
          style={{
            position: 'absolute',
            right: 0,
            bottom: 'calc(100% + 8px)',
            minWidth: '220px',
            maxWidth: '280px',
            maxHeight: '240px',
            overflowY: 'auto',
            zIndex: 70,
            border: '1px solid var(--border-default)',
            borderRadius: '12px',
            background: 'var(--bg-secondary)',
            boxShadow: '0 12px 30px rgba(0, 0, 0, 0.26)',
            padding: '6px'
          }}
        >
          {options.map((option, index) => {
            const selected = option === modelName
            const highlighted = index === highlight
            return (
              <button
                key={option}
                type="button"
                role="option"
                aria-selected={selected}
                onMouseEnter={() => {
                  setHighlight(index)
                }}
                onClick={() => {
                  onChange?.(option)
                  setOpen(false)
                }}
                style={{
                  width: '100%',
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'space-between',
                  gap: '10px',
                  border: 'none',
                  borderRadius: '10px',
                  padding: '8px 10px',
                  background: highlighted ? 'var(--bg-active)' : 'transparent',
                  color: selected ? 'var(--text-primary)' : 'var(--text-secondary)',
                  cursor: 'pointer',
                  textAlign: 'left',
                  fontSize: 'var(--type-secondary-size)',
                  lineHeight: 'var(--type-secondary-line-height)'
                }}
              >
                <span style={{ minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                  {option === 'Default' ? t('composer.defaultModel') : option}
                </span>
                {selected && (
                  <span
                    aria-hidden
                    style={{
                      width: '7px',
                      height: '7px',
                      borderRadius: '999px',
                      background: 'var(--accent)',
                      flexShrink: 0
                    }}
                  />
                )}
              </button>
            )
          })}
        </div>
      )}
    </div>
  )
}
