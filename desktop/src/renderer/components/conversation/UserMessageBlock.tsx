import { useEffect, useRef, useState } from 'react'
import { Bot, FileText, Pencil, Sparkle, Terminal } from 'lucide-react'
import { useLocale, useT } from '../../contexts/LocaleContext'
import { translate } from '../../../shared/locales'
import { useConversationStore } from '../../stores/conversationStore'
import { useThreadStore } from '../../stores/threadStore'
import { useUIStore } from '../../stores/uiStore'
import { useCronStore } from '../../stores/cronStore'
import { ImageLightbox } from './ImageLightbox'
import { MessageCopyButton } from './MessageCopyButton'
import { parseUserMessageSegments, segmentsFromNativeInputParts } from './parseUserMessageSegments'
import type { ConversationItem, InputPart, UserMessageImageRef } from '../../types/conversation'
import { openImagePathInViewer } from '../../utils/conversationDeepLink'
import { ActionTooltip } from '../ui/ActionTooltip'

const imageDataUrlCache = new Map<string, string>()

interface UserMessageBlockProps {
  text: string
  nativeInputParts?: InputPart[]
  imageDataUrls?: string[]
  images?: UserMessageImageRef[]
  triggerKind?: ConversationItem['triggerKind']
  triggerLabel?: string
  triggerRefId?: string
  editable?: boolean
  onEdit?: () => void
  editing?: boolean
  editText?: string
  editSubmitting?: boolean
  editSubmitDisabled?: boolean
  onEditTextChange?: (text: string) => void
  onCancelEdit?: () => void
  onSubmitEdit?: () => void
}

/**
 * Renders a user message with a subtle background tint.
 * Plain text only — no Markdown. Spec §10.3.2
 * `@relative/path` tokens (from RichInputArea) render as compact file chips.
 */
