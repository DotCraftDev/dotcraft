import { useRef, useState, useCallback, useEffect, useMemo } from 'react'
import { useLocale, useT } from '../../contexts/LocaleContext'
import { useConversationStore } from '../../stores/conversationStore'
import { addToast } from '../../stores/toastStore'
import { useUIStore } from '../../stores/uiStore'
import { useConnectionStore } from '../../stores/connectionStore'
import { useCustomCommandCatalog } from '../../hooks/useCustomCommandCatalog'
import { useSkillsStore } from '../../stores/skillsStore'
import type { ComposerFileAttachment, ImageAttachment } from '../../types/conversation'
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

    if (isRunning) {
      if (images.length > 0) {
        addToast(t('input.imageAttachmentsQueued'), 'warning')
      }
      if (trimmed || files.length > 0) {
        const { inputParts } = buildComposerInputParts({ text: trimmed, segments, files })
        setPendingMessage({
          text: trimmed,
          inputParts,
          files: files.length > 0 ? [...files] : undefined
        })
      }
      richRef.current?.clear()
      setImages([])
      setFiles([])
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
  }, [files, images, isRunning, isWaitingApproval, modelLoading, threadId, workspacePath, setPendingMessage, t])

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
              disabled={modelDisabled}
              onChange={onModelChange}
              shortcut={ACTION_SHORTCUTS.selectModel}
              triggerStyle={composerModelPillStyle(
                modelDisabled || modelLoading ? 'var(--text-dimmed)' : 'var(--text-secondary)',
                modelDisabled || modelLoading
              )}
            />
            {!isWaitingApproval ? (
              isRunning ? (
                <ActionTooltip label={t('composer.stopTitle')} placement="top">
                  <button
                    type="button"
                    onClick={stopTurn}
                    aria-label={t('composer.stopAria')}
                    style={composerSendButtonStyle('enabled')}
                >
                    <StopIcon />
                  </button>
                </ActionTooltip>
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
