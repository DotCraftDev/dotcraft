import { useMemo, type CSSProperties } from 'react'
import { translate, type AppLocale } from '../../../shared/locales'
import type { ConversationItem } from '../../types/conversation'
import type { FileDiff } from '../../types/toolCall'
import { useSkillsStore } from '../../stores/skillsStore'
import { useUIStore } from '../../stores/uiStore'
import { getSkillManageDisplay } from '../../utils/skillManageToolDisplay'
import { SkillAvatar } from '../skills/SkillAvatar'
import { ActionTooltip } from '../ui/ActionTooltip'
import { InlineDiffView } from './InlineDiffView'

interface SkillManageCardProps {
  item: ConversationItem
  locale: AppLocale
  diff: FileDiff | null
}

export function SkillManageCard({ item, locale, diff }: SkillManageCardProps): JSX.Element | null {
  const display = useMemo(
    () => getSkillManageDisplay(item.arguments, item.result),
    [item.arguments, item.result]
  )
  const setActiveMainView = useUIStore((s) => s.setActiveMainView)
  const fetchSkills = useSkillsStore((s) => s.fetchSkills)
  const selectSkill = useSkillsStore((s) => s.selectSkill)

  if (!display.result?.success || !display.name) return null

  const badge = display.variantUpdated
    ? translate(locale, 'skillManage.card.variantUpdatedBadge')
    : actionBadge(display.action, locale)
  const subtitle = display.message || translate(locale, 'skillManage.card.updatedFallback')

  async function openSkill(): Promise<void> {
    if (!display.name) return
    setActiveMainView('skills')
    await fetchSkills()
    await selectSkill(display.name)
  }

  return (
    <div style={card}>
      <div style={header}>
        <SkillAvatar name={display.name} displayName={display.name} size={34} />
        <div style={{ minWidth: 0, flex: 1 }}>
          <div style={eyebrowRow}>
            <span style={eyebrow}>{translate(locale, 'skillManage.card.title')}</span>
            <span style={badgeStyle}>{badge}</span>
          </div>
          <div style={title}>{display.name}</div>
        </div>
        <ActionTooltip label={translate(locale, 'skillManage.card.viewInSkills')} placement="top">
          <button
            type="button"
            onClick={() => void openSkill()}
            style={viewButton}
            aria-label={translate(locale, 'skillManage.card.viewInSkills')}
          >
            {translate(locale, 'skillManage.card.view')}
          </button>
        </ActionTooltip>
      </div>
      <div style={subtitleStyle}>{subtitle}</div>
      {diff && (
        <div style={diffFrame}>
          <InlineDiffView
            diff={diff}
            variant="embedded"
            showStreamingIndicator={false}
            headerMode="compact"
          />
        </div>
      )}
    </div>
  )
}

function actionBadge(action: ReturnType<typeof getSkillManageDisplay>['action'], locale: AppLocale): string {
  switch (action) {
    case 'create':
      return translate(locale, 'skillManage.card.createdBadge')
    case 'edit':
      return translate(locale, 'skillManage.card.updatedBadge')
    case 'patch':
      return translate(locale, 'skillManage.card.patchedBadge')
    case 'write_file':
      return translate(locale, 'skillManage.card.fileAddedBadge')
    default:
      return translate(locale, 'skillManage.card.updatedBadge')
  }
}

const card: CSSProperties = {
  border: '1px solid var(--border-default)',
  borderRadius: '10px',
  background: 'var(--bg-secondary)',
  padding: '12px 14px',
  display: 'flex',
  flexDirection: 'column',
  gap: '8px'
}

const header: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: '10px',
  minWidth: 0
}

const eyebrowRow: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: '8px',
  minWidth: 0
}

const eyebrow: CSSProperties = {
  color: 'var(--text-secondary)',
  fontSize: '11px',
  fontWeight: 600
}

const badgeStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  padding: '1px 8px',
  borderRadius: '999px',
  background: 'var(--bg-tertiary)',
  color: 'var(--success)',
  fontSize: '10px',
  fontWeight: 600,
  lineHeight: 1.4,
  whiteSpace: 'nowrap'
}

const title: CSSProperties = {
  marginTop: '3px',
  color: 'var(--text-primary)',
  fontSize: '14px',
  fontWeight: 700,
  lineHeight: 1.25,
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap'
}

const subtitleStyle: CSSProperties = {
  color: 'var(--text-secondary)',
  fontSize: '12px',
  lineHeight: 1.45,
  wordBreak: 'break-word'
}

const diffFrame: CSSProperties = {
  marginTop: '2px',
  border: '1px solid var(--border-default)',
  borderRadius: '6px',
  overflow: 'hidden'
}

const viewButton: CSSProperties = {
  border: '1px solid var(--border-default)',
  borderRadius: '999px',
  padding: '2px 10px',
  background: 'var(--bg-primary)',
  color: 'var(--text-secondary)',
  cursor: 'pointer',
  fontSize: '11px',
  lineHeight: 1.3,
  flexShrink: 0
}
