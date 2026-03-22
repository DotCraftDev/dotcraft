import { useRef, useState, useCallback, useEffect } from 'react'
import { useConversationStore } from '../../stores/conversationStore'
import { useThreadStore } from '../../stores/threadStore'
import { addToast } from '../../stores/toastStore'
import { useUIStore } from '../../stores/uiStore'
import type { ConversationItem, ConversationTurn } from '../../types/conversation'
import { PendingMessageIndicator } from './PendingMessageIndicator'

interface ImageAttachment {
  dataUrl: string
  mimeType: string
}

const MAX_ROWS = 8
const MAX_TEXT_LENGTH = 100_000

interface InputComposerProps {
  threadId: string
  workspacePath: string
  modelName?: string
}

/**
 * Bottom input area for the conversation panel.
 * - Multi-line textarea that grows 1–8 rows.
 * - Enter sends; Shift+Enter inserts newline.
 * - When a turn is running: enter queues a pending message instead.
 * - Send button changes to Stop (square) when running.
 * - Mode indicator (Agent / Plan) toggles via thread/mode/set.
 * - Model name display (read-only).
 * Spec §10.3.4
 */
export function InputComposer({ threadId, workspacePath, modelName = 'Default' }: InputComposerProps): JSX.Element {
  const [text, setText] = useState('')
  const [imageAttachment, setImageAttachment] = useState<ImageAttachment | null>(null)
  const textareaRef = useRef<HTMLTextAreaElement>(null)

  const turnStatus = useConversationStore((s) => s.turnStatus)
  const pendingMessage = useConversationStore((s) => s.pendingMessage)
  const threadMode = useConversationStore((s) => s.threadMode)
  const setPendingMessage = useConversationStore((s) => s.setPendingMessage)
  const setThreadMode = useConversationStore((s) => s.setThreadMode)
  const composerPrefill = useUIStore((s) => s.composerPrefill)

  const isRunning = turnStatus === 'running'
  const isWaitingApproval = turnStatus === 'waitingApproval'

  // Consume any pending prefill text written by ConversationWelcome before this mounted
  useEffect(() => {
    if (composerPrefill) {
      setText(composerPrefill)
      useUIStore.getState().consumeComposerPrefill()
      setTimeout(() => textareaRef.current?.focus(), 0)
    }
  }, [composerPrefill])

  // Expose focus and pre-fill functions globally so other components can drive the composer
  useEffect(() => {
    const focus = (): void => { textareaRef.current?.focus() }
    const setTextAndFocus = (value: string): void => {
      setText(value)
      // Focus after a tick so the textarea has re-rendered with the new value
      setTimeout(() => textareaRef.current?.focus(), 0)
    }
    ;(window as Window & { __inputComposerFocus?: () => void }).__inputComposerFocus = focus
    ;(window as Window & { __inputComposerSetText?: (v: string) => void }).__inputComposerSetText = setTextAndFocus
    return () => {
      delete (window as Window & { __inputComposerFocus?: () => void }).__inputComposerFocus
      delete (window as Window & { __inputComposerSetText?: (v: string) => void }).__inputComposerSetText
    }
  }, [])

  // Return focus to textarea when transitioning from waitingApproval back to running/idle
  const prevTurnStatusRef = useRef(turnStatus)
  useEffect(() => {
    const prev = prevTurnStatusRef.current
    if (prev === 'waitingApproval' && turnStatus !== 'waitingApproval') {
      textareaRef.current?.focus()
    }
    prevTurnStatusRef.current = turnStatus
  }, [turnStatus])

  // Auto-resize textarea
  function adjustHeight(): void {
    const el = textareaRef.current
    if (!el) return
    el.style.height = 'auto'
    const lineHeight = parseInt(getComputedStyle(el).lineHeight) || 20
    const maxHeight = lineHeight * MAX_ROWS + 24 // 24px vertical padding
    el.style.height = `${Math.min(el.scrollHeight, maxHeight)}px`
  }

  useEffect(() => {
    adjustHeight()
  }, [text])

  const sendMessage = useCallback(async () => {
    const trimmed = text.trim()
    if (!trimmed) return

    // Block sending and queueing during approval wait — user must decide first
    if (isWaitingApproval) return

    if (isRunning) {
      // Queue as pending (only one queued at a time — latest wins)
      setPendingMessage(trimmed)
      setText('')
      return
    }

    const capturedImage = imageAttachment
    setText('')
    setImageAttachment(null)

    // Optimistically name a new thread from the first message — mirrors server behaviour.
    // This fires immediately so the sidebar shows the name while the agent is still running.
    const threadEntry = useThreadStore.getState().threadList.find((t) => t.id === threadId)
    if (!threadEntry?.displayName) {
      const autoName = trimmed.length > 50 ? trimmed.slice(0, 50) + '...' : trimmed
      useThreadStore.getState().renameThread(threadId, autoName)
    }

    // Optimistically add the user message and a placeholder turn to the store
    // so the UI updates immediately without waiting for server notifications.
    const optimisticItemId = `local-${Date.now()}`
    const optimisticTurnId = `local-turn-${Date.now()}`
    const optimisticNow = new Date().toISOString()
    const userItem: ConversationItem = {
      id: optimisticItemId,
      type: 'userMessage',
      status: 'completed',
      text: trimmed,
      createdAt: optimisticNow,
      completedAt: optimisticNow
    }
    const optimisticTurn: ConversationTurn = {
      id: optimisticTurnId,
      threadId,
      status: 'running',
      items: [userItem],
      startedAt: optimisticNow
    }
    useConversationStore.getState().addOptimisticTurn(optimisticTurn)

    try {
      const inputParts: Array<{ type: string; text?: string; data?: string; mimeType?: string }> = [
        { type: 'text', text: trimmed }
      ]
      if (capturedImage) {
        // Strip data URL prefix to get raw base64
        const base64 = capturedImage.dataUrl.split(',')[1] ?? ''
        inputParts.push({ type: 'localImage', data: base64, mimeType: capturedImage.mimeType })
      }

      const result = await window.api.appServer.sendRequest('turn/start', {
        threadId,
        input: inputParts,
        identity: {
          channelName: 'dotcraft-desktop',
          userId: 'local',
          channelContext: `workspace:${workspacePath}`,
          workspacePath
        }
      })
      // Promote the optimistic turn to the real server turn ID immediately,
      // so turn/interrupt can send the correct turnId without waiting for
      // the turn/started notification.
      const res = result as { turn?: { id?: string } }
      if (res.turn?.id) {
        useConversationStore.getState().promoteOptimisticTurn(optimisticTurnId, res.turn.id)
      }
    } catch (err) {
      console.error('turn/start failed:', err)
      // On failure, remove the optimistic turn and show an error
      useConversationStore.getState().removeOptimisticTurn(optimisticTurnId)
    }
  }, [text, imageAttachment, isRunning, isWaitingApproval, threadId, workspacePath, setPendingMessage])

  const stopTurn = useCallback(async () => {
    const activeTurnId = useConversationStore.getState().activeTurnId
    // Don't send interrupt if we only have a local optimistic ID (server hasn't confirmed yet)
    if (!activeTurnId || activeTurnId.startsWith('local-turn-')) return
    try {
      await window.api.appServer.sendRequest('turn/interrupt', { threadId, turnId: activeTurnId })
    } catch (err) {
      console.error('turn/interrupt failed:', err)
    }
  }, [threadId])

  function handleKeyDown(e: React.KeyboardEvent<HTMLTextAreaElement>): void {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault()
      void sendMessage()
    }
  }

  function handlePaste(e: React.ClipboardEvent<HTMLTextAreaElement>): void {
    // Handle image paste
    const items = Array.from(e.clipboardData.items)
    const imageItem = items.find((item) => item.type.startsWith('image/'))
    if (imageItem) {
      e.preventDefault()
      const file = imageItem.getAsFile()
      if (!file) return
      const reader = new FileReader()
      reader.onload = (ev) => {
        const dataUrl = ev.target?.result as string
        if (dataUrl) setImageAttachment({ dataUrl, mimeType: imageItem.type })
      }
      reader.readAsDataURL(file)
      return
    }

    // Handle oversized text paste
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

  async function toggleMode(): Promise<void> {
    const newMode = threadMode === 'agent' ? 'plan' : 'agent'
    setThreadMode(newMode)
    try {
      await window.api.appServer.sendRequest('thread/mode/set', {
        threadId,
        mode: newMode
      })
    } catch (err) {
      console.error('thread/mode/set failed:', err)
    }
  }

  const canSend = text.trim().length > 0 && !isWaitingApproval

  return (
    <div
      style={{
        borderTop: '1px solid var(--border-default)',
        flexShrink: 0
      }}
    >
      {/* Pending message indicator */}
      {pendingMessage && <PendingMessageIndicator message={pendingMessage} />}

      <div style={{ padding: '10px 14px', display: 'flex', flexDirection: 'column', gap: '6px' }}>
        {/* Image attachment thumbnail */}
        {imageAttachment && (
          <div style={{ display: 'flex', alignItems: 'flex-start', gap: '8px' }}>
            <div style={{ position: 'relative', flexShrink: 0 }}>
              <img
                src={imageAttachment.dataUrl}
                alt="Attachment preview"
                style={{
                  maxWidth: '80px',
                  maxHeight: '60px',
                  borderRadius: '4px',
                  border: '1px solid var(--border-default)',
                  objectFit: 'cover'
                }}
              />
              <button
                onClick={() => setImageAttachment(null)}
                aria-label="Remove image attachment"
                title="Remove image"
                style={{
                  position: 'absolute',
                  top: '-6px',
                  right: '-6px',
                  width: '16px',
                  height: '16px',
                  borderRadius: '50%',
                  background: 'var(--bg-tertiary)',
                  border: '1px solid var(--border-default)',
                  color: 'var(--text-secondary)',
                  fontSize: '10px',
                  cursor: 'pointer',
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  padding: 0
                }}
              >
                ✕
              </button>
            </div>
          </div>
        )}

        {/* Textarea row */}
        <div style={{ display: 'flex', alignItems: 'flex-end', gap: '8px' }}>
          <textarea
            ref={textareaRef}
            value={text}
            onChange={(e) => { if (!isWaitingApproval) setText(e.target.value) }}
            onKeyDown={handleKeyDown}
            onPaste={handlePaste}
            placeholder={isWaitingApproval ? 'Waiting for approval decision...' : 'Ask DotCraft anything'}
            rows={1}
            disabled={isWaitingApproval}
            style={{
              flex: 1,
              resize: 'none',
              border: '1px solid var(--border-default)',
              borderRadius: '8px',
              padding: '8px 12px',
              fontSize: '14px',
              lineHeight: '20px',
              fontFamily: 'var(--font-sans)',
              backgroundColor: isWaitingApproval ? 'var(--bg-tertiary)' : 'var(--bg-secondary)',
              color: isWaitingApproval ? 'var(--text-dimmed)' : 'var(--text-primary)',
              outline: 'none',
              overflowY: 'auto',
              transition: 'border-color 100ms ease, background-color 150ms ease',
              cursor: isWaitingApproval ? 'not-allowed' : 'text',
              opacity: isWaitingApproval ? 0.6 : 1
            }}
            onFocus={(e) => { if (!isWaitingApproval) e.currentTarget.style.borderColor = 'var(--border-active)' }}
            onBlur={(e) => (e.currentTarget.style.borderColor = 'var(--border-default)')}
          />

          {/* Send / Stop button — hidden during approval wait */}
          {!isWaitingApproval && (
            isRunning ? (
              <button
                onClick={stopTurn}
                title="Stop (Esc)"
                aria-label="Stop turn"
                style={{
                  ...sendButtonBase,
                  backgroundColor: 'var(--error)',
                  color: '#fff'
                }}
              >
                <StopIcon />
              </button>
            ) : (
              <button
                onClick={sendMessage}
                disabled={!canSend}
                title="Send (Enter)"
                aria-label="Send message"
                style={{
                  ...sendButtonBase,
                  backgroundColor: canSend ? 'var(--accent)' : 'var(--bg-tertiary)',
                  color: canSend ? '#fff' : 'var(--text-dimmed)',
                  cursor: canSend ? 'pointer' : 'default'
                }}
              >
                <SendIcon />
              </button>
            )
          )}
        </div>

        {/* Bottom row: mode + model */}
        <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
          {/* Mode toggle */}
          <button
            onClick={toggleMode}
            title={`Mode: ${threadMode}. Click to toggle.`}
            style={{
              display: 'flex',
              alignItems: 'center',
              gap: '5px',
              background: 'none',
              border: 'none',
              cursor: 'pointer',
              padding: '2px 4px',
              borderRadius: '4px',
              fontSize: '12px',
              color: 'var(--text-secondary)'
            }}
          >
            <span
              style={{
                width: '7px',
                height: '7px',
                borderRadius: '50%',
                backgroundColor: threadMode === 'agent' ? 'var(--success)' : 'var(--info)',
                flexShrink: 0
              }}
            />
            <span style={{ textTransform: 'capitalize' }}>{threadMode}</span>
          </button>

          <span style={{ color: 'var(--border-default)' }}>·</span>

          {/* Model name (read-only) */}
          <span
            style={{
              fontSize: '12px',
              color: 'var(--text-dimmed)'
            }}
          >
            {modelName}
          </span>
        </div>
      </div>
    </div>
  )
}

// ---------------------------------------------------------------------------
// Icons
// ---------------------------------------------------------------------------

function SendIcon(): JSX.Element {
  return (
    <svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
      <path d="M2.01 21L23 12 2.01 3 2 10l15 2-15 2z" />
    </svg>
  )
}

function StopIcon(): JSX.Element {
  return (
    <svg width="12" height="12" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
      <rect x="4" y="4" width="16" height="16" />
    </svg>
  )
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const sendButtonBase: React.CSSProperties = {
  width: '34px',
  height: '34px',
  borderRadius: '8px',
  border: 'none',
  flexShrink: 0,
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  cursor: 'pointer',
  transition: 'background-color 100ms ease'
}
