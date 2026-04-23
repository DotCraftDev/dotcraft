import { useEffect, useState } from 'react'
import type { ReactNode } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { formatDurationShort } from '../../utils/formatDurationShort'
import { CollapsibleContent } from './CollapsibleContent'
import { ToolCollapseChevron } from './ToolCollapseChevron'

interface TurnCollapsedSummaryProps {
  elapsedMs: number
  children: ReactNode
}

export function TurnCollapsedSummary({
  elapsedMs,
  children
}: TurnCollapsedSummaryProps): JSX.Element {
  const t = useT()
  const [expanded, setExpanded] = useState(false)
  const [renderExpanded, setRenderExpanded] = useState(false)

  useEffect(() => {
    if (expanded) {
      setRenderExpanded(true)
    }
  }, [expanded])

  const duration = formatDurationShort(elapsedMs)
  const label = t('conversation.turnCollapsed.processed', { duration })

  return (
    <div
      style={{
        borderRadius: '4px',
        border: expanded ? '1px solid var(--border-default)' : 'none',
        overflow: 'hidden'
      }}
    >
      <button
        onClick={() => setExpanded((value) => !value)}
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: '6px',
          width: '100%',
          padding: '3px 6px',
          background: expanded ? 'var(--bg-tertiary)' : 'transparent',
          border: 'none',
          borderBottom: expanded ? '1px solid var(--border-default)' : 'none',
          color: 'var(--text-secondary)',
          fontSize: '12px',
          textAlign: 'left',
          cursor: 'pointer',
          borderRadius: expanded ? '4px 4px 0 0' : '4px'
        }}
      >
        <span style={{ flex: 1 }}>{label}</span>
        <ToolCollapseChevron expanded={expanded} visible />
      </button>
      <CollapsibleContent
        expanded={expanded}
        renderExpanded={renderExpanded}
        setRenderExpanded={setRenderExpanded}
      >
        <div style={{ background: 'var(--bg-secondary)', padding: '8px' }}>
          {children}
        </div>
      </CollapsibleContent>
    </div>
  )
}
