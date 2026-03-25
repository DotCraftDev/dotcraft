import { useCallback, useEffect, useRef, useState } from 'react'

const DEBOUNCE_MS = 80

export interface FileMatch {
  name: string
  relativePath: string
  dir: string
}

interface FileSearchPopoverProps {
  query: string
  visible: boolean
  workspacePath: string
  onSelect: (relativePath: string) => void
  onDismiss: () => void
}

/**
 * Floating file search for @ mentions; debounced IPC to workspace.searchFiles.
 */
export function FileSearchPopover({
  query,
  visible,
  workspacePath,
  onSelect,
  onDismiss
}: FileSearchPopoverProps): JSX.Element | null {
  const [loading, setLoading] = useState(false)
  const [files, setFiles] = useState<FileMatch[]>([])
  const [highlight, setHighlight] = useState(0)
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  const lastReq = useRef(0)

  const runSearch = useCallback(
    async (q: string): Promise<void> => {
      const id = ++lastReq.current
      if (!q.trim()) {
        setFiles([])
        setLoading(false)
        return
      }
      setLoading(true)
      try {
        const res = (await window.api.workspace.searchFiles({
          query: q,
          workspacePath,
          limit: 10
        })) as { files: FileMatch[] }
        if (id !== lastReq.current) return
        setFiles(res.files ?? [])
      } catch {
        if (id !== lastReq.current) return
        setFiles([])
      } finally {
        if (id === lastReq.current) setLoading(false)
      }
    },
    [workspacePath]
  )

  useEffect(() => {
    if (!visible) {
      setFiles([])
      setHighlight(0)
      return
    }
    if (debounceRef.current) clearTimeout(debounceRef.current)
    debounceRef.current = setTimeout(() => {
      void runSearch(query)
    }, DEBOUNCE_MS)
    return () => {
      if (debounceRef.current) clearTimeout(debounceRef.current)
    }
  }, [query, visible, runSearch])

  useEffect(() => {
    setHighlight(0)
  }, [files])

  useEffect(() => {
    if (!visible) return
    const onKey = (e: KeyboardEvent): void => {
      if (e.key === 'Escape') {
        e.preventDefault()
        e.stopPropagation()
        onDismiss()
        return
      }
      if (files.length === 0) return
      if (e.key === 'ArrowDown') {
        e.preventDefault()
        e.stopPropagation()
        setHighlight((h) => Math.min(files.length - 1, h + 1))
      } else if (e.key === 'ArrowUp') {
        e.preventDefault()
        e.stopPropagation()
        setHighlight((h) => Math.max(0, h - 1))
      } else if (e.key === 'Enter' || e.key === 'Tab') {
        e.preventDefault()
        e.stopPropagation()
        const f = files[highlight]
        if (f) onSelect(f.relativePath)
      }
    }
    window.addEventListener('keydown', onKey, true)
    return () => { window.removeEventListener('keydown', onKey, true) }
  }, [visible, files, highlight, onSelect, onDismiss])

  if (!visible) return null

  return (
    <div
      role="listbox"
      style={{
        position: 'absolute',
        bottom: '100%',
        left: 0,
        marginBottom: '4px',
        minWidth: '280px',
        maxWidth: '420px',
        maxHeight: '240px',
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
          Loading…
        </div>
      )}
      {!loading && files.length === 0 && query.trim() !== '' && (
        <div style={{ padding: '8px 12px', fontSize: '12px', color: 'var(--text-dimmed)' }}>
          No matching files
        </div>
      )}
      {!loading && files.length === 0 && query.trim() === '' && (
        <div style={{ padding: '8px 12px', fontSize: '12px', color: 'var(--text-dimmed)' }}>
          Type to search files
        </div>
      )}
      {files.map((f, i) => (
        <button
          key={f.relativePath}
          type="button"
          role="option"
          aria-selected={i === highlight}
          onMouseEnter={() => { setHighlight(i) }}
          onClick={() => { onSelect(f.relativePath) }}
          style={{
            display: 'flex',
            width: '100%',
            alignItems: 'center',
            gap: '8px',
            padding: '6px 12px',
            border: 'none',
            background: i === highlight ? 'var(--bg-active)' : 'transparent',
            color: 'var(--text-primary)',
            cursor: 'pointer',
            textAlign: 'left',
            fontSize: '13px'
          }}
        >
          <span style={{ flexShrink: 0 }}>📄</span>
          <span style={{ fontWeight: 600, overflow: 'hidden', textOverflow: 'ellipsis' }}>
            {highlightMatch(f.name, query)}
          </span>
          <span
            style={{
              marginLeft: 'auto',
              fontSize: '11px',
              color: 'var(--text-dimmed)',
              overflow: 'hidden',
              textOverflow: 'ellipsis',
              whiteSpace: 'nowrap',
              maxWidth: '140px'
            }}
            title={f.dir}
          >
            {f.dir || '.'}
          </span>
        </button>
      ))}
    </div>
  )
}

function highlightMatch(name: string, q: string): JSX.Element {
  const lower = name.toLowerCase()
  const qi = q.toLowerCase()
  const idx = lower.indexOf(qi)
  if (!q || idx < 0) return <>{name}</>
  return (
    <>
      {name.slice(0, idx)}
      <span style={{ color: 'var(--accent)' }}>{name.slice(idx, idx + q.length)}</span>
      {name.slice(idx + q.length)}
    </>
  )
}