export function UserMessageBlock({
  text,
  nativeInputParts,
  imageDataUrls,
  images,
  triggerKind,
  triggerLabel,
  triggerRefId,
  editable = false,
  onEdit,
  editing = false,
  editText,
  editSubmitting = false,
  editSubmitDisabled = false,
  onEditTextChange,
  onCancelEdit,
  onSubmitEdit
}: UserMessageBlockProps): JSX.Element {
  const t = useT()
  const editAreaRef = useRef<HTMLTextAreaElement | null>(null)
  const [lightboxSrc, setLightboxSrc] = useState<string | null>(null)
  const [hovered, setHovered] = useState(false)
  const [hydratedImages, setHydratedImages] = useState<Array<{ url: string; absolutePath?: string }>>(
    (imageDataUrls ?? []).map((url) => ({ url }))
  )
  const [failedImageCount, setFailedImageCount] = useState(0)
  const workspacePath = useConversationStore((s) => s.workspacePath)
  const activeThreadId = useThreadStore((s) => s.activeThreadId)
  const hasImages = hydratedImages.length > 0
  const segments = nativeInputParts != null && nativeInputParts.length > 0
    ? segmentsFromNativeInputParts(nativeInputParts)
    : text.length > 0
      ? parseUserMessageSegments(text)
      : []
  const attachedFiles = segments.filter(
    (seg): seg is Extract<(typeof segments)[number], { type: 'attachedFile' }> => seg.type === 'attachedFile'
  )
  const textSegments = segments.filter((seg) => seg.type !== 'attachedFile')

  useEffect(() => {
    if (!editing) return
    const el = editAreaRef.current
    if (!el) return
    el.style.height = 'auto'
    const lineHeight = parseInt(getComputedStyle(el).lineHeight) || 20
    const maxHeight = lineHeight * 8 + 24
    el.style.height = `${Math.min(el.scrollHeight, maxHeight)}px`
    el.focus()
  }, [editing, editText])

  useEffect(() => {
    let cancelled = false

    const hydrateImages = async (): Promise<void> => {
      if (Array.isArray(imageDataUrls) && imageDataUrls.length > 0) {
        if (cancelled) return
        setHydratedImages(imageDataUrls.map((url) => ({ url })))
        setFailedImageCount(0)
        return
      }
      if (!Array.isArray(images) || images.length === 0) {
        if (cancelled) return
        setHydratedImages([])
        setFailedImageCount(0)
        return
      }

      const loaded: Array<{ url: string; absolutePath?: string }> = []
      let failed = 0
      for (const image of images) {
        const cached = imageDataUrlCache.get(image.path)
        if (cached) {
          loaded.push({ url: cached, absolutePath: image.path })
          continue
        }
        try {
          const result = await window.api.workspace.readImageAsDataUrl({ path: image.path })
          const dataUrl = result.dataUrl
          if (dataUrl) {
            imageDataUrlCache.set(image.path, dataUrl)
            loaded.push({ url: dataUrl, absolutePath: image.path })
          } else {
            failed++
          }
        } catch {
          failed++
        }
      }
      if (cancelled) return
      setHydratedImages(loaded)
      setFailedImageCount(failed)
    }

    void hydrateImages()
    return () => {
      cancelled = true
    }
  }, [imageDataUrls, images])

  return (
    <>
      <div
        onMouseEnter={() => setHovered(true)}
        onMouseLeave={() => setHovered(false)}
        style={{
          backgroundColor: 'var(--user-message-bg)',
          borderRadius: '12px',
          padding: '9px 13px',
          fontFamily: 'var(--font-body)',
          fontSize: 'var(--text-body-size)',
          lineHeight: 'var(--text-body-line-height)',
          color: 'var(--text-primary)',
          whiteSpace: 'pre-wrap',
          wordBreak: 'break-word',
          alignSelf: 'flex-end',
          width: editing ? 'min(100%, var(--conversation-reading-width))' : undefined,
          maxWidth: editing
            ? 'var(--conversation-reading-width)'
            : 'min(82%, var(--conversation-reading-width))',
          display: 'flex',
          flexDirection: 'column',
          gap: '6px',
          userSelect: 'text',
          position: 'relative'
        }}
      >
        {editing ? (
          <>
            <textarea
              ref={editAreaRef}
              value={editText ?? text}
              aria-label={t('conversation.editTextarea')}
              disabled={editSubmitting}
              onChange={(e) => onEditTextChange?.(e.currentTarget.value)}
              onKeyDown={(e) => {
                if (e.key === 'Escape') {
                  e.preventDefault()
                  onCancelEdit?.()
                  return
                }
                if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
                  e.preventDefault()
                  if (!editSubmitDisabled) {
                    onSubmitEdit?.()
                  }
                }
              }}
              style={{
                width: '100%',
                minHeight: '72px',
                maxHeight: '184px',
                resize: 'none',
                overflowY: 'auto',
                border: 'none',
                outline: 'none',
                background: 'transparent',
                color: 'var(--text-primary)',
                font: 'inherit',
                lineHeight: 'inherit',
                padding: 0
              }}
            />
            <div
              style={{
                display: 'flex',
                justifyContent: 'flex-end',
                alignItems: 'center',
                gap: '8px'
              }}
            >
              <button
                type="button"
                onClick={onCancelEdit}
                disabled={editSubmitting}
                style={{
                  height: 32,
                  padding: '0 12px',
                  borderRadius: 16,
                  border: '1px solid var(--border-default)',
                  background: 'var(--bg-secondary)',
                  color: 'var(--text-secondary)',
                  cursor: editSubmitting ? 'default' : 'pointer',
                  opacity: editSubmitting ? 0.7 : 1
                }}
              >
                {t('common.cancel')}
              </button>
              <button
                type="button"
                onClick={onSubmitEdit}
                disabled={editSubmitDisabled}
                aria-label={t('conversation.editSend')}
                style={{
                  height: 32,
                  padding: '0 14px',
                  borderRadius: 16,
                  border: '1px solid transparent',
                  background: editSubmitDisabled ? 'var(--bg-tertiary)' : 'var(--text-primary)',
                  color: editSubmitDisabled ? 'var(--text-dimmed)' : 'var(--bg-primary)',
                  cursor: editSubmitDisabled ? 'not-allowed' : 'pointer',
                  fontWeight: 600
                }}
              >
                {editSubmitting ? t('conversation.editSending') : t('conversation.editSend')}
              </button>
            </div>
          </>
        ) : (
          <>
        {hasImages && (
          <div
            style={{
              display: 'flex',
              flexDirection: 'row',
              flexWrap: 'wrap',
              gap: '8px'
            }}
          >
            {hydratedImages.map((imageItem, idx) => (
              <button
                key={`${idx}-${imageItem.url.slice(0, 32)}`}
                type="button"
                onClick={() => {
                  const fallbackToLightbox = (): void => {
                    setLightboxSrc(imageItem.url)
                  }
                  if (!imageItem.absolutePath || !workspacePath || !activeThreadId) {
                    fallbackToLightbox()
                    return
                  }
                  void openImagePathInViewer({
                    absolutePath: imageItem.absolutePath,
                    workspacePath,
                    threadId: activeThreadId,
                    t
                  }).then((opened) => {
                    if (!opened) fallbackToLightbox()
                  })
                }}
                style={{
                  padding: 0,
                  border: 'none',
                  background: 'transparent',
                  cursor: 'pointer',
                  borderRadius: '6px',
                  overflow: 'hidden',
                  lineHeight: 0
                }}
                aria-label={`View attached image ${idx + 1}`}
              >
                <img
                  src={imageItem.url}
                  alt=""
                  style={{
                    maxHeight: '80px',
                    maxWidth: '120px',
                    objectFit: 'cover',
                    display: 'block'
                  }}
                />
              </button>
            ))}
          </div>
        )}
        {!hasImages && failedImageCount > 0 && (
          <span style={{ color: 'var(--text-tertiary)', fontSize: '12px' }}>
            {failedImageCount === 1 ? 'Image unavailable' : `${failedImageCount} images unavailable`}
          </span>
        )}
        {attachedFiles.length > 0 && (
          <div
            style={{
              display: 'flex',
              flexDirection: 'row',
              flexWrap: 'wrap',
              gap: '8px'
            }}
          >
            {attachedFiles.map((file, idx) => (
              <AttachedFileChip key={`${file.path}-${idx}`} path={file.path} fileName={file.fileName} />
            ))}
          </div>
        )}
        {text.length > 0 && (
          <span>
            {textSegments.map((seg, idx) =>
              seg.type === 'text' ? (
                <span key={`t-${idx}`}>{seg.value}</span>
              ) : seg.type === 'fileRef' ? (
                <FileRefChip
                  key={`f-${idx}-${seg.relativePath}`}
                  relativePath={seg.relativePath}
                  workspacePath={workspacePath}
                />
              ) : seg.type === 'commandRef' ? (
                <CommandRefChip key={`c-${idx}-${seg.commandText}`} commandText={seg.commandText} />
              ) : (
                <SkillRefChip key={`s-${idx}-${seg.skillName}`} skillName={seg.skillName} />
              )
            )}
          </span>
        )}
        {triggerKind && (
          <AutomationTriggerPill
            kind={triggerKind}
            label={triggerLabel}
            refId={triggerRefId}
          />
        )}
        <MessageCopyButton
          getText={() => text}
          visible={hovered && text.length > 0}
        />
        {editable && onEdit && (
          <ActionTooltip
            label={t('conversation.editMessage')}
            placement="top"
            wrapperStyle={{
              position: 'absolute',
              right: text.length > 0 ? '40px' : '8px',
              bottom: '6px',
              opacity: hovered ? 1 : 0,
              pointerEvents: hovered ? 'auto' : 'none',
              zIndex: 2
            }}
          >
            <button
              type="button"
              onClick={onEdit}
              aria-label={t('conversation.editMessage')}
              style={{
                width: '24px',
                height: '24px',
                borderRadius: '6px',
                border: '1px solid var(--border-default)',
                background: 'var(--bg-secondary)',
                color: 'var(--text-secondary)',
                display: 'inline-flex',
                alignItems: 'center',
                justifyContent: 'center',
                cursor: 'pointer',
                transition: 'opacity 120ms ease, color 120ms ease'
              }}
            >
              <Pencil size={14} aria-hidden />
            </button>
          </ActionTooltip>
        )}
          </>
        )}
      </div>
      {lightboxSrc != null && (
        <ImageLightbox src={lightboxSrc} onClose={() => { setLightboxSrc(null) }} />
      )}
    </>
  )
}

