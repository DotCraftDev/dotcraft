import { useState, useEffect, useRef } from 'react'
import { createPortal } from 'react-dom'
import { useConversationStore } from '../../stores/conversationStore'
import { addToast } from '../../stores/toastStore'

interface CommitDialogProps {
  workspacePath: string
  onClose: () => void
}

/**
 * Modal dialog for staging and committing file changes to git.
 * Lists only non-reverted (written) files; commit message is pre-populated.
 * Spec §M6-16, §M6-17, §16.5
 */
export function CommitDialog({ workspacePath, onClose }: CommitDialogProps): JSX.Element {
  const changedFiles = useConversationStore((s) => s.changedFiles)
  const turns = useConversationStore((s) => s.turns)

  const allFiles = Array.from(changedFiles.values())
  const writtenFiles = allFiles.filter((f) => f.status === 'written')
  const revertedCount = allFiles.length - writtenFiles.length

  const [message, setMessage] = useState(() => generateCommitMessage(turns))
  const [committing, setCommitting] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [success, setSuccess] = useState(false)
  const messageRef = useRef<HTMLTextAreaElement>(null)

  useEffect(() => {
    messageRef.current?.focus()

    function handleKeyDown(e: KeyboardEvent): void {
      if (e.key === 'Escape' && !committing) onClose()
    }
    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [committing, onClose])

  async function handleCommit(): Promise<void> {
    if (!message.trim() || writtenFiles.length === 0) return
    setCommitting(true)
    setError(null)
    try {
      const filePaths = writtenFiles.map((f) => f.filePath)
      await window.api.git.commit(workspacePath, filePaths, message.trim())
      setSuccess(true)
      addToast(`Changes committed: ${message.trim().split('\n')[0]}`, 'success')
      setTimeout(onClose, 1200)
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
      setCommitting(false)
    }
  }

  const dialog = (
    <div
      role="dialog"
      aria-modal="true"
      aria-label="Commit Changes"
      style={{
        position: 'fixed',
        inset: 0,
        zIndex: 10000,
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        backgroundColor: 'var(--overlay-scrim)'
      }}
      onMouseDown={(e) => {
        if (e.target === e.currentTarget && !committing) onClose()
      }}
    >
      <div
        style={{
          backgroundColor: 'var(--bg-secondary)',
          borderRadius: '10px',
          boxShadow: 'var(--shadow-level-3)',
          padding: '24px',
          width: '480px',
          maxWidth: 'calc(100vw - 48px)',
          maxHeight: 'calc(100vh - 96px)',
          overflow: 'auto'
        }}
        onMouseDown={(e) => e.stopPropagation()}
      >
        {/* Title */}
        <h2
          style={{
            margin: '0 0 16px',
            fontSize: '15px',
            fontWeight: 600,
            color: 'var(--text-primary)'
          }}
        >
          Commit Changes
        </h2>

        {/* File list header */}
        <div
          style={{
            fontSize: '12px',
            color: 'var(--text-secondary)',
            marginBottom: '6px'
          }}
        >
          Files to commit ({writtenFiles.length} of {allFiles.length}
          {revertedCount > 0 ? ` — ${revertedCount} reverted` : ''}):
        </div>

        {/* Written files */}
        <div
          style={{
            border: '1px solid var(--border-default)',
            borderRadius: '6px',
            overflow: 'hidden',
            marginBottom: '16px',
            maxHeight: '200px',
            overflowY: 'auto'
          }}
        >
          {writtenFiles.map((file, idx) => (
            <div
              key={file.filePath}
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: '8px',
                padding: '5px 10px',
                borderBottom: idx < writtenFiles.length - 1 ? '1px solid var(--border-default)' : 'none',
                fontSize: '12px'
              }}
            >
              <span
                style={{
                  width: '7px',
                  height: '7px',
                  borderRadius: '50%',
                  background: 'var(--info)',
                  flexShrink: 0
                }}
              />
              <span
                style={{
                  flex: 1,
                  overflow: 'hidden',
                  textOverflow: 'ellipsis',
                  whiteSpace: 'nowrap',
                  fontFamily: 'var(--font-mono)',
                  color: 'var(--text-primary)'
                }}
              >
                {toRelativePath(file.filePath, workspacePath)}
              </span>
              <span style={{ display: 'flex', gap: '4px', fontFamily: 'var(--font-mono)', fontSize: '11px', flexShrink: 0 }}>
                {file.additions > 0 && <span style={{ color: 'var(--success)' }}>+{file.additions}</span>}
                {file.deletions > 0 && <span style={{ color: 'var(--error)' }}>-{file.deletions}</span>}
              </span>
            </div>
          ))}
        </div>

        {/* Commit message label */}
        <div style={{ fontSize: '12px', color: 'var(--text-secondary)', marginBottom: '6px' }}>
          Commit message:
        </div>

        {/* Commit message textarea */}
        <textarea
          ref={messageRef}
          value={message}
          onChange={(e) => setMessage(e.target.value)}
          disabled={committing || success}
          rows={3}
          style={{
            width: '100%',
            boxSizing: 'border-box',
            padding: '8px 10px',
            fontSize: '13px',
            borderRadius: '6px',
            border: '1px solid var(--border-default)',
            background: 'var(--bg-primary)',
            color: 'var(--text-primary)',
            resize: 'vertical',
            outline: 'none',
            marginBottom: '8px',
            fontFamily: 'inherit',
            lineHeight: 1.5
          }}
          placeholder="Describe what was changed..."
        />

        {/* Error */}
        {error && (
          <div
            style={{
              padding: '8px 10px',
              borderRadius: '6px',
              background: 'var(--error-bg, rgba(255,80,80,0.1))',
              border: '1px solid var(--error)',
              color: 'var(--error)',
              fontSize: '12px',
              marginBottom: '12px',
              whiteSpace: 'pre-wrap',
              wordBreak: 'break-word'
            }}
          >
            {error}
          </div>
        )}

        {/* Buttons */}
        <div style={{ display: 'flex', gap: '8px', justifyContent: 'flex-end', marginTop: '8px' }}>
          <button
            onClick={onClose}
            disabled={committing}
            style={{
              padding: '7px 16px',
              border: '1px solid var(--border-default)',
              borderRadius: '6px',
              backgroundColor: 'transparent',
              color: 'var(--text-primary)',
              fontSize: '13px',
              cursor: committing ? 'default' : 'pointer',
              opacity: committing ? 0.5 : 1
            }}
          >
            Cancel
          </button>
          <button
            onClick={() => { void handleCommit() }}
            disabled={committing || success || !message.trim() || writtenFiles.length === 0}
            style={{
              padding: '7px 16px',
              border: 'none',
              borderRadius: '6px',
              backgroundColor: 'var(--accent)',
              color: 'var(--on-accent)',
              fontSize: '13px',
              fontWeight: 500,
              cursor: (committing || success || !message.trim()) ? 'default' : 'pointer',
              opacity: (committing || success || !message.trim()) ? 0.6 : 1
            }}
          >
            {success ? 'Committed ✓' : committing ? 'Committing...' : 'Commit →'}
          </button>
        </div>
      </div>
    </div>
  )

  return createPortal(dialog, document.body) as JSX.Element
}

function toRelativePath(filePath: string, workspacePath: string): string {
  if (!workspacePath) return filePath
  const ws = workspacePath.replace(/\\/g, '/').replace(/\/$/, '')
  const fp = filePath.replace(/\\/g, '/')
  if (fp.startsWith(ws + '/')) return fp.slice(ws.length + 1)
  return filePath
}

function generateCommitMessage(turns: ReturnType<typeof useConversationStore.getState>['turns']): string {
  // Try to get the last agent message as a commit message suggestion
  for (let i = turns.length - 1; i >= 0; i--) {
    const turn = turns[i]
    for (let j = turn.items.length - 1; j >= 0; j--) {
      const item = turn.items[j]
      if (item.type === 'agentMessage' && item.text) {
        // Take first 72 chars of first non-empty line
        const firstLine = item.text.split('\n').find((l) => l.trim().length > 0) ?? ''
        const cleaned = firstLine.replace(/^[#*\->\s]+/, '').trim()
        if (cleaned) return cleaned.slice(0, 72)
      }
    }
  }
  return ''
}

