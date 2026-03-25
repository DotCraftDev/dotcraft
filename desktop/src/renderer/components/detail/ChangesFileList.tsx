import { useRef } from 'react'
import { useT } from '../../contexts/LocaleContext'
import type { FileDiff } from '../../types/toolCall'

interface ChangesFileListProps {
  files: FileDiff[]
  selectedFile: string | null
  workspacePath: string
  onSelect: (filePath: string) => void
  onRevert: (diff: FileDiff) => void
  onReapply: (diff: FileDiff) => void
}

/**
 * Scrollable file list for the Changes tab.
 * Each row shows: relative path, +/- counts, status dot, and hover action (Revert/Re-apply).
 * Supports keyboard navigation with ArrowUp/ArrowDown.
 * Spec §11.3.2, §11.3.4
 */
export function ChangesFileList({
  files,
  selectedFile,
  workspacePath,
  onSelect,
  onRevert,
  onReapply
}: ChangesFileListProps): JSX.Element {
  const listRef = useRef<HTMLDivElement>(null)

  function handleKeyDown(e: React.KeyboardEvent): void {
    if (e.key !== 'ArrowUp' && e.key !== 'ArrowDown') return
    e.preventDefault()
    const idx = files.findIndex((f) => f.filePath === selectedFile)
    if (idx === -1) {
      if (files.length > 0) onSelect(files[0].filePath)
      return
    }
    if (e.key === 'ArrowUp' && idx > 0) onSelect(files[idx - 1].filePath)
    if (e.key === 'ArrowDown' && idx < files.length - 1) onSelect(files[idx + 1].filePath)
  }

  return (
    <div
      ref={listRef}
      tabIndex={0}
      onKeyDown={handleKeyDown}
      style={{
        outline: 'none',
        overflowY: 'auto',
        borderBottom: '1px solid var(--border-default)'
      }}
    >
      {files.map((file) => (
        <FileRow
          key={file.filePath}
          file={file}
          workspacePath={workspacePath}
          isSelected={file.filePath === selectedFile}
          onSelect={() => onSelect(file.filePath)}
          onRevert={() => onRevert(file)}
          onReapply={() => onReapply(file)}
        />
      ))}
    </div>
  )
}

interface FileRowProps {
  file: FileDiff
  workspacePath: string
  isSelected: boolean
  onSelect: () => void
  onRevert: () => void
  onReapply: () => void
}

function FileRow({ file, workspacePath, isSelected, onSelect, onRevert, onReapply }: FileRowProps): JSX.Element {
  const t = useT()
  const isReverted = file.status === 'reverted'
  const relativePath = toRelativePath(file.filePath, workspacePath)
  const filename = relativePath.split(/[\\/]/).pop() ?? relativePath
  const dir = relativePath.includes('/') || relativePath.includes('\\')
    ? relativePath.slice(0, relativePath.length - filename.length)
    : ''

  function handleContextMenu(e: React.MouseEvent): void {
    e.preventDefault()
    if (isReverted) onReapply()
    else onRevert()
  }

  return (
    <div
      role="button"
      tabIndex={-1}
      onClick={onSelect}
      onContextMenu={handleContextMenu}
      title={relativePath}
      style={{
        display: 'flex',
        alignItems: 'center',
        gap: '6px',
        padding: '4px 10px',
        cursor: 'pointer',
        background: isSelected ? 'var(--accent-subtle)' : 'transparent',
        borderLeft: isSelected ? '2px solid var(--accent)' : '2px solid transparent',
        fontSize: '12px',
        userSelect: 'none'
      }}
    >
      {/* Status dot */}
      <span
        title={isReverted ? t('changesFile.reverted') : t('changesFile.written')}
        style={{
          width: '7px',
          height: '7px',
          borderRadius: '50%',
          flexShrink: 0,
          background: isReverted ? 'transparent' : 'var(--info)',
          border: isReverted ? '1.5px solid var(--text-dimmed)' : 'none'
        }}
      />

      {/* File path */}
      <span style={{ flex: 1, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
        {dir && (
          <span style={{ color: 'var(--text-dimmed)' }}>{dir}</span>
        )}
        <span
          style={{
            color: isReverted ? 'var(--text-dimmed)' : 'var(--text-primary)',
            fontFamily: 'var(--font-mono)'
          }}
        >
          {filename}
        </span>
        {file.isNewFile && (
          <span style={{ color: 'var(--info)', marginLeft: '4px', fontSize: '10px' }}>
            {t('changesFile.newBadge')}
          </span>
        )}
      </span>

      {/* Stats */}
      <span style={{ display: 'flex', gap: '4px', flexShrink: 0, fontFamily: 'var(--font-mono)', fontSize: '11px' }}>
        {file.additions > 0 && (
          <span style={{ color: isReverted ? 'var(--text-dimmed)' : 'var(--success)' }}>+{file.additions}</span>
        )}
        {file.deletions > 0 && (
          <span style={{ color: isReverted ? 'var(--text-dimmed)' : 'var(--error)' }}>-{file.deletions}</span>
        )}
      </span>

      {/* Hover action */}
      <button
        onClick={(e) => {
          e.stopPropagation()
          if (isReverted) onReapply()
          else onRevert()
        }}
        title={isReverted ? t('changesFile.reapplyTitle') : t('changesFile.revertTitle')}
        style={{
          padding: '1px 6px',
          fontSize: '10px',
          borderRadius: '3px',
          border: '1px solid var(--border-default)',
          background: 'transparent',
          color: 'var(--text-dimmed)',
          cursor: 'pointer',
          flexShrink: 0
        }}
      >
        {isReverted ? t('changesFile.reapply') : t('changesFile.revert')}
      </button>
    </div>
  )
}

function toRelativePath(filePath: string, workspacePath: string): string {
  if (!workspacePath) return filePath
  const ws = workspacePath.replace(/\\/g, '/').replace(/\/$/, '')
  const fp = filePath.replace(/\\/g, '/')
  if (fp.startsWith(ws + '/')) return fp.slice(ws.length + 1)
  return filePath
}