function SkillRefChip({ skillName }: { skillName: string }): JSX.Element {
  return (
    <span
      title={`$${skillName}`}
        style={{
          display: 'inline-flex',
          alignItems: 'center',
          gap: '4px',
          verticalAlign: 'baseline',
          margin: '0 2px',
          padding: '2px 8px',
          borderRadius: '999px',
          border: '1px solid color-mix(in srgb, var(--success) 38%, transparent)',
          background: 'color-mix(in srgb, var(--success) 16%, transparent)',
          color: 'var(--success)',
          fontSize: '12px',
          lineHeight: 1.25,
          whiteSpace: 'nowrap',
          userSelect: 'none',
          fontWeight: 600,
          maxWidth: 'var(--inline-reference-max-width)'
        }}
      >
      <Sparkle size={12} strokeWidth={2.25} aria-hidden />
      <span>{skillName}</span>
    </span>
  )
}

function CommandRefChip({ commandText }: { commandText: string }): JSX.Element {
  const label = commandText.startsWith('/') ? commandText.slice(1) : commandText
  return (
    <span
      title={commandText}
        style={{
          display: 'inline-flex',
          alignItems: 'center',
          gap: '4px',
          verticalAlign: 'baseline',
          margin: '0 2px',
          padding: '2px 8px',
          borderRadius: '999px',
          border: '1px solid color-mix(in srgb, var(--accent) 38%, transparent)',
          background: 'color-mix(in srgb, var(--accent) 16%, transparent)',
          color: 'var(--accent)',
          fontSize: '12px',
          lineHeight: 1.25,
          whiteSpace: 'nowrap',
          userSelect: 'none',
          fontWeight: 600,
          maxWidth: 'var(--inline-reference-max-width)'
        }}
      >
      <Terminal size={12} strokeWidth={2.25} aria-hidden />
      <span>{label}</span>
    </span>
  )
}

