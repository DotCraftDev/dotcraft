import { useEffect, useState, type CSSProperties, type ReactNode } from 'react'
import {
  CheckCircle2,
  ChevronRight,
  CircleDashed,
  ExternalLink,
  FilePlus2,
  RotateCcw
} from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { addToast } from '../../stores/toastStore'
import { ToggleSwitch } from '../channels/ToggleSwitch'
import { FormActions, SecretInput, formStyles } from '../channels/FormShared'
import { GitHubWorkflowTemplateDialog } from './GitHubWorkflowTemplateDialog'

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
  const [workspacePath, setWorkspacePath] = useState('')
  const [showTemplateDialog, setShowTemplateDialog] = useState<null | 'issue' | 'pullRequest'>(null)
  const [issueWorkflowExists, setIssueWorkflowExists] = useState(false)
  const [pullRequestWorkflowExists, setPullRequestWorkflowExists] = useState(false)

  useEffect(() => {
    let cancelled = false

    async function load(): Promise<void> {
      setLoading(true)
      setError(null)
      try {
        const wsPath = await window.api.window.getWorkspacePath()
        const result = (await window.api.appServer.sendRequest('githubTracker/get', {})) as {
          config?: GitHubTrackerConfig
        }
        if (!cancelled) {
          setWorkspacePath(wsPath)
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

  useEffect(() => {
    let cancelled = false

    async function refreshWorkflowState(): Promise<void> {
      if (!workspacePath) return
      try {
        const issuePath = resolveWorkflowPath(workspacePath, config.issuesWorkflowPath)
        const prPath = resolveWorkflowPath(workspacePath, config.pullRequestWorkflowPath)
        const [issueExists, prExists] = await Promise.all([
          issuePath ? window.api.file.exists(issuePath) : Promise.resolve(false),
          prPath ? window.api.file.exists(prPath) : Promise.resolve(false)
        ])
        if (!cancelled) {
          setIssueWorkflowExists(issueExists)
          setPullRequestWorkflowExists(prExists)
        }
      } catch {
        if (!cancelled) {
          setIssueWorkflowExists(false)
          setPullRequestWorkflowExists(false)
        }
      }
    }

    void refreshWorkflowState()
    return () => {
      cancelled = true
    }
  }, [config.issuesWorkflowPath, config.pullRequestWorkflowPath, workspacePath])

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

  async function handleOpenWorkflowFile(kind: 'issue' | 'pullRequest'): Promise<void> {
    const relativePath = kind === 'issue' ? config.issuesWorkflowPath : config.pullRequestWorkflowPath
    const absPath = resolveWorkflowPath(workspacePath, relativePath)
    if (!absPath) return
    const result = await window.api.shell.openPath(absPath)
    if (result) {
      addToast(result, 'error')
    }
  }

  const disabled = !config.enabled
  const showWorkflowEmptyState = config.enabled && !issueWorkflowExists && !pullRequestWorkflowExists

  return (
    <div style={panelShell}>
      <div style={scrollRegion}>
        <div style={contentFrame}>
          <button type="button" onClick={onBack} style={breadcrumbButton}>
            <span>{t('auto.viewTitle')}</span>
            <ChevronRight size={14} aria-hidden />
            <span style={{ color: 'var(--text-primary)' }}>GitHub</span>
          </button>

          <section style={heroPanel}>
            <div aria-hidden="true" style={githubLogoBox}>
              <GitHubMark size={20} />
            </div>
            <div style={{ minWidth: 0, flex: 1 }}>
              <div style={heroTitleRow}>
                <h2 style={heroTitle}>{t('auto.githubConfig.title')}</h2>
                <StatusPill
                  active={config.enabled}
                  label={config.enabled ? t('auto.githubConfig.statusEnabled') : t('auto.githubConfig.statusDisabled')}
                />
              </div>
              <p style={heroDescription}>{t('auto.githubConfig.subtitle')}</p>
            </div>
            <div style={{ flexShrink: 0, minWidth: 220 }}>
              <ToggleSwitch
                checked={config.enabled}
                onChange={(checked) => updateConfig((current) => ({ ...current, enabled: checked }))}
                label={t('auto.githubConfig.enableGitHub')}
              />
            </div>
          </section>

          <div style={infoStrip}>
            <span>{t('auto.githubConfig.restartHintShort')}</span>
            <span aria-hidden="true">·</span>
            <span>{t('auto.githubConfig.dashboardHintShort')}</span>
          </div>

          {loading && <div style={loadingBox}>{t('auto.githubConfig.loading')}</div>}

          {!loading && (
            <>
              <SectionCard
                title={t('auto.githubConfig.connectionTitle')}
                description={t('auto.githubConfig.connectionDescription')}
                disabled={disabled}
              >
                <div style={formGrid}>
                  <FieldBlock label={t('auto.githubConfig.repository')}>
                    <input
                      aria-label={t('auto.githubConfig.repository')}
                      disabled={disabled}
                      type="text"
                      value={config.tracker.repository ?? ''}
                      onChange={(e) =>
                        updateConfig((current) => ({
                          ...current,
                          tracker: { ...current.tracker, repository: normalizeOptionalString(e.target.value) }
                        }))}
                      placeholder="owner/repo"
                      style={inputStyle(disabled)}
                      onFocus={formStyles.inputFocus}
                      onBlur={formStyles.inputBlur}
                    />
                  </FieldBlock>

                  <FieldBlock label={t('auto.githubConfig.apiKey')}>
                    <SecretInput
                      ariaLabel={t('auto.githubConfig.apiKey')}
                      disabled={disabled}
                      value={config.tracker.apiKey ?? ''}
                      onChange={(nextValue) =>
                        updateConfig((current) => ({
                          ...current,
                          tracker: { ...current.tracker, apiKey: normalizeOptionalString(nextValue) }
                        }))}
                      onFocus={formStyles.inputFocus}
                      onBlur={formStyles.inputBlur}
                      style={inputStyle(disabled)}
                    />
                  </FieldBlock>
                </div>
              </SectionCard>

              <SectionCard
                title={t('auto.githubConfig.workflowTitle')}
                description={t('auto.githubConfig.workflowDescription')}
                disabled={disabled}
              >
                <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
                  <WorkflowPathRow
                    disabled={disabled}
                    label={t('auto.githubConfig.issuesWorkflowPath')}
                    value={config.issuesWorkflowPath}
                    exists={issueWorkflowExists}
                    onChange={(value) =>
                      updateConfig((current) => ({
                        ...current,
                        issuesWorkflowPath: value
                      }))}
                    onCreate={() => setShowTemplateDialog('issue')}
                    onOpen={() => void handleOpenWorkflowFile('issue')}
                  />
                  <WorkflowPathRow
                    disabled={disabled}
                    label={t('auto.githubConfig.pullRequestWorkflowPath')}
                    value={config.pullRequestWorkflowPath}
                    exists={pullRequestWorkflowExists}
                    onChange={(value) =>
                      updateConfig((current) => ({
                        ...current,
                        pullRequestWorkflowPath: value
                      }))}
                    onCreate={() => setShowTemplateDialog('pullRequest')}
                    onOpen={() => void handleOpenWorkflowFile('pullRequest')}
                  />
                </div>

                {showWorkflowEmptyState && (
                  <div style={emptyHint}>
                    <FilePlus2 size={16} aria-hidden />
                    <div style={{ minWidth: 0 }}>
                      <div style={emptyHintTitle}>{t('auto.githubConfig.workflowEmptyTitle')}</div>
                      <div style={emptyHintText}>{t('auto.githubConfig.workflowEmptyDescription')}</div>
                    </div>
                  </div>
                )}
              </SectionCard>
            </>
          )}

          {error && <div style={errorBox}>{error}</div>}
        </div>
      </div>

      <div style={footerBar}>
        <div style={contentFrame}>
          <div style={{ opacity: loading ? 0.6 : 1, pointerEvents: loading ? 'none' : 'auto' }}>
            <FormActions saving={saving} onSave={() => void handleSave()} />
          </div>
        </div>
      </div>

      {showTemplateDialog && workspacePath && (
        <GitHubWorkflowTemplateDialog
          config={config}
          workspacePath={workspacePath}
          initialKind={showTemplateDialog}
          onClose={() => setShowTemplateDialog(null)}
          onConfigSaved={(nextConfig) => {
            setConfig(nextConfig)
            setShowTemplateDialog(null)
          }}
        />
      )}
    </div>
  )
}

function SectionCard({
  title,
  description,
  disabled,
  children
}: {
  title: string
  description: string
  disabled: boolean
  children: ReactNode
}): JSX.Element {
  return (
    <section style={sectionCard}>
      <div style={sectionHeader}>
        <div>
          <h3 style={sectionTitle}>{title}</h3>
          <p style={sectionDescription}>{description}</p>
        </div>
      </div>
      <div style={{ opacity: disabled ? 0.58 : 1 }}>{children}</div>
    </section>
  )
}

function FieldBlock({ label, children }: { label: string; children: ReactNode }): JSX.Element {
  return (
    <label style={{ display: 'block', minWidth: 0 }}>
      <span style={fieldLabel}>{label}</span>
      {children}
    </label>
  )
}

function WorkflowPathRow({
  disabled,
  label,
  value,
  exists,
  onChange,
  onCreate,
  onOpen
}: {
  disabled: boolean
  label: string
  value: string
  exists: boolean
  onChange: (value: string) => void
  onCreate: () => void
  onOpen: () => void
}): JSX.Element {
  const t = useT()
  return (
    <div style={workflowRow}>
      <div style={{ minWidth: 0, flex: 1 }}>
        <div style={workflowLabelRow}>
          <span style={fieldLabel}>{label}</span>
          <WorkflowStatus exists={exists} />
        </div>
        <input
          aria-label={label}
          disabled={disabled}
          type="text"
          value={value}
          onChange={(e) => onChange(e.target.value)}
          style={inputStyle(disabled)}
          onFocus={formStyles.inputFocus}
          onBlur={formStyles.inputBlur}
        />
      </div>
      <div style={workflowActions}>
        {exists ? (
          <>
            <button type="button" disabled={disabled} onClick={onOpen} style={actionButtonStyle(disabled, false)}>
              <ExternalLink size={14} aria-hidden />
              {t('auto.githubConfig.openWorkflow')}
            </button>
            <button type="button" disabled={disabled} onClick={onCreate} style={actionButtonStyle(disabled, false)}>
              <RotateCcw size={14} aria-hidden />
              {t('auto.githubConfig.replaceTemplate')}
            </button>
          </>
        ) : (
          <button type="button" disabled={disabled} onClick={onCreate} style={actionButtonStyle(disabled, true)}>
            <FilePlus2 size={14} aria-hidden />
            {t('auto.githubConfig.createTemplate')}
          </button>
        )}
      </div>
    </div>
  )
}

function WorkflowStatus({ exists }: { exists: boolean }): JSX.Element {
  const t = useT()
  return (
    <span style={workflowStatusStyle(exists)}>
      {exists ? <CheckCircle2 size={12} aria-hidden /> : <CircleDashed size={12} aria-hidden />}
      {exists ? t('auto.githubConfig.workflowCreated') : t('auto.githubConfig.workflowMissing')}
    </span>
  )
}

function StatusPill({ active, label }: { active: boolean; label: string }): JSX.Element {
  return (
    <span style={statusPillStyle(active)}>
      <span aria-hidden style={statusDotStyle(active)} />
      {label}
    </span>
  )
}

function GitHubMark({ size }: { size: number }): JSX.Element {
  return (
    <svg viewBox="0 0 16 16" width={size} height={size} fill="currentColor" aria-hidden="true">
      <path d="M8 0C3.58 0 0 3.73 0 8.333c0 3.684 2.292 6.81 5.47 7.913.4.077.547-.179.547-.4 0-.197-.007-.845-.01-1.533-2.226.498-2.695-.98-2.695-.98-.364-.955-.89-1.209-.89-1.209-.727-.514.055-.504.055-.504.803.059 1.225.85 1.225.85.714 1.27 1.872.903 2.328.69.072-.533.279-.903.508-1.11-1.777-.209-3.644-.914-3.644-4.068 0-.899.31-1.635.818-2.211-.082-.209-.354-1.05.078-2.189 0 0 .668-.219 2.188.845A7.34 7.34 0 0 1 8 4.64c.68.003 1.366.095 2.006.279 1.52-1.064 2.186-.845 2.186-.845.434 1.139.162 1.98.08 2.189.51.576.818 1.312.818 2.211 0 3.162-1.87 3.857-3.652 4.062.287.256.543.759.543 1.53 0 1.104-.01 1.993-.01 2.264 0 .223.145.481.553.399C13.71 15.14 16 12.015 16 8.333 16 3.73 12.42 0 8 0Z" />
    </svg>
  )
}

function resolveWorkflowPath(workspacePath: string, relativePath: string): string {
  const trimmed = relativePath.trim()
  if (!workspacePath || trimmed.length === 0) return ''
  return `${workspacePath.replace(/[\\/]+$/, '')}/${trimmed.replace(/^[\\/]+/, '').replace(/[\\/]+/g, '/')}`
}

function inputStyle(disabled: boolean): CSSProperties {
  return {
    ...formStyles.input,
    height: '34px',
    borderRadius: '8px',
    background: 'var(--bg-primary)',
    opacity: disabled ? 0.65 : 1,
    cursor: disabled ? 'not-allowed' : 'text'
  }
}

function actionButtonStyle(disabled: boolean, primary: boolean): CSSProperties {
  return {
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    gap: 6,
    minHeight: '32px',
    padding: '0 10px',
    borderRadius: '8px',
    border: primary ? 'none' : '1px solid var(--border-default)',
    backgroundColor: primary ? 'var(--accent)' : 'var(--bg-secondary)',
    color: primary ? 'var(--on-accent)' : 'var(--text-primary)',
    fontSize: '12px',
    fontWeight: 600,
    whiteSpace: 'nowrap',
    cursor: disabled ? 'not-allowed' : 'pointer',
    opacity: disabled ? 0.6 : 1
  }
}

function workflowStatusStyle(exists: boolean): CSSProperties {
  return {
    display: 'inline-flex',
    alignItems: 'center',
    gap: 4,
    minHeight: 20,
    padding: '0 7px',
    borderRadius: 999,
    color: exists ? 'var(--success)' : 'var(--text-tertiary)',
    backgroundColor: exists
      ? 'color-mix(in srgb, var(--success) 14%, transparent)'
      : 'var(--bg-tertiary)',
    fontSize: '11px',
    fontWeight: 600,
    whiteSpace: 'nowrap'
  }
}

function statusPillStyle(active: boolean): CSSProperties {
  return {
    display: 'inline-flex',
    alignItems: 'center',
    gap: 6,
    minHeight: 22,
    padding: '0 8px',
    borderRadius: 999,
    backgroundColor: active
      ? 'color-mix(in srgb, var(--success) 13%, transparent)'
      : 'var(--bg-tertiary)',
    color: active ? 'var(--success)' : 'var(--text-secondary)',
    fontSize: '11px',
    fontWeight: 600
  }
}

function statusDotStyle(active: boolean): CSSProperties {
  return {
    width: 6,
    height: 6,
    borderRadius: '50%',
    backgroundColor: active ? 'var(--success)' : 'var(--text-dimmed)'
  }
}

const panelShell: CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  flex: 1,
  minHeight: 0,
  minWidth: 0,
  width: '100%'
}

const scrollRegion: CSSProperties = {
  flex: 1,
  minHeight: 0,
  overflow: 'auto',
  boxSizing: 'border-box',
  padding: '0 8px 4px'
}

const contentFrame: CSSProperties = {
  width: '100%',
  maxWidth: '820px',
  margin: '0 auto',
  boxSizing: 'border-box'
}

const breadcrumbButton: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  gap: 6,
  height: 32,
  padding: '0 4px',
  margin: '0 0 14px',
  border: 'none',
  borderRadius: 8,
  background: 'transparent',
  color: 'var(--text-secondary)',
  fontSize: '13px',
  fontWeight: 500,
  cursor: 'pointer'
}

const heroPanel: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  flexWrap: 'wrap',
  gap: 14,
  padding: '16px 0 18px',
  borderBottom: '1px solid var(--border-subtle)'
}

const githubLogoBox: CSSProperties = {
  width: 40,
  height: 40,
  borderRadius: 10,
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  color: 'var(--text-primary)',
  backgroundColor: 'var(--bg-secondary)',
  border: '1px solid var(--border-default)',
  flexShrink: 0
}

const heroTitleRow: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: 10,
  flexWrap: 'wrap'
}

