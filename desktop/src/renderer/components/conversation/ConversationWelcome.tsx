import { useCallback, useEffect, useMemo, useRef, useState, type ComponentType, type CSSProperties } from 'react'
import { BookText, Bug, FileText, Sparkles } from 'lucide-react'
import { useLocale, useT } from '../../contexts/LocaleContext'
import { useConnectionStore } from '../../stores/connectionStore'
import { useModelCatalogStore } from '../../stores/modelCatalogStore'
import { useThreadStore } from '../../stores/threadStore'
import { useUIStore } from '../../stores/uiStore'
import { useSkillsStore } from '../../stores/skillsStore'
import { addToast } from '../../stores/toastStore'
import { useCustomCommandCatalog } from '../../hooks/useCustomCommandCatalog'
import type { ComposerFileAttachment, ImageAttachment, ThreadMode } from '../../types/conversation'
import type { ComposerDraftSegment } from '../../types/composerDraft'
import type { ThreadSummary } from '../../types/thread'
import { parseJsonConfig } from '../../../shared/jsonConfig'
import {
  classifyDroppedComposerFiles,
  isImageFile,
  mergeComposerFileAttachments
} from '../../utils/composerAttachments'
import { CommandSearchPopover } from './CommandSearchPopover'
import { FileSearchPopover } from './FileSearchPopover'
import { AttachmentStrip } from './AttachmentStrip'
import { ComposerAttachmentMenu } from './ComposerAttachmentMenu'
import { RichInputArea, type RichInputAreaHandle } from './RichInputArea'
import { ModelPicker } from './ModelPicker'
import {
  ComposerModeSwitch,
  ComposerShell,
  SendIcon,
  composerSendButtonStyle,
  composerModelPillStyle
} from './ComposerShell'

interface ConversationWelcomeProps {
  workspacePath: string
}

interface Suggestion {
  icon: ComponentType<{ size?: number; strokeWidth?: number; style?: CSSProperties }>
  title: string
  prompt: string
}

const MAX_TEXT_LENGTH = 100_000
const MAX_IMAGES = 5
const MAX_IMAGE_BYTES = 10 * 1024 * 1024
const WELCOME_DRAFT_DEBOUNCE_MS = 250

/**
 * Welcome state when the workspace is connected but no thread is selected.
 * Keeps the composer centered in the page so users can start a conversation
 * without clicking New Thread first; quick-start rows prefill the composer.
 */