function FileRefChip({
  relativePath,
  workspacePath
}: {
  relativePath: string
  workspacePath: string
}): JSX.Element {
  const fileName = relativePath.split('/').pop() ?? relativePath
  const title =
    workspacePath.length > 0
      ? `${workspacePath.replace(/[/\\]+$/, '')}/${relativePath.replace(/^[/\\]+/, '')}`
      : relativePath

  return (
    <span
      title={title}
        style={{
          display: 'inline-flex',
          alignItems: 'center',
          gap: '4px',
          verticalAlign: 'baseline',
          margin: '0 2px',
          padding: '2px 8px',
          borderRadius: '999px',
          border: '1px solid color-mix(in srgb, var(--border-active) 44%, transparent)',
          background: 'color-mix(in srgb, var(--bg-tertiary) 88%, transparent)',
          color: 'var(--text-primary)',
          fontSize: '12px',
          lineHeight: 1.25,
          whiteSpace: 'nowrap',
          userSelect: 'none',
          maxWidth: 'var(--inline-reference-max-width)'
        }}
      >
      <FileText size={12} strokeWidth={2.1} aria-hidden />
      <span>{fileName}</span>
    </span>
  )
}

function AutomationTriggerPill({
  kind,
  label,
  refId
}: {
  kind: NonNullable<ConversationItem['triggerKind']>
  label?: string
  refId?: string
}): JSX.Element {
  const locale = useLocale()
  const setActiveMainView = useUIStore((s) => s.setActiveMainView)
  const setAutomationsTab = useUIStore((s) => s.setAutomationsTab)
  const selectCronJob = useCronStore((s) => s.selectCronJob)

  const canNavigate =
    (kind === 'cron' && !!refId) || (kind === 'automation' && !!refId)
  const badgeText = translate(locale, 'automation.triggeredBy.badge')
  const detailText = label
    ? translate(
        locale,
        kind === 'heartbeat'
          ? 'automation.triggeredBy.heartbeat'
          : kind === 'cron'
            ? 'automation.triggeredBy.cron'
            : 'automation.triggeredBy.task',
        { label }
      )
    : translate(locale, 'automation.triggeredBy.generic')

  const onClick = canNavigate
    ? () => {
        setActiveMainView('automations')
        if (kind === 'cron') {
          setAutomationsTab('cron')
          if (refId) selectCronJob(refId)
        } else {
          setAutomationsTab('tasks')
        }
      }
    : undefined

  const commonStyle = {
    display: 'inline-flex',
    alignItems: 'center',
    gap: '4px',
    padding: '2px 8px',
    borderRadius: '999px',
    border: '1px solid color-mix(in srgb, var(--border-active) 36%, transparent)',
    background: 'color-mix(in srgb, var(--bg-tertiary) 80%, transparent)',
    color: 'var(--text-dimmed)',
    fontSize: '11px',
    lineHeight: 1.25,
    fontWeight: 500,
    alignSelf: 'flex-start',
    userSelect: 'none' as const
  }

  const title = `${badgeText} · ${detailText}`

  if (onClick) {
    return (
      <ActionTooltip label={title} wrapperStyle={{ display: 'inline-flex' }}>
        <button
          type="button"
          onClick={onClick}
          aria-label={title}
          style={{ ...commonStyle, cursor: 'pointer', border: commonStyle.border }}
        >
          <Bot size={11} strokeWidth={2.1} aria-hidden />
          <span>{badgeText}</span>
        </button>
      </ActionTooltip>
    )
  }

  return (
    <span title={title} style={commonStyle}>
      <Bot size={11} strokeWidth={2.1} aria-hidden />
      <span>{badgeText}</span>
    </span>
  )
}

function AttachedFileChip({ path, fileName }: { path: string; fileName: string }): JSX.Element {
  return (
    <span
      title={path}
        style={{
          display: 'inline-flex',
          alignItems: 'center',
          gap: '4px',
          padding: '4px 8px',
          borderRadius: '999px',
          border: '1px solid color-mix(in srgb, var(--border-active) 40%, transparent)',
          background: 'color-mix(in srgb, var(--bg-tertiary) 84%, var(--bg-primary))',
          color: 'var(--text-primary)',
          fontSize: '12px',
          whiteSpace: 'nowrap',
          userSelect: 'none',
          lineHeight: 1.25
        }}
      >
      <FileText size={12} strokeWidth={2.1} aria-hidden />
      <span>{fileName}</span>
    </span>
  )
}
