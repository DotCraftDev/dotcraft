import type { CSSProperties, JSX } from 'react'
import type { ShortcutSpec } from './shortcutKeys'
import { formatShortcutParts } from './shortcutKeys'

interface ShortcutBadgeProps {
  shortcut: ShortcutSpec
  platform?: string
  style?: CSSProperties
}

export function ShortcutBadge({ shortcut, platform, style }: ShortcutBadgeProps): JSX.Element {
  const parts = formatShortcutParts(shortcut, platform)
  return (
    <span
      aria-hidden="true"
      className="dc-shortcut-badge"
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: '3px',
        flexShrink: 0,
        ...style
      }}
    >
      {parts.map((part, index) => (
        <kbd key={`${part}-${index}`} className="dc-shortcut-key">
          {part}
        </kbd>
      ))}
    </span>
  )
}
