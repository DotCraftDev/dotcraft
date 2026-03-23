import { useState, type CSSProperties } from 'react'
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

          <div style={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
            <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                <label
                  id="new-task-agent-workspace-label"
                  htmlFor="new-task-agent-workspace"
                  style={{ fontSize: '12px', fontWeight: 500, color: 'var(--text-secondary)' }}
                >
                  Agent workspace
                </label>
                <button
                  type="button"
                  aria-label="Agent workspace details"
                  aria-expanded={showWorkspaceHelp}
                  aria-controls={showWorkspaceHelp ? 'agent-workspace-details' : undefined}
                  title={showWorkspaceHelp ? 'Hide details' : 'Show details'}
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
                <option value="project">Project (open workspace root)</option>
                <option value="isolated">Isolated (sandbox under .craft/tasks)</option>
              </select>
              {showWorkspaceHelp && (
                <div
                  id="agent-workspace-details"
                  role="region"
                  aria-label="Agent workspace details"
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
                  <strong style={{ color: 'var(--text-primary)' }}>Project</strong>
                  <p style={{ margin: '4px 0 8px' }}>
                    The agent&apos;s file/shell tools use the current DotCraft workspace folder (your repo). Best for
                    real code changes.
                  </p>
                  <strong style={{ color: 'var(--text-primary)' }}>Isolated</strong>
                  <p style={{ margin: '4px 0 0' }}>
                    The agent runs in an empty folder under this task in <code style={{ fontSize: '11px' }}>.craft/tasks</code>.
                    Safer when you want containment; copy or generate files there before integrating.
                  </p>
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
                  Tool policy
                </label>
                <button
                  type="button"
                  aria-label="Tool policy details"
                  aria-expanded={showPolicyHelp}
                  aria-controls={showPolicyHelp ? 'tool-policy-details' : undefined}
                  title={showPolicyHelp ? 'Hide details' : 'Show details'}
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
                <option value="workspaceScope">Workspace scope (default, safer)</option>
                <option value="fullAuto">Full auto (higher risk)</option>
              </select>
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
                      File and shell tools may access paths inside the task&apos;s agent workspace only (see Agent
                      workspace above). Operations outside that boundary are rejected automatically (no prompts).
                    </p>
                  </div>
                  <div>
                    <strong style={{ color: 'var(--text-primary)' }}>Full auto</strong>
                    <p style={{ margin: '4px 0 0' }}>
                      File and shell tools can also target paths outside the agent workspace; those operations are
                      auto-approved without asking. Use only when you trust the workflow and accept higher risk.
                    </p>
                  </div>
                </div>
              )}
            </div>
          </div>

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
