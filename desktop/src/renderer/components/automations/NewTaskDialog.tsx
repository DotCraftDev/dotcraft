import { useState, type CSSProperties } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { useAutomationsStore } from '../../stores/automationsStore'

interface Props {
  onClose(): void
}

const selectStyle: CSSProperties = {
  padding: '8px 10px',
  borderRadius: '6px',
  border: '1px solid var(--border-default)',
  backgroundColor: 'var(--bg-secondary)',
  color: 'var(--text-primary)',
  fontSize: '13px',
  outline: 'none',
  width: '100%',
  cursor: 'pointer'
}

function helpButtonStyle(): CSSProperties {
  return {
    width: '22px',
    height: '22px',
    borderRadius: '50%',
    border: '1px solid var(--border-default)',
    backgroundColor: 'var(--bg-secondary)',
    color: 'var(--text-secondary)',
    fontSize: '12px',
    fontWeight: 700,
    cursor: 'pointer',
    lineHeight: 1,
    padding: 0,
    flexShrink: 0
  }
}

export function NewTaskDialog({ onClose }: Props): JSX.Element {
  const t = useT()
  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [workspaceMode, setWorkspaceMode] = useState<'project' | 'isolated'>('project')
  const [approvalPolicy, setApprovalPolicy] = useState<'workspaceScope' | 'fullAuto'>('workspaceScope')
  const [showWorkspaceHelp, setShowWorkspaceHelp] = useState(false)
  const [showPolicyHelp, setShowPolicyHelp] = useState(false)
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const createTask = useAutomationsStore((s) => s.createTask)

  const canSubmit = title.trim().length > 0 && description.trim().length > 0 && !submitting

  async function handleSubmit(): Promise<void> {
    if (!canSubmit) return
    setSubmitting(true)
    setError(null)
    try {
      await createTask(title.trim(), description.trim(), undefined, approvalPolicy, workspaceMode)
      onClose()
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div
      style={{
        position: 'fixed',
        inset: 0,
        zIndex: 1000,
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        backgroundColor: 'rgba(0,0,0,0.5)'
      }}
      onClick={onClose}
    >
      <div
        onClick={(e) => e.stopPropagation()}
        style={{
          width: '480px',
          maxHeight: '80vh',
          backgroundColor: 'var(--bg-primary)',
          borderRadius: '12px',
          border: '1px solid var(--border-default)',
          display: 'flex',
          flexDirection: 'column',
          overflow: 'hidden',
          boxShadow: '0 8px 32px rgba(0,0,0,0.3)'
        }}
      >
        <div
          style={{
            padding: '16px 20px',
            borderBottom: '1px solid var(--border-default)',
            fontSize: '15px',
            fontWeight: 600,
            color: 'var(--text-primary)'
          }}
        >
          {t('auto.newTask.title')}
        </div>

        <div style={{ padding: '16px 20px', display: 'flex', flexDirection: 'column', gap: '12px', overflow: 'auto' }}>
          <label style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
            <span style={{ fontSize: '12px', fontWeight: 500, color: 'var(--text-secondary)' }}>
              {t('auto.newTask.titleLabel')} <span style={{ color: 'var(--error)' }}>*</span>
            </span>
            <input
              type="text"
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              maxLength={120}
              placeholder={t('auto.newTask.namePlaceholder')}
              autoFocus
              style={{
                padding: '8px 10px',
                borderRadius: '6px',
                border: '1px solid var(--border-default)',
                backgroundColor: 'var(--bg-secondary)',
                color: 'var(--text-primary)',
                fontSize: '13px',
                outline: 'none'
              }}
            />
          </label>

          <div style={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
            <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                <label
                  id="new-task-agent-workspace-label"
                  htmlFor="new-task-agent-workspace"
                  style={{ fontSize: '12px', fontWeight: 500, color: 'var(--text-secondary)' }}
                >
                  {t('auto.newTask.agentWorkspaceLabel')}
                </label>
                <button
                  type="button"
                  aria-label={t('auto.newTask.agentWorkspaceLabel')}
                  aria-expanded={showWorkspaceHelp}
                  aria-controls={showWorkspaceHelp ? 'agent-workspace-details' : undefined}
                  title={showWorkspaceHelp ? t('auto.newTask.detailsHide') : t('auto.newTask.detailsShow')}
                  onClick={() => setShowWorkspaceHelp((v) => !v)}
                  style={helpButtonStyle()}
                >
                  ?
                </button>
              </div>
              <select
                id="new-task-agent-workspace"
                value={workspaceMode}
                onChange={(e) => setWorkspaceMode(e.target.value as 'project' | 'isolated')}
                aria-labelledby="new-task-agent-workspace-label"
                aria-describedby={showWorkspaceHelp ? 'agent-workspace-details' : undefined}
                style={selectStyle}
              >
                <option value="project">{t('auto.newTask.workspaceProject')}</option>
                <option value="isolated">{t('auto.newTask.workspaceIsolated')}</option>
              </select>
              {showWorkspaceHelp && (
                <div
                  id="agent-workspace-details"
                  role="region"
                  aria-label={t('auto.newTask.agentWorkspaceLabel')}
                  style={{
                    padding: '10px 12px',
                    borderRadius: '8px',
                    backgroundColor: 'var(--bg-secondary)',
                    border: '1px solid var(--border-default)',
                    fontSize: '12px',
                    color: 'var(--text-secondary)',
                    lineHeight: 1.5
                  }}
                >
                  <strong style={{ color: 'var(--text-primary)' }}>{t('auto.newTask.helpProjectTitle')}</strong>
                  <p style={{ margin: '4px 0 8px' }}>{t('auto.newTask.helpProjectBody')}</p>
                  <strong style={{ color: 'var(--text-primary)' }}>{t('auto.newTask.helpIsolatedTitle')}</strong>
                  <p style={{ margin: '4px 0 0' }}>{t('auto.newTask.helpIsolatedBody')}</p>
                </div>
              )}
            </div>

            <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                <label
                  id="new-task-tool-policy-label"
                  htmlFor="new-task-tool-policy"
                  style={{ fontSize: '12px', fontWeight: 500, color: 'var(--text-secondary)' }}
                >
                  {t('auto.newTask.toolPolicyLabel')}
                </label>
                <button
                  type="button"
                  aria-label={t('auto.newTask.toolPolicyLabel')}
                  aria-expanded={showPolicyHelp}
                  aria-controls={showPolicyHelp ? 'tool-policy-details' : undefined}
                  title={showPolicyHelp ? t('auto.newTask.detailsHide') : t('auto.newTask.detailsShow')}
                  onClick={() => setShowPolicyHelp((v) => !v)}
                  style={helpButtonStyle()}
                >
                  ?
                </button>
              </div>
              <select
                id="new-task-tool-policy"
                value={approvalPolicy}
                onChange={(e) => setApprovalPolicy(e.target.value as 'workspaceScope' | 'fullAuto')}
                aria-labelledby="new-task-tool-policy-label"
                aria-describedby={showPolicyHelp ? 'tool-policy-details' : undefined}
                style={selectStyle}
              >
                <option value="workspaceScope">{t('auto.newTask.policyWorkspace')}</option>
                <option value="fullAuto">{t('auto.newTask.policyFullAuto')}</option>
              </select>
              {showPolicyHelp && (
                <div
                  id="tool-policy-details"
                  role="region"
                  aria-label={t('auto.newTask.toolPolicyLabel')}
                  style={{
                    padding: '10px 12px',
                    borderRadius: '8px',
                    backgroundColor: 'var(--bg-secondary)',
                    border: '1px solid var(--border-default)',
                    fontSize: '12px',
                    color: 'var(--text-secondary)',
                    lineHeight: 1.5,
                    display: 'flex',
                    flexDirection: 'column',
                    gap: '10px'
                  }}
                >
                  <div>
                    <strong style={{ color: 'var(--text-primary)' }}>
                      {t('auto.newTask.policyHelpWorkspaceTitle')}
                    </strong>
                    <p style={{ margin: '4px 0 0' }}>{t('auto.review.policyWorkspace')}</p>
                  </div>
                  <div>
                    <strong style={{ color: 'var(--text-primary)' }}>{t('auto.newTask.policyHelpFullTitle')}</strong>
                    <p style={{ margin: '4px 0 0' }}>{t('auto.review.policyFullAuto')}</p>
                  </div>
                </div>
              )}
            </div>
          </div>

          <label style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
            <span style={{ fontSize: '12px', fontWeight: 500, color: 'var(--text-secondary)' }}>
              {t('auto.newTask.descriptionLabel')} <span style={{ color: 'var(--error)' }}>*</span>
            </span>
            <textarea
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              rows={6}
              placeholder={t('auto.newTask.promptPlaceholder')}
              style={{
                padding: '8px 10px',
                borderRadius: '6px',
                border: '1px solid var(--border-default)',
                backgroundColor: 'var(--bg-secondary)',
                color: 'var(--text-primary)',
                fontSize: '13px',
                resize: 'vertical',
                outline: 'none',
                fontFamily: 'inherit'
              }}
            />
          </label>

          {error && (
            <div
              style={{
                padding: '8px 10px',
                borderRadius: '6px',
                backgroundColor: 'color-mix(in srgb, var(--error) 10%, transparent)',
                color: 'var(--error)',
                fontSize: '12px'
              }}
            >
              {error}
            </div>
          )}
        </div>

        <div
          style={{
            padding: '12px 20px',
            borderTop: '1px solid var(--border-default)',
            display: 'flex',
            justifyContent: 'flex-end',
            gap: '8px'
          }}
        >
          <button
            type="button"
            onClick={onClose}
            style={{
              padding: '6px 14px',
              borderRadius: '6px',
              border: '1px solid var(--border-default)',
              backgroundColor: 'transparent',
              color: 'var(--text-secondary)',
              fontSize: '13px',
              cursor: 'pointer'
            }}
          >
            {t('common.cancel')}
          </button>
          <button
            type="button"
            onClick={handleSubmit}
            disabled={!canSubmit}
            style={{
              padding: '6px 14px',
              borderRadius: '6px',
              border: 'none',
              backgroundColor: canSubmit ? 'var(--accent)' : 'var(--bg-tertiary)',
              color: canSubmit ? '#fff' : 'var(--text-tertiary)',
              fontSize: '13px',
              fontWeight: 600,
              cursor: canSubmit ? 'pointer' : 'default',
              opacity: submitting ? 0.7 : 1
            }}
          >
            {submitting ? t('auto.newTask.creating') : t('auto.newTask.create')}
          </button>
        </div>
      </div>
    </div>
  )
}
