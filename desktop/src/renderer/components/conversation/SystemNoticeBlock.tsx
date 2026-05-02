import { Archive, ChevronsDown } from 'lucide-react'
import type { ReactNode } from 'react'
import { useT } from '../../contexts/LocaleContext'
import type { ConversationItem } from '../../types/conversation'

interface SystemNoticeBlockProps {
  item: ConversationItem
}

/**
 * Inline divider rendered inside the conversation timeline for persisted
 * maintenance events. Stays visible after thread reloads because the item
 * is persisted alongside normal turn items.
 *
 * Unknown notice kinds render nothing — the wire protocol reserves kind as a
 * string so future additions can light up their own renderers without touching
 * the rest of the conversation pipeline.
 */
export function SystemNoticeBlock({ item }: SystemNoticeBlockProps): JSX.Element | null {
  const t = useT()
  const notice = item.systemNotice
  if (!notice) return null

  if (notice.kind === 'memoryConsolidated') {
    return (
      <NoticeDivider
        ariaLabel={t('systemNotice.memoryConsolidated.title')}
        icon={<Archive size={12} aria-hidden />}
        title={t('systemNotice.memoryConsolidated.updated')}
      />
    )
  }

  if (notice.kind !== 'compacted') return null

  const title =
    notice.trigger === 'reactive'
      ? t('systemNotice.compacted.reactive')
      : t('systemNotice.compacted.auto')

  const before = typeof notice.tokensBefore === 'number' ? notice.tokensBefore : 0
  const after = typeof notice.tokensAfter === 'number' ? notice.tokensAfter : 0
  const freed = Math.max(0, before - after)
  const percentLeft =
    typeof notice.percentLeftAfter === 'number'
      ? Math.round(notice.percentLeftAfter * 100)
      : null

  const detail =
    percentLeft !== null
      ? t('systemNotice.compacted.detail', {
          freed: formatTokens(freed),
          percent: percentLeft
        })
      : null

  return (
    <NoticeDivider
      ariaLabel={t('systemNotice.compacted.title')}
      icon={<ChevronsDown size={12} aria-hidden />}
      title={title}
      detail={detail}
    />
  )
}

interface NoticeDividerProps {
  ariaLabel: string
  icon: ReactNode
  title: string
  detail?: string | null
}

function NoticeDivider({ ariaLabel, icon, title, detail }: NoticeDividerProps): JSX.Element {
  return (
    <div
      role="separator"
      aria-label={ariaLabel}
      style={{
        display: 'flex',
        alignItems: 'center',
        gap: 8,
        padding: '14px 4px 14px 4px',
        color: 'var(--text-secondary, #8a8a8a)',
        fontSize: 11,
        lineHeight: 1.4,
        userSelect: 'none'
      }}
    >
      <span
        aria-hidden
        style={{
          flex: 1,
          height: 1,
          background: 'var(--border-color, rgba(127,127,127,0.25))'
        }}
      />
      <span
        style={{
          display: 'inline-flex',
          alignItems: 'center',
          gap: 6,
          padding: '2px 8px',
          borderRadius: 999,
          background: 'var(--bg-subtle, rgba(127,127,127,0.08))'
        }}
      >
        {icon}
        <span style={{ fontWeight: 600 }}>{title}</span>
        {detail && (
          <span style={{ color: 'var(--text-dimmed, #9a9a9a)' }}>· {detail}</span>
        )}
      </span>
      <span
        aria-hidden
        style={{
          flex: 1,
          height: 1,
          background: 'var(--border-color, rgba(127,127,127,0.25))'
        }}
      />
    </div>
  )
}

function formatTokens(n: number): string {
  if (!Number.isFinite(n) || n <= 0) return '0'
  if (n >= 1_000_000) return `${(n / 1_000_000).toFixed(1)}M`
  if (n >= 1_000) return `${(n / 1_000).toFixed(1)}k`
  return String(Math.round(n))
}
