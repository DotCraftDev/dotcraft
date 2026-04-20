import { useEffect, useState } from 'react'
import { Sparkle, Terminal } from 'lucide-react'
import { useConversationStore } from '../../stores/conversationStore'
import { ImageLightbox } from './ImageLightbox'
import { MessageCopyButton } from './MessageCopyButton'
import { parseUserMessageSegments, segmentsFromNativeInputParts } from './parseUserMessageSegments'
import type { InputPart, UserMessageImageRef } from '../../types/conversation'

const imageDataUrlCache = new Map<string, string>()

interface UserMessageBlockProps {
  text: string
  nativeInputParts?: InputPart[]
  imageDataUrls?: string[]
  images?: UserMessageImageRef[]
}

/**
 * Renders a user message with a subtle background tint.
 * Plain text only — no Markdown. Spec §10.3.2
 * `@relative/path` tokens (from RichInputArea) render as compact file chips.
 */
export function UserMessageBlock({ text, nativeInputParts, imageDataUrls, images }: UserMessageBlockProps): JSX.Element {
  const [lightboxSrc, setLightboxSrc] = useState<string | null>(null)
  const [hovered, setHovered] = useState(false)
  const [hydratedImageDataUrls, setHydratedImageDataUrls] = useState<string[]>(imageDataUrls ?? [])
  const [failedImageCount, setFailedImageCount] = useState(0)
  const workspacePath = useConversationStore((s) => s.workspacePath)
  const hasImages = hydratedImageDataUrls.length > 0
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
    let cancelled = false

    const hydrateImages = async (): Promise<void> => {
      if (Array.isArray(imageDataUrls) && imageDataUrls.length > 0) {
        if (cancelled) return
        setHydratedImageDataUrls(imageDataUrls)
        setFailedImageCount(0)
        return
      }
      if (!Array.isArray(images) || images.length === 0) {
        if (cancelled) return
        setHydratedImageDataUrls([])
        setFailedImageCount(0)
        return
      }

      const loaded: string[] = []
      let failed = 0
      for (const image of images) {
        const cached = imageDataUrlCache.get(image.path)
        if (cached) {
          loaded.push(cached)
          continue
        }
        try {
          const result = await window.api.workspace.readImageAsDataUrl({ path: image.path })
          const dataUrl = result.dataUrl
          if (dataUrl) {
            imageDataUrlCache.set(image.path, dataUrl)
            loaded.push(dataUrl)
          } else {
            failed++
          }
        } catch {
          failed++
        }
      }
      if (cancelled) return
      setHydratedImageDataUrls(loaded)
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
          borderRadius: '8px',
          padding: '10px 14px',
          fontSize: '14px',
          lineHeight: 1.6,
          color: 'var(--text-primary)',
          whiteSpace: 'pre-wrap',
          wordBreak: 'break-word',
          alignSelf: 'flex-end',
          maxWidth: '85%',
          display: 'flex',
          flexDirection: 'column',
          gap: '8px',
          userSelect: 'text',
          position: 'relative'
        }}
      >
        {hasImages && (
          <div
            style={{
              display: 'flex',
              flexDirection: 'row',
              flexWrap: 'wrap',
              gap: '8px'
            }}
          >
            {hydratedImageDataUrls.map((url, idx) => (
              <button
                key={`${idx}-${url.slice(0, 32)}`}
                type="button"
                onClick={() => {
                  setLightboxSrc(url)
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
                  src={url}
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
        <MessageCopyButton
          getText={() => text}
          visible={hovered && text.length > 0}
        />
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
        margin: '0 1px',
        padding: '1px 6px',
        borderRadius: '6px',
        border: '1px solid color-mix(in srgb, var(--success) 38%, transparent)',
        background: 'color-mix(in srgb, var(--success) 16%, transparent)',
        color: 'var(--success)',
        fontSize: '13px',
        whiteSpace: 'nowrap',
        userSelect: 'none',
        fontWeight: 600
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
        margin: '0 1px',
        padding: '1px 6px',
        borderRadius: '6px',
        border: '1px solid color-mix(in srgb, var(--accent) 38%, transparent)',
        background: 'color-mix(in srgb, var(--accent) 16%, transparent)',
        color: 'var(--accent)',
        fontSize: '13px',
        whiteSpace: 'nowrap',
        userSelect: 'none',
        fontWeight: 600
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
        margin: '0 1px',
        padding: '1px 6px',
        borderRadius: '4px',
        background: 'var(--bg-tertiary)',
        fontSize: '13px',
        whiteSpace: 'nowrap',
        userSelect: 'none'
      }}
    >
      <span aria-hidden>📄</span>
      <span>{fileName}</span>
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
        borderRadius: '8px',
        border: '1px solid var(--border-default)',
        background: 'color-mix(in srgb, var(--bg-tertiary) 78%, var(--bg-primary))',
        fontSize: '12px',
        whiteSpace: 'nowrap',
        userSelect: 'none'
      }}
    >
      <span aria-hidden>📄</span>
      <span>{fileName}</span>
    </span>
  )
}
