import { useThreadStore } from '../../stores/threadStore'
import { useConversationStore } from '../../stores/conversationStore'
import { useConnectionStore } from '../../stores/connectionStore'
import { ThreadHeader } from '../conversation/ThreadHeader'
import { MessageStream } from '../conversation/MessageStream'
import { TurnStatusIndicator } from '../conversation/TurnStatusIndicator'
import { InputComposer } from '../conversation/InputComposer'

interface ConversationPanelProps {
  workspacePath?: string
}

/**
 * Main conversation panel — M3 full implementation.
 * Composes: ThreadHeader, MessageStream, TurnStatusIndicator, InputComposer.
 * Spec §10
 */
export function ConversationPanel({ workspacePath = '' }: ConversationPanelProps): JSX.Element {
  const { activeThread, activeThreadId, loading } = useThreadStore()
  const turns = useConversationStore((s) => s.turns)
  const turnStatus = useConversationStore((s) => s.turnStatus)
  const connectionStatus = useConnectionStore((s) => s.status)
  const connectionErrorMessage = useConnectionStore((s) => s.errorMessage)

  const showReconnectionBanner = connectionStatus === 'disconnected'

  // Loading state: thread selected but full data not yet fetched
  if (activeThreadId && !activeThread && loading) {
    return (
      <div style={centeredStyle}>
        <span style={{ color: 'var(--text-dimmed)', fontSize: '13px' }}>Loading thread...</span>
      </div>
    )
  }

  // No thread selected
  if (!activeThread) {
    return (
      <div style={centeredStyle}>
        <div style={{ textAlign: 'center', maxWidth: '360px', padding: '0 24px' }}>
          <h2
            style={{
              fontSize: '18px',
              fontWeight: 600,
              color: 'var(--text-primary)',
              marginBottom: '8px',
              margin: '0 0 8px 0'
            }}
          >
            What can I help you with?
          </h2>
          <p
            style={{
              fontSize: '14px',
              color: 'var(--text-secondary)',
              lineHeight: 1.6,
              margin: 0
            }}
          >
            Select a conversation from the sidebar or click &ldquo;+ New Thread&rdquo; to start.
          </p>
        </div>
      </div>
    )
  }

  const threadName = activeThread.displayName ?? 'New conversation'
  const hasContent = turns.length > 0 || turnStatus === 'running'

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        height: '100%',
        backgroundColor: 'var(--bg-primary)',
        overflow: 'hidden'
      }}
    >
      {/* Fixed header */}
      <ThreadHeader threadName={threadName} threadId={activeThread.id} workspacePath={workspacePath} />

      {/* Reconnection banner */}
      {showReconnectionBanner && (
        <div
          role="status"
          aria-live="polite"
          style={{
            padding: '8px 16px',
            backgroundColor: 'rgba(220,38,38,0.1)',
            borderBottom: '1px solid var(--error)',
            color: 'var(--error)',
            fontSize: '12px',
            fontWeight: 500,
            display: 'flex',
            alignItems: 'center',
            gap: '8px',
            flexShrink: 0
          }}
        >
          <span style={{ width: '7px', height: '7px', borderRadius: '50%', background: 'var(--error)', flexShrink: 0, animation: 'pulse 1.5s ease-in-out infinite' }} />
          {connectionErrorMessage || 'Connection lost. Reconnecting...'}
        </div>
      )}

      {/* Message stream (fills remaining space) */}
      {hasContent ? (
        <MessageStream />
      ) : (
        <div style={centeredStyle}>
          <p
            style={{
              fontSize: '14px',
              color: 'var(--text-secondary)',
              lineHeight: 1.6,
              margin: 0,
              textAlign: 'center'
            }}
          >
            No messages yet. Ask DotCraft anything to get started.
          </p>
        </div>
      )}

      {/* Turn running indicator */}
      <TurnStatusIndicator threadId={activeThread.id} />

      {/* Input composer */}
      <InputComposer threadId={activeThread.id} workspacePath={workspacePath} />
    </div>
  )
}

const centeredStyle: React.CSSProperties = {
  display: 'flex',
  flex: 1,
  flexDirection: 'column',
  alignItems: 'center',
  justifyContent: 'center',
  backgroundColor: 'var(--bg-primary)'
}
