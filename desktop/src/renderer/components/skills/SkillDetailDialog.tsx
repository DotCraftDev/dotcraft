import { useEffect } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { MarkdownRenderer } from '../conversation/MarkdownRenderer'
import type { SkillEntry } from '../../stores/skillsStore'
import { dirname } from '../../utils/path'

interface SkillDetailDialogProps {
  skill: SkillEntry
  markdownBody: string
  loading: boolean
  onClose: () => void
  onToggleEnabled: (enabled: boolean) => void
}

/**
 * Modal: skill title, open folder, rendered SKILL.md body, enable/disable.
 */
export function SkillDetailDialog({
  skill,
  markdownBody,
  loading,
  onClose,
  onToggleEnabled
}: SkillDetailDialogProps): JSX.Element {
  const t = useT()
  const skillDir = dirname(skill.path)

  useEffect(() => {
    function onKey(e: KeyboardEvent): void {
      if (e.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  return (
    <div
      role="presentation"
      style={{
        position: 'fixed',
        inset: 0,
        zIndex: 1000,
        backgroundColor: 'rgba(0,0,0,0.55)',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        padding: '24px'
      }}
      onClick={onClose}
    >
      <div
        role="dialog"
        aria-modal
        aria-labelledby="skill-detail-title"
        style={{
          width: 'min(720px, 100%)',
          maxHeight: 'min(85vh, 900px)',
          backgroundColor: 'var(--bg-primary)',
          borderRadius: '12px',
          border: '1px solid var(--border-default)',
          boxShadow: '0 16px 48px rgba(0,0,0,0.45)',
          display: 'flex',
          flexDirection: 'column',
          overflow: 'hidden'
        }}
        onClick={(e) => e.stopPropagation()}
      >
        <header
          style={{
            padding: '16px 20px',
            borderBottom: '1px solid var(--border-default)',
            display: 'flex',
            alignItems: 'flex-start',
            justifyContent: 'space-between',
            gap: '12px',
            flexShrink: 0
          }}
        >
          <div style={{ flex: '1 1 auto', minWidth: 0 }}>
            <div
              style={{
                display: 'flex',
                gap: '12px',
                alignItems: 'flex-start'
              }}
            >
            <div
              aria-hidden
              style={{
                width: '40px',
                height: '40px',
                minWidth: '40px',
                minHeight: '40px',
                flex: '0 0 auto',
                flexShrink: 0,
                borderRadius: '8px',
                backgroundColor: 'var(--bg-tertiary)',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                fontWeight: 700,
                fontSize: '16px',
                color: 'var(--text-primary)'
              }}
            >
              {(skill.name[0] ?? '?').toUpperCase()}
            </div>
            <div style={{ flex: '1 1 auto', minWidth: 0 }}>
              <h2
                id="skill-detail-title"
                style={{
                  margin: 0,
                  fontSize: '18px',
                  fontWeight: 600,
                  color: 'var(--text-primary)',
                  wordBreak: 'break-word'
                }}
              >
                {skill.name}
              </h2>
              {skill.description ? (
                <p
                  style={{
                    margin: '6px 0 0',
                    fontSize: '13px',
                    color: 'var(--text-secondary)',
                    lineHeight: 1.4,
                    display: '-webkit-box',
                    WebkitLineClamp: 2,
                    WebkitBoxOrient: 'vertical',
                    overflow: 'hidden'
                  }}
                >
                  {skill.description}
                </p>
              ) : null}
              <p style={{ margin: '6px 0 0', fontSize: '12px', color: 'var(--text-dimmed)' }}>
                {skill.source === 'builtin' && t('skillDetail.builtInSubtitle')}
                {skill.source === 'workspace' && t('skillDetail.workspaceSubtitle')}
                {skill.source === 'user' && t('skillDetail.userSubtitle')}
              </p>
            </div>
            </div>
          </div>
          <div style={{ display: 'flex', alignItems: 'center', gap: '8px', flexShrink: 0 }}>
            <button
              type="button"
              onClick={() => void window.api.shell.openPath(skillDir)}
              style={secondaryBtn}
            >
              {t('skillDetail.openFolder')}
            </button>
            <button type="button" onClick={onClose} style={iconCloseBtn} aria-label={t('skillDetail.close')}>
              ×
            </button>
          </div>
        </header>

        <div
          style={{
            flex: 1,
            overflow: 'auto',
            padding: '16px 20px',
            minHeight: '200px'
          }}
        >
          {loading ? (
            <p style={{ color: 'var(--text-secondary)' }}>{t('skillDetail.loading')}</p>
          ) : (
            <MarkdownRenderer content={markdownBody} />
          )}
        </div>

        <footer
          style={{
            padding: '12px 20px',
            borderTop: '1px solid var(--border-default)',
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'center',
            flexShrink: 0,
            gap: '12px',
            flexWrap: 'wrap'
          }}
        >
          <button
            type="button"
            onClick={() => void onToggleEnabled(!skill.enabled)}
            style={skill.enabled ? dangerOutlineBtn : primaryBtn}
          >
            {skill.enabled ? t('skillDetail.disableWorkspace') : t('skillDetail.enableWorkspace')}
          </button>
          <button type="button" onClick={onClose} style={secondaryBtn}>
            {t('skillDetail.close')}
          </button>
        </footer>
      </div>
    </div>
  )
}

const secondaryBtn: React.CSSProperties = {
  padding: '6px 12px',
  fontSize: '13px',
  borderRadius: '6px',
  border: '1px solid var(--border-default)',
  backgroundColor: 'var(--bg-tertiary)',
  color: 'var(--text-primary)',
  cursor: 'pointer'
}

const primaryBtn: React.CSSProperties = {
  ...secondaryBtn,
  backgroundColor: 'var(--accent)',
  borderColor: 'var(--accent)',
  color: '#fff'
}

const dangerOutlineBtn: React.CSSProperties = {
  ...secondaryBtn,
  borderColor: 'var(--error)',
  color: 'var(--error)'
}

const iconCloseBtn: React.CSSProperties = {
  width: '32px',
  height: '32px',
  fontSize: '22px',
  lineHeight: 1,
  borderRadius: '6px',
  border: 'none',
  backgroundColor: 'transparent',
  color: 'var(--text-secondary)',
  cursor: 'pointer'
}
