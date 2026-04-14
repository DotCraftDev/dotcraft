import { useEffect, useMemo, useState } from 'react'
import { useT } from '../../contexts/LocaleContext'
import type { CustomCommandInfo } from '../../hooks/useCustomCommandCatalog'

interface CommandSearchPopoverProps {
  query: string
  visible: boolean
  loading: boolean
  commands: CustomCommandInfo[]
  onSelect: (commandName: string) => void
  onDismiss: () => void
}

export function CommandSearchPopover({
  query,
  visible,
  loading,
  commands,
  onSelect,
  onDismiss
}: CommandSearchPopoverProps): JSX.Element | null {
  const t = useT()
  const [highlight, setHighlight] = useState(0)
  const filtered = useMemo(() => {
    const prefix = query.toLowerCase()
    if (!prefix) return commands
    return commands.filter((cmd) => cmd.name.slice(1).toLowerCase().startsWith(prefix))
  }, [commands, query])

  useEffect(() => {
    setHighlight(0)
  }, [filtered, query])

  useEffect(() => {
    if (!visible) return
    const onKey = (e: KeyboardEvent): void => {
      if (e.key === 'Escape') {
        e.preventDefault()
        e.stopPropagation()
        onDismiss()
        return
      }
      if (filtered.length === 0) return
      if (e.key === 'ArrowDown') {
        e.preventDefault()
        e.stopPropagation()
        setHighlight((h) => Math.min(filtered.length - 1, h + 1))
      } else if (e.key === 'ArrowUp') {
        e.preventDefault()
        e.stopPropagation()
        setHighlight((h) => Math.max(0, h - 1))
      } else if (e.key === 'Enter' || e.key === 'Tab') {
        e.preventDefault()
        e.stopPropagation()
        const command = filtered[highlight]
        if (command) onSelect(command.name)
      }
    }
    window.addEventListener('keydown', onKey, true)
    return () => {
      window.removeEventListener('keydown', onKey, true)
    }
  }, [filtered, highlight, onDismiss, onSelect, visible])

  if (!visible) return null

  return (
    <div
      role="listbox"
      style={{
        position: 'absolute',
        bottom: '100%',
        left: 0,
        marginBottom: '4px',
        minWidth: '320px',
        maxWidth: '480px',
        maxHeight: '260px',
        overflowY: 'auto',
        zIndex: 50,
        boxShadow: '0 4px 12px rgba(0,0,0,0.4)',
        background: 'var(--bg-secondary)',
        border: '1px solid var(--border-default)',
        borderRadius: '8px',
        padding: '4px 0'
      }}
    >
      {loading && (
        <div style={{ padding: '8px 12px', fontSize: '12px', color: 'var(--text-dimmed)' }}>
          {t('commandSearch.loading')}
        </div>
      )}
      {!loading && filtered.length === 0 && query.trim() !== '' && (
        <div style={{ padding: '8px 12px', fontSize: '12px', color: 'var(--text-dimmed)' }}>
          {t('commandSearch.noMatch')}
        </div>
      )}
      {!loading && filtered.length === 0 && query.trim() === '' && (
        <div style={{ padding: '8px 12px', fontSize: '12px', color: 'var(--text-dimmed)' }}>
          {t('commandSearch.hint')}
        </div>
      )}
      {filtered.map((cmd, i) => (
        <button
          key={cmd.name}
          type="button"
          role="option"
          aria-selected={i === highlight}
          onMouseEnter={() => {
            setHighlight(i)
          }}
          onClick={() => {
            onSelect(cmd.name)
          }}
          style={{
            display: 'flex',
            width: '100%',
            alignItems: 'center',
            gap: '8px',
            padding: '7px 12px',
            border: 'none',
            background: i === highlight ? 'var(--bg-active)' : 'transparent',
            color: 'var(--text-primary)',
            cursor: 'pointer',
            textAlign: 'left'
          }}
        >
          <span
            style={{
              display: 'inline-flex',
              alignItems: 'center',
              borderRadius: '5px',
              padding: '1px 6px',
              fontSize: '12px',
              fontWeight: 600,
              background: 'color-mix(in srgb, var(--accent) 16%, transparent)',
              border: '1px solid color-mix(in srgb, var(--accent) 38%, transparent)',
              color: 'var(--accent)',
              whiteSpace: 'nowrap'
            }}
          >
            {highlightMatch(cmd.name, query)}
          </span>
          <span
            style={{
              fontSize: '12px',
              color: 'var(--text-secondary)',
              overflow: 'hidden',
              textOverflow: 'ellipsis',
              whiteSpace: 'nowrap'
            }}
            title={cmd.description}
          >
            {cmd.description || t('commandSearch.noDescription')}
          </span>
        </button>
      ))}
    </div>
  )
}

function highlightMatch(name: string, query: string): JSX.Element {
  const target = name.slice(1)
  const lower = target.toLowerCase()
  const lowerQuery = query.toLowerCase()
  const idx = lower.indexOf(lowerQuery)
  if (!query || idx < 0) return <>{name}</>
  return (
    <>
      /
      {target.slice(0, idx)}
      <span style={{ color: 'var(--text-primary)' }}>{target.slice(idx, idx + query.length)}</span>
      {target.slice(idx + query.length)}
    </>
  )
}
