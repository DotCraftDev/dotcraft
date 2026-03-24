import { useCallback, useEffect, useRef, useState } from 'react'
import { DotCraftLogo } from '../ui/DotCraftLogo'
import { useConnectionStore } from '../../stores/connectionStore'
import { useThreadStore } from '../../stores/threadStore'
import { useUIStore } from '../../stores/uiStore'
import { addToast } from '../../stores/toastStore'

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

const MAX_ROWS = 4
const MAX_TEXT_LENGTH = 100_000

/**
 * Welcome state when the workspace is connected but no thread is selected.
 * Includes a bottom-pinned composer so users can start a conversation without
 * clicking New Thread first; quick-start cards prefill the composer.
 */
export function ConversationWelcome({ workspacePath }: ConversationWelcomeProps): JSX.Element {
  const [text, setText] = useState('')
  const [hoveredIdx, setHoveredIdx] = useState<number | null>(null)
  const [starting, setStarting] = useState(false)
  const sendInFlightRef = useRef(false)
  const textareaRef = useRef<HTMLTextAreaElement>(null)
  const connectionStatus = useConnectionStore((s) => s.status)
  const { addThread, setActiveThreadId } = useThreadStore()

  const isConnected = connectionStatus === 'connected'
  const busy = starting || !isConnected

  useEffect(() => {
    if (isConnected) {
      textareaRef.current?.focus()
    }
  }, [isConnected])

  function adjustHeight(): void {
    const el = textareaRef.current
    if (!el) return
    el.style.height = 'auto'
    const lineHeight = parseInt(getComputedStyle(el).lineHeight) || 20
    const maxHeight = lineHeight * MAX_ROWS + 24
    el.style.height = `${Math.min(el.scrollHeight, maxHeight)}px`
  }

  useEffect(() => {
    adjustHeight()
  }, [text])

  const sendFromWelcome = useCallback(async (): Promise<void> => {
    const trimmed = text.trim()
    if (!trimmed || sendInFlightRef.current || connectionStatus !== 'connected') return

    sendInFlightRef.current = true
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

      useUIStore.getState().setPendingWelcomeTurn({ threadId: res.thread.id, text: trimmed })
      addThread(res.thread)
      setActiveThreadId(res.thread.id)
      useUIStore.getState().setActiveMainView('conversation')
      setText('')
    } catch (err) {
      console.error('Failed to start thread from welcome composer:', err)
    } finally {
      sendInFlightRef.current = false
      setStarting(false)
    }
  }, [text, connectionStatus, workspacePath, addThread, setActiveThreadId])

  function handleKeyDown(e: React.KeyboardEvent<HTMLTextAreaElement>): void {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault()
      void sendFromWelcome()
    }
  }

  function handlePaste(e: React.ClipboardEvent<HTMLTextAreaElement>): void {
    const pastedText = e.clipboardData.getData('text')
    if (pastedText.length > MAX_TEXT_LENGTH) {
      e.preventDefault()
      const truncated = pastedText.slice(0, MAX_TEXT_LENGTH)
      setText((prev) => {
        const combined = prev + truncated
        return combined.slice(0, MAX_TEXT_LENGTH)
      })
      addToast(`Input truncated to ${MAX_TEXT_LENGTH.toLocaleString()} characters`, 'warning')
    }
  }

  function fillSuggestion(prompt: string): void {
    setText(prompt)
    setTimeout(() => textareaRef.current?.focus(), 0)
  }

  const canSend = text.trim().length > 0 && isConnected && !starting

  return (
    <div
      style={{
        display: 'flex',
        flex: 1,
        flexDirection: 'column',
        minHeight: 0,
        backgroundColor: 'var(--bg-primary)',
        overflow: 'hidden'
      }}
    >
      {/* Upper + middle: scrollable */}
      <div
        style={{
          flex: 1,
          minHeight: 0,
          overflowY: 'auto',
          display: 'flex',
          flexDirection: 'column',
          alignItems: 'center',
          justifyContent: 'center',
          padding: '24px 24px 16px'
        }}
      >
        <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', marginBottom: '20px' }}>
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
          <p style={{ fontSize: '14px', color: 'var(--text-secondary)', margin: 0, textAlign: 'center', maxWidth: '420px' }}>
            {isConnected
              ? 'Select a thread from the sidebar, type below, or pick a quick start.'
              : 'Connecting to workspace…'}
          </p>
        </div>

        {isConnected && (
          <div
            style={{
              display: 'grid',
              gridTemplateColumns: 'repeat(2, 1fr)',
              gap: '10px',
              width: '100%',
              maxWidth: '520px',
              marginBottom: '8px'
            }}
          >
            {SUGGESTIONS.map((s, idx) => (
              <button
                key={idx}
                type="button"
                onClick={() => { fillSuggestion(s.prompt) }}
                disabled={busy}
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
                  cursor: busy ? 'default' : 'pointer',
                  textAlign: 'left',
                  transition: 'border-color 120ms ease, background-color 120ms ease',
                  opacity: busy ? 0.6 : 1
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

      {/* Bottom composer */}
      <div
        style={{
          borderTop: '1px solid var(--border-default)',
          flexShrink: 0,
          padding: '10px 14px',
          opacity: starting ? 0.65 : 1
        }}
      >
        <div style={{ maxWidth: '720px', margin: '0 auto', display: 'flex', flexDirection: 'column', gap: '6px' }}>
          <div style={{ display: 'flex', alignItems: 'flex-end', gap: '8px' }}>
            <textarea
              ref={textareaRef}
              value={text}
              onChange={(e) => { setText(e.target.value.slice(0, MAX_TEXT_LENGTH)) }}
              onKeyDown={handleKeyDown}
              onPaste={handlePaste}
              placeholder={isConnected ? 'Ask DotCraft anything…' : 'Connecting…'}
              rows={1}
              disabled={busy}
              aria-busy={starting}
              style={{
                flex: 1,
                resize: 'none',
                border: '1px solid var(--border-default)',
                borderRadius: '8px',
                padding: '8px 12px',
                fontSize: '14px',
                lineHeight: '20px',
                fontFamily: 'var(--font-sans)',
                backgroundColor: busy ? 'var(--bg-tertiary)' : 'var(--bg-secondary)',
                color: busy ? 'var(--text-dimmed)' : 'var(--text-primary)',
                outline: 'none',
                overflowY: 'auto',
                transition: 'border-color 100ms ease, background-color 150ms ease',
                cursor: busy ? 'not-allowed' : 'text'
              }}
              onFocus={(e) => { if (!busy) e.currentTarget.style.borderColor = 'var(--border-active)' }}
              onBlur={(e) => { e.currentTarget.style.borderColor = 'var(--border-default)' }}
            />
            <button
              type="button"
              onClick={() => { void sendFromWelcome() }}
              disabled={!canSend}
              title="Send (Enter)"
              aria-label="Send message"
              style={{
                width: '34px',
                height: '34px',
                borderRadius: '8px',
                border: 'none',
                flexShrink: 0,
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                cursor: canSend ? 'pointer' : 'default',
                transition: 'background-color 100ms ease',
                backgroundColor: canSend ? 'var(--accent)' : 'var(--bg-tertiary)',
                color: canSend ? '#fff' : 'var(--text-dimmed)'
              }}
            >
              <SendIcon />
            </button>
          </div>
        </div>
      </div>
    </div>
  )
}

function SendIcon(): JSX.Element {
  return (
    <svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
      <path d="M2.01 21L23 12 2.01 3 2 10l15 2-15 2z" />
    </svg>
  )
}
