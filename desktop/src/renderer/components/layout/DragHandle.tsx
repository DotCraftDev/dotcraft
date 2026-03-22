import { useCallback, useRef } from 'react'

interface DragHandleProps {
  onDrag: (delta: number) => void
  className?: string
}

/**
 * A 4px-wide drag handle for resizing panels.
 * Transparent by default, highlights on hover.
 * Fires onDrag with the pixel delta during dragging.
 */
export function DragHandle({ onDrag, className = '' }: DragHandleProps): JSX.Element {
  const isDragging = useRef(false)
  const lastX = useRef(0)

  const handleMouseDown = useCallback(
    (e: React.MouseEvent) => {
      e.preventDefault()
      isDragging.current = true
      lastX.current = e.clientX

      function onMouseMove(event: MouseEvent): void {
        if (!isDragging.current) return
        const delta = event.clientX - lastX.current
        lastX.current = event.clientX
        onDrag(delta)
      }

      function onMouseUp(): void {
        isDragging.current = false
        document.removeEventListener('mousemove', onMouseMove)
        document.removeEventListener('mouseup', onMouseUp)
        document.body.style.cursor = ''
        document.body.style.userSelect = ''
      }

      document.addEventListener('mousemove', onMouseMove)
      document.addEventListener('mouseup', onMouseUp)
      document.body.style.cursor = 'col-resize'
      document.body.style.userSelect = 'none'
    },
    [onDrag]
  )

  return (
    <div
      className={`drag-handle ${className}`}
      onMouseDown={handleMouseDown}
      style={{
        width: '4px',
        flexShrink: 0,
        cursor: 'col-resize',
        backgroundColor: 'transparent',
        transition: 'background-color 150ms ease',
        zIndex: 10
      }}
      onMouseEnter={(e) => {
        ;(e.currentTarget as HTMLDivElement).style.backgroundColor = 'var(--border-active)'
      }}
      onMouseLeave={(e) => {
        ;(e.currentTarget as HTMLDivElement).style.backgroundColor = 'transparent'
      }}
      role="separator"
      aria-orientation="vertical"
    />
  )
}
