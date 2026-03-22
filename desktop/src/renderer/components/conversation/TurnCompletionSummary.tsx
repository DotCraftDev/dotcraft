import { memo } from 'react'
import { useConversationStore } from '../../stores/conversationStore'
import { useUIStore } from '../../stores/uiStore'

interface TurnCompletionSummaryProps {
  turnId: string
}

/**
 * Shows a post-turn summary of all files changed during the turn.
 *
 * Variants:
 *   - Compact (1–2 files): single line summary
 *   - Expanded (3+ files): header + per-file rows with Revert button
 *
 * Spec §M4-12 through M4-16.
 */
export const TurnCompletionSummary = memo(function TurnCompletionSummary({ turnId }: TurnCompletionSummaryProps): JSX.Element | null {
  const changedFiles = useConversationStore((s) => s.changedFiles)
  const revertFilesForTurn = useConversationStore((s) => s.revertFilesForTurn)

  const turnFiles = Array.from(changedFiles.values()).filter((f) => {
    const ids = f.turnIds?.length ? f.turnIds : [f.turnId]
    return ids.includes(turnId)
  })
  if (turnFiles.length === 0) return null

  const totalAdd = turnFiles.reduce((sum, f) => sum + f.additions, 0)
  const totalDel = turnFiles.reduce((sum, f) => sum + f.deletions, 0)
  const hasReverted = turnFiles.every((f) => f.status === 'reverted')

  // ── Compact variant (1–2 files) ──────────────────────────────────────────
  if (turnFiles.length <= 2) {
    return (
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          flexWrap: 'wrap',
          gap: '6px',
          padding: '4px 6px',
          borderRadius: '4px',
          background: 'var(--bg-secondary)',
          fontSize: '12px',
          color: 'var(--text-secondary)',
          marginTop: '4px'
        }}
      >
        <span style={{ color: 'var(--info)', fontSize: '11px' }}>◈</span>
        <span>
          {turnFiles.map((f, i) => (
            <span key={f.filePath}>
              {i > 0 && <span style={{ color: 'var(--text-dimmed)' }}>, </span>}
              <FilePathLink filePath={f.filePath} />
              <span style={{ color: 'var(--text-dimmed)', marginLeft: '4px' }}>
                <FileStats additions={f.additions} deletions={f.deletions} status={f.status} />
              </span>
            </span>
          ))}
        </span>
      </div>
    )
  }

  // ── Expanded variant (3+ files) ───────────────────────────────────────────
  return (
    <div
      style={{
        borderRadius: '4px',
        border: '1px solid var(--border-default)',
        background: 'var(--bg-secondary)',
        overflow: 'hidden',
        marginTop: '4px',
        fontSize: '12px'
      }}
    >
      {/* Header */}
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: '8px',
          padding: '5px 8px',
          background: 'var(--bg-tertiary)',
          borderBottom: '1px solid var(--border-default)',
          color: 'var(--text-secondary)'
        }}
      >
        <span style={{ color: 'var(--info)', fontSize: '11px' }}>◈</span>
        <span style={{ flex: 1 }}>
          {turnFiles.length} files changed
        </span>
        <span style={{ display: 'flex', gap: '4px', color: 'var(--text-dimmed)' }}>
          {totalAdd > 0 && <span style={{ color: 'var(--success)' }}>+{totalAdd}</span>}
          {totalDel > 0 && <span style={{ color: 'var(--error)' }}>-{totalDel}</span>}
        </span>
        {/* Revert button — state-only in M4; actual file revert in M6 */}
        {!hasReverted && (
          <button
            onClick={() => revertFilesForTurn(turnId)}
            style={{
              padding: '2px 8px',
              borderRadius: '3px',
              border: '1px solid var(--border-default)',
              background: 'transparent',
              color: 'var(--text-secondary)',
              cursor: 'pointer',
              fontSize: '11px'
            }}
          >
            Revert
          </button>
        )}
        {hasReverted && (
          <span style={{ color: 'var(--text-dimmed)', fontSize: '11px' }}>Reverted</span>
        )}
      </div>

      {/* Per-file rows */}
      {turnFiles.map((file, idx) => (
        <div
          key={file.filePath}
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: '8px',
            padding: '3px 8px',
            borderBottom: idx < turnFiles.length - 1 ? '1px solid var(--border-default)' : 'none',
            color: 'var(--text-secondary)'
          }}
        >
          {/* Status dot */}
          <span
            title={file.status}
            style={{
              width: '7px',
              height: '7px',
              borderRadius: '50%',
              background: file.status === 'reverted' ? 'var(--text-dimmed)' : 'var(--info)',
              flexShrink: 0
            }}
          />
          {/* File path */}
          <span style={{ flex: 1, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
            <FilePathLink filePath={file.filePath} />
            {file.isNewFile && (
              <span style={{ color: 'var(--text-dimmed)', marginLeft: '4px', fontSize: '11px' }}>new</span>
            )}
          </span>
          {/* +/- counts */}
          <FileStats additions={file.additions} deletions={file.deletions} status={file.status} />
        </div>
      ))}
    </div>
  )
})

// ── Helper components ────────────────────────────────────────────────────────

interface FilePathLinkProps {
  filePath: string
}

function FilePathLink({ filePath }: FilePathLinkProps): JSX.Element {
  function handleClick(): void {
    useUIStore.getState().showChangesForFile(filePath)
  }

  const filename = filePath.split(/[\\/]/).pop() ?? filePath

  return (
    <button
      onClick={handleClick}
      title={filePath}
      style={{
        background: 'none',
        border: 'none',
        padding: 0,
        color: 'var(--text-primary)',
        cursor: 'pointer',
        fontFamily: 'var(--font-mono)',
        fontSize: '11px',
        textDecoration: 'underline',
        textDecorationColor: 'var(--text-dimmed)'
      }}
    >
      {filename}
    </button>
  )
}

interface FileStatsProps {
  additions: number
  deletions: number
  status: 'written' | 'reverted'
}

function FileStats({ additions, deletions, status }: FileStatsProps): JSX.Element {
  const dim = status === 'reverted'
  return (
    <span style={{ display: 'flex', gap: '4px', flexShrink: 0, fontFamily: 'var(--font-mono)', fontSize: '11px' }}>
      {additions > 0 && (
        <span style={{ color: dim ? 'var(--text-dimmed)' : 'var(--success)' }}>+{additions}</span>
      )}
      {deletions > 0 && (
        <span style={{ color: dim ? 'var(--text-dimmed)' : 'var(--error)' }}>-{deletions}</span>
      )}
    </span>
  )
}
