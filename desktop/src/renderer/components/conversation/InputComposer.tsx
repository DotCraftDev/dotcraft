import { useRef, useState, useCallback, useEffect, useMemo } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { useConversationStore } from '../../stores/conversationStore'
import { useThreadStore } from '../../stores/threadStore'
import { addToast } from '../../stores/toastStore'
import { useUIStore } from '../../stores/uiStore'
import type { ConversationItem, ConversationTurn, ImageAttachment } from '../../types/conversation'
import { PendingMessageIndicator } from './PendingMessageIndicator'
import { RichInputArea, type RichInputAreaHandle } from './RichInputArea'
import { ImageStrip } from './ImageStrip'
import { FileSearchPopover } from './FileSearchPopover'

const MAX_TEXT_LENGTH = 100_000
const MAX_IMAGES = 5
const MAX_IMAGE_BYTES = 10 * 1024 * 1024

const IMAGE_EXTENSIONS = new Set(['.png', '.jpg', '.jpeg', '.gif', '.webp', '.bmp'])

function extForFile(name: string): string {
  const i = name.lastIndexOf('.')
  return i >= 0 ? name.slice(i).toLowerCase() : ''
}

function isImageFile(file: File): boolean {
  if (file.type.startsWith('image/')) return true
  return IMAGE_EXTENSIONS.has(extForFile(file.name))
}

interface InputComposerProps {
  threadId: string
  workspacePath: string
  modelName?: string
  modelOptions?: string[]
  modelLoading?: boolean
  modelDisabled?: boolean
  /** When true, model/list reported that the upstream API does not support listing; show a read-only label. */
  modelListUnsupportedEndpoint?: boolean
  onModelChange?: (model: string) => void
}

/**
 * Bottom input area for the conversation panel.
 * Rich input with @ file refs, image strip (paste / drag-drop), Enter to send.
 */
