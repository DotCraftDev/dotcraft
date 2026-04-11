import { useEffect, useState } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { addToast } from '../../stores/toastStore'
import { ToggleSwitch } from '../channels/ToggleSwitch'
import { FieldCard, FormActions, formStyles } from '../channels/FormShared'

interface Props {
  onBack(): void
}

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

function createDefaultConfig(): GitHubTrackerConfig {
  return {
    enabled: false,
    issuesWorkflowPath: 'WORKFLOW.md',
    pullRequestWorkflowPath: 'PR_WORKFLOW.md',
    tracker: {
      endpoint: null,
      apiKey: null,
      repository: null,
      activeStates: ['Todo', 'In Progress'],
      terminalStates: ['Done', 'Closed', 'Cancelled'],
      gitHubStateLabelPrefix: 'status:',
      assigneeFilter: null,
      pullRequestActiveStates: ['Pending Review', 'Review Requested', 'Changes Requested'],
      pullRequestTerminalStates: ['Merged', 'Closed', 'Approved']
    },
    polling: {
      intervalMs: 30_000
    },
    workspace: {
      root: null
    },
    agent: {
      maxConcurrentAgents: 3,
      maxTurns: 20,
      maxRetryBackoffMs: 300_000,
      turnTimeoutMs: 3_600_000,
      stallTimeoutMs: 300_000,
      maxConcurrentByState: {},
      maxConcurrentPullRequestAgents: 0
    },
    hooks: {
      afterCreate: null,
      beforeRun: null,
      afterRun: null,
      beforeRemove: null,
      timeoutMs: 60_000
    }
  }
}

function normalizeOptionalString(value: string): string | null {
  const trimmed = value.trim()
  return trimmed.length > 0 ? trimmed : null
}