export function ConversationWelcome({ workspacePath }: ConversationWelcomeProps): JSX.Element {
  const t = useT()
  const [contentRevision, setContentRevision] = useState(0)
  const [images, setImages] = useState<ImageAttachment[]>([])
  const [files, setFiles] = useState<ComposerFileAttachment[]>([])
  const [dragOver, setDragOver] = useState(false)
  const [editorFocused, setEditorFocused] = useState(false)
  const [hoveredIdx, setHoveredIdx] = useState<number | null>(null)
  const [starting, setStarting] = useState(false)
  const [atQuery, setAtQuery] = useState<string | null>(null)
  const [mentionDismissed, setMentionDismissed] = useState(false)
  const [slashQuery, setSlashQuery] = useState<string | null>(null)
  const [slashDismissed, setSlashDismissed] = useState(false)
  /** Agent/plan before a thread exists; applied when the first thread is created. */
  const [welcomeMode, setWelcomeMode] = useState<ThreadMode>('agent')
  const [modelName, setModelName] = useState<string>('Default')
  const [modelApplying, setModelApplying] = useState(false)
  const sendInFlightRef = useRef(false)
  const skipDraftPersistRef = useRef(false)
  const draftHydratedRef = useRef(false)
  const latestDraftTextRef = useRef('')
  const latestDraftSegmentsRef = useRef<ComposerDraftSegment[]>([])
  const initialWelcomeDraftRef = useRef(useUIStore.getState().welcomeDraft)
  const richRef = useRef<RichInputAreaHandle>(null)
  const connectionStatus = useConnectionStore((s) => s.status)
  const capabilities = useConnectionStore((s) => s.capabilities)
  const locale = useLocale()
  const modelOptions = useModelCatalogStore((s) => s.modelOptions)
  const modelCatalogStatus = useModelCatalogStore((s) => s.status)
  const modelListUnsupportedEndpoint = useModelCatalogStore((s) => s.modelListUnsupportedEndpoint)
  const { addThread, setActiveThreadId } = useThreadStore()
  const setWelcomeDraft = useUIStore((s) => s.setWelcomeDraft)
  const clearWelcomeDraft = useUIStore((s) => s.clearWelcomeDraft)

  const isConnected = connectionStatus === 'connected'
  const busy = starting || !isConnected
  const showMentionPopover = atQuery !== null && !mentionDismissed
  const canUseCommandPicker = capabilities?.commandManagement === true
  const canUseSkillPicker = capabilities?.skillsManagement === true
  const canUseSlashPicker = canUseCommandPicker || canUseSkillPicker
  const showSlashPopover = slashQuery !== null && !slashDismissed && canUseSlashPicker
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
  const modelApiAvailable =
    isConnected &&
    capabilities?.modelCatalogManagement === true &&
    capabilities?.workspaceConfigManagement === true
  const modelLoading = modelApiAvailable && modelCatalogStatus === 'loading'
  const workspaceConfigPath = useMemo(() => {
    if (!workspacePath) return ''
    const normalized = workspacePath.replace(/[\\/]+$/, '')
    const sep = normalized.includes('\\') ? '\\' : '/'
    return `${normalized}${sep}.craft${sep}config.json`
  }, [workspacePath])

  const readWorkspaceConfig = useCallback(async (): Promise<Record<string, unknown>> => {
    if (!workspaceConfigPath) return {}
    const raw = await window.api.file.readFile(workspaceConfigPath)
    return parseJsonConfig<Record<string, unknown>>(raw, {})
  }, [workspaceConfigPath])

  const resolveModelFromConfig = useCallback((cfg: Record<string, unknown>): string => {
    const modelRaw = cfg.Model ?? cfg.model
    if (typeof modelRaw !== 'string') return 'Default'
    const trimmed = modelRaw.trim()
    if (trimmed.length === 0 || trimmed === 'Default') return 'Default'
    return trimmed
  }, [])

  const suggestions: Suggestion[] = useMemo(
    () => [
      {
        icon: FileText,
        title: t('welcome.suggestion.explore'),
        prompt:
          'Give me a quick overview of this project: what it does, its structure, and where the main entry points are.'
      },
      {
        icon: Bug,
        title: t('welcome.suggestion.bug'),
        prompt:
          'Scan the codebase for potential bugs, error-prone patterns, or unhandled edge cases and suggest fixes.'
      },
      {
        icon: Sparkles,
        title: t('welcome.suggestion.feature'),
        prompt:
          'Help me design and implement a new feature for this project. Describe what you want to build.'
      },
      {
        icon: BookText,
        title: t('welcome.suggestion.docs'),
        prompt:
          'Generate clear documentation for this codebase: README sections, inline comments, and API docs.'
      }
    ],
    [t]
  )

  const handleAtQuery = useCallback((q: string | null): void => {
    setAtQuery(q)
    if (q !== null) setMentionDismissed(false)
  }, [])

  const handleSlashQuery = useCallback((q: string | null): void => {
    setSlashQuery(q)
    if (q !== null) setSlashDismissed(false)
  }, [])

  const onSelectFile = useCallback((relativePath: string): void => {
    richRef.current?.insertFileTag(relativePath)
  }, [])

  const onSelectCommand = useCallback((commandName: string): void => {
    richRef.current?.insertCommandTag(commandName)
  }, [])

  const onSelectSkill = useCallback((skillName: string): void => {
    richRef.current?.insertSkillTag(skillName)
  }, [])

  useEffect(() => {
    if (isConnected) {
      richRef.current?.focus()
    }
  }, [isConnected])

  useEffect(() => {
    if (!canUseSkillPicker) return
    void fetchSkills()
  }, [canUseSkillPicker, fetchSkills])

  useEffect(() => {
    const welcomeDraft = initialWelcomeDraftRef.current
    if (!welcomeDraft) {
      draftHydratedRef.current = true
      return
    }

    richRef.current?.setContent({
      text: welcomeDraft.text,
      segments: welcomeDraft.segments
    })
    latestDraftTextRef.current = welcomeDraft.text
    latestDraftSegmentsRef.current = [...(welcomeDraft.segments ?? [])]
    setImages(welcomeDraft.images)
    setFiles([...(welcomeDraft.files ?? [])])
    setWelcomeMode(welcomeDraft.mode)
    setModelName(welcomeDraft.model || 'Default')
    setContentRevision((n) => n + 1)
    draftHydratedRef.current = true
  }, [])

  useEffect(() => {
    let disposed = false
    const loadModelName = async (): Promise<void> => {
      if (initialWelcomeDraftRef.current) return
      if (!workspaceConfigPath) {
        setModelName('Default')
        return
      }

      try {
        const cfg = await readWorkspaceConfig()
        if (disposed) return
        setModelName(resolveModelFromConfig(cfg))
      } catch {
        if (!disposed) setModelName('Default')
      }
    }

    void loadModelName()
    return () => {
      disposed = true
    }
  }, [
    readWorkspaceConfig,
    resolveModelFromConfig,
    workspaceConfigPath
  ])

  const flushWelcomeDraft = useCallback((): void => {
    if (skipDraftPersistRef.current) return
    const text = richRef.current?.getText() ?? latestDraftTextRef.current
    const segments = richRef.current?.getSegments() ?? latestDraftSegmentsRef.current
    const hasText = text.trim().length > 0
    const hasImages = images.length > 0
    const hasFiles = files.length > 0
    const model = modelName || 'Default'
    const hasCustomSettings = welcomeMode !== 'agent' || model !== 'Default'

    if (!hasText && !hasImages && !hasFiles && !hasCustomSettings) {
      clearWelcomeDraft()
      return
    }

    setWelcomeDraft({
      text,
      segments: [...segments],
      images: [...images],
      files: [...files],
      mode: welcomeMode,
      model
    })
  }, [clearWelcomeDraft, files, images, modelName, setWelcomeDraft, welcomeMode])

  useEffect(() => {
    if (!draftHydratedRef.current) return
    const timer = setTimeout(() => {
      flushWelcomeDraft()
    }, WELCOME_DRAFT_DEBOUNCE_MS)
    return () => {
      clearTimeout(timer)
    }
  }, [contentRevision, files, flushWelcomeDraft, images, modelName, welcomeMode])

  useEffect(() => {
    return () => {
      if (!draftHydratedRef.current || skipDraftPersistRef.current) return
      flushWelcomeDraft()
    }
  }, [flushWelcomeDraft])

  const handleModelChange = useCallback(
    async (nextModel: string): Promise<void> => {
      if (!workspaceConfigPath || !nextModel || nextModel === modelName) return
      setModelApplying(true)
      const previousModel = modelName
      setModelName(nextModel)
      try {
        await window.api.appServer.sendRequest('workspace/config/update', {
          model: nextModel === 'Default' ? null : nextModel
        })
      } catch (err) {
        const msg = err instanceof Error ? err.message : String(err)
        setModelName(previousModel)
        addToast(`Failed to save model: ${msg}`, 'error')
      } finally {
        setModelApplying(false)
      }
    },
    [modelName, workspaceConfigPath]
  )

  const saveDataUrlAsTemp = useCallback(
    async (dataUrl: string, fileName: string, mimeType: string): Promise<void> => {
      const baseLen = dataUrl.split(',')[1]?.length ?? 0
      const approxBytes = Math.floor((baseLen * 3) / 4)
      if (approxBytes > MAX_IMAGE_BYTES) {
        addToast(
          t('welcomeComposer.imageTooLarge', { mb: MAX_IMAGE_BYTES / 1024 / 1024 }),
          'warning'
        )
        return
      }
      if (images.length >= MAX_IMAGES) {
        addToast(t('welcomeComposer.maxImages', { max: MAX_IMAGES }), 'warning')
        return
      }
      try {
        const { path } = await window.api.workspace.saveImageToTemp({ dataUrl, fileName })
        setImages((prev) => [...prev, { tempPath: path, dataUrl, fileName, mimeType }])
      } catch (e) {
        const msg = e instanceof Error ? e.message : String(e)
        addToast(t('welcomeComposer.saveImageFailed', { error: msg }), 'error')
      }
    },
    [images.length, t]
  )

  const sendFromWelcome = useCallback(async (): Promise<void> => {
    const trimmed = (richRef.current?.getText() ?? '').trim()
    if (
      (!trimmed && images.length === 0 && files.length === 0) ||
      sendInFlightRef.current ||
      connectionStatus !== 'connected' ||
      modelLoading
    ) {
      return
    }

    sendInFlightRef.current = true
    setStarting(true)
    const capturedImages = [...images]
    const capturedFiles = [...files]
    const capturedMode = welcomeMode
    const capturedModel = modelName === 'Default' ? '' : modelName
    try {
      const res = await window.api.appServer.sendRequest('thread/start', {
        identity: {
          channelName: 'dotcraft-desktop',
          userId: 'local',
          channelContext: `workspace:${workspacePath}`,
          workspacePath
        },
        historyMode: 'server'
      }) as { thread: ThreadSummary }

      skipDraftPersistRef.current = true
      latestDraftTextRef.current = ''
      latestDraftSegmentsRef.current = []
      clearWelcomeDraft()
      useUIStore.getState().setPendingWelcomeTurn({
        threadId: res.thread.id,
        text: trimmed,
        images: capturedImages.length > 0 ? capturedImages : undefined,
        files: capturedFiles.length > 0 ? capturedFiles : undefined,
        mode: capturedMode,
        model: capturedModel
      })
      addThread(res.thread)
      setActiveThreadId(res.thread.id)
      useUIStore.getState().setActiveMainView('conversation')
      richRef.current?.clear()
      setImages([])
      setFiles([])
    } catch (err) {
      console.error('Failed to start thread from welcome composer:', err)
    } finally {
      sendInFlightRef.current = false
      setStarting(false)
    }
  }, [
    files,
    images,
    connectionStatus,
    workspacePath,
    addThread,
    setActiveThreadId,
    welcomeMode,
    modelName,
    modelLoading,
    clearWelcomeDraft
  ])

  const onPasteImage = useCallback(
    (file: File): void => {
      if (!isImageFile(file)) return
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

  const toggleWelcomeMode = useCallback((): void => {
    setWelcomeMode((m) => (m === 'agent' ? 'plan' : 'agent'))
  }, [])

  function fillSuggestion(prompt: string): void {
    richRef.current?.setPlainText(prompt)
    setTimeout(() => richRef.current?.focus(), 0)
  }

  const canSend = useMemo(() => {
    const textLen = (richRef.current?.getText() ?? '').trim().length
    return (textLen > 0 || images.length > 0 || files.length > 0) && isConnected && !starting && !modelLoading
  }, [contentRevision, files.length, images.length, isConnected, starting, modelLoading])

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
      <div
        style={{
          flex: 1,
          minHeight: 0,
          overflowY: 'auto',
          display: 'flex',
          justifyContent: 'center',
          padding: '48px 24px'
        }}
      >
        <div
          style={{
            width: '100%',
            maxWidth: '720px',
            margin: 'auto',
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'center'
          }}
        >
          <div
            style={{
              display: 'flex',
              flexDirection: 'column',
              alignItems: 'center',
              gap: '8px',
              marginBottom: '18px'
            }}
          >
            <h1
              style={{
                fontSize: '26px',
                fontWeight: 600,
                color: 'var(--text-primary)',
                margin: 0,
                letterSpacing: '-0.4px'
              }}
            >
              {t('welcome.heroTitle')}
            </h1>
            <p style={{ fontSize: '13px', color: 'var(--text-secondary)', margin: 0, textAlign: 'center', maxWidth: '520px' }}>
              {isConnected
                ? t('welcomeComposer.hint.select')
                : t('welcomeComposer.hint.connecting')}
            </p>
          </div>

          <div style={{ width: '100%' }}>
            <ComposerShell
              dragOver={dragOver}
              dropLabel={t('composer.dropImage')}
              onDragOver={onDragOver}
              onDragLeave={onDragLeave}
              onDrop={onDrop}
              opacity={starting ? 0.65 : 1}
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
                      disabled={busy}
                      suppressSubmit={showMentionPopover || showSlashPopover || modelLoading}
                      onToggleModeShortcut={toggleWelcomeMode}
                      placeholder={
                        isConnected
                          ? t('welcomeComposer.placeholder.ask')
                          : t('composer.placeholder.connecting')
                      }
                      onSubmit={() => {
                        void sendFromWelcome()
                      }}
                      onAtQuery={handleAtQuery}
                      onSlashQuery={handleSlashQuery}
                      onContentChange={() => {
                        latestDraftTextRef.current = richRef.current?.getText() ?? latestDraftTextRef.current
                        latestDraftSegmentsRef.current =
                          richRef.current?.getSegments() ?? latestDraftSegmentsRef.current
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
                    value={welcomeMode}
                    onToggle={() => {
                      toggleWelcomeMode()
                    }}
                    agentLabel={t('composer.mode.agent')}
                    planLabel={t('composer.mode.plan')}
                    title={t('composer.modeTitle', {
                      mode: welcomeMode === 'agent' ? t('composer.mode.agent') : t('composer.mode.plan')
                    })}
                  />

                  <ModelPicker
                    modelName={modelName}
                    modelOptions={modelApiAvailable ? modelOptions : []}
                    loading={modelLoading}
                    unsupported={modelListUnsupportedEndpoint}
                    disabled={modelApplying || starting}
                    onChange={(nextModel) => {
                      void handleModelChange(nextModel)
                    }}
                    triggerStyle={composerModelPillStyle(
                      modelApplying || starting || modelLoading ? 'var(--text-dimmed)' : 'var(--text-secondary)',
                      modelApplying || starting || modelLoading
                    )}
                  />
                </div>
              }
              footerAction={
                <button
                  type="button"
                  onClick={() => { void sendFromWelcome() }}
                  disabled={!canSend}
                  title={t('welcome.sendTitle')}
                  aria-label={t('welcome.sendAria')}
                  style={composerSendButtonStyle(canSend ? 'enabled' : 'disabled')}
                >
                  <SendIcon />
                </button>
              }
            />
          </div>

          <div
            style={{
              width: '100%',
              display: 'flex',
              flexDirection: 'column',
              paddingLeft: '14px',
              marginTop: '-4px'
            }}
          >
            {suggestions.map((s, idx) => {
              const Icon = s.icon
              return (
                <button
                  key={idx}
                  type="button"
                  onClick={() => { fillSuggestion(s.prompt) }}
                  disabled={busy}
                  onMouseEnter={() => setHoveredIdx(idx)}
                  onMouseLeave={() => setHoveredIdx(null)}
                  style={{
                    display: 'flex',
                    alignItems: 'center',
                    gap: '8px',
                    width: 'fit-content',
                    padding: '6px 10px',
                    margin: '1px 0',
                    background: hoveredIdx === idx ? 'var(--bg-tertiary)' : 'transparent',
                    border: 'none',
                    borderRadius: '8px',
                    color: 'var(--text-secondary)',
                    cursor: busy ? 'default' : 'pointer',
                    textAlign: 'left',
                    fontSize: '13px',
                    fontWeight: 400,
                    lineHeight: 1.4,
                    transition: 'background-color 120ms ease, color 120ms ease',
                    opacity: busy ? 0.7 : 1
                  }}
                  onFocus={(e) => {
                    e.currentTarget.style.color = 'var(--text-primary)'
                  }}
                  onBlur={(e) => {
                    e.currentTarget.style.color = 'var(--text-secondary)'
                  }}
                  aria-label={s.title}
                >
                  <Icon size={16} strokeWidth={1.8} style={{ flexShrink: 0 }} />
                  <span>{s.title}</span>
                </button>
              )
            })}
          </div>
        </div>
      </div>
    </div>
  )
}
