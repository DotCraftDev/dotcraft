import type { ComposerFileAttachment, ImageAttachment } from '../../types/conversation'
import { useConversationStore } from '../../stores/conversationStore'
import { useThreadStore } from '../../stores/threadStore'
import { useT } from '../../contexts/LocaleContext'
import { openImagePathInViewer } from '../../utils/conversationDeepLink'

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
  const t = useT()
  const workspacePath = useConversationStore((s) => s.workspacePath)
  const activeThreadId = useThreadStore((s) => s.activeThreadId)
  if (images.length === 0 && files.length === 0) return null

  const normalizePath = (value: string): string => value.replace(/\\/g, '/').replace(/\/+$/, '')
  const isWorkspaceBacked = (candidatePath: string): boolean => {
    if (!workspacePath) return false
    const workspaceNorm = normalizePath(workspacePath).toLowerCase()
    const candidateNorm = normalizePath(candidatePath).toLowerCase()
    return candidateNorm === workspaceNorm || candidateNorm.startsWith(`${workspaceNorm}/`)
  }

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
          <button
            type="button"
            onClick={() => {
              if (!activeThreadId || !isWorkspaceBacked(img.tempPath)) return
              void openImagePathInViewer({
                absolutePath: img.tempPath,
                workspacePath,
                threadId: activeThreadId,
                t
              })
            }}
            disabled={!activeThreadId || !isWorkspaceBacked(img.tempPath)}
            title={isWorkspaceBacked(img.tempPath) ? img.fileName : undefined}
            style={{
              padding: 0,
              border: 'none',
              background: 'transparent',
              lineHeight: 0,
              borderRadius: '3px',
              cursor: activeThreadId && isWorkspaceBacked(img.tempPath) ? 'pointer' : 'default',
              opacity: activeThreadId && isWorkspaceBacked(img.tempPath) ? 1 : 0.9
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
          </button>
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
