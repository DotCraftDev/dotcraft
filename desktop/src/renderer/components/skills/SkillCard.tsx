import { useT } from '../../contexts/LocaleContext'
import type { SkillEntry } from '../../stores/skillsStore'

interface SkillCardProps {
  skill: SkillEntry
  onOpen: () => void
  onToggleEnabled: (enabled: boolean) => void
}

/**
 * Single skill row in the grid: generic glyph, title, description, source badge, enable switch.
 */
export function SkillCard({ skill, onOpen, onToggleEnabled }: SkillCardProps): JSX.Element {
  const t = useT()
  const letter = (skill.name[0] ?? '?').toUpperCase()
  const hue = hashHue(skill.name)

  function handleSwitchClick(e: React.MouseEvent): void {
    e.stopPropagation()
  }

  return (
    <div
      role="button"
      tabIndex={0}
      onClick={onOpen}
      onKeyDown={(e) => {
        if (e.key === 'Enter' || e.key === ' ') {
          e.preventDefault()
          onOpen()
        }
      }}
      style={{
        display: 'flex',
        alignItems: 'flex-start',
        gap: '12px',
        padding: '12px 14px',
        borderRadius: '8px',
        border: '1px solid var(--border-default)',
        backgroundColor: 'var(--bg-secondary)',
        cursor: 'pointer',
        opacity: skill.enabled ? 1 : 0.65,
        transition: 'background-color 120ms ease'
      }}
      onMouseEnter={(e) => {
        ;(e.currentTarget as HTMLDivElement).style.backgroundColor = 'var(--bg-tertiary)'
      }}
      onMouseLeave={(e) => {
        ;(e.currentTarget as HTMLDivElement).style.backgroundColor = 'var(--bg-secondary)'
      }}
    >
      <div
        aria-hidden
        style={{
          width: '40px',
          height: '40px',
          borderRadius: '8px',
          backgroundColor: `hsla(${hue}, 35%, 28%, 0.9)`,
          color: 'var(--text-primary)',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          fontSize: '16px',
          fontWeight: 700,
          flexShrink: 0
        }}
      >
        {letter}
      </div>
      <div style={{ flex: 1, minWidth: 0 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: '8px', flexWrap: 'wrap' }}>
          <span style={{ fontWeight: 600, fontSize: '14px', color: 'var(--text-primary)' }}>
            {skill.name}
          </span>
          <SourceBadge source={skill.source} t={t} />
          {!skill.available && (
            <span
              style={{
                fontSize: '11px',
                padding: '2px 6px',
                borderRadius: '4px',
                backgroundColor: 'var(--bg-tertiary)',
                color: 'var(--warning)'
              }}
              title={skill.unavailableReason ?? ''}
            >
              {t('skillCard.unavailable')}
            </span>
          )}
          {!skill.enabled && (
            <span
              style={{
                fontSize: '11px',
                padding: '2px 6px',
                borderRadius: '4px',
                backgroundColor: 'var(--bg-tertiary)',
                color: 'var(--text-dimmed)'
              }}
            >
              {t('skillCard.disabledBadge')}
            </span>
          )}
        </div>
        <p
          style={{
            margin: '6px 0 0',
            fontSize: '12px',
            color: 'var(--text-secondary)',
            lineHeight: 1.4,
            display: '-webkit-box',
            WebkitLineClamp: 2,
            WebkitBoxOrient: 'vertical',
            overflow: 'hidden'
          }}
        >
          {skill.description || t('skillCard.noDescription')}
        </p>
      </div>
      <div style={{ flexShrink: 0, paddingTop: '2px' }} onClick={handleSwitchClick}>
        <label style={{ display: 'flex', alignItems: 'center', gap: '6px', cursor: 'pointer' }}>
          <span style={{ fontSize: '11px', color: 'var(--text-dimmed)', userSelect: 'none' }}>
            {t('skillCard.on')}
          </span>
          <input
            type="checkbox"
            checked={skill.enabled}
            onChange={(e) => {
              void onToggleEnabled(e.target.checked)
            }}
            aria-label={skill.enabled ? t('skillCard.toggleDisable') : t('skillCard.toggleEnable')}
          />
        </label>
      </div>
    </div>
  )
}

function SourceBadge({
  source,
  t
}: {
  source: SkillEntry['source']
  t: ReturnType<typeof useT>
}): JSX.Element {
  const styles: Record<SkillEntry['source'], React.CSSProperties> = {
    builtin: {
      fontSize: '11px',
      padding: '2px 6px',
      borderRadius: '4px',
      backgroundColor: 'var(--bg-tertiary)',
      color: 'var(--text-dimmed)'
    },
    workspace: {
      fontSize: '11px',
      padding: '2px 6px',
      borderRadius: '4px',
      backgroundColor: 'var(--bg-tertiary)',
      color: 'var(--accent)'
    },
    user: {
      fontSize: '11px',
      padding: '2px 6px',
      borderRadius: '4px',
      backgroundColor: 'rgba(34, 197, 94, 0.12)',
      color: 'var(--success)'
    }
  }
  const labels: Record<SkillEntry['source'], string> = {
    builtin: t('skillCard.builtin'),
    workspace: t('skills.source.workspace'),
    user: t('skills.source.user')
  }
  return <span style={styles[source]}>{labels[source]}</span>
}

function hashHue(s: string): number {
  let h = 0
  for (let i = 0; i < s.length; i++) h = (h * 31 + s.charCodeAt(i)) >>> 0
  return h % 360
}
