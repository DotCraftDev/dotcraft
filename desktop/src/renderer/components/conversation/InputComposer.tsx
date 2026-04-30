import { useRef, useState, useCallback, useEffect, useMemo, type CSSProperties } from 'react'
import { useLocale, useT } from '../../contexts/LocaleContext'
import { useConversationStore } from '../../stores/conversationStore'
import { addToast } from '../../stores/toastStore'
import { useUIStore } from '../../stores/uiStore'
import { useConnectionStore } from '../../stores/connectionStore'
import { useCustomCommandCatalog } from '../../hooks/useCustomCommandCatalog'
import { useSkillsStore } from '../../stores/skillsStore'
import type { ComposerFileAttachment, ImageAttachment, QueuedTurnInput } from '../../types/conversation'
import { startTurnWithOptimisticUI } from '../../utils/startTurn'
import { buildComposerInputParts } from '../../utils/composeInputParts'
import {
  classifyDroppedComposerFiles,
  extForFile,
  isImageFile,
  mergeComposerFileAttachments
} from '../../utils/composerAttachments'
import { PendingMessageIndicator } from './PendingMessageIndicator'
import { RichInputArea, type RichInputAreaHandle } from './RichInputArea'
import { AttachmentStrip } from './AttachmentStrip'
import { FileSearchPopover } from './FileSearchPopover'
import { CommandSearchPopover } from './CommandSearchPopover'
import { ModelPicker } from './ModelPicker'
import { ComposerAttachmentMenu } from './ComposerAttachmentMenu'
import { ContextUsageRing } from './ContextUsageRing'
import { ApprovalPolicyPicker } from './ApprovalPolicyPicker'
import {
  ComposerModeSwitch,
  ComposerShell,
  SendIcon,
  StopIcon,
  composerSendButtonStyle,
  composerModelPillStyle
} from './ComposerShell'
import { ActionTooltip } from '../ui/ActionTooltip'
import { ACTION_SHORTCUTS } from '../ui/shortcutKeys'

const MAX_TEXT_LENGTH = 100_000
const MAX_IMAGES = 5
const MAX_IMAGE_BYTES = 10 * 1024 * 1024

