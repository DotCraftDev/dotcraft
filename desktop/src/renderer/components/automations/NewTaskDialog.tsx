import { useState } from 'react'
import { useAutomationsStore } from '../../stores/automationsStore'

interface Props {
  onClose(): void
}

export function NewTaskDialog({ onClose }: Props): JSX.Element {
  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [approvalPolicy, setApprovalPolicy] = useState<'workspaceScope' | 'fullAuto'>('workspaceScope')
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
      await createTask(title.trim(), description.trim(), undefined, approvalPolicy)
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
          New Automation Task
        </div>

        <div style={{ padding: '16px 20px', display: 'flex', flexDirection: 'column', gap: '12px', overflow: 'auto' }}>
          <label style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
            <span style={{ fontSize: '12px', fontWeight: 500, color: 'var(--text-secondary)' }}>
              Title <span style={{ color: 'var(--error)' }}>*</span>
            </span>
            <input
              type="text"
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              maxLength={120}
              placeholder="e.g. Implement feature X"
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

          <fieldset
            style={{
              margin: 0,
              padding: 0,
              border: 'none',
              display: 'flex',
              flexDirection: 'column',
              gap: '8px'
            }}
            aria-describedby={showPolicyHelp ? 'tool-policy-details' : undefined}
          >
            <legend style={{ display: 'flex', alignItems: 'center', gap: '6px', padding: 0, width: '100%' }}>
              <span style={{ fontSize: '12px', fontWeight: 500, color: 'var(--text-secondary)' }}>
                Tool policy
              </span>
              <button
                type="button"
                aria-label="Tool policy details"
                aria-expanded={showPolicyHelp}
                aria-controls={showPolicyHelp ? 'tool-policy-details' : undefined}
                title={showPolicyHelp ? 'Hide details' : 'Show details'}
                onClick={() => setShowPolicyHelp((v) => !v)}
                style={{
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
                  padding: 0
                }}
              >
                ?
              </button>
            </legend>
            <label
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: '8px',
                fontSize: '13px',
                color: 'var(--text-primary)',
                cursor: 'pointer'
              }}
            >
              <input
                type="radio"
                name="approvalPolicy"
                checked={approvalPolicy === 'workspaceScope'}
                onChange={() => setApprovalPolicy('workspaceScope')}
              />
              <span>
                <strong>Workspace scope</strong>
                <span style={{ color: 'var(--text-tertiary)', fontWeight: 400 }}> (default)</span>
              </span>
            </label>
            <label
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: '8px',
                fontSize: '13px',
                color: 'var(--text-primary)',
                cursor: 'pointer'
              }}
            >
              <input
                type="radio"
                name="approvalPolicy"
                checked={approvalPolicy === 'fullAuto'}
                onChange={() => setApprovalPolicy('fullAuto')}
              />
              <span>
                <strong>Full auto</strong>
                <span style={{ color: 'var(--text-tertiary)', fontWeight: 400 }}> (higher risk)</span>
              </span>
            </label>
            {showPolicyHelp && (
              <div
                id="tool-policy-details"
                role="region"
                aria-label="Tool policy details"
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
                  <strong style={{ color: 'var(--text-primary)' }}>Workspace scope (default)</strong>
                  <p style={{ margin: '4px 0 0' }}>
                    File and shell tools may access paths inside the task&apos;s agent workspace only. Operations
                    outside that boundary are rejected automatically (no prompts). Safer for typical automation.
                  </p>
                </div>
                <div>
                  <strong style={{ color: 'var(--text-primary)' }}>Full auto</strong>
                  <p style={{ margin: '4px 0 0' }}>
                    File and shell tools can also target paths outside the agent workspace; those operations are
                    auto-approved without asking. Use only when you trust the workflow and accept higher risk.
                  </p>
                </div>
                <div>
                  <strong style={{ color: 'var(--text-primary)' }}>Agent workspace vs workflow</strong>
                  <p style={{ margin: '4px 0 0' }}>
                    The agent workspace root comes from the task workflow (project root by default; see{' '}
                    <code style={{ fontSize: '11px' }}>workspace: isolated</code> for a sandbox under{' '}
                    <code style={{ fontSize: '11px' }}>.craft/tasks</code>). This setting is separate from that
                    layout and only controls outside-workspace tool behavior.
                  </p>
                </div>
              </div>
            )}
          </fieldset>

          <label style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
            <span style={{ fontSize: '12px', fontWeight: 500, color: 'var(--text-secondary)' }}>
              Description <span style={{ color: 'var(--error)' }}>*</span>
            </span>
            <textarea
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              rows={6}
              placeholder="Describe what the agent should do. Markdown supported."
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
            Cancel
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
            {submitting ? 'Creating...' : 'Create Task'}
          </button>
        </div>
      </div>
    </div>
  )
}
