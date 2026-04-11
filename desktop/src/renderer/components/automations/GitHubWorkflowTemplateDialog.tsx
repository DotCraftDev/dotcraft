import { useMemo, useState, type CSSProperties } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { addToast } from '../../stores/toastStore'
import {
  buildGitHubWorkflowTemplate,
  buildWorkflowCopyPath,
  getDefaultBeforeRunHook,
  resolveWorkflowAbsolutePath,
  type GitHubIssueWorkMode,
  type GitHubReviewStyle,
  type GitHubWorkflowTemplateKind
} from './githubWorkflowTemplates'

interface GitHubTrackerConfig {
  enabled: boolean
  issuesWorkflowPath: string
  pullRequestWorkflowPath: string
  tracker: {
    endpoint: string | null
    apiKey: string | null
    repository: string | null
    activeStates: string[]
    terminalStates: string[]
    gitHubStateLabelPrefix: string
    assigneeFilter: string | null
    pullRequestActiveStates: string[]
    pullRequestTerminalStates: string[]
  }
  polling: {
    intervalMs: number
  }
  workspace: {
    root: string | null
  }
  agent: {
    maxConcurrentAgents: number
    maxTurns: number
    maxRetryBackoffMs: number
    turnTimeoutMs: number
    stallTimeoutMs: number
    maxConcurrentByState: Record<string, number>
    maxConcurrentPullRequestAgents: number
  }
  hooks: {
    afterCreate: string | null
    beforeRun: string | null
    afterRun: string | null
    beforeRemove: string | null
    timeoutMs: number
  }
}

interface Props {
  config: GitHubTrackerConfig
  workspacePath: string
  initialKind: GitHubWorkflowTemplateKind
  onClose(): void
  onConfigSaved(config: GitHubTrackerConfig): void
}

type Step = 1 | 2 | 3
type ConflictAction = 'replace' | 'copy'

function dialogButtonStyle(primary = false): CSSProperties {
  return {
    padding: '8px 14px',
    borderRadius: '8px',
    border: primary ? 'none' : '1px solid var(--border-default)',
    backgroundColor: primary ? 'var(--accent)' : 'transparent',
    color: primary ? 'var(--on-accent)' : 'var(--text-primary)',
    fontSize: '13px',
    fontWeight: 600,
    cursor: 'pointer'
  }
}

function cardStyle(selected: boolean): CSSProperties {
  return {
    borderRadius: '10px',
    border: selected ? '1px solid var(--accent)' : '1px solid var(--border-default)',
    backgroundColor: selected ? 'var(--bg-tertiary)' : 'var(--bg-secondary)',
    padding: '14px',
    textAlign: 'left',
    cursor: 'pointer'
  }
}

