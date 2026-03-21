import { useState } from 'react'
import { DotCraftLogo } from '../ui/DotCraftLogo'
import { useConnectionStore } from '../../stores/connectionStore'
import { useThreadStore } from '../../stores/threadStore'
import { useUIStore } from '../../stores/uiStore'

interface ConversationWelcomeProps {
  workspacePath: string
}

interface Suggestion {
  icon: string
  title: string
  prompt: string
}

const SUGGESTIONS: Suggestion[] = [
  {
    icon: '📄',
    title: 'Explore this workspace',
    prompt: 'Give me a quick overview of this project: what it does, its structure, and where the main entry points are.'
  },
  {
    icon: '🐛',
    title: 'Find and fix a bug',
    prompt: 'Scan the codebase for potential bugs, error-prone patterns, or unhandled edge cases and suggest fixes.'
  },
  {
    icon: '✨',
    title: 'Write a new feature',
    prompt: 'Help me design and implement a new feature for this project. Describe what you want to build.'
  },
  {
    icon: '📝',
    title: 'Generate documentation',
    prompt: 'Generate clear documentation for this codebase: README sections, inline comments, and API docs.'
  }
]

/**
 * Codex-style welcome card shown in the conversation panel when the workspace
 * is connected but no thread is active (sidebar selected nothing, or fresh open).
 */
export function ConversationWelcome({ workspacePath }: ConversationWelcomeProps): JSX.Element {
  const [hoveredIdx, setHoveredIdx] = useState<number | null>(null)
  const [starting, setStarting] = useState(false)
  const connectionStatus = useConnectionStore((s) => s.status)
  const { addThread, setActiveThreadId } = useThreadStore()

  async function startWithPrompt(prompt: string): Promise<void> {
    if (starting || connectionStatus !== 'connected') return
    setStarting(true)
    try {
      const res = await window.api.appServer.sendRequest('thread/start', {
        identity: {
          channelName: 'dotcraft-desktop',
          userId: 'local',
          channelContext: `workspace:${workspacePath}`,
          workspacePath
        },
        historyMode: 'server'
      }) as { thread: { id: string; displayName?: string | null; status?: string; createdAt?: string } }

      // Write prefill before activating the thread so InputComposer reads it on mount
      useUIStore.getState().setComposerPrefill(prompt)
      addThread(res.thread)
      setActiveThreadId(res.thread.id)
    } catch (err) {
      console.error('Failed to create quick-start thread:', err)
    } finally {
      setStarting(false)
    }
  }

  const isConnected = connectionStatus === 'connected'

  return (
    <div
      style={{
        display: 'flex',
        flex: 1,
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        backgroundColor: 'var(--bg-primary)',
        padding: '32px 24px',
        overflowY: 'auto'
      }}
    >
      {/* Logo + headline */}
      <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', marginBottom: '36px' }}>
        <DotCraftLogo size={56} style={{ marginBottom: '16px' }} />
        <h1
          style={{
            fontSize: '22px',
            fontWeight: 700,
            color: 'var(--text-primary)',
            margin: '0 0 6px 0',
            letterSpacing: '-0.3px'
          }}
        >
          What can I help you build?
        </h1>
        <p style={{ fontSize: '14px', color: 'var(--text-secondary)', margin: 0, textAlign: 'center' }}>
          {isConnected
            ? 'Select a thread from the sidebar or use a quick start below.'
            : 'Connecting to workspace…'}
        </p>
      </div>

      {/* Quick-start suggestion cards */}
      {isConnected && (
        <div
          style={{
            display: 'grid',
            gridTemplateColumns: 'repeat(2, 1fr)',
            gap: '10px',
            width: '100%',
            maxWidth: '520px'
          }}
        >
          {SUGGESTIONS.map((s, idx) => (
            <button
              key={idx}
              onClick={() => { void startWithPrompt(s.prompt) }}
              disabled={starting}
              onMouseEnter={() => setHoveredIdx(idx)}
              onMouseLeave={() => setHoveredIdx(null)}
              style={{
                display: 'flex',
                flexDirection: 'column',
                alignItems: 'flex-start',
                gap: '6px',
                padding: '14px 16px',
                background: hoveredIdx === idx ? 'var(--bg-tertiary)' : 'var(--bg-secondary)',
                border: `1px solid ${hoveredIdx === idx ? 'var(--accent)' : 'var(--border-default)'}`,
                borderRadius: '10px',
                cursor: starting ? 'default' : 'pointer',
                textAlign: 'left',
                transition: 'border-color 120ms ease, background-color 120ms ease',
                opacity: starting ? 0.6 : 1
              }}
              aria-label={s.title}
            >
              <span style={{ fontSize: '18px', lineHeight: 1 }}>{s.icon}</span>
              <span
                style={{
                  fontSize: '13px',
                  fontWeight: 600,
                  color: 'var(--text-primary)',
                  lineHeight: 1.3
                }}
              >
                {s.title}
              </span>
            </button>
          ))}
        </div>
      )}
    </div>
  )
}
