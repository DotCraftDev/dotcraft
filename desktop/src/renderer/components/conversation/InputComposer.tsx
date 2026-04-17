import { useRef, useState, useCallback, useEffect, useMemo } from 'react'
import { useLocale, useT } from '../../contexts/LocaleContext'
import { useConversationStore } from '../../stores/conversationStore'
import { useThreadStore } from '../../stores/threadStore'
import { addToast } from '../../stores/toastStore'
import { useUIStore } from '../../stores/uiStore'
import { useConnectionStore } from '../../stores/connectionStore'
import { useCustomCommandCatalog } from '../../hooks/useCustomCommandCatalog'
import { resolveCustomCommandExecution } from '../../utils/customCommandExecution'
import type { ConversationItem, ConversationTurn, ImageAttachment } from '../../types/conversation'
import { PendingMessageIndicator } from './PendingMessageIndicator'
import { RichInputArea, type RichInputAreaHandle } from './RichInputArea'
import { ImageStrip } from './ImageStrip'
import { FileSearchPopover } from './FileSearchPopover'
import { CommandSearchPopover } from './CommandSearchPopover'
import { ModelPicker } from './ModelPicker'
import {
  ComposerModeSwitch,
  ComposerShell,
  SendIcon,
  StopIcon,
  composerActionButtonStyle,
  composerModelPillStyle
} from './ComposerShell'

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
  const [slashQuery, setSlashQuery] = useState<string | null>(null)
  const [slashDismissed, setSlashDismissed] = useState(false)
  const [dragOver, setDragOver] = useState(false)
  const [editorFocused, setEditorFocused] = useState(false)
  /** Bumps on rich-input edits so `canSend` re-evaluates from ref (contentEditable has no React state). */
  const [contentRevision, setContentRevision] = useState(0)
  const richRef = useRef<RichInputAreaHandle>(null)
  const sendInFlightRef = useRef(false)

  const turnStatus = useConversationStore((s) => s.turnStatus)
  const pendingMessage = useConversationStore((s) => s.pendingMessage)
  const threadMode = useConversationStore((s) => s.threadMode)
  const setPendingMessage = useConversationStore((s) => s.setPendingMessage)
  const setThreadMode = useConversationStore((s) => s.setThreadMode)
  const composerPrefill = useUIStore((s) => s.composerPrefill)
  const capabilities = useConnectionStore((s) => s.capabilities)
  const locale = useLocale()

  const isRunning = turnStatus === 'running'
  const isWaitingApproval = turnStatus === 'waitingApproval'
  const canUseCommandPicker = capabilities?.commandManagement === true

  const showMentionPopover = atQuery !== null && !mentionDismissed
  const showSlashPopover = slashQuery !== null && !slashDismissed && canUseCommandPicker
  const { commands: customCommands, status: customCommandStatus } = useCustomCommandCatalog({
    enabled: canUseCommandPicker,
    locale
  })

  const handleAtQuery = useCallback((q: string | null): void => {
    setAtQuery(q)
    if (q !== null) setMentionDismissed(false)
  }, [])

  const handleSlashQuery = useCallback((q: string | null): void => {
    setSlashQuery(q)
    if (q !== null) setSlashDismissed(false)
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

    if (sendInFlightRef.current) return
    sendInFlightRef.current = true
    try {
      let effectiveText = trimmed
      let effectiveThreadId = threadId
      try {
        const commandResult = await resolveCustomCommandExecution({
          text: trimmed,
          threadId,
          commands: customCommands,
          sendRequest: (method, params) => window.api.appServer.sendRequest(method, params)
        })
        if (commandResult.message) {
          addToast(commandResult.message, 'info', undefined, commandResult.isMarkdown)
        }
        if (commandResult.sessionResetThreadSummary != null) {
          useThreadStore.getState().addThread(commandResult.sessionResetThreadSummary)
        }
        if (commandResult.sessionResetThreadId != null) {
          effectiveThreadId = commandResult.sessionResetThreadId
          useThreadStore.getState().setActiveThreadId(commandResult.sessionResetThreadId)
        }
        if (commandResult.matchedCustomCommand) {
          if (!commandResult.shouldSendTurn) {
            richRef.current?.clear()
            setImages([])
            return
          }
          effectiveText = commandResult.textForTurn.trim()
        }
      } catch (err) {
        const msg = err instanceof Error ? err.message : String(err)
        addToast(msg, 'error')
        return
      }

      const capturedImages = [...images]
      richRef.current?.clear()
      setImages([])

      const threadEntry = useThreadStore.getState().threadList.find((t) => t.id === effectiveThreadId)
      if (!threadEntry?.displayName) {
        const autoName =
          effectiveText.length > 50 ? effectiveText.slice(0, 50) + '...' : effectiveText || t('toast.imageMessage')
        useThreadStore.getState().renameThread(effectiveThreadId, autoName)
      }

      const optimisticItemId = `local-${Date.now()}`
      const optimisticTurnId = `local-turn-${Date.now()}`
      const optimisticNow = new Date().toISOString()
      const userItem: ConversationItem = {
        id: optimisticItemId,
        type: 'userMessage',
        status: 'completed',
        text: effectiveText,
        imageDataUrls: capturedImages.map((i) => i.dataUrl),
        createdAt: optimisticNow,
        completedAt: optimisticNow
      }
      const optimisticTurn: ConversationTurn = {
        id: optimisticTurnId,
        threadId: effectiveThreadId,
        status: 'running',
        items: [userItem],
        startedAt: optimisticNow
      }
      useConversationStore.getState().addOptimisticTurn(optimisticTurn)

      const inputParts: Array<{ type: string; text?: string; path?: string }> = []
      if (effectiveText.length > 0) {
        inputParts.push({ type: 'text', text: effectiveText })
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
          threadId: effectiveThreadId,
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
    } finally {
      sendInFlightRef.current = false
    }
  }, [customCommands, images, isRunning, isWaitingApproval, modelLoading, threadId, workspacePath, setPendingMessage, t])

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

  const onSelectFile = useCallback(
    (relativePath: string): void => {
      richRef.current?.insertFileTag(relativePath)
    },
    []
  )

  const onSelectCommand = useCallback((commandName: string): void => {
    richRef.current?.insertCommandTag(commandName)
  }, [])

  return (
    <div style={{ flexShrink: 0 }}>
      {pendingMessage && <PendingMessageIndicator message={pendingMessage} />}

      <ComposerShell
        dragOver={dragOver}
        dropLabel={t('composer.dropImage')}
        onDragOver={onDragOver}
        onDragLeave={onDragLeave}
        onDrop={onDrop}
        focused={editorFocused}
        imageStrip={
          <ImageStrip
            images={images}
            onRemove={(idx) => {
              setImages((prev) => prev.filter((_, i) => i !== idx))
            }}
          />
        }
        editor={
          <div style={{ position: 'relative' }}>
            <div style={{ position: 'relative', minWidth: 0 }}>
              <CommandSearchPopover
                query={slashQuery ?? ''}
                visible={showSlashPopover}
                loading={customCommandStatus === 'loading'}
                commands={customCommands}
                onSelect={onSelectCommand}
                onDismiss={() => {
                  setSlashDismissed(true)
                }}
              />
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
                chrome="minimal"
                disabled={isWaitingApproval}
                suppressSubmit={showMentionPopover || showSlashPopover || modelLoading}
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
                onSlashQuery={handleSlashQuery}
                onContentChange={() => {
                  setContentRevision((n) => n + 1)
                }}
                onFocusChange={setEditorFocused}
                onPasteImage={onPasteImage}
                onPasteTextOversized={() => {
                  addToast(
                    t('input.truncated', { max: MAX_TEXT_LENGTH.toLocaleString() }),
                    'warning'
                  )
                }}
              />
            </div>
          </div>
        }
        footerLeading={
          <div style={{ display: 'flex', alignItems: 'center', gap: '8px', minWidth: 0, flexWrap: 'wrap' }}>
            <ComposerModeSwitch
              value={threadMode}
              onToggle={() => {
                void toggleMode()
              }}
              agentLabel={t('composer.mode.agent')}
              planLabel={t('composer.mode.plan')}
            />

            <ModelPicker
              modelName={modelName}
              modelOptions={modelOptions}
              loading={modelLoading}
              unsupported={modelListUnsupportedEndpoint}
              disabled={modelDisabled}
              onChange={onModelChange}
              triggerStyle={composerModelPillStyle(
                modelDisabled || modelLoading ? 'var(--text-dimmed)' : 'var(--text-primary)',
                modelDisabled || modelLoading
              )}
            />
          </div>
        }
        footerAction={
          !isWaitingApproval ? (
            isRunning ? (
              <button
                type="button"
                onClick={stopTurn}
                title={t('composer.stopTitle')}
                aria-label={t('composer.stopAria')}
                style={{
                  ...composerActionButtonStyle,
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
                  ...composerActionButtonStyle,
                  backgroundColor: canSend ? '#f5f6f7' : 'color-mix(in srgb, var(--bg-primary) 92%, #ffffff 8%)',
                  color: canSend ? '#1f2328' : 'var(--text-dimmed)',
                  cursor: canSend ? 'pointer' : 'default'
                }}
              >
                <SendIcon />
              </button>
            )
          ) : (
            <div />
          )
        }
      />
    </div>
  )
}