export function InputComposer({
  threadId,
  workspacePath,
  modelName = 'Default',
  modelOptions = [],
  modelLoading = false,
  modelDisabled = false,
  modelListUnsupportedEndpoint = false,
  onModelChange
}: InputComposerProps): JSX.Element {
  const t = useT()
  const [images, setImages] = useState<ImageAttachment[]>([])
  const [atQuery, setAtQuery] = useState<string | null>(null)
  const [mentionDismissed, setMentionDismissed] = useState(false)
  const [dragOver, setDragOver] = useState(false)
  /** Bumps on rich-input edits so `canSend` re-evaluates from ref (contentEditable has no React state). */
  const [contentRevision, setContentRevision] = useState(0)
  const richRef = useRef<RichInputAreaHandle>(null)
  const composerWrapRef = useRef<HTMLDivElement>(null)

  const turnStatus = useConversationStore((s) => s.turnStatus)
  const pendingMessage = useConversationStore((s) => s.pendingMessage)
  const threadMode = useConversationStore((s) => s.threadMode)
  const setPendingMessage = useConversationStore((s) => s.setPendingMessage)
  const setThreadMode = useConversationStore((s) => s.setThreadMode)
  const composerPrefill = useUIStore((s) => s.composerPrefill)

  const isRunning = turnStatus === 'running'
  const isWaitingApproval = turnStatus === 'waitingApproval'

  const showMentionPopover = atQuery !== null && !mentionDismissed

  const handleAtQuery = useCallback((q: string | null): void => {
    setAtQuery(q)
    if (q !== null) setMentionDismissed(false)
  }, [])

  // Consume any pending prefill text when InputComposer mounts
  useEffect(() => {
    if (composerPrefill) {
      const prefill = composerPrefill
      useUIStore.getState().consumeComposerPrefill()
      setTimeout(() => {
        richRef.current?.setPlainText(prefill)
        richRef.current?.focus()
      }, 0)
    }
  }, [composerPrefill])

  useEffect(() => {
    const focus = (): void => {
      richRef.current?.focus()
    }
    const setTextAndFocus = (value: string): void => {
      richRef.current?.setPlainText(value)
      setTimeout(() => richRef.current?.focus(), 0)
    }
    ;(window as Window & { __inputComposerFocus?: () => void }).__inputComposerFocus = focus
    ;(window as Window & { __inputComposerSetText?: (v: string) => void }).__inputComposerSetText = setTextAndFocus
    return () => {
      delete (window as Window & { __inputComposerFocus?: () => void }).__inputComposerFocus
      delete (window as Window & { __inputComposerSetText?: (v: string) => void }).__inputComposerSetText
    }
  }, [])

  const prevTurnStatusRef = useRef(turnStatus)
  useEffect(() => {
    const prev = prevTurnStatusRef.current
    if (prev === 'waitingApproval' && turnStatus !== 'waitingApproval') {
      richRef.current?.focus()
    }
    prevTurnStatusRef.current = turnStatus
  }, [turnStatus])

  const saveDataUrlAsTemp = useCallback(
    async (dataUrl: string, fileName: string, mimeType: string): Promise<void> => {
      const baseLen = dataUrl.split(',')[1]?.length ?? 0
      const approxBytes = Math.floor((baseLen * 3) / 4)
      if (approxBytes > MAX_IMAGE_BYTES) {
        addToast(
          t('input.imageTooLarge', { mb: MAX_IMAGE_BYTES / 1024 / 1024 }),
          'warning'
        )
        return
      }
      if (images.length >= MAX_IMAGES) {
        addToast(t('input.maxImages', { max: MAX_IMAGES }), 'warning')
        return
      }
      try {
        const { path } = await window.api.workspace.saveImageToTemp({ dataUrl, fileName })
        setImages((prev) => [
          ...prev,
          { tempPath: path, dataUrl, fileName, mimeType }
        ])
      } catch (e) {
        const msg = e instanceof Error ? e.message : String(e)
        addToast(t('input.saveImageFailed', { error: msg }), 'error')
      }
    },
    [images.length, t]
  )

  const onPasteImage = useCallback(
    (file: File): void => {
      if (!isImageFile(file)) {
        addToast(
          t('input.unsupportedImage', { ext: extForFile(file.name) || 'unknown' }),
          'warning'
        )
        return
      }
      const reader = new FileReader()
      reader.onload = () => {
        const dataUrl = reader.result as string
        void saveDataUrlAsTemp(dataUrl, file.name, file.type || 'image/png')
      }
      reader.readAsDataURL(file)
    },
    [saveDataUrlAsTemp]
  )

  const onDragOver = useCallback((e: React.DragEvent): void => {
    e.preventDefault()
    e.stopPropagation()
    setDragOver(true)
  }, [])

  const onDragLeave = useCallback((e: React.DragEvent): void => {
    e.preventDefault()
    e.stopPropagation()
    setDragOver(false)
  }, [])

  const onDrop = useCallback(
    (e: React.DragEvent): void => {
      e.preventDefault()
      e.stopPropagation()
      setDragOver(false)
      const fl = Array.from(e.dataTransfer.files || [])
      let rejected = 0
      for (const file of fl) {
        if (!isImageFile(file)) {
          rejected++
          continue
        }
        const reader = new FileReader()
        reader.onload = () => {
          const dataUrl = reader.result as string
          void saveDataUrlAsTemp(dataUrl, file.name, file.type || 'image/png')
        }
        reader.readAsDataURL(file)
      }
      if (rejected > 0) {
        addToast(t('input.nonImageRejected', { count: rejected }), 'warning')
      }
    },
    [saveDataUrlAsTemp, t]
  )

  const sendMessage = useCallback(async () => {
    const text = richRef.current?.getText() ?? ''
    const trimmed = text.trim()
    if (!trimmed && images.length === 0) return
    if (isWaitingApproval) return
    if (modelLoading) return

    if (isRunning) {
      if (images.length > 0) {
        addToast(t('input.imageAttachmentsQueued'), 'warning')
      }
      setPendingMessage(trimmed)
      richRef.current?.clear()
      setImages([])
      return
    }

    const capturedImages = [...images]
    richRef.current?.clear()
    setImages([])

    const threadEntry = useThreadStore.getState().threadList.find((t) => t.id === threadId)
    if (!threadEntry?.displayName) {
      const autoName =
        trimmed.length > 50 ? trimmed.slice(0, 50) + '...' : trimmed || t('toast.imageMessage')
      useThreadStore.getState().renameThread(threadId, autoName)
    }

    const optimisticItemId = `local-${Date.now()}`
    const optimisticTurnId = `local-turn-${Date.now()}`
    const optimisticNow = new Date().toISOString()
    const userItem: ConversationItem = {
      id: optimisticItemId,
      type: 'userMessage',
      status: 'completed',
      text: trimmed,
      imageDataUrls: capturedImages.map((i) => i.dataUrl),
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

    const inputParts: Array<{ type: string; text?: string; path?: string }> = []
    if (trimmed.length > 0) {
      inputParts.push({ type: 'text', text: trimmed })
    }
    for (const img of capturedImages) {
      inputParts.push({ type: 'localImage', path: img.tempPath })
    }
    if (inputParts.length === 0) {
      useConversationStore.getState().removeOptimisticTurn(optimisticTurnId)
      return
    }

    try {
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
      const res = result as { turn?: { id?: string } }
      if (res.turn?.id) {
        useConversationStore.getState().promoteOptimisticTurn(optimisticTurnId, res.turn.id)
      }
    } catch (err) {
      console.error('turn/start failed:', err)
      useConversationStore.getState().removeOptimisticTurn(optimisticTurnId)
    }
  }, [images, isRunning, isWaitingApproval, modelLoading, threadId, workspacePath, setPendingMessage, t])

  const stopTurn = useCallback(async () => {
    const activeTurnId = useConversationStore.getState().activeTurnId
    if (!activeTurnId || activeTurnId.startsWith('local-turn-')) return
    try {
      await window.api.appServer.sendRequest('turn/interrupt', { threadId, turnId: activeTurnId })
    } catch (err) {
      console.error('turn/interrupt failed:', err)
    }
  }, [threadId])

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

  const canSend = useMemo(() => {
    const textLen = (richRef.current?.getText() ?? '').trim().length
    return (textLen > 0 || images.length > 0) && !isWaitingApproval && !modelLoading
  }, [contentRevision, images.length, isWaitingApproval, modelLoading])

  const effectiveModelOptions = useMemo(() => {
    const withDefault = ['Default', ...modelOptions.filter((o) => o !== 'Default')]
    if (!modelName || modelName === 'Default') return withDefault
    if (withDefault.includes(modelName)) return withDefault
    return [modelName, ...withDefault]
  }, [modelName, modelOptions])

  const onSelectFile = useCallback(
    (relativePath: string): void => {
      richRef.current?.insertFileTag(relativePath)
    },
    []
  )

  return (
    <div style={{ flexShrink: 0 }}>
      {pendingMessage && <PendingMessageIndicator message={pendingMessage} />}

      <div style={{ padding: '14px 14px', display: 'flex', flexDirection: 'column', gap: '6px' }}>
        <div
          ref={composerWrapRef}
          style={{ position: 'relative' }}
          onDragOver={onDragOver}
          onDragLeave={onDragLeave}
          onDrop={onDrop}
        >
          {dragOver && (
            <div
              style={{
                position: 'absolute',
                inset: 0,
                zIndex: 20,
                border: '2px dashed var(--accent)',
                borderRadius: '10px',
                background: 'rgba(124, 58, 237, 0.08)',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                pointerEvents: 'none',
                fontSize: '13px',
                color: 'var(--accent)'
              }}
            >
              {t('composer.dropImage')}
            </div>
          )}

          <ImageStrip
            images={images}
            onRemove={(idx) => {
              setImages((prev) => prev.filter((_, i) => i !== idx))
            }}
          />

          <div style={{ display: 'flex', alignItems: 'flex-end', gap: '8px', position: 'relative' }}>
            <div style={{ flex: 1, position: 'relative', minWidth: 0 }}>
              <FileSearchPopover
                query={atQuery ?? ''}
                visible={showMentionPopover}
                workspacePath={workspacePath}
                onSelect={onSelectFile}
                onDismiss={() => {
                  setMentionDismissed(true)
                }}
              />
              <RichInputArea
                ref={richRef}
                disabled={isWaitingApproval}
                suppressSubmit={showMentionPopover || modelLoading}
                onToggleModeShortcut={() => {
                  void toggleMode()
                }}
                placeholder={
                  isWaitingApproval ? t('composer.placeholder.approval') : t('composer.placeholder.ask')
                }
                onSubmit={() => {
                  void sendMessage()
                }}
                onAtQuery={handleAtQuery}
                onContentChange={() => {
                  setContentRevision((n) => n + 1)
                }}
                onPasteImage={onPasteImage}
                onPasteTextOversized={() => {
                  addToast(
                    t('input.truncated', { max: MAX_TEXT_LENGTH.toLocaleString() }),
                    'warning'
                  )
                }}
              />
            </div>

            {!isWaitingApproval &&
              (isRunning ? (
                <button
                  type="button"
                  onClick={stopTurn}
                  title={t('composer.stopTitle')}
                  aria-label={t('composer.stopAria')}
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
                  type="button"
                  onClick={() => {
                    void sendMessage()
                  }}
                  disabled={!canSend}
                  title={t('composer.sendTitleAlt')}
                  aria-label={t('composer.sendAriaAlt')}
                  style={{
                    ...sendButtonBase,
                    backgroundColor: canSend ? 'var(--accent)' : 'var(--bg-tertiary)',
                    color: canSend ? '#fff' : 'var(--text-dimmed)',
                    cursor: canSend ? 'pointer' : 'default'
                  }}
                >
                  <SendIcon />
                </button>
              ))}
          </div>
        </div>

        <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
          <button
            type="button"
            onClick={() => {
              void toggleMode()
            }}
            title={t('composer.modeTitle', {
              mode: t(threadMode === 'agent' ? 'composer.mode.agent' : 'composer.mode.plan')
            })}
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
                width: 14,
                height: 14,
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                flexShrink: 0
              }}
            >
              <span
                style={{
                  width: '7px',
                  height: '7px',
                  borderRadius: '50%',
                  backgroundColor: threadMode === 'agent' ? 'var(--success)' : 'var(--info)',
                  display: 'block'
                }}
              />
            </span>
            <span style={{ lineHeight: 1.2 }}>
              {t(threadMode === 'agent' ? 'composer.mode.agent' : 'composer.mode.plan')}
            </span>
          </button>

          <span style={{ color: 'var(--border-default)' }}>·</span>

          {modelLoading ? (
            <span
              role="status"
              aria-live="polite"
              style={{
                fontSize: '12px',
                color: 'var(--text-dimmed)',
                display: 'inline-block',
                width: '170px',
                minWidth: '170px',
                maxWidth: '170px',
                whiteSpace: 'nowrap',
                overflow: 'hidden',
                textOverflow: 'ellipsis'
              }}
              title={t('composer.modelListLoading')}
            >
              {t('composer.modelListLoading')}
            </span>
          ) : modelListUnsupportedEndpoint ? (
            <span
              style={{
                fontSize: '12px',
                color: 'var(--text-dimmed)',
                display: 'inline-block',
                width: '170px',
                minWidth: '170px',
                maxWidth: '170px',
                whiteSpace: 'nowrap',
                overflow: 'hidden',
                textOverflow: 'ellipsis'
              }}
              title={t('composer.modelListUnsupportedTitle')}
            >
              {modelName === 'Default' ? t('composer.defaultModel') : modelName}
            </span>
          ) : effectiveModelOptions.length > 0 ? (
            <select
              value={modelName}
              disabled={modelDisabled}
              onChange={(e) => onModelChange?.(e.target.value)}
              title={t('composer.selectModelTitle')}
              style={{
                fontSize: '12px',
                color: modelDisabled ? 'var(--text-dimmed)' : 'var(--text-primary)',
                backgroundColor: 'var(--bg-secondary)',
                border: '1px solid var(--border-default)',
                borderRadius: '6px',
                padding: '2px 6px',
                minHeight: '22px',
                width: '170px',
                minWidth: '170px',
                maxWidth: '170px',
                outline: 'none',
                cursor: modelDisabled ? 'default' : 'pointer'
              }}
            >
              {effectiveModelOptions.map((opt) => (
                <option key={opt} value={opt}>
                  {opt === 'Default' ? t('composer.defaultModel') : opt}
                </option>
              ))}
            </select>
          ) : (
            <span
              style={{
                fontSize: '12px',
                color: 'var(--text-dimmed)',
                display: 'inline-block',
                width: '170px',
                minWidth: '170px',
                maxWidth: '170px',
                whiteSpace: 'nowrap',
                overflow: 'hidden',
                textOverflow: 'ellipsis'
              }}
              title={modelName === 'Default' ? t('composer.defaultModel') : modelName}
            >
              {modelName === 'Default' ? t('composer.defaultModel') : modelName}
            </span>
          )}
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

function StopIcon(): JSX.Element {
  return (
    <svg width="12" height="12" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
      <rect x="4" y="4" width="16" height="16" />
    </svg>
  )
}

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
