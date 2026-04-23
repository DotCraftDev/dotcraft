import type { JSX } from 'react'

interface ToolCollapseChevronProps {
  expanded: boolean
  visible?: boolean
}

export function ToolCollapseChevron({
  expanded,
  visible = true
}: ToolCollapseChevronProps): JSX.Element {
  return (
    <span
      aria-hidden={!visible}
      style={{
        color: 'var(--text-dimmed)',
        fontSize: '10px',
        opacity: visible ? 1 : 0,
        transform: expanded ? 'rotate(180deg)' : 'rotate(0deg)',
        transition: 'transform 150ms ease, opacity 120ms ease'
      }}
    >
      ▼
    </span>
  )
}