const heroTitle: CSSProperties = {
  margin: 0,
  fontSize: '18px',
  lineHeight: 1.25,
  fontWeight: 700,
  color: 'var(--text-primary)'
}

const heroDescription: CSSProperties = {
  margin: '5px 0 0',
  fontSize: '13px',
  lineHeight: 1.4,
  color: 'var(--text-secondary)'
}

const infoStrip: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: 8,
  minHeight: 32,
  margin: '12px 0',
  color: 'var(--text-tertiary)',
  fontSize: '12px',
  lineHeight: 1.4,
  flexWrap: 'wrap'
}

const loadingBox: CSSProperties = {
  padding: '18px 0',
  color: 'var(--text-secondary)',
  fontSize: '13px'
}

const sectionCard: CSSProperties = {
  padding: '16px',
  marginBottom: 12,
  borderRadius: 10,
  border: '1px solid var(--border-default)',
  backgroundColor: 'var(--bg-secondary)'
}

const sectionHeader: CSSProperties = {
  display: 'flex',
  justifyContent: 'space-between',
  alignItems: 'flex-start',
  gap: 12,
  marginBottom: 14
}

const sectionTitle: CSSProperties = {
  margin: 0,
  fontSize: '14px',
  lineHeight: 1.3,
  fontWeight: 700,
  color: 'var(--text-primary)'
}