export function GitHubTrackerConfigPanel({ onBack }: Props): JSX.Element {
  const t = useT()
  const [config, setConfig] = useState<GitHubTrackerConfig>(createDefaultConfig)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false

    async function load(): Promise<void> {
      setLoading(true)
      setError(null)
      try {
        const result = (await window.api.appServer.sendRequest('githubTracker/get', {})) as {
          config?: GitHubTrackerConfig
        }
        if (!cancelled) {
          setConfig(result.config ?? createDefaultConfig())
        }
      } catch (e: unknown) {
        if (!cancelled) setError(e instanceof Error ? e.message : String(e))
      } finally {
        if (!cancelled) setLoading(false)
      }
    }

    void load()
    return () => {
      cancelled = true
    }
  }, [])

  async function handleSave(): Promise<void> {
    setSaving(true)
    setError(null)
    try {
      const result = (await window.api.appServer.sendRequest('githubTracker/update', {
        config
      })) as { config?: GitHubTrackerConfig }
      setConfig(result.config ?? config)
      addToast(t('channels.savedRestart'), 'success')
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setSaving(false)
    }
  }

  function updateConfig(updater: (current: GitHubTrackerConfig) => GitHubTrackerConfig): void {
    setConfig((current) => updater(current))
  }

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        flex: 1,
        minHeight: 0,
        minWidth: 0,
        width: '100%'
      }}
    >
      <div
        style={{
          display: 'flex',
          flexDirection: 'column',
          gap: '12px',
          flex: 1,
          minHeight: 0,
          overflow: 'auto',
          boxSizing: 'border-box',
          paddingRight: '12px'
        }}
      >
        <button
          type="button"
          onClick={onBack}
          style={{
            display: 'inline-flex',
            alignItems: 'center',
            gap: '6px',
            alignSelf: 'flex-start',
            padding: '6px 10px',
            margin: 0,
            border: 'none',
            borderRadius: '8px',
            background: 'transparent',
            color: 'var(--text-secondary)',
            fontSize: '13px',
            fontWeight: 500,
            cursor: 'pointer'
          }}
        >
          <svg width="16" height="16" viewBox="0 0 24 24" aria-hidden fill="currentColor">
            <path d="M15.41 7.41L14 6l-6 6 6 6 1.41-1.41L10.83 12z" />
          </svg>
          {t('auto.githubConfig.back')}
        </button>

        <div style={formStyles.header}>
          <div
            aria-hidden="true"
            style={{
              ...formStyles.headerLogo,
              width: 32,
              height: 32,
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              color: 'var(--text-primary)'
            }}
          >
            <svg viewBox="0 0 16 16" width="18" height="18" fill="currentColor">
              <path d="M8 0C3.58 0 0 3.73 0 8.333c0 3.684 2.292 6.81 5.47 7.913.4.077.547-.179.547-.4 0-.197-.007-.845-.01-1.533-2.226.498-2.695-.98-2.695-.98-.364-.955-.89-1.209-.89-1.209-.727-.514.055-.504.055-.504.803.059 1.225.85 1.225.85.714 1.27 1.872.903 2.328.69.072-.533.279-.903.508-1.11-1.777-.209-3.644-.914-3.644-4.068 0-.899.31-1.635.818-2.211-.082-.209-.354-1.05.078-2.189 0 0 .668-.219 2.188.845A7.34 7.34 0 0 1 8 4.64c.68.003 1.366.095 2.006.279 1.52-1.064 2.186-.845 2.186-.845.434 1.139.162 1.98.08 2.189.51.576.818 1.312.818 2.211 0 3.162-1.87 3.857-3.652 4.062.287.256.543.759.543 1.53 0 1.104-.01 1.993-.01 2.264 0 .223.145.481.553.399C13.71 15.14 16 12.015 16 8.333 16 3.73 12.42 0 8 0Z" />
            </svg>
          </div>
          <div>
            <div style={formStyles.headerTitle}>{t('auto.githubConfig.title')}</div>
          </div>
        </div>

        {loading && (
          <FieldCard>
            <div style={{ fontSize: '13px', color: 'var(--text-secondary)' }}>{t('auto.githubConfig.loading')}</div>
          </FieldCard>
        )}

        {!loading && (
          <>
            <FieldCard>
              <div
                style={{
                  display: 'flex',
                  flexDirection: 'column',
                  gap: '10px',
                  fontSize: '12px',
                  color: 'var(--text-secondary)',
                  lineHeight: 1.5
                }}
              >
                <p style={{ margin: 0 }}>{t('auto.githubConfig.restartHint')}</p>
                <p style={{ margin: 0 }}>{t('auto.githubConfig.dashboardHint')}</p>
              </div>
            </FieldCard>

            <FieldCard>
              <ToggleSwitch
                checked={config.enabled}
                onChange={(checked) => updateConfig((current) => ({ ...current, enabled: checked }))}
                label={t('auto.githubConfig.enableGitHub')}
              />
            </FieldCard>

            <div style={{ opacity: config.enabled ? 1 : 0.5, pointerEvents: config.enabled ? 'auto' : 'none' }}>
              <FieldCard>
                <div style={formStyles.fieldGroup}>
                  <label style={formStyles.label}>{t('auto.githubConfig.repository')}</label>
                  <input
                    type="text"
                    value={config.tracker.repository ?? ''}
                    onChange={(e) =>
                      updateConfig((current) => ({
                        ...current,
                        tracker: { ...current.tracker, repository: normalizeOptionalString(e.target.value) }
                      }))}
                    placeholder="owner/repo"
                    style={formStyles.input}
                    onFocus={formStyles.inputFocus}
                    onBlur={formStyles.inputBlur}
                  />
                </div>

                <div style={formStyles.fieldGroup}>
                  <label style={formStyles.label}>{t('auto.githubConfig.apiKey')}</label>
                  <input
                    type="password"
                    value={config.tracker.apiKey ?? ''}
                    onChange={(e) =>
                      updateConfig((current) => ({
                        ...current,
                        tracker: { ...current.tracker, apiKey: normalizeOptionalString(e.target.value) }
                      }))}
                    style={formStyles.input}
                    onFocus={formStyles.inputFocus}
                    onBlur={formStyles.inputBlur}
                  />
                </div>

                <div style={formStyles.fieldGroup}>
                  <label style={formStyles.label}>{t('auto.githubConfig.issuesWorkflowPath')}</label>
                  <input
                    type="text"
                    value={config.issuesWorkflowPath}
                    onChange={(e) =>
                      updateConfig((current) => ({
                        ...current,
                        issuesWorkflowPath: e.target.value
                      }))}
                    style={formStyles.input}
                    onFocus={formStyles.inputFocus}
                    onBlur={formStyles.inputBlur}
                  />
                </div>

                <div style={{ ...formStyles.fieldGroup, marginBottom: 0 }}>
                  <label style={formStyles.label}>{t('auto.githubConfig.pullRequestWorkflowPath')}</label>
                  <input
                    type="text"
                    value={config.pullRequestWorkflowPath}
                    onChange={(e) =>
                      updateConfig((current) => ({
                        ...current,
                        pullRequestWorkflowPath: e.target.value
                      }))}
                    style={formStyles.input}
                    onFocus={formStyles.inputFocus}
                    onBlur={formStyles.inputBlur}
                  />
                </div>
              </FieldCard>
            </div>
          </>
        )}

        {error && (
          <div
            style={{
              padding: '10px 12px',
              borderRadius: '8px',
              backgroundColor: 'color-mix(in srgb, var(--error) 10%, transparent)',
              color: 'var(--error)',
              fontSize: '12px',
              lineHeight: 1.5
            }}
          >
            {error}
          </div>
        )}
      </div>

      <div style={{ flexShrink: 0, paddingTop: '8px' }}>
        <div style={{ opacity: loading ? 0.6 : 1, pointerEvents: loading ? 'none' : 'auto' }}>
          <FormActions saving={saving} onSave={() => void handleSave()} />
        </div>
      </div>
    </div>
  )
}
