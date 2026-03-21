import { useState } from 'react'
import { useConnectionStore } from '../../stores/connectionStore'
import { useThreadStore } from '../../stores/threadStore'
import type { SessionIdentity, ThreadSummary } from '../../types/thread'

interface NewThreadButtonProps {
  workspacePath: string
}

/**
 * Primary action button that creates a new thread via thread/start.
 * Disabled when not connected.
 * Keyboard shortcut Ctrl+N is registered globally in App.tsx.
 * Spec §9.3
 */
export function NewThreadButton({ workspacePath }: NewThreadButtonProps): JSX.Element {
  const { status } = useConnectionStore()
  const { addThread, setActiveThreadId } = useThreadStore()
  const [creating, setCreating] = useState(false)

  const isConnected = status === 'connected'

  async function handleClick(): Promise<void> {
    if (!isConnected || creating) return
    setCreating(true)
    try {
      const identity: SessionIdentity = {
        channelName: 'dotcraft-desktop',
        userId: 'local',
        channelContext: `workspace:${workspacePath}`,
        workspacePath
      }
      const result = await window.api.appServer.sendRequest('thread/start', {
        identity,
        historyMode: 'server'
      }) as { thread: ThreadSummary }
      addThread(result.thread)
      setActiveThreadId(result.thread.id)
    } catch (err) {
      console.error('Failed to create thread:', err)
    } finally {
      setCreating(false)
    }
  }

  return (
    <div style={{ padding: '8px 12px', borderBottom: '1px solid var(--border-default)', flexShrink: 0 }}>
      <button
        onClick={handleClick}
        disabled={!isConnected || creating}
        title="New Thread (Ctrl+N)"
        style={{
          width: '100%',
          padding: '6px 12px',
          backgroundColor: isConnected ? 'var(--accent)' : 'var(--bg-tertiary)',
          color: isConnected ? '#ffffff' : 'var(--text-dimmed)',
          border: 'none',
          borderRadius: '6px',
          fontSize: '13px',
          fontWeight: 500,
          cursor: isConnected ? 'pointer' : 'default',
          display: 'flex',
          alignItems: 'center',
          gap: '6px',
          transition: 'background-color 150ms ease',
          opacity: creating ? 0.7 : 1
        }}
        onMouseEnter={(e) => {
          if (isConnected && !creating) {
            ;(e.currentTarget as HTMLButtonElement).style.backgroundColor = 'var(--accent-hover)'
          }
        }}
        onMouseLeave={(e) => {
          if (isConnected) {
            ;(e.currentTarget as HTMLButtonElement).style.backgroundColor = 'var(--accent)'
          }
        }}
      >
        <span aria-hidden="true">{creating ? '…' : '+'}</span>
        {creating ? 'Creating...' : 'New Thread'}
      </button>
    </div>
  )
}
