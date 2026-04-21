import { useCallback, useRef } from 'react'

interface DragHandleProps {
  onDrag: (delta: number) => void
  className?: string
}

/**
 * A 4px-wide drag handle for resizing panels.
 *
 * The outer element spans full column height to keep the hit area generous
 * (users can grab anywhere along the vertical edge). The hover highlight,
 * however, is painted by an inner absolutely-positioned child that starts at
 * y = var(--chrome-header-height), so it never shows inside the header row.
 * This avoids clashing with the unified header line / T-shape divider.
 */
export function DragHandle({ onDrag, className = '' }: DragHandleProps): JSX.Element {
  const isDragging = useRef(false)
  const lastX = useRef(0)
  const highlightRef = useRef<HTMLDivElement | null>(null)

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
      onMouseEnter={() => {
        if (highlightRef.current) {
          highlightRef.current.style.backgroundColor = 'var(--border-active)'
        }
      }}
      onMouseLeave={() => {
        if (highlightRef.current) {
          highlightRef.current.style.backgroundColor = 'transparent'
        }
      }}
      style={{
        position: 'relative',
        width: '4px',
        flexShrink: 0,
        cursor: 'col-resize',
        backgroundColor: 'transparent',
        zIndex: 10
      }}
      role="separator"
      aria-orientation="vertical"
    >
      <div
        ref={highlightRef}
        aria-hidden
        style={{
          position: 'absolute',
          top: 'var(--chrome-header-height)',
          bottom: 0,
          left: 0,
          right: 0,
          backgroundColor: 'transparent',
          transition: 'background-color 150ms ease',
          pointerEvents: 'none'
        }}
      />
    </div>
  )
}
