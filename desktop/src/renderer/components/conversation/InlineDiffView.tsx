import type { FileDiff } from '../../types/toolCall'

interface InlineDiffViewProps {
  diff: FileDiff
  streaming?: boolean
}

/**
 * Lightweight inline diff renderer for file changes.
 * Shows line-by-line additions (green) and deletions (red) with context lines.
 * Spec §M4-5: expand file diff variant.
 */
export function InlineDiffView({ diff, streaming = false }: InlineDiffViewProps): JSX.Element {
  const totalAdd = diff.additions
  const totalDel = diff.deletions

  return (
    <div
      className="selectable"
      style={{
        fontFamily: 'var(--font-mono)',
        fontSize: '12px',
        lineHeight: '1.5',
        borderRadius: '4px',
        overflow: 'hidden',
        border: '1px solid var(--border-default)'
      }}
    >
      {/* Header */}
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: '8px',
          padding: '4px 8px',
          background: 'var(--bg-tertiary)',
          borderBottom: '1px solid var(--border-default)',
          color: 'var(--text-secondary)',
          fontSize: '11px'
        }}
      >
        <span style={{ color: 'var(--text-primary)', fontWeight: 500 }}>
          {diff.filePath}
        </span>
        {diff.isNewFile && (
          <span style={{ color: 'var(--text-dimmed)' }}>(new file)</span>
        )}
        <span style={{ marginLeft: 'auto', display: 'flex', gap: '6px' }}>
          {streaming && (
            <span style={{ display: 'inline-flex', alignItems: 'center', gap: '4px', color: 'var(--text-dimmed)' }}>
              <span
                className="animate-spin-custom"
                style={{
                  display: 'inline-block',
                  width: '8px',
                  height: '8px',
                  borderRadius: '50%',
                  border: '1px solid var(--border-active)',
                  borderTopColor: 'var(--accent)'
                }}
              />
              <span>streaming</span>
            </span>
          )}
          {totalAdd > 0 && (
            <span style={{ color: 'var(--success)' }}>+{totalAdd}</span>
          )}
          {totalDel > 0 && (
            <span style={{ color: 'var(--error)' }}>-{totalDel}</span>
          )}
        </span>
      </div>

      {/* Hunks */}
      <div style={{ maxHeight: '300px', overflowY: 'auto' }}>
        {diff.diffHunks.map((hunk, hunkIdx) => (
          <div key={hunkIdx}>
            {/* Hunk separator */}
            <div
              style={{
                padding: '2px 8px',
                background: 'var(--bg-secondary)',
                color: 'var(--text-dimmed)',
                fontSize: '11px',
                userSelect: 'none'
              }}
            >
              @@ -{hunk.oldStart},{hunk.oldLines} +{hunk.newStart},{hunk.newLines} @@
            </div>
            {/* Lines */}
            {hunk.lines.map((line, lineIdx) => (
              <div
                key={lineIdx}
                style={{
                  display: 'flex',
                  background:
                    line.type === 'add'
                      ? 'var(--diff-add-bg)'
                      : line.type === 'remove'
                        ? 'var(--diff-remove-bg)'
                        : 'transparent',
                  whiteSpace: 'pre'
                }}
              >
                {/* Gutter symbol */}
                <span
                  style={{
                    width: '16px',
                    flexShrink: 0,
                    textAlign: 'center',
                    color:
                      line.type === 'add'
                        ? 'var(--success)'
                        : line.type === 'remove'
                          ? 'var(--error)'
                          : 'var(--text-dimmed)',
                    userSelect: 'none'
                  }}
                >
                  {line.type === 'add' ? '+' : line.type === 'remove' ? '-' : ' '}
                </span>
                <span
                  style={{
                    padding: '0 8px',
                    color:
                      line.type === 'add'
                        ? 'var(--text-primary)'
                        : line.type === 'remove'
                          ? 'var(--text-secondary)'
                          : 'var(--text-secondary)',
                    overflow: 'hidden',
                    textOverflow: 'ellipsis'
                  }}
                >
                  {line.content}
                  {streaming && line.type === 'add' && hunkIdx === diff.diffHunks.length - 1 && lineIdx === hunk.lines.length - 1 && (
                    <span style={{ color: 'var(--accent)', marginLeft: '2px' }}>▌</span>
                  )}
                </span>
              </div>
            ))}
          </div>
        ))}
        {diff.diffHunks.length === 0 && (
          <div style={{ padding: '8px', color: 'var(--text-dimmed)' }}>
            {streaming ? 'Waiting for content...' : 'No changes'}
          </div>
        )}
      </div>
    </div>
  )
}
