import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { DotCraftLogo } from '../ui/DotCraftLogo'
import { useConnectionStore } from '../../stores/connectionStore'
import { useThreadStore } from '../../stores/threadStore'
import { useUIStore } from '../../stores/uiStore'
import { addToast } from '../../stores/toastStore'
import type { ImageAttachment } from '../../types/conversation'
import { FileSearchPopover } from './FileSearchPopover'
import { ImageStrip } from './ImageStrip'
import { RichInputArea, type RichInputAreaHandle } from './RichInputArea'

interface ConversationWelcomeProps {
  workspacePath: string
}

interface Suggestion {
  icon: string
  title: string
  prompt: string
}

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

/**
 * Welcome state when the workspace is connected but no thread is selected.
 * Includes a bottom-pinned composer so users can start a conversation without
 * clicking New Thread first; quick-start cards prefill the composer.
 */
export function ConversationWelcome({ workspacePath }: ConversationWelcomeProps): JSX.Element {
  const t = useT()
  const [contentRevision, setContentRevision] = useState(0)
  const [images, setImages] = useState<ImageAttachment[]>([])
  const [dragOver, setDragOver] = useState(false)
  const [hoveredIdx, setHoveredIdx] = useState<number | null>(null)
  const [starting, setStarting] = useState(false)
  const [atQuery, setAtQuery] = useState<string | null>(null)
  const [mentionDismissed, setMentionDismissed] = useState(false)
  const sendInFlightRef = useRef(false)
  const richRef = useRef<RichInputAreaHandle>(null)
  const connectionStatus = useConnectionStore((s) => s.status)
  const dashboardUrl = useConnectionStore((s) => s.dashboardUrl)
  const { addThread, setActiveThreadId } = useThreadStore()

  const isConnected = connectionStatus === 'connected'
  const busy = starting || !isConnected
  const showMentionPopover = atQuery !== null && !mentionDismissed

  const suggestions: Suggestion[] = useMemo(
    () => [
      {
        icon: '📄',
        title: t('welcome.suggestion.explore'),
        prompt:
          'Give me a quick overview of this project: what it does, its structure, and where the main entry points are.'
      },
      {
        icon: '🐛',
        title: t('welcome.suggestion.bug'),
        prompt:
          'Scan the codebase for potential bugs, error-prone patterns, or unhandled edge cases and suggest fixes.'
      },
      {
        icon: '✨',
        title: t('welcome.suggestion.feature'),
        prompt:
          'Help me design and implement a new feature for this project. Describe what you want to build.'
      },
      {
        icon: '📝',
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

  const onSelectFile = useCallback((relativePath: string): void => {
    richRef.current?.insertFileTag(relativePath)
  }, [])

  useEffect(() => {
    if (isConnected) {
      richRef.current?.focus()
    }
  }, [isConnected])

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
      (!trimmed && images.length === 0) ||
      sendInFlightRef.current ||
      connectionStatus !== 'connected'
    ) {
      return
    }

    sendInFlightRef.current = true
    setStarting(true)
    const capturedImages = [...images]
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

      useUIStore.getState().setPendingWelcomeTurn({
        threadId: res.thread.id,
        text: trimmed,
        images: capturedImages.length > 0 ? capturedImages : undefined
      })
      addThread(res.thread)
      setActiveThreadId(res.thread.id)
      useUIStore.getState().setActiveMainView('conversation')
      richRef.current?.clear()
      setImages([])
    } catch (err) {
      console.error('Failed to start thread from welcome composer:', err)
    } finally {
      sendInFlightRef.current = false
      setStarting(false)
    }
  }, [images, connectionStatus, workspacePath, addThread, setActiveThreadId])

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

  function fillSuggestion(prompt: string): void {
    richRef.current?.setPlainText(prompt)
    setTimeout(() => richRef.current?.focus(), 0)
  }

  const canSend = useMemo(() => {
    const textLen = (richRef.current?.getText() ?? '').trim().length
    return (textLen > 0 || images.length > 0) && isConnected && !starting
  }, [contentRevision, images.length, isConnected, starting])

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
            {t('welcome.heroTitle')}
          </h1>
          <p style={{ fontSize: '14px', color: 'var(--text-secondary)', margin: 0, textAlign: 'center', maxWidth: '420px' }}>
            {isConnected
              ? t('welcomeComposer.hint.select')
              : t('welcomeComposer.hint.connecting')}
          </p>
          {isConnected && dashboardUrl ? (
            <p style={{ fontSize: '12px', color: 'var(--text-tertiary)', margin: '10px 0 0 0', textAlign: 'center', maxWidth: '420px' }}>
              <button
                type="button"
                onClick={() => void window.api.shell.openExternal(dashboardUrl)}
                style={{
                  background: 'none',
                  border: 'none',
                  color: 'var(--accent)',
                  cursor: 'pointer',
                  padding: 0,
                  font: 'inherit',
                  textDecoration: 'underline'
                }}
              >
                {t('welcome.openDashboardHint')}
              </button>
            </p>
          ) : null}
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
            {suggestions.map((s, idx) => (
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
          <div
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
            <div style={{ display: 'flex', alignItems: 'flex-end', gap: '8px' }}>
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
                disabled={busy}
                suppressSubmit={showMentionPopover}
                placeholder={
                  isConnected
                    ? t('welcomeComposer.placeholder.ask')
                    : t('composer.placeholder.connecting')
                }
                onSubmit={() => {
                  void sendFromWelcome()
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
            <button
              type="button"
              onClick={() => { void sendFromWelcome() }}
              disabled={!canSend}
              title={t('welcome.sendTitle')}
              aria-label={t('welcome.sendAria')}
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