export function GitHubWorkflowTemplateDialog({
  config,
  workspacePath,
  initialKind,
  onClose,
  onConfigSaved
}: Props): JSX.Element {
  const t = useT()
  const [step, setStep] = useState<Step>(1)
  const [kind, setKind] = useState<GitHubWorkflowTemplateKind>(initialKind)
  const [reviewStyle, setReviewStyle] = useState<GitHubReviewStyle>('balanced')
  const [issueWorkMode, setIssueWorkMode] = useState<GitHubIssueWorkMode>('plan-implement-pr')
  const [path, setPath] = useState(
    initialKind === 'pullRequest'
      ? config.pullRequestWorkflowPath || 'PR_WORKFLOW.md'
      : config.issuesWorkflowPath || 'WORKFLOW.md'
  )
  const [maxTurns, setMaxTurns] = useState(
    initialKind === 'pullRequest' ? Math.max(1, config.agent.maxTurns || 10) : Math.max(1, config.agent.maxTurns || 30)
  )
  const [concurrency, setConcurrency] = useState(
    initialKind === 'pullRequest'
      ? Math.max(1, config.agent.maxConcurrentPullRequestAgents || config.agent.maxConcurrentAgents || 2)
      : Math.max(1, config.agent.maxConcurrentAgents || 2)
  )
  const [activeStatesText, setActiveStatesText] = useState(
    (config.tracker.activeStates?.length ? config.tracker.activeStates : ['Todo', 'In Progress']).join(', ')
  )
  const [showAdvanced, setShowAdvanced] = useState(false)
  const [beforeRunHookEnabled, setBeforeRunHookEnabled] = useState(Boolean(config.hooks.beforeRun))
  const [beforeRunHook, setBeforeRunHook] = useState(
    config.hooks.beforeRun?.trim() || getDefaultBeforeRunHook(initialKind)
  )
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [conflictMessage, setConflictMessage] = useState<string | null>(null)

  const activeStates = useMemo(
    () =>
      activeStatesText
        .split(',')
        .map((value) => value.trim())
        .filter(Boolean),
    [activeStatesText]
  )

  const preview = useMemo(
    () =>
      buildGitHubWorkflowTemplate({
        kind,
        path,
        maxTurns,
        concurrency,
        beforeRunHook,
        reviewStyle,
        issueWorkMode,
        activeIssueStates: activeStates
      }),
    [activeStates, beforeRunHook, concurrency, issueWorkMode, kind, maxTurns, path, reviewStyle]
  )

  const targetAbsPath = resolveWorkflowAbsolutePath(workspacePath, path)
  const canProceed = path.trim().length > 0

  async function persistTemplate(conflictAction: ConflictAction): Promise<void> {
    const finalRelativePath = conflictAction === 'copy' ? buildWorkflowCopyPath(path) : path
    const finalAbsPath = resolveWorkflowAbsolutePath(workspacePath, finalRelativePath)
    const nextConfig: GitHubTrackerConfig = {
      ...config,
      issuesWorkflowPath: kind === 'issue' ? finalRelativePath : config.issuesWorkflowPath,
      pullRequestWorkflowPath: kind === 'pullRequest' ? finalRelativePath : config.pullRequestWorkflowPath,
      tracker: {
        ...config.tracker,
        activeStates: kind === 'issue' ? activeStates : config.tracker.activeStates
      },
      agent: {
        ...config.agent,
        maxTurns,
        maxConcurrentAgents: kind === 'issue' ? concurrency : config.agent.maxConcurrentAgents,
        maxConcurrentPullRequestAgents:
          kind === 'pullRequest' ? concurrency : config.agent.maxConcurrentPullRequestAgents
      },
      hooks: {
        ...config.hooks,
        beforeRun: beforeRunHookEnabled ? beforeRun.trim() : ''
      }
    }

    await window.api.file.writeFile(finalAbsPath, preview)
    const result = (await window.api.appServer.sendRequest('githubTracker/update', {
      config: nextConfig
    })) as { config?: GitHubTrackerConfig }
    onConfigSaved(result.config ?? nextConfig)
    addToast(
      kind === 'pullRequest'
        ? `PR workflow created at ${finalAbsPath}`
        : `Issue workflow created at ${finalAbsPath}`,
      'success'
    )
    if (!nextConfig.tracker.repository || !nextConfig.tracker.apiKey) {
      addToast(
        'Template created. GitHub automation will start only after repository and token are configured.',
        'info',
        6000
      )
    }
    onClose()
  }

  async function handleCreate(): Promise<void> {
    if (!canProceed) return
    setSubmitting(true)
    setError(null)
    try {
      const exists = await window.api.file.exists(targetAbsPath)
      if (exists) {
        setConflictMessage(`A workflow already exists at ${targetAbsPath}.`)
        return
      }
      await persistTemplate('replace')
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setSubmitting(false)
    }
  }

  async function handleConflictDecision(action: ConflictAction): Promise<void> {
    setSubmitting(true)
    setError(null)
    try {
      await persistTemplate(action)
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setSubmitting(false)
      setConflictMessage(null)
    }
  }

  return (
    <div
      style={{
        position: 'fixed',
        inset: 0,
        zIndex: 2000,
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        backgroundColor: 'var(--overlay-scrim)'
      }}
      onMouseDown={(e) => {
        if (e.target === e.currentTarget) onClose()
      }}
    >
      <div
        style={{
          width: '920px',
          maxWidth: 'calc(100vw - 40px)',
          maxHeight: 'calc(100vh - 40px)',
          display: 'flex',
          flexDirection: 'column',
          backgroundColor: 'var(--bg-primary)',
          borderRadius: '14px',
          border: '1px solid var(--border-default)',
          overflow: 'hidden'
        }}
      >
        <div
          style={{
            padding: '18px 20px',
            borderBottom: '1px solid var(--border-default)',
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'center'
          }}
        >
          <div>
            <div style={{ fontSize: '15px', fontWeight: 700, color: 'var(--text-primary)' }}>
              Workflow template
            </div>
            <div style={{ marginTop: '4px', fontSize: '12px', color: 'var(--text-secondary)' }}>
              GitHub Integration tells DotCraft which repo to watch. Workflow templates tell the agent how to review PRs or work on issues.
            </div>
          </div>
          <button type="button" onClick={onClose} style={dialogButtonStyle(false)}>
            {t('common.close')}
          </button>
        </div>

        <div style={{ padding: '10px 20px 0', fontSize: '12px', color: 'var(--text-secondary)' }}>
          Step {step} of 3
        </div>

        <div
          style={{
            display: 'grid',
            gridTemplateColumns: 'minmax(320px, 380px) minmax(0, 1fr)',
            gap: '16px',
            padding: '16px 20px 20px',
            overflow: 'auto'
          }}
        >
          <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
            {step === 1 && (
              <>
                <button type="button" onClick={() => setKind('pullRequest')} style={cardStyle(kind === 'pullRequest')}>
                  <div style={{ fontSize: '14px', fontWeight: 700, color: 'var(--text-primary)' }}>Review pull requests</div>
                  <div style={{ marginTop: '6px', fontSize: '12px', color: 'var(--text-secondary)', lineHeight: 1.5 }}>
                    Automatically reviews open PRs, leaves comments, and never approves, blocks, or merges.
                  </div>
                </button>
                <button type="button" onClick={() => setKind('issue')} style={cardStyle(kind === 'issue')}>
                  <div style={{ fontSize: '14px', fontWeight: 700, color: 'var(--text-primary)' }}>Implement GitHub issues</div>
                  <div style={{ marginTop: '6px', fontSize: '12px', color: 'var(--text-secondary)', lineHeight: 1.5 }}>
                    Plans work from GitHub issues, can implement code changes, and can open a PR for review.
                  </div>
                </button>
              </>
            )}

            {step === 2 && (
              <div
                style={{
                  borderRadius: '10px',
                  border: '1px solid var(--border-default)',
                  backgroundColor: 'var(--bg-secondary)',
                  padding: '16px'
                }}
              >
                <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
                  <label>
                    <div style={{ fontSize: '12px', fontWeight: 500, color: 'var(--text-secondary)', marginBottom: '6px' }}>
                      Workflow file path
                    </div>
                    <input
                      value={path}
                      onChange={(e) => setPath(e.target.value)}
                      style={{ width: '100%', boxSizing: 'border-box', ...inputStyle }}
                    />
                  </label>

                  {kind === 'pullRequest' ? (
                    <label>
                      <div style={{ fontSize: '12px', fontWeight: 500, color: 'var(--text-secondary)', marginBottom: '6px' }}>
                        Review style
                      </div>
                      <select value={reviewStyle} onChange={(e) => setReviewStyle(e.target.value as GitHubReviewStyle)} style={{ width: '100%', ...inputStyle }}>
                        <option value="strict">Strict review</option>
                        <option value="balanced">Balanced</option>
                        <option value="lightweight">Lightweight</option>
                      </select>
                    </label>
                  ) : (
                    <>
                      <label>
                        <div style={{ fontSize: '12px', fontWeight: 500, color: 'var(--text-secondary)', marginBottom: '6px' }}>
                          Work mode
                        </div>
                        <select
                          value={issueWorkMode}
                          onChange={(e) => setIssueWorkMode(e.target.value as GitHubIssueWorkMode)}
                          style={{ width: '100%', ...inputStyle }}
                        >
                          <option value="plan-implement-pr">Plan + implement + open PR</option>
                          <option value="plan-only">Plan only</option>
                        </select>
                      </label>
                      <label>
                        <div style={{ fontSize: '12px', fontWeight: 500, color: 'var(--text-secondary)', marginBottom: '6px' }}>
                          Active labels
                        </div>
                        <input
                          value={activeStatesText}
                          onChange={(e) => setActiveStatesText(e.target.value)}
                          style={{ width: '100%', boxSizing: 'border-box', ...inputStyle }}
                        />
                      </label>
                    </>
                  )}

                  <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '10px' }}>
                    <label>
                      <div style={{ fontSize: '12px', fontWeight: 500, color: 'var(--text-secondary)', marginBottom: '6px' }}>
                        Max turns
                      </div>
                      <input
                        type="number"
                        min={1}
                        value={maxTurns}
                        onChange={(e) => setMaxTurns(Math.max(1, Number(e.target.value) || 1))}
                        style={{ width: '100%', boxSizing: 'border-box', ...inputStyle }}
                      />
                    </label>
                    <label>
                      <div style={{ fontSize: '12px', fontWeight: 500, color: 'var(--text-secondary)', marginBottom: '6px' }}>
                        Concurrency
                      </div>
                      <input
                        type="number"
                        min={1}
                        value={concurrency}
                        onChange={(e) => setConcurrency(Math.max(1, Number(e.target.value) || 1))}
                        style={{ width: '100%', boxSizing: 'border-box', ...inputStyle }}
                      />
                    </label>
                  </div>

                  <button
                    type="button"
                    onClick={() => setShowAdvanced((value) => !value)}
                    style={{ ...dialogButtonStyle(false), width: '100%' }}
                  >
                    {showAdvanced ? 'Hide advanced options' : 'Show advanced options'}
                  </button>

                  {showAdvanced && (
                    <div style={{ padding: '12px', borderRadius: '8px', backgroundColor: 'var(--bg-tertiary)' }}>
                      <label style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '10px' }}>
                        <input
                          type="checkbox"
                          checked={beforeRunHookEnabled}
                          onChange={(e) => setBeforeRunHookEnabled(e.target.checked)}
                        />
                        <span style={{ fontSize: '12px', color: 'var(--text-secondary)' }}>Set git / GitHub identity before run</span>
                      </label>
                      <textarea
                        value={beforeRunHook}
                        onChange={(e) => setBeforeRunHook(e.target.value)}
                        rows={4}
                        disabled={!beforeRunHookEnabled}
                        style={{ width: '100%', boxSizing: 'border-box', ...textAreaStyle }}
                      />
                    </div>
                  )}
                </div>
              </div>
            )}

            {step === 3 && (
              <div
                style={{
                  borderRadius: '10px',
                  border: '1px solid var(--border-default)',
                  backgroundColor: 'var(--bg-secondary)',
                  padding: '16px',
                  display: 'flex',
                  flexDirection: 'column',
                  gap: '10px'
                }}
              >
                <SummaryLine label="Template type" value={kind === 'pullRequest' ? 'PR Review Workflow' : 'Issue Development Workflow'} />
                <SummaryLine label="Target file" value={targetAbsPath} />
                <SummaryLine
                  label="Will update config path"
                  value={kind === 'pullRequest' ? 'PullRequestWorkflowPath' : 'IssuesWorkflowPath'}
                />
                <SummaryLine
                  label="Overwrite behavior"
                  value="If the file already exists, you can replace it or create a copy instead."
                />
              </div>
            )}

            {error && (
              <div style={{ padding: '10px 12px', borderRadius: '8px', backgroundColor: 'color-mix(in srgb, var(--error) 10%, transparent)', color: 'var(--error)', fontSize: '12px' }}>
                {error}
              </div>
            )}
          </div>

          <div
            style={{
              borderRadius: '10px',
              border: '1px solid var(--border-default)',
              backgroundColor: 'var(--bg-secondary)',
              padding: '14px',
              minHeight: '420px',
              display: 'flex',
              flexDirection: 'column'
            }}
          >
            <div style={{ fontSize: '12px', fontWeight: 600, color: 'var(--text-secondary)', marginBottom: '10px' }}>
              Preview
            </div>
            <pre
              style={{
                margin: 0,
                flex: 1,
                overflow: 'auto',
                whiteSpace: 'pre-wrap',
                wordBreak: 'break-word',
                fontSize: '12px',
                lineHeight: 1.55,
                color: 'var(--text-primary)',
                fontFamily: 'Consolas, Monaco, monospace'
              }}
            >
              {preview}
            </pre>
          </div>
        </div>

        <div
          style={{
            padding: '14px 20px',
            borderTop: '1px solid var(--border-default)',
            display: 'flex',
            justifyContent: 'space-between',
            gap: '10px'
          }}
        >
          <div style={{ fontSize: '12px', color: 'var(--text-secondary)', display: 'flex', alignItems: 'center' }}>
            {kind === 'pullRequest'
              ? 'Review bot instructions will be created in your workspace.'
              : 'Issue bot instructions will be created in your workspace.'}
          </div>
          <div style={{ display: 'flex', gap: '8px' }}>
            {step > 1 && (
              <button type="button" onClick={() => setStep((step - 1) as Step)} style={dialogButtonStyle(false)}>
                Back
              </button>
            )}
            {step < 3 ? (
              <button
                type="button"
                disabled={!canProceed}
                onClick={() => setStep((step + 1) as Step)}
                style={{ ...dialogButtonStyle(true), opacity: canProceed ? 1 : 0.5 }}
              >
                Continue
              </button>
            ) : (
              <button type="button" disabled={submitting} onClick={() => void handleCreate()} style={dialogButtonStyle(true)}>
                {submitting ? 'Creating...' : 'Create template'}
              </button>
            )}
          </div>
        </div>
      </div>

      {conflictMessage && (
        <div
          style={{
            position: 'fixed',
            inset: 0,
            zIndex: 2100,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            backgroundColor: 'rgba(0,0,0,0.2)'
          }}
        >
          <div
            style={{
              width: '420px',
              maxWidth: 'calc(100vw - 48px)',
              backgroundColor: 'var(--bg-secondary)',
              borderRadius: '10px',
              border: '1px solid var(--border-default)',
              padding: '18px'
            }}
          >
            <div style={{ fontSize: '15px', fontWeight: 700, color: 'var(--text-primary)', marginBottom: '8px' }}>
              Replace existing template?
            </div>
            <div style={{ fontSize: '13px', color: 'var(--text-secondary)', lineHeight: 1.5, marginBottom: '16px' }}>
              {conflictMessage}
            </div>
            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '8px' }}>
              <button type="button" onClick={() => setConflictMessage(null)} style={dialogButtonStyle(false)}>
                Cancel
              </button>
              <button type="button" onClick={() => void handleConflictDecision('copy')} style={dialogButtonStyle(false)}>
                Create copy instead
              </button>
              <button type="button" onClick={() => void handleConflictDecision('replace')} style={dialogButtonStyle(true)}>
                Replace file
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}

function SummaryLine({ label, value }: { label: string; value: string }): JSX.Element {
  return (
    <div>
      <div style={{ fontSize: '11px', fontWeight: 600, color: 'var(--text-tertiary)', textTransform: 'uppercase' }}>
        {label}
      </div>
      <div style={{ marginTop: '4px', fontSize: '13px', color: 'var(--text-primary)', lineHeight: 1.5 }}>
        {value}
      </div>
    </div>
  )
}

const inputStyle: CSSProperties = {
  height: '36px',
  padding: '0 10px',
  borderRadius: '8px',
  border: '1px solid var(--border-default)',
  backgroundColor: 'var(--bg-primary)',
  color: 'var(--text-primary)',
  fontSize: '13px'
}

const textAreaStyle: CSSProperties = {
  padding: '8px 10px',
  borderRadius: '8px',
  border: '1px solid var(--border-default)',
  backgroundColor: 'var(--bg-primary)',
  color: 'var(--text-primary)',
  fontSize: '12px',
  resize: 'vertical',
  fontFamily: 'Consolas, Monaco, monospace'
}
