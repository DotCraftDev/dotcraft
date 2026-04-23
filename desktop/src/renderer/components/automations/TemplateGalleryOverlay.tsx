import { useEffect } from 'react'
import { useT } from '../../contexts/LocaleContext'
import {
  useAutomationsStore,
  type AutomationTemplate
} from '../../stores/automationsStore'

interface Props {
  onSelect(template: AutomationTemplate): void
  onClose(): void
}

/**
 * Modal gallery of built-in local templates. Used both from the AutomationsView templates strip
 * and from the New Task dialog's "Use template" button — both routes funnel to the same picker.
 */
export function TemplateGalleryOverlay({ onSelect, onClose }: Props): JSX.Element {
  const t = useT()
  const templates = useAutomationsStore((s) => s.templates)
  const templatesLoaded = useAutomationsStore((s) => s.templatesLoaded)
  const fetchTemplates = useAutomationsStore((s) => s.fetchTemplates)

  useEffect(() => {
    if (!templatesLoaded) void fetchTemplates()
  }, [templatesLoaded, fetchTemplates])

  return (
    <div
      onClick={onClose}
      style={{
        position: 'fixed',
        inset: 0,
        zIndex: 1100,
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        backgroundColor: 'rgba(0,0,0,0.5)'
      }}
    >
      <div
        onClick={(e) => e.stopPropagation()}
        style={{
          width: '640px',
          maxHeight: '80vh',
          display: 'flex',
          flexDirection: 'column',
          backgroundColor: 'var(--bg-primary)',
          border: '1px solid var(--border-default)',
          borderRadius: '10px',
          overflow: 'hidden',
          boxShadow: '0 8px 32px rgba(0,0,0,0.3)'
        }}
      >
        <div
          style={{
            padding: '14px 18px',
            borderBottom: '1px solid var(--border-default)',
            display: 'flex',
            flexDirection: 'column',
            gap: '4px'
          }}
        >
          <div style={{ fontSize: '15px', fontWeight: 600, color: 'var(--text-primary)' }}>
            {t('auto.templates.title')}
          </div>
          <div style={{ fontSize: '12px', color: 'var(--text-tertiary)' }}>
            {t('auto.templates.subtitle')}
          </div>
        </div>

        <div style={{ flex: 1, overflowY: 'auto', padding: '16px' }}>
          {templates.length === 0 && (
            <div
              style={{
                padding: '32px 16px',
                textAlign: 'center',
                fontSize: '13px',
                color: 'var(--text-tertiary)'
              }}
            >
              {t('auto.templates.empty')}
            </div>
          )}
          <div
            style={{
              display: 'grid',
              gap: '10px',
              gridTemplateColumns: 'repeat(auto-fill, minmax(260px, 1fr))'
            }}
          >
            {templates.map((tpl) => (
              <TemplateCard key={tpl.id} template={tpl} onSelect={onSelect} />
            ))}
          </div>
        </div>
      </div>
    </div>
  )
}

function TemplateCard({
  template,
  onSelect
}: {
  template: AutomationTemplate
  onSelect(tpl: AutomationTemplate): void
}): JSX.Element {
  const t = useT()
  return (
    <button
      type="button"
      onClick={() => onSelect(template)}
      style={{
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'flex-start',
        gap: '6px',
        padding: '12px',
        borderRadius: '10px',
        border: '1px solid var(--border-default)',
        backgroundColor: 'var(--bg-secondary)',
        color: 'var(--text-primary)',
        textAlign: 'left',
        cursor: 'pointer',
        minHeight: '96px'
      }}
      onMouseEnter={(e) => (e.currentTarget.style.borderColor = 'var(--accent)')}
      onMouseLeave={(e) => (e.currentTarget.style.borderColor = 'var(--border-default)')}
    >
      <div style={{ display: 'flex', alignItems: 'center', gap: '8px', width: '100%' }}>
        <span style={{ fontSize: '20px' }}>{template.icon ?? '✦'}</span>
        <span style={{ fontSize: '13px', fontWeight: 600, flex: 1 }}>{template.title}</span>
      </div>
      {template.description && (
        <span style={{ fontSize: '12px', color: 'var(--text-secondary)', lineHeight: 1.4 }}>
          {template.description}
        </span>
      )}
      {template.needsThreadBinding && (
        <span
          style={{
            marginTop: 'auto',
            fontSize: '11px',
            color: 'var(--accent)',
            fontWeight: 500
          }}
        >
          💬 {t('auto.templates.needsThread')}
        </span>
      )}
    </button>
  )
}