interface InputComposerProps {
  threadId: string
  workspacePath: string
  modelName?: string
  modelOptions?: string[]
  modelLoading?: boolean
  modelDisabled?: boolean
  /** When true, model/list reported that the upstream API does not support listing; show a read-only label. */
  modelListUnsupportedEndpoint?: boolean
  modelCatalogError?: boolean
  modelCatalogErrorMessage?: string | null
  onModelChange?: (model: string) => void
  onModelCatalogRetry?: () => void
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
  modelCatalogError = false,
  modelCatalogErrorMessage = null,
  onModelChange,
  onModelCatalogRetry
}: InputComposerProps): JSX.Element {
  const t = useT()
  const [images, setImages] = useState<ImageAttachment[]>([])
  const [files, setFiles] = useState<ComposerFileAttachment[]>([])
  const [atQuery, setAtQuery] = useState<string | null>(null)
  const [mentionDismissed, setMentionDismissed] = useState(false)
  const [slashQuery, setSlashQuery] = useState<string | null>(null)
  const [slashDismissed, setSlashDismissed] = useState(false)
  const [skillQuery, setSkillQuery] = useState<string | null>(null)
  const [skillDismissed, setSkillDismissed] = useState(false)
  const [dragOver, setDragOver] = useState(false)
  const [editorFocused, setEditorFocused] = useState(false)
  /** Bumps on rich-input edits so `canSend` re-evaluates from ref (contentEditable has no React state). */
  const [contentRevision, setContentRevision] = useState(0)
  const richRef = useRef<RichInputAreaHandle>(null)
  const sendInFlightRef = useRef(false)
  const pendingModeChangeRef = useRef<Promise<void> | null>(null)

  const turnStatus = useConversationStore((s) => s.turnStatus)
  const pendingMessage = useConversationStore((s) => s.pendingMessage)
  const queuedInputs = useConversationStore((s) => s.queuedInputs)
  const threadMode = useConversationStore((s) => s.threadMode)
  const setThreadMode = useConversationStore((s) => s.setThreadMode)
  const composerPrefill = useUIStore((s) => s.composerPrefill)
  const capabilities = useConnectionStore((s) => s.capabilities)
  const locale = useLocale()

  const isRunning = turnStatus === 'running'
  const isWaitingApproval = turnStatus === 'waitingApproval'
  const canUseCommandPicker = capabilities?.commandManagement === true
  const canUseSkillPicker = capabilities?.skillsManagement === true
  const canUseSlashPicker = canUseCommandPicker || canUseSkillPicker

  const showMentionPopover = atQuery !== null && !mentionDismissed
  const showSlashPopover = slashQuery !== null && !slashDismissed && canUseSlashPicker
  const showSkillPopover = skillQuery !== null && !skillDismissed && canUseSkillPicker
  const { commands: customCommands, status: customCommandStatus } = useCustomCommandCatalog({
    enabled: canUseCommandPicker,
    locale
  })
  const skills = useSkillsStore((s) => s.skills)
  const skillsLoading = useSkillsStore((s) => s.loading)
  const fetchSkills = useSkillsStore((s) => s.fetchSkills)
  const availableSkills = useMemo(
    () =>
      skills
        .filter((skill) => skill.available)
        .map((skill) => ({
          name: skill.name.replace(/^\/+/, ''),
          description: skill.description
        }))
        .sort((a, b) => a.name.localeCompare(b.name)),
    [skills]
  )
  const richRefCatalog = useMemo(
    () => ({
      commands: customCommands,
      skills: availableSkills
    }),
    [availableSkills, customCommands]
  )

  useEffect(() => {
    if (!canUseSkillPicker) return
    void fetchSkills()
  }, [canUseSkillPicker, fetchSkills])

  const handleAtQuery = useCallback((q: string | null): void => {
    setAtQuery(q)
    if (q !== null) setMentionDismissed(false)
  }, [])

  const handleSlashQuery = useCallback((q: string | null): void => {
    setSlashQuery(q)
    if (q !== null) setSlashDismissed(false)
  }, [])

  const handleSkillQuery = useCallback((q: string | null): void => {
    setSkillQuery(q)
    if (q !== null) setSkillDismissed(false)
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

  const attachImages = useCallback((picked: File[]): void => {
    for (const file of picked) {
      onPasteImage(file)
    }
  }, [onPasteImage])

  const onDrop = useCallback(
    (e: React.DragEvent): void => {
      e.preventDefault()
      e.stopPropagation()
      setDragOver(false)
      const { imageFiles, fileAttachments, skippedCount } = classifyDroppedComposerFiles(e.dataTransfer)
      attachImages(imageFiles)
      if (fileAttachments.length > 0) {
        setFiles((prev) => mergeComposerFileAttachments(prev, fileAttachments))
      }
      if (skippedCount > 0) {
        addToast(t('input.dropItemsSkipped', { count: skippedCount }), 'warning')
      }
    },
    [attachImages, t]
  )

  const sendMessage = useCallback(async () => {
    const text = richRef.current?.getText() ?? ''
    const segments = richRef.current?.getSegments() ?? []
    const trimmed = text.trim()
    if (!trimmed && images.length === 0 && files.length === 0) return
    if (isWaitingApproval) return
    if (modelLoading) return

    if (pendingModeChangeRef.current) {
      await pendingModeChangeRef.current
    }

    if (isRunning) {
      if (sendInFlightRef.current) return
      sendInFlightRef.current = true
      try {
        if (trimmed || files.length > 0 || images.length > 0) {
          const { inputParts } = buildComposerInputParts({ text: trimmed, segments, files, images })
          await window.api.appServer.sendRequest('turn/enqueue', {
            threadId,
            input: inputParts,
            sender: undefined
          })
        }
        richRef.current?.clear()
        setImages([])
        setFiles([])
      } catch (err) {
        console.error('turn/enqueue failed:', err)
        addToast(err instanceof Error ? err.message : String(err), 'error')
      } finally {
        sendInFlightRef.current = false
      }
      return
    }

    if (sendInFlightRef.current) return
    sendInFlightRef.current = true
    try {
      const capturedImages = [...images]
      const capturedFiles = [...files]
      richRef.current?.clear()
      setImages([])
      setFiles([])
      await startTurnWithOptimisticUI({
        threadId,
        workspacePath,
        text: trimmed,
        segments,
        images: capturedImages,
        files: capturedFiles,
        fallbackThreadName: t('toast.imageMessage'),
        fileFallbackThreadName: t('toast.fileReferenceMessage'),
        attachmentFallbackThreadName: t('toast.attachmentMessage')
      })
    } catch (err) {
      console.error('turn/start failed:', err)
      addToast(err instanceof Error ? err.message : String(err), 'error')
    } finally {
      sendInFlightRef.current = false
    }
  }, [files, images, isRunning, isWaitingApproval, modelLoading, threadId, workspacePath, t])

  const removeQueuedInput = useCallback(async (queuedInputId: string): Promise<void> => {
    try {
      const res = await window.api.appServer.sendRequest('turn/queue/remove', { threadId, queuedInputId }) as {
        queuedInputs?: unknown[]
      }
      useConversationStore.getState().setQueuedInputs((res.queuedInputs ?? []) as QueuedTurnInput[])
    } catch (err) {
      addToast(err instanceof Error ? err.message : String(err), 'error')
    }
  }, [threadId])

  const steerQueuedInput = useCallback(async (queuedInputId: string): Promise<void> => {
    const state = useConversationStore.getState()
    const activeTurnId = state.activeTurnId
    const queued = state.queuedInputs.find((item) => item.id === queuedInputId)
    if (!activeTurnId || !queued) return
    if (queued.status === 'guidancePending') return
    try {
      const res = await window.api.appServer.sendRequest('turn/steer', {
        threadId,
        expectedTurnId: activeTurnId,
        queuedInputId
      }) as { queuedInputs?: unknown[] }
      useConversationStore.getState().setQueuedInputs((res.queuedInputs ?? []) as QueuedTurnInput[])
    } catch (err) {
      addToast(err instanceof Error ? err.message : String(err), 'error')
    }
  }, [threadId])

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
    if (pendingModeChangeRef.current) return

    const previousMode = useConversationStore.getState().threadMode
    const newMode = previousMode === 'agent' ? 'plan' : 'agent'
    setThreadMode(newMode)
    const request = window.api.appServer
      .sendRequest('thread/mode/set', {
        threadId,
        mode: newMode
      })
      .catch((err) => {
        console.error('thread/mode/set failed:', err)
        setThreadMode(previousMode)
        addToast(
          t('composer.modeSwitchFailed', {
            error: err instanceof Error ? err.message : String(err)
          }),
          'error'
        )
      })
      .finally(() => {
        if (pendingModeChangeRef.current === request) {
          pendingModeChangeRef.current = null
        }
      })

    pendingModeChangeRef.current = request
    await request
  }

  const canSend = useMemo(() => {
    const textLen = (richRef.current?.getText() ?? '').trim().length
    return (textLen > 0 || images.length > 0 || files.length > 0) && !isWaitingApproval && !modelLoading
  }, [contentRevision, files.length, images.length, isWaitingApproval, modelLoading])

  const addPickedFiles = useCallback((picked: Array<{ path: string; fileName: string }>): void => {
    if (picked.length === 0) return
    setFiles((prev) => mergeComposerFileAttachments(prev, picked))
  }, [])

  const pickFiles = useCallback(async (): Promise<void> => {
    try {
      const picked = await window.api.workspace.pickFiles()
      addPickedFiles(picked)
    } catch (err) {
      const msg = err instanceof Error ? err.message : String(err)
      addToast(t('input.pickFilesFailed', { error: msg }), 'error')
    }
  }, [addPickedFiles, t])

  const onSelectFile = useCallback(
    (relativePath: string): void => {
      richRef.current?.insertFileTag(relativePath)
    },
    []
  )

  const onSelectCommand = useCallback((commandName: string): void => {
    richRef.current?.insertCommandTag(commandName)
  }, [])

  const onSelectSkill = useCallback((skillName: string): void => {
    richRef.current?.insertSkillTag(skillName)
  }, [])

  return (
    <div style={{ flexShrink: 0 }}>
      {pendingMessage && <PendingMessageIndicator message={pendingMessage} />}
      {queuedInputs.length > 0 && (
        <QueuedInputList
          queuedInputs={queuedInputs}
          onSteer={(id) => { void steerQueuedInput(id) }}
          onRemove={(id) => { void removeQueuedInput(id) }}
        />
      )}

      <ComposerShell
        dragOver={dragOver}
        dropLabel={t('composer.dropImage')}
        onDragOver={onDragOver}
        onDragLeave={onDragLeave}
        onDrop={onDrop}
        focused={editorFocused}
        attachmentStrip={
          <AttachmentStrip
            images={images}
            files={files}
            onRemoveImage={(idx) => {
              setImages((prev) => prev.filter((_, i) => i !== idx))
            }}
            onRemoveFile={(idx) => {
              setFiles((prev) => prev.filter((_, i) => i !== idx))
            }}
            removeImageLabel={t('composer.removeImageAria')}
            removeFileLabel={t('composer.removeFileAria')}
          />
        }
        editor={
          <div style={{ position: 'relative' }}>
            <div style={{ position: 'relative', minWidth: 0 }}>
              <CommandSearchPopover
                query={slashQuery ?? ''}
                visible={showSlashPopover}
                loading={customCommandStatus === 'loading' || skillsLoading}
                commands={customCommands}
                skills={availableSkills}
                onSelectCommand={onSelectCommand}
                onSelectSkill={onSelectSkill}
                onDismiss={() => {
                  setSlashDismissed(true)
                }}
              />
              <CommandSearchPopover
                query={skillQuery ?? ''}
                visible={showSkillPopover}
                loading={skillsLoading}
                commands={[]}
                skills={availableSkills}
                onSelectCommand={() => {}}
                onSelectSkill={onSelectSkill}
                onDismiss={() => {
                  setSkillDismissed(true)
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
                suppressSubmit={showMentionPopover || showSlashPopover || showSkillPopover || modelLoading}
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
                onSkillQuery={handleSkillQuery}
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
                refCatalog={richRefCatalog}
              />
            </div>
          </div>
        }
        footerLeading={
          <div style={{ display: 'flex', alignItems: 'center', gap: '10px', minWidth: 0, flexWrap: 'wrap' }}>
            <ComposerAttachmentMenu
              title={t('composer.attachFileTitle')}
              ariaLabel={t('composer.attachFileAria')}
              attachImageLabel={t('composer.attachImage')}
              referenceFileLabel={t('composer.referenceFile')}
              onAttachImages={attachImages}
              onReferenceFiles={() => {
                void pickFiles()
              }}
            />

            <ComposerModeSwitch
              value={threadMode}
              onToggle={() => {
                void toggleMode()
              }}
              agentLabel={t('composer.mode.agent')}
              planLabel={t('composer.mode.plan')}
              shortcut={ACTION_SHORTCUTS.toggleMode}
              title={t('composer.modeTitle', {
                mode: threadMode === 'agent' ? t('composer.mode.agent') : t('composer.mode.plan')
              })}
            />

            <ApprovalPolicyPicker threadId={threadId} disabled={isRunning || isWaitingApproval} />
          </div>
        }
        footerAction={
          <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
            <ContextUsageRing />
            <ModelPicker
              modelName={modelName}
              modelOptions={modelOptions}
              loading={modelLoading}
              unsupported={modelListUnsupportedEndpoint}
              errorMessage={modelCatalogError ? (modelCatalogErrorMessage || t('composer.modelListError')) : null}
              disabled={modelDisabled}
              onChange={onModelChange}
              onRetry={onModelCatalogRetry}
              shortcut={ACTION_SHORTCUTS.selectModel}
              triggerStyle={composerModelPillStyle(
                modelDisabled || modelLoading ? 'var(--text-dimmed)' : 'var(--text-secondary)',
                modelDisabled || modelLoading
              )}
            />
            {!isWaitingApproval ? (
              isRunning ? (
                canSend ? (
                  <ActionTooltip label={t('composer.queueSendTitle')} placement="top">
                    <button
                      type="button"
                      onClick={() => {
                        void sendMessage()
                      }}
                      aria-label={t('composer.queueSendAria')}
                      style={composerSendButtonStyle('enabled')}
                    >
                      <SendIcon />
                    </button>
                  </ActionTooltip>
                ) : (
                  <ActionTooltip
                    label={t('composer.stopTitle')}
                    shortcut={ACTION_SHORTCUTS.cancel}
                    placement="top"
                  >
                    <button
                      type="button"
                      onClick={stopTurn}
                      aria-label={t('composer.stopAria')}
                      style={composerSendButtonStyle('enabled')}
                    >
                      <StopIcon />
                    </button>
                  </ActionTooltip>
                )
              ) : (
                <ActionTooltip
                  label={t('composer.sendAriaAlt')}
                  shortcut={canSend ? ACTION_SHORTCUTS.send : undefined}
                  placement="top"
                >
                  <button
                    type="button"
                    onClick={() => {
                      void sendMessage()
                    }}
                    disabled={!canSend}
                    aria-label={t('composer.sendAriaAlt')}
                    style={composerSendButtonStyle(canSend ? 'enabled' : 'disabled')}
                  >
                    <SendIcon />
                  </button>
                </ActionTooltip>
              )
            ) : null}
          </div>
        }
      />
    </div>
  )
}

function QueuedInputList({
  queuedInputs,
  onSteer,
  onRemove
}: {
  queuedInputs: QueuedTurnInput[]
  onSteer: (id: string) => void
  onRemove: (id: string) => void
}): JSX.Element {
  const t = useT()
  return (
    <div
      style={{
        padding: '0 18px 4px',
        display: 'flex',
        flexDirection: 'column',
        gap: '6px'
      }}
    >
      {queuedInputs.map((item) => {
        const label = summarizeQueuedInput(item, t)
        const isGuidancePending = item.status === 'guidancePending'
        return (
          <div
            key={item.id}
            style={{
              display: 'grid',
              gridTemplateColumns: 'auto minmax(0, 1fr) auto auto',
              alignItems: 'center',
              gap: '8px',
              minHeight: '30px',
              padding: '5px 8px',
              borderRadius: '8px',
              background: 'color-mix(in srgb, var(--bg-secondary) 86%, var(--bg-primary))',
              color: 'var(--text-secondary)',
              fontSize: '12px'
            }}
          >
            <span
              aria-hidden
              style={{
                width: '7px',
                height: '7px',
                borderRadius: '999px',
                background: isGuidancePending ? 'var(--accent)' : 'var(--warning)'
              }}
            />
            <span
              title={label}
              style={{
                minWidth: 0,
                overflow: 'hidden',
                whiteSpace: 'nowrap',
                textOverflow: 'ellipsis'
              }}
            >
              {label}
            </span>
            <button
              type="button"
              onClick={() => onSteer(item.id)}
              disabled={isGuidancePending}
              style={{
                ...queuedTextButtonStyle,
                opacity: isGuidancePending ? 0.55 : 1,
                cursor: isGuidancePending ? 'default' : 'pointer'
              }}
            >
              {isGuidancePending ? t('composer.queueGuidancePending') : t('composer.queueGuide')}
            </button>
            <button
              type="button"
              onClick={() => onRemove(item.id)}
              aria-label={t('composer.queueRemove')}
              style={queuedTextButtonStyle}
            >
              {t('composer.queueRemove')}
            </button>
          </div>
        )
      })}
    </div>
  )
}

const queuedTextButtonStyle: CSSProperties = {
  border: 'none',
  background: 'transparent',
  color: 'var(--text-dimmed)',
  cursor: 'pointer',
  fontSize: '12px',
  padding: '2px 4px'
}

function summarizeQueuedInput(
  item: QueuedTurnInput,
  t: (key: string, vars?: Record<string, string | number>) => string
): string {
  const text = item.displayText?.trim()
  if (text) return text.length > 90 ? `${text.slice(0, 90)}...` : text
  const parts = item.nativeInputParts ?? item.materializedInputParts ?? []
  const files = parts.filter((part) => part.type === 'fileRef').length
  const images = parts.filter((part) => part.type === 'image' || part.type === 'localImage').length
  const labels: string[] = []
  if (files > 0) {
    labels.push(t(files === 1 ? 'composer.queueFileCountOne' : 'composer.queueFileCountMany', { count: files }))
  }
  if (images > 0) {
    labels.push(t(images === 1 ? 'composer.queueImageCountOne' : 'composer.queueImageCountMany', { count: images }))
  }
  return labels.length > 0 ? labels.join(', ') : t('composer.queueFallbackLabel')
}