const sectionDescription: CSSProperties = {
  margin: '4px 0 0',
  color: 'var(--text-secondary)',
  fontSize: '12px',
  lineHeight: 1.4
}

const formGrid: CSSProperties = {
  display: 'grid',
  gridTemplateColumns: 'repeat(auto-fit, minmax(240px, 1fr))',
  gap: 12
}

const fieldLabel: CSSProperties = {
  display: 'block',
  marginBottom: 6,
  color: 'var(--text-secondary)',
  fontSize: '12px',
  fontWeight: 600
}

const workflowRow: CSSProperties = {
  display: 'flex',
  alignItems: 'flex-end',
  flexWrap: 'wrap',
  gap: 10,
  padding: '12px',
  borderRadius: 8,
  backgroundColor: 'var(--bg-primary)',
  border: '1px solid var(--border-subtle)'
}

const workflowLabelRow: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'space-between',
  gap: 8
}

const workflowActions: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: 8,
  flexWrap: 'wrap',
  justifyContent: 'flex-end',
  flexShrink: 0
}

const emptyHint: CSSProperties = {
  display: 'flex',
  alignItems: 'flex-start',
  gap: 10,
  marginTop: 12,
  padding: '10px 12px',
  borderRadius: 8,
  color: 'var(--text-secondary)',
  backgroundColor: 'var(--bg-tertiary)'
}

const emptyHintTitle: CSSProperties = {
  fontSize: '12px',
  fontWeight: 700,
  color: 'var(--text-primary)'
}

const emptyHintText: CSSProperties = {
  marginTop: 2,
  fontSize: '12px',
  lineHeight: 1.4
}

const errorBox: CSSProperties = {
  padding: '10px 12px',
  borderRadius: 8,
  backgroundColor: 'color-mix(in srgb, var(--error) 10%, transparent)',
  color: 'var(--error)',
  fontSize: '12px',
  lineHeight: 1.5
}

const footerBar: CSSProperties = {
  flexShrink: 0,
  padding: '10px 8px 0',
  borderTop: '1px solid var(--border-subtle)'
}
