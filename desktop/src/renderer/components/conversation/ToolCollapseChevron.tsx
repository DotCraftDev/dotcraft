import type { JSX } from 'react'

interface ToolCollapseChevronProps {
  expanded: boolean
}

export function ToolCollapseChevron({ expanded }: ToolCollapseChevronProps): JSX.Element {
  return (
    <span
      style={{
        color: 'var(--text-dimmed)',
        fontSize: '10px',
        transform: expanded ? 'rotate(180deg)' : 'rotate(0deg)',
        transition: 'transform 150ms ease'
      }}
    >
      ▼
    </span>
  )
}
