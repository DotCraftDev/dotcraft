import { useState } from 'react'
import { useConversationStore } from '../../stores/conversationStore'
import { ImageLightbox } from './ImageLightbox'
import { parseUserMessageFileRefs } from './parseUserMessageFileRefs'

interface UserMessageBlockProps {
  text: string
  imageDataUrls?: string[]
}

/**
 * Renders a user message with a subtle background tint.
 * Plain text only — no Markdown. Spec §10.3.2
 * `@relative/path` tokens (from RichInputArea) render as compact file chips.
 */
export function UserMessageBlock({ text, imageDataUrls }: UserMessageBlockProps): JSX.Element {
  const [lightboxSrc, setLightboxSrc] = useState<string | null>(null)
  const workspacePath = useConversationStore((s) => s.workspacePath)
  const hasImages = imageDataUrls != null && imageDataUrls.length > 0
  const segments = text.length > 0 ? parseUserMessageFileRefs(text) : []

  return (
    <>
      <div
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
          gap: '8px'
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
            {imageDataUrls!.map((url, idx) => (
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
        {text.length > 0 && (
          <span>
            {segments.map((seg, idx) =>
              seg.type === 'text' ? (
                <span key={`t-${idx}`}>{seg.value}</span>
              ) : (
                <FileRefChip
                  key={`f-${idx}-${seg.relativePath}`}
                  relativePath={seg.relativePath}
                  workspacePath={workspacePath}
                />
              )
            )}
          </span>
        )}
      </div>
      {lightboxSrc != null && (
        <ImageLightbox src={lightboxSrc} onClose={() => { setLightboxSrc(null) }} />
      )}
    </>
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
