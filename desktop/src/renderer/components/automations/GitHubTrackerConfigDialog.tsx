import { useEffect, useState, type CSSProperties } from 'react'
import { useT } from '../../contexts/LocaleContext'

interface Props {
  onClose(): void
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

function cardStyle(): CSSProperties {
  return {
    border: '1px solid var(--border-default)',
    borderRadius: '10px',
    background: 'var(--bg-secondary)',
    padding: '14px'
  }
}

function sectionLabelStyle(): CSSProperties {
  return {
    display: 'block',
    fontSize: '12px',
    fontWeight: 600,
    color: 'var(--text-secondary)',
    marginBottom: '6px'
  }
}

function inputStyle(mono = false): CSSProperties {
  return {
    width: '100%',
    boxSizing: 'border-box',
    padding: '8px 10px',
    fontSize: '13px',
    borderRadius: '8px',
    border: '1px solid var(--border-default)',
    background: 'var(--bg-primary)',
    color: 'var(--text-primary)',
    outline: 'none',
    fontFamily: mono ? 'var(--font-mono)' : undefined
  }
}

function secondaryButtonStyle(): CSSProperties {
  return {
    padding: '8px 14px',
    border: '1px solid var(--border-default)',
    borderRadius: '8px',
    background: 'transparent',
    color: 'var(--text-primary)',
    fontSize: '13px',
    fontWeight: 500,
    cursor: 'pointer'
  }
}

function primaryButtonStyle(disabled = false): CSSProperties {
  return {
    padding: '8px 14px',
    border: 'none',
    borderRadius: '8px',
    background: 'var(--accent)',
    color: 'var(--on-accent)',
    fontSize: '13px',
    fontWeight: 600,
    cursor: disabled ? 'default' : 'pointer',
    opacity: disabled ? 0.7 : 1
  }
}

function sectionSummaryStyle(): CSSProperties {
  return {
    cursor: 'pointer',
    fontSize: '13px',
    fontWeight: 700,
    color: 'var(--text-primary)'
  }
}

function normalizeOptionalString(value: string): string | null {
  const trimmed = value.trim()
  return trimmed.length > 0 ? trimmed : null
}

function parseInteger(value: string, fallback: number): number {
  const parsed = Number.parseInt(value, 10)
  return Number.isFinite(parsed) ? parsed : fallback
}

function StringListEditor({
  values,
  onChange,
  placeholder,
  addLabel,
  removeLabel
}: {
  values: string[]
  onChange(values: string[]): void
  placeholder: string
  addLabel: string
  removeLabel: string
}): JSX.Element {
  const rows = values.length > 0 ? values : ['']

  function updateRow(index: number, nextValue: string): void {
    onChange(rows.map((row, rowIndex) => (rowIndex === index ? nextValue : row)))
  }

  function removeRow(index: number): void {
    const nextRows = rows.filter((_, rowIndex) => rowIndex !== index)
    onChange(nextRows.length > 0 ? nextRows : [''])
  }

  function addRow(): void {
    onChange([...rows, ''])
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
      {rows.map((value, index) => (
        <div key={`${index}-${value}`} style={{ display: 'grid', gridTemplateColumns: '1fr auto', gap: '8px' }}>
          <input
            type="text"
            value={value}
            onChange={(e) => updateRow(index, e.target.value)}
            placeholder={placeholder}
            style={inputStyle(true)}
          />
          <button type="button" onClick={() => removeRow(index)} style={secondaryButtonStyle()}>
            {removeLabel}
          </button>
        </div>
      ))}
      <button type="button" onClick={addRow} style={secondaryButtonStyle()}>
        {addLabel}
      </button>
    </div>
  )
}

function NumberMapEditor({
  values,
  onChange,
  keyPlaceholder,
  valuePlaceholder,
  addLabel,
  removeLabel
}: {
  values: Record<string, number>
  onChange(values: Record<string, number>): void
  keyPlaceholder: string
  valuePlaceholder: string
  addLabel: string
  removeLabel: string
}): JSX.Element {
  const [rows, setRows] = useState<Array<{ id: string; key: string; value: string }>>([])
  const valueSignature = JSON.stringify(
    Object.entries(values).sort(([left], [right]) => left.localeCompare(right))
  )

  useEffect(() => {
    const entries = Object.entries(values)
    setRows(
      entries.length > 0
        ? entries.map(([key, value], index) => ({ id: `${index}-${key}`, key, value: String(value) }))
        : [{ id: 'empty-0', key: '', value: '' }]
    )
  }, [valueSignature])

  function pushRows(nextRows: Array<{ id: string; key: string; value: string }>): void {
    setRows(nextRows)
    const nextMap: Record<string, number> = {}
    for (const row of nextRows) {
      const key = row.key.trim()
      const value = Number.parseInt(row.value, 10)
      if (!key || !Number.isFinite(value)) continue
      nextMap[key] = value
    }
    onChange(nextMap)
  }

  function updateRow(index: number, patch: Partial<{ key: string; value: string }>): void {
    pushRows(rows.map((row, rowIndex) => (rowIndex === index ? { ...row, ...patch } : row)))
  }

  function removeRow(index: number): void {
    const nextRows = rows.filter((_, rowIndex) => rowIndex !== index)
    pushRows(nextRows.length > 0 ? nextRows : [{ id: 'empty-0', key: '', value: '' }])
  }

  function addRow(): void {
    pushRows([...rows, { id: `new-${rows.length}-${Date.now()}`, key: '', value: '' }])
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
      {rows.map((row, index) => (
        <div key={`${index}-${row.key}-${row.value}`} style={{ display: 'grid', gridTemplateColumns: '1fr 140px auto', gap: '8px' }}>
          <input
            type="text"
            value={row.key}
            onChange={(e) => updateRow(index, { key: e.target.value })}
            placeholder={keyPlaceholder}
            style={inputStyle(true)}
          />
          <input
            type="number"
            value={row.value}
            onChange={(e) => updateRow(index, { value: e.target.value })}
            placeholder={valuePlaceholder}
            style={inputStyle(true)}
          />
          <button type="button" onClick={() => removeRow(index)} style={secondaryButtonStyle()}>
            {removeLabel}
          </button>
        </div>
      ))}
      <button type="button" onClick={addRow} style={secondaryButtonStyle()}>
        {addLabel}
      </button>
    </div>
  )
}

export function GitHubTrackerConfigDialog({ onClose }: Props): JSX.Element {
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
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setSaving(false)
    }
  }

  function updateConfig(updater: (current: GitHubTrackerConfig) => GitHubTrackerConfig): void {
    setConfig((current) => updater(current))
  }

  function renderTextField(
    label: string,
    value: string | null,
    onChange: (value: string) => void,
    options?: { mono?: boolean; password?: boolean; placeholder?: string }
  ): JSX.Element {
    return (
      <label style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
        <span style={sectionLabelStyle()}>{label}</span>
        <input
          type={options?.password ? 'password' : 'text'}
          value={value ?? ''}
          onChange={(e) => onChange(e.target.value)}
          placeholder={options?.placeholder}
          style={inputStyle(options?.mono)}
        />
      </label>
    )
  }

  function renderNumberField(
    label: string,
    value: number,
    onChange: (value: number) => void
  ): JSX.Element {
    return (
      <label style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
        <span style={sectionLabelStyle()}>{label}</span>
        <input
          type="number"
          value={String(value)}
          onChange={(e) => onChange(parseInteger(e.target.value, value))}
          style={inputStyle(true)}
        />
      </label>
    )
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
        backgroundColor: 'rgba(0, 0, 0, 0.5)'
      }}
      onClick={onClose}
    >
      <div
        onClick={(e) => e.stopPropagation()}
        style={{
          width: 'min(920px, calc(100vw - 40px))',
          maxHeight: '85vh',
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
          {t('auto.githubConfig.title')}
        </div>

        <div style={{ padding: '16px 20px', overflow: 'auto', display: 'flex', flexDirection: 'column', gap: '12px' }}>
          {loading && (
            <div style={cardStyle()}>
              <div style={{ fontSize: '13px', color: 'var(--text-secondary)' }}>{t('auto.githubConfig.loading')}</div>
            </div>
          )}

          {!loading && (
            <>
              <div
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
                {t('auto.githubConfig.restartHint')}
              </div>

              <details open style={cardStyle()}>
                <summary style={sectionSummaryStyle()}>{t('auto.githubConfig.section.general')}</summary>
                <div style={{ marginTop: '12px', display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px' }}>
                  <label
                    style={{
                      display: 'flex',
                      alignItems: 'center',
                      gap: '8px',
                      color: 'var(--text-primary)',
                      fontSize: '13px'
                    }}
                  >
                    <input
                      type="checkbox"
                      checked={config.enabled}
                      onChange={(e) => updateConfig((current) => ({ ...current, enabled: e.target.checked }))}
                    />
                    {t('auto.githubConfig.enabled')}
                  </label>
                  <div />
                  {renderTextField(t('auto.githubConfig.issuesWorkflowPath'), config.issuesWorkflowPath, (value) =>
                    updateConfig((current) => ({ ...current, issuesWorkflowPath: value })), { mono: true })}
                  {renderTextField(t('auto.githubConfig.pullRequestWorkflowPath'), config.pullRequestWorkflowPath, (value) =>
                    updateConfig((current) => ({ ...current, pullRequestWorkflowPath: value })), { mono: true })}
                </div>
              </details>

              <details open style={cardStyle()}>
                <summary style={sectionSummaryStyle()}>{t('auto.githubConfig.section.tracker')}</summary>
                <div style={{ marginTop: '12px', display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px' }}>
                  {renderTextField(t('auto.githubConfig.endpoint'), config.tracker.endpoint, (value) =>
                    updateConfig((current) => ({
                      ...current,
                      tracker: { ...current.tracker, endpoint: normalizeOptionalString(value) }
                    })), { mono: true })}
                  {renderTextField(t('auto.githubConfig.apiKey'), config.tracker.apiKey, (value) =>
                    updateConfig((current) => ({
                      ...current,
                      tracker: { ...current.tracker, apiKey: normalizeOptionalString(value) }
                    })), { mono: true, password: true })}
                  {renderTextField(t('auto.githubConfig.repository'), config.tracker.repository, (value) =>
                    updateConfig((current) => ({
                      ...current,
                      tracker: { ...current.tracker, repository: normalizeOptionalString(value) }
                    })), { mono: true, placeholder: 'owner/repo' })}
                  {renderTextField(t('auto.githubConfig.gitHubStateLabelPrefix'), config.tracker.gitHubStateLabelPrefix, (value) =>
                    updateConfig((current) => ({
                      ...current,
                      tracker: { ...current.tracker, gitHubStateLabelPrefix: value }
                    })), { mono: true })}
                  {renderTextField(t('auto.githubConfig.assigneeFilter'), config.tracker.assigneeFilter, (value) =>
                    updateConfig((current) => ({
                      ...current,
                      tracker: { ...current.tracker, assigneeFilter: normalizeOptionalString(value) }
                    })), { mono: true })}
                </div>
                <div style={{ marginTop: '12px', display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px' }}>
                  <div>
                    <div style={sectionLabelStyle()}>{t('auto.githubConfig.activeStates')}</div>
                    <StringListEditor
                      values={config.tracker.activeStates}
                      onChange={(values) =>
                        updateConfig((current) => ({
                          ...current,
                          tracker: { ...current.tracker, activeStates: values }
                        }))}
                      placeholder={t('auto.githubConfig.stringValuePlaceholder')}
                      addLabel={t('auto.githubConfig.addValue')}
                      removeLabel={t('auto.githubConfig.removeValue')}
                    />
                  </div>
                  <div>
                    <div style={sectionLabelStyle()}>{t('auto.githubConfig.terminalStates')}</div>
                    <StringListEditor
                      values={config.tracker.terminalStates}
                      onChange={(values) =>
                        updateConfig((current) => ({
                          ...current,
                          tracker: { ...current.tracker, terminalStates: values }
                        }))}
                      placeholder={t('auto.githubConfig.stringValuePlaceholder')}
                      addLabel={t('auto.githubConfig.addValue')}
                      removeLabel={t('auto.githubConfig.removeValue')}
                    />
                  </div>
                  <div>
                    <div style={sectionLabelStyle()}>{t('auto.githubConfig.pullRequestActiveStates')}</div>
                    <StringListEditor
                      values={config.tracker.pullRequestActiveStates}
                      onChange={(values) =>
                        updateConfig((current) => ({
                          ...current,
                          tracker: { ...current.tracker, pullRequestActiveStates: values }
                        }))}
                      placeholder={t('auto.githubConfig.stringValuePlaceholder')}
                      addLabel={t('auto.githubConfig.addValue')}
                      removeLabel={t('auto.githubConfig.removeValue')}
                    />
                  </div>
                  <div>
                    <div style={sectionLabelStyle()}>{t('auto.githubConfig.pullRequestTerminalStates')}</div>
                    <StringListEditor
                      values={config.tracker.pullRequestTerminalStates}
                      onChange={(values) =>
                        updateConfig((current) => ({
                          ...current,
                          tracker: { ...current.tracker, pullRequestTerminalStates: values }
                        }))}
                      placeholder={t('auto.githubConfig.stringValuePlaceholder')}
                      addLabel={t('auto.githubConfig.addValue')}
                      removeLabel={t('auto.githubConfig.removeValue')}
                    />
                  </div>
                </div>
              </details>

              <details open style={cardStyle()}>
                <summary style={sectionSummaryStyle()}>{t('auto.githubConfig.section.polling')}</summary>
                <div style={{ marginTop: '12px' }}>
                  {renderNumberField(t('auto.githubConfig.intervalMs'), config.polling.intervalMs, (value) =>
                    updateConfig((current) => ({
                      ...current,
                      polling: { ...current.polling, intervalMs: value }
                    })))}
                </div>
              </details>

              <details open style={cardStyle()}>
                <summary style={sectionSummaryStyle()}>{t('auto.githubConfig.section.workspace')}</summary>
                <div style={{ marginTop: '12px' }}>
                  {renderTextField(t('auto.githubConfig.workspaceRoot'), config.workspace.root, (value) =>
                    updateConfig((current) => ({
                      ...current,
                      workspace: { ...current.workspace, root: normalizeOptionalString(value) }
                    })), { mono: true })}
                </div>
              </details>

              <details open style={cardStyle()}>
                <summary style={sectionSummaryStyle()}>{t('auto.githubConfig.section.agent')}</summary>
                <div style={{ marginTop: '12px', display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px' }}>
                  {renderNumberField(t('auto.githubConfig.maxConcurrentAgents'), config.agent.maxConcurrentAgents, (value) =>
                    updateConfig((current) => ({
                      ...current,
                      agent: { ...current.agent, maxConcurrentAgents: value }
                    })))}
                  {renderNumberField(t('auto.githubConfig.maxTurns'), config.agent.maxTurns, (value) =>
                    updateConfig((current) => ({
                      ...current,
                      agent: { ...current.agent, maxTurns: value }
                    })))}
                  {renderNumberField(t('auto.githubConfig.maxRetryBackoffMs'), config.agent.maxRetryBackoffMs, (value) =>
                    updateConfig((current) => ({
                      ...current,
                      agent: { ...current.agent, maxRetryBackoffMs: value }
                    })))}
                  {renderNumberField(t('auto.githubConfig.turnTimeoutMs'), config.agent.turnTimeoutMs, (value) =>
                    updateConfig((current) => ({
                      ...current,
                      agent: { ...current.agent, turnTimeoutMs: value }
                    })))}
                  {renderNumberField(t('auto.githubConfig.stallTimeoutMs'), config.agent.stallTimeoutMs, (value) =>
                    updateConfig((current) => ({
                      ...current,
                      agent: { ...current.agent, stallTimeoutMs: value }
                    })))}
                  {renderNumberField(
                    t('auto.githubConfig.maxConcurrentPullRequestAgents'),
                    config.agent.maxConcurrentPullRequestAgents,
                    (value) =>
                      updateConfig((current) => ({
                        ...current,
                        agent: { ...current.agent, maxConcurrentPullRequestAgents: value }
                      }))
                  )}
                </div>
                <div style={{ marginTop: '12px' }}>
                  <div style={sectionLabelStyle()}>{t('auto.githubConfig.maxConcurrentByState')}</div>
                  <NumberMapEditor
                    values={config.agent.maxConcurrentByState}
                    onChange={(values) =>
                      updateConfig((current) => ({
                        ...current,
                        agent: { ...current.agent, maxConcurrentByState: values }
                      }))}
                    keyPlaceholder={t('auto.githubConfig.stateNamePlaceholder')}
                    valuePlaceholder={t('auto.githubConfig.numberValuePlaceholder')}
                    addLabel={t('auto.githubConfig.addMapping')}
                    removeLabel={t('auto.githubConfig.removeMapping')}
                  />
                </div>
              </details>

              <details open style={cardStyle()}>
                <summary style={sectionSummaryStyle()}>{t('auto.githubConfig.section.hooks')}</summary>
                <div style={{ marginTop: '12px', display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px' }}>
                  {renderTextField(t('auto.githubConfig.afterCreate'), config.hooks.afterCreate, (value) =>
                    updateConfig((current) => ({
                      ...current,
                      hooks: { ...current.hooks, afterCreate: normalizeOptionalString(value) }
                    })), { mono: true })}
                  {renderTextField(t('auto.githubConfig.beforeRun'), config.hooks.beforeRun, (value) =>
                    updateConfig((current) => ({
                      ...current,
                      hooks: { ...current.hooks, beforeRun: normalizeOptionalString(value) }
                    })), { mono: true })}
                  {renderTextField(t('auto.githubConfig.afterRun'), config.hooks.afterRun, (value) =>
                    updateConfig((current) => ({
                      ...current,
                      hooks: { ...current.hooks, afterRun: normalizeOptionalString(value) }
                    })), { mono: true })}
                  {renderTextField(t('auto.githubConfig.beforeRemove'), config.hooks.beforeRemove, (value) =>
                    updateConfig((current) => ({
                      ...current,
                      hooks: { ...current.hooks, beforeRemove: normalizeOptionalString(value) }
                    })), { mono: true })}
                  {renderNumberField(t('auto.githubConfig.timeoutMs'), config.hooks.timeoutMs, (value) =>
                    updateConfig((current) => ({
                      ...current,
                      hooks: { ...current.hooks, timeoutMs: value }
                    })))}
                </div>
              </details>
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

        <div
          style={{
            padding: '12px 20px',
            borderTop: '1px solid var(--border-default)',
            display: 'flex',
            justifyContent: 'flex-end',
            gap: '8px'
          }}
        >
          <button type="button" onClick={onClose} style={secondaryButtonStyle()}>
            {t('common.cancel')}
          </button>
          <button type="button" onClick={() => void handleSave()} disabled={loading || saving} style={primaryButtonStyle(loading || saving)}>
            {saving ? t('settings.saving') : t('settings.save')}
          </button>
        </div>
      </div>
    </div>
  )
}
