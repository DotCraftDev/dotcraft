import type { ComposerFileAttachment, ImageAttachment } from '../../types/conversation'

interface AttachmentStripProps {
  images: ImageAttachment[]
  files: ComposerFileAttachment[]
  onRemoveImage: (index: number) => void
  onRemoveFile: (index: number) => void
  removeImageLabel?: string
  removeFileLabel?: string
}

const chipStyle = {
  display: 'inline-flex',
  alignItems: 'center',
  gap: '6px',
  flexShrink: 0,
  maxWidth: '220px',
  padding: '4px 8px',
  borderRadius: '8px',
  border: '1px solid var(--border-default)',
  fontSize: '12px',
  color: 'var(--text-secondary)'
} as const

const removeButtonStyle = {
  width: 16,
  height: 16,
  borderRadius: '50%',
  border: '1px solid var(--border-default)',
  background: 'var(--bg-secondary)',
  color: 'var(--text-secondary)',
  fontSize: '10px',
  cursor: 'pointer',
  padding: 0,
  lineHeight: 1,
  flexShrink: 0
} as const

export function AttachmentStrip({
  images,
  files,
  onRemoveImage,
  onRemoveFile,
  removeImageLabel = 'Remove image',
  removeFileLabel = 'Remove file'
}: AttachmentStripProps): JSX.Element | null {
  if (images.length === 0 && files.length === 0) return null

  return (
    <div
      style={{
        display: 'flex',
        flexWrap: 'nowrap',
        gap: '8px',
        overflowX: 'auto',
        paddingBottom: '4px',
        alignItems: 'flex-start'
      }}
    >
      {images.map((img, idx) => (
        <div
          key={`image-${img.tempPath}-${idx}`}
          style={{
            ...chipStyle,
            background: 'var(--bg-tertiary)'
          }}
        >
          <img
            src={img.dataUrl}
            alt=""
            style={{
              width: 20,
              height: 20,
              borderRadius: '3px',
              objectFit: 'cover',
              flexShrink: 0
            }}
          />
          <span
            style={{
              overflow: 'hidden',
              textOverflow: 'ellipsis',
              whiteSpace: 'nowrap',
              maxWidth: '140px'
            }}
            title={img.fileName}
          >
            {img.fileName}
          </span>
          <button
            type="button"
            onClick={() => { onRemoveImage(idx) }}
            aria-label={removeImageLabel}
            title={removeImageLabel}
            style={removeButtonStyle}
          >
            ✕
          </button>
        </div>
      ))}

      {files.map((file, idx) => (
        <div
          key={`file-${file.path}-${idx}`}
          style={{
            ...chipStyle,
            background: 'color-mix(in srgb, var(--bg-tertiary) 78%, var(--bg-primary))'
          }}
          title={file.path}
        >
          <span aria-hidden style={{ flexShrink: 0 }}>
            📄
          </span>
          <span
            style={{
              overflow: 'hidden',
              textOverflow: 'ellipsis',
              whiteSpace: 'nowrap',
              maxWidth: '150px'
            }}
          >
            {file.fileName}
          </span>
          <button
            type="button"
            onClick={() => { onRemoveFile(idx) }}
            aria-label={removeFileLabel}
            title={removeFileLabel}
            style={removeButtonStyle}
          >
            ✕
          </button>
        </div>
      ))}
    </div>
  )
}
