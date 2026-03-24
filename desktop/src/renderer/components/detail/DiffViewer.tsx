import type { FileDiff } from '../../types/toolCall'

interface DiffViewerProps {
  diff: FileDiff
  workspacePath: string
  onRevert?: () => void
}

/**
 * Full-height diff viewer for the detail panel Changes tab.
 * Renders unified diff with line numbers, gutter markers, and a per-file revert button.
 * Spec §11.3.3
 */
export function DiffViewer({ diff, workspacePath, onRevert }: DiffViewerProps): JSX.Element {
  const relativePath = toRelativePath(diff.filePath, workspacePath)
  const isReverted = diff.status === 'reverted'

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        height: '100%',
        fontFamily: 'var(--font-mono)',
        fontSize: '12px',
        lineHeight: '1.5',
        overflow: 'hidden'
      }}
    >
      {/* Header */}
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: '8px',
          padding: '6px 12px',
          background: 'var(--bg-tertiary)',
          borderBottom: '1px solid var(--border-default)',
          flexShrink: 0
        }}
      >
        <span style={{ flex: 1, color: 'var(--text-primary)', fontWeight: 500, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
          {relativePath}
        </span>
        {diff.isNewFile ? (
          <span style={{ fontSize: '11px', color: 'var(--info)', border: '1px solid var(--info)', borderRadius: '3px', padding: '1px 5px' }}>
            New file
          </span>
        ) : (
          <span style={{ display: 'flex', gap: '6px', fontSize: '11px', color: 'var(--text-dimmed)' }}>
            {diff.additions > 0 && <span style={{ color: 'var(--success)' }}>+{diff.additions}</span>}
            {diff.deletions > 0 && <span style={{ color: 'var(--error)' }}>-{diff.deletions}</span>}
          </span>
        )}
        {onRevert && (
          <button
            onClick={onRevert}
            title={isReverted ? 'Re-apply this file' : 'Revert this file'}
            style={{
              padding: '2px 8px',
              fontSize: '11px',
              borderRadius: '4px',
              border: '1px solid var(--border-default)',
              background: 'transparent',
              color: isReverted ? 'var(--info)' : 'var(--text-secondary)',
              cursor: 'pointer'
            }}
          >
            {isReverted ? 'Re-apply' : '↺ Revert'}
          </button>
        )}
      </div>

      {/* Diff content */}
      <div style={{ flex: 1, overflowY: 'auto' }}>
        {diff.diffHunks.length === 0 ? (
          <div style={{ padding: '16px', color: 'var(--text-dimmed)', fontSize: '12px' }}>No changes</div>
        ) : (
          diff.diffHunks.map((hunk, hunkIdx) => {
            let oldLineNum = hunk.oldStart
            let newLineNum = hunk.newStart

            return (
              <div key={hunkIdx}>
                {/* Hunk header */}
                <div
                  style={{
                    padding: '2px 8px',
                    background: 'var(--bg-secondary)',
                    color: 'var(--text-dimmed)',
                    fontSize: '11px',
                    userSelect: 'none',
                    borderTop: hunkIdx > 0 ? '1px solid var(--border-default)' : undefined,
                    overflowWrap: 'break-word'
                  }}
                >
                  @@ -{hunk.oldStart},{hunk.oldLines} +{hunk.newStart},{hunk.newLines} @@
                </div>
                {/* Lines */}
                {hunk.lines.map((line, lineIdx) => {
                  const isAdd = line.type === 'add'
                  const isRemove = line.type === 'remove'
                  const isContext = line.type === 'context'

                  const oldNum = (isAdd) ? '' : String(oldLineNum)
                  const newNum = (isRemove) ? '' : String(newLineNum)

                  if (!isAdd) oldLineNum++
                  if (!isRemove) newLineNum++

                  return (
                    <div
                      key={lineIdx}
                      style={{
                        display: 'flex',
                        background: isAdd
                          ? 'var(--diff-add-bg)'
                          : isRemove
                            ? 'var(--diff-remove-bg)'
                            : 'transparent'
                      }}
                    >
                      {/* Old line number */}
                      <span
                        style={{
                          width: '40px',
                          flexShrink: 0,
                          textAlign: 'right',
                          paddingRight: '6px',
                          color: 'var(--text-dimmed)',
                          userSelect: 'none',
                          fontSize: '11px'
                        }}
                      >
                        {oldNum}
                      </span>
                      {/* New line number */}
                      <span
                        style={{
                          width: '40px',
                          flexShrink: 0,
                          textAlign: 'right',
                          paddingRight: '6px',
                          color: 'var(--text-dimmed)',
                          userSelect: 'none',
                          fontSize: '11px'
                        }}
                      >
                        {newNum}
                      </span>
                      {/* Gutter marker */}
                      <span
                        style={{
                          width: '16px',
                          flexShrink: 0,
                          textAlign: 'center',
                          color: isAdd
                            ? 'var(--success)'
                            : isRemove
                              ? 'var(--error)'
                              : 'var(--text-dimmed)',
                          userSelect: 'none'
                        }}
                      >
                        {isAdd ? '+' : isRemove ? '-' : ' '}
                      </span>
                      {/* Line content */}
                      <span
                        style={{
                          paddingLeft: '4px',
                          color: isAdd
                            ? 'var(--text-primary)'
                            : isRemove
                              ? 'var(--text-secondary)'
                              : isContext
                                ? 'var(--text-secondary)'
                                : 'var(--text-secondary)',
                          flex: 1,
                          minWidth: 0,
                          whiteSpace: 'pre-wrap',
                          overflowWrap: 'break-word'
                        }}
                      >
                        {line.content}
                      </span>
                    </div>
                  )
                })}
              </div>
            )
          })
        )}
      </div>
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
