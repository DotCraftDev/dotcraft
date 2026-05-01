import type { CSSProperties, ReactNode } from 'react'
import { translate, type AppLocale } from '../../../shared/locales'
import { useSkillsStore } from '../../stores/skillsStore'
import { useUIStore } from '../../stores/uiStore'
import { SkillAvatar } from '../skills/SkillAvatar'
import { ActionTooltip } from '../ui/ActionTooltip'

interface SkillToolCardProps {
  locale: AppLocale
  skillName: string
  badge: string
  subtitle: string
  children?: ReactNode
}

export function SkillToolCard({
  locale,
  skillName,
  badge,
  subtitle,
  children
}: SkillToolCardProps): JSX.Element {
  const setActiveMainView = useUIStore((s) => s.setActiveMainView)
  const fetchSkills = useSkillsStore((s) => s.fetchSkills)
  const selectSkill = useSkillsStore((s) => s.selectSkill)

  async function openSkill(): Promise<void> {
    setActiveMainView('skills')
    await fetchSkills()
    await selectSkill(skillName)
  }

  return (
    <div style={card}>
      <div style={header}>
        <SkillAvatar name={skillName} displayName={skillName} size={34} />
        <div style={{ minWidth: 0, flex: 1 }}>
          <div style={eyebrowRow}>
            <span style={eyebrow}>{translate(locale, 'skillTool.card.title')}</span>
            <span style={badgeStyle}>{badge}</span>
          </div>
          <div style={title}>{skillName}</div>
        </div>
        <ActionTooltip label={translate(locale, 'skillTool.card.viewInSkills')} placement="top">
          <button
            type="button"
            onClick={() => void openSkill()}
            style={viewButton}
            aria-label={translate(locale, 'skillTool.card.viewInSkills')}
          >
            {translate(locale, 'skillTool.card.view')}
          </button>
        </ActionTooltip>
      </div>
      <div style={subtitleStyle}>{subtitle}</div>
      {children}
    </div>
  )
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
