import { useEffect, useMemo, useState, type CSSProperties } from 'react'
import { useLocale, useT } from '../contexts/LocaleContext'
import type {
  WorkspaceBootstrapProfile,
  WorkspaceLanguage,
  WorkspaceSetupRequest,
  WorkspaceStatusPayload
} from '../../preload/api.d'
import { SecretInput } from './channels/FormShared'

interface WorkspaceSetupWizardProps {
  workspacePath: string
  workspaceStatus: WorkspaceStatusPayload
  onCancel: () => void
}

type WizardStep = 0 | 1 | 2 | 3

function cardStyle(active: boolean): CSSProperties {
  return {
    border: active ? '1px solid var(--accent)' : '1px solid var(--border-default)',
    borderRadius: '10px',
    background: active ? 'var(--bg-tertiary)' : 'var(--bg-secondary)',
    padding: '14px',
    cursor: 'pointer'
  }
}

function isValidHttpUrl(value: string): boolean {
  try {
    const parsed = new URL(value.trim())
    return parsed.protocol === 'http:' || parsed.protocol === 'https:'
  } catch {
    return false
  }
}

export function WorkspaceSetupWizard({
  workspacePath,
  workspaceStatus,
  onCancel
}: WorkspaceSetupWizardProps): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const defaultLanguage: WorkspaceLanguage = locale === 'zh-Hans' ? 'Chinese' : 'English'
  const hasUserConfig = workspaceStatus.hasUserConfig
  const userConfigDefaults = workspaceStatus.userConfigDefaults
  const inheritedApiKeyPresent = userConfigDefaults?.apiKeyPresent === true
  const hasInheritedLanguage = Boolean(userConfigDefaults?.language)
  const hasInheritedEndpoint = Boolean(userConfigDefaults?.endpoint?.trim())
  const hasInheritedModel = Boolean(userConfigDefaults?.model?.trim())
  const [step, setStep] = useState<WizardStep>(0)
  const [profile, setProfile] = useState<WorkspaceBootstrapProfile>('default')
  const [language, setLanguage] = useState<WorkspaceLanguage>(
    userConfigDefaults?.language ?? defaultLanguage
  )
  const [apiKey, setApiKey] = useState('')
  const [endpoint, setEndpoint] = useState(
    userConfigDefaults?.endpoint?.trim() || 'https://api.openai.com/v1'
  )
  const [model, setModel] = useState(userConfigDefaults?.model?.trim() || 'gpt-4o-mini')
  const [saveToUserConfig, setSaveToUserConfig] = useState(!hasUserConfig)
  const [submitting, setSubmitting] = useState(false)
  const [submitError, setSubmitError] = useState<string | null>(null)
  const [languageDirty, setLanguageDirty] = useState(false)
  const [endpointDirty, setEndpointDirty] = useState(false)
  const [modelDirty, setModelDirty] = useState(false)
  const [saveScopeDirty, setSaveScopeDirty] = useState(false)
  const [modelLoadState, setModelLoadState] = useState<'idle' | 'loading' | 'ready' | 'unsupported' | 'error'>('idle')
  const [modelOptions, setModelOptions] = useState<string[]>([])

  const steps = useMemo(
    () => [
      t('setupWizard.step.welcome'),
      t('setupWizard.step.profile'),
      t('setupWizard.step.config'),
      t('setupWizard.step.confirm')
    ],
    [t]
  )

  const canAdvanceFromConfig =
    model.trim().length > 0 &&
    endpoint.trim().length > 0 &&
    (apiKey.trim().length > 0 || inheritedApiKeyPresent) &&
    isValidHttpUrl(endpoint)

  const effectiveModelOptions = useMemo(() => {
    const normalized = modelOptions.map((item) => item.trim()).filter(Boolean)
    const current = model.trim()
    if (!current) return normalized
    if (normalized.includes(current)) return normalized
    return [current, ...normalized]
  }, [model, modelOptions])
  const modelSelectAvailable =
    modelLoadState === 'ready' &&
    effectiveModelOptions.length > 0
  const modelListLoading = modelLoadState === 'loading'

  useEffect(() => {
    if (!languageDirty) {
      setLanguage(userConfigDefaults?.language ?? defaultLanguage)
    }
  }, [defaultLanguage, languageDirty, userConfigDefaults?.language])

  useEffect(() => {
    if (!endpointDirty) {
      setEndpoint(userConfigDefaults?.endpoint?.trim() || 'https://api.openai.com/v1')
    }
  }, [endpointDirty, userConfigDefaults?.endpoint])

  useEffect(() => {
    if (!modelDirty) {
      setModel(userConfigDefaults?.model?.trim() || 'gpt-4o-mini')
    }
  }, [modelDirty, userConfigDefaults?.model])

  useEffect(() => {
    if (!saveScopeDirty) {
      setSaveToUserConfig(!hasUserConfig)
    }
  }, [hasUserConfig, saveScopeDirty])

  useEffect(() => {
    if (step !== 2) {
      return
    }

    if (!isValidHttpUrl(endpoint)) {
      setModelLoadState('error')
      setModelOptions([])
      return
    }

    const controller = new AbortController()
    setModelLoadState('loading')

    void window.api.workspace
      .listSetupModels({
        endpoint: endpoint.trim(),
        apiKey: apiKey.trim(),
        preferExistingUserConfig: hasUserConfig
      })
      .then((result) => {
      if (controller.signal.aborted) {
        return
      }

      if (result.kind === 'success') {
        setModelOptions(result.models)
        setModelLoadState('ready')
        return
      }

      setModelOptions([])
      setModelLoadState(result.kind === 'unsupported' ? 'unsupported' : 'error')
    })

    return () => {
      controller.abort()
    }
  }, [apiKey, endpoint, hasUserConfig, step])

  async function handleSubmit(): Promise<void> {
    const request: WorkspaceSetupRequest = {
      language,
      model: model.trim(),
      endpoint: endpoint.trim(),
      apiKey: apiKey.trim(),
      profile,
      saveToUserConfig,
      preferExistingUserConfig: hasUserConfig
    }

    setSubmitting(true)
    setSubmitError(null)
    try {
      await window.api.workspace.runSetup(request)
    } catch (err) {
      setSubmitError(err instanceof Error ? err.message : String(err))
    } finally {
      setSubmitting(false)
    }
  }

  function goBack(): void {
    if (submitting) return
    if (step === 0) {
      onCancel()
      return
    }
    setStep((prev) => Math.max(0, prev - 1) as WizardStep)
  }

  function goNext(): void {
    if (submitting) return
    if (step === 2 && !canAdvanceFromConfig) {
      return
    }
    setStep((prev) => Math.min(3, prev + 1) as WizardStep)
  }

  return (
    <div
      style={{
        display: 'flex',
        justifyContent: 'center',
        height: '100%',
        overflowY: 'auto',
        background: 'var(--bg-primary)',
        padding: '28px 20px 36px',
        boxSizing: 'border-box'
      }}
    >
      <div style={{ width: '100%', maxWidth: '760px' }}>
        <div style={{ marginBottom: '18px' }}>
          <div
            style={{
              fontSize: '12px',
              fontWeight: 600,
              color: 'var(--text-dimmed)',
              textTransform: 'uppercase',
              letterSpacing: '0.05em',
              marginBottom: '8px'
            }}
          >
            {t('setupWizard.title')}
          </div>
          <div
            style={{
              display: 'grid',
              gridTemplateColumns: 'repeat(4, minmax(0, 1fr))',
              gap: '8px'
            }}
          >
            {steps.map((label, idx) => {
              const active = idx === step
              const completed = idx < step
              return (
                <div
                  key={label}
                  style={{
                    borderRadius: '10px',
                    border: active ? '1px solid var(--accent)' : '1px solid var(--border-default)',
                    background: active ? 'var(--bg-tertiary)' : 'var(--bg-secondary)',
                    padding: '10px 12px'
                  }}
                >
                  <div
                    style={{
                      fontSize: '11px',
                      color: completed ? 'var(--success)' : 'var(--text-dimmed)',
                      marginBottom: '4px'
                    }}
                  >
                    {completed ? t('setupWizard.done') : t('setupWizard.stepCount', { n: idx + 1 })}
                  </div>
                  <div
                    style={{
                      fontSize: '13px',
                      fontWeight: active ? 600 : 500,
                      color: 'var(--text-primary)'
                    }}
                  >
                    {label}
                  </div>
                </div>
              )
            })}
          </div>
        </div>

        <div
          style={{
            border: '1px solid var(--border-default)',
            borderRadius: '12px',
            background: 'var(--bg-secondary)',
            padding: '22px'
          }}
        >
          {step === 0 && (
            <div>
              <h1 style={{ margin: '0 0 10px', fontSize: '24px', fontWeight: 700 }}>
                {t('setupWizard.welcome.title')}
              </h1>
              <p style={{ margin: '0 0 14px', color: 'var(--text-secondary)', lineHeight: 1.6 }}>
                {t('setupWizard.welcome.description')}
              </p>
              <div
                style={{
                  padding: '12px 14px',
                  borderRadius: '10px',
                  border: '1px solid var(--border-default)',
                  background: 'var(--bg-primary)',
                  color: 'var(--text-secondary)',
                  fontSize: '12px',
                  fontFamily: 'var(--font-mono)',
                  wordBreak: 'break-all'
                }}
              >
                {workspacePath}
              </div>
              <p style={{ margin: '14px 0 0', color: 'var(--text-dimmed)', fontSize: '13px' }}>
                {t('setupWizard.welcome.note')}
              </p>
            </div>
          )}

          {step === 1 && (
            <div>
              <h1 style={{ margin: '0 0 10px', fontSize: '24px', fontWeight: 700 }}>
                {t('setupWizard.profile.title')}
              </h1>
              <p style={{ margin: '0 0 16px', color: 'var(--text-secondary)', lineHeight: 1.6 }}>
                {t('setupWizard.profile.description')}
              </p>
              <div style={{ display: 'grid', gap: '10px' }}>
                {(
                  [
                    ['default', 'setupWizard.profile.default.title', 'setupWizard.profile.default.description'],
                    ['developer', 'setupWizard.profile.developer.title', 'setupWizard.profile.developer.description'],
                    ['personal-assistant', 'setupWizard.profile.personal.title', 'setupWizard.profile.personal.description']
                  ] as const
                ).map(([value, titleKey, descKey]) => {
                  const active = profile === value
                  return (
                    <button
                      key={value}
                      type="button"
                      onClick={() => {
                        setProfile(value)
                      }}
                      style={{
                        ...cardStyle(active),
                        textAlign: 'left'
                      }}
                    >
                      <div style={{ fontSize: '14px', fontWeight: 600, color: 'var(--text-primary)' }}>
                        {t(titleKey)}
                      </div>
                      <div
                        style={{
                          marginTop: '6px',
                          fontSize: '13px',
                          lineHeight: 1.55,
                          color: 'var(--text-secondary)'
                        }}
                      >
                        {t(descKey)}
                      </div>
                    </button>
                  )
                })}
              </div>
            </div>
          )}

          {step === 2 && (
            <div>
              <h1 style={{ margin: '0 0 10px', fontSize: '24px', fontWeight: 700 }}>
                {t('setupWizard.config.title')}
              </h1>
              <p style={{ margin: '0 0 18px', color: 'var(--text-secondary)', lineHeight: 1.6 }}>
                {t('setupWizard.config.description')}
              </p>

              {hasUserConfig && (
                <div
                  style={{
                    marginBottom: '18px',
                    padding: '12px 14px',
                    borderRadius: '10px',
                    border: '1px solid var(--border-default)',
                    background: 'var(--bg-primary)',
                    color: 'var(--text-secondary)',
                    fontSize: '13px',
                    lineHeight: 1.6
                  }}
                >
                  {t('setupWizard.config.userConfigDetected')}
                </div>
              )}

              <div style={{ display: 'grid', gap: '14px' }}>
                <div>
                  <label htmlFor="setup-language" style={{ display: 'block', marginBottom: '6px', fontSize: '12px', fontWeight: 600 }}>
                    {t('setupWizard.field.language')}
                  </label>
                  <select
                    id="setup-language"
                    value={language}
                    onChange={(e) => {
                      setLanguageDirty(true)
                      setLanguage(e.target.value as WorkspaceLanguage)
                    }}
                    style={{
                      width: '220px',
                      padding: '9px 10px',
                      borderRadius: '8px',
                      border: '1px solid var(--border-default)',
                      background: 'var(--bg-primary)',
                      color: 'var(--text-primary)',
                      fontSize: '13px'
                    }}
                  >
                    <option value="English">{t('setupWizard.language.english')}</option>
                    <option value="Chinese">{t('setupWizard.language.chinese')}</option>
                  </select>
                  {hasInheritedLanguage && (
                    <div style={{ marginTop: '6px', fontSize: '12px', color: 'var(--text-dimmed)' }}>
                      {t('setupWizard.config.inheritedFieldHint')}
                    </div>
                  )}
                </div>

                <div>
                  <label style={{ display: 'block', marginBottom: '6px', fontSize: '12px', fontWeight: 600 }}>
                    {t('setupWizard.field.apiKey')}
                  </label>
                  <SecretInput
                    value={apiKey}
                    onChange={setApiKey}
                    placeholder={
                      hasUserConfig && inheritedApiKeyPresent
                        ? t('setupWizard.placeholder.apiKeyInherited')
                        : t('setupWizard.placeholder.apiKey')
                    }
                    style={{
                      width: '100%',
                      boxSizing: 'border-box',
                      padding: '9px 10px',
                      borderRadius: '8px',
                      border: '1px solid var(--border-default)',
                      background: 'var(--bg-primary)',
                      color: 'var(--text-primary)',
                      fontSize: '13px'
                    }}
                  />
                  {hasUserConfig && (
                    <div style={{ marginTop: '6px', fontSize: '12px', color: 'var(--text-dimmed)' }}>
                      {inheritedApiKeyPresent
                        ? t('setupWizard.config.apiKeyInheritedHint')
                        : t('setupWizard.config.apiKeyMissingHint')}
                    </div>
                  )}
                </div>

                <div>
                  <label htmlFor="setup-endpoint" style={{ display: 'block', marginBottom: '6px', fontSize: '12px', fontWeight: 600 }}>
                    {t('setupWizard.field.endpoint')}
                  </label>
                  <input
                    id="setup-endpoint"
                    type="text"
                    value={endpoint}
                    onChange={(e) => {
                      setEndpointDirty(true)
                      setEndpoint(e.target.value)
                    }}
                    placeholder="https://api.openai.com/v1"
                    style={{
                      width: '100%',
                      boxSizing: 'border-box',
                      padding: '9px 10px',
                      borderRadius: '8px',
                      border: '1px solid var(--border-default)',
                      background: 'var(--bg-primary)',
                      color: 'var(--text-primary)',
                      fontSize: '13px'
                    }}
                  />
                  {endpoint.trim().length > 0 && !isValidHttpUrl(endpoint) && (
                    <div style={{ marginTop: '6px', fontSize: '12px', color: 'var(--error)' }}>
                      {t('setupWizard.validation.endpoint')}
                    </div>
                  )}
                  {hasInheritedEndpoint && (
                    <div style={{ marginTop: '6px', fontSize: '12px', color: 'var(--text-dimmed)' }}>
                      {t('setupWizard.config.inheritedFieldHint')}
                    </div>
                  )}
                </div>

                <div>
                  <label htmlFor="setup-model" style={{ display: 'block', marginBottom: '6px', fontSize: '12px', fontWeight: 600 }}>
                    {t('setupWizard.field.model')}
                  </label>
                  {modelListLoading ? (
                    <div
                      role="status"
                      aria-live="polite"
                      style={{ marginTop: '2px', fontSize: '12px', color: 'var(--text-dimmed)' }}
                    >
                      {t('setupWizard.modelListLoading')}
                    </div>
                  ) : modelSelectAvailable ? (
                    <select
                      id="setup-model"
                      value={model}
                      onChange={(e) => {
                        setModelDirty(true)
                        setModel(e.target.value)
                      }}
                      style={{
                        width: '100%',
                        boxSizing: 'border-box',
                        padding: '9px 10px',
                        borderRadius: '8px',
                        border: '1px solid var(--border-default)',
                        background: 'var(--bg-primary)',
                        color: 'var(--text-primary)',
                        fontSize: '13px'
                      }}
                    >
                      {effectiveModelOptions.map((item) => (
                        <option key={item} value={item}>
                          {item}
                        </option>
                      ))}
                    </select>
                  ) : (
                    <input
                      id="setup-model"
                      type="text"
                      value={model}
                      onChange={(e) => {
                        setModelDirty(true)
                        setModel(e.target.value)
                      }}
                      placeholder="gpt-4o-mini"
                      style={{
                        width: '100%',
                        boxSizing: 'border-box',
                        padding: '9px 10px',
                        borderRadius: '8px',
                        border: '1px solid var(--border-default)',
                        background: 'var(--bg-primary)',
                        color: 'var(--text-primary)',
                        fontSize: '13px'
                      }}
                    />
                  )}
                  {hasInheritedModel && (
                    <div style={{ marginTop: '6px', fontSize: '12px', color: 'var(--text-dimmed)' }}>
                      {t('setupWizard.config.inheritedFieldHint')}
                    </div>
                  )}
                </div>
              </div>
            </div>
          )}

          {step === 3 && (
            <div>
              <h1 style={{ margin: '0 0 10px', fontSize: '24px', fontWeight: 700 }}>
                {t('setupWizard.confirm.title')}
              </h1>
              <p style={{ margin: '0 0 18px', color: 'var(--text-secondary)', lineHeight: 1.6 }}>
                {t('setupWizard.confirm.description')}
              </p>

              <div
                style={{
                  display: 'grid',
                  gap: '10px',
                  marginBottom: '16px',
                  padding: '14px',
                  borderRadius: '10px',
                  border: '1px solid var(--border-default)',
                  background: 'var(--bg-primary)',
                  fontSize: '13px'
                }}
              >
                <SummaryRow label={t('setupWizard.summary.profile')} value={t(`setupWizard.profileSummary.${profile}`)} />
                <SummaryRow label={t('setupWizard.summary.language')} value={language} />
                <SummaryRow label={t('setupWizard.summary.endpoint')} value={endpoint.trim()} mono />
                <SummaryRow label={t('setupWizard.summary.model')} value={model.trim()} mono />
                <SummaryRow
                  label={t('setupWizard.summary.apiKey')}
                  value={
                    apiKey.trim().length > 0
                      ? t('setupWizard.summary.apiKeySet')
                      : inheritedApiKeyPresent
                        ? t('setupWizard.summary.apiKeyInherited')
                        : t('setupWizard.summary.apiKeyMissing')
                  }
                />
              </div>

              {hasUserConfig ? (
                <div
                  style={{
                    padding: '12px 14px',
                    borderRadius: '10px',
                    border: '1px solid var(--border-default)',
                    background: 'var(--bg-primary)',
                    color: 'var(--text-secondary)',
                    fontSize: '12px',
                    lineHeight: 1.6
                  }}
                >
                  {t('setupWizard.confirm.userConfigDetected')}
                </div>
              ) : (
                <label
                  style={{
                    display: 'flex',
                    gap: '10px',
                    alignItems: 'flex-start',
                    padding: '12px 14px',
                    borderRadius: '10px',
                    border: '1px solid var(--border-default)',
                    background: 'var(--bg-primary)',
                    cursor: submitting ? 'default' : 'pointer'
                  }}
                >
                  <input
                    type="checkbox"
                    checked={saveToUserConfig}
                    disabled={submitting}
                    onChange={(e) => {
                      setSaveScopeDirty(true)
                      setSaveToUserConfig(e.target.checked)
                    }}
                    style={{ marginTop: '2px' }}
                  />
                  <div>
                    <div style={{ fontSize: '13px', fontWeight: 600, color: 'var(--text-primary)' }}>
                      {t('setupWizard.saveScope.title')}
                    </div>
                    <div style={{ marginTop: '4px', fontSize: '12px', lineHeight: 1.6, color: 'var(--text-secondary)' }}>
                      {t('setupWizard.saveScope.description')}
                    </div>
                  </div>
                </label>
              )}

              {submitError && (
                <div
                  style={{
                    marginTop: '14px',
                    padding: '12px 14px',
                    borderRadius: '10px',
                    border: '1px solid rgba(239, 68, 68, 0.35)',
                    background: 'rgba(239, 68, 68, 0.08)',
                    color: 'var(--error)',
                    fontSize: '13px',
                    whiteSpace: 'pre-wrap',
                    wordBreak: 'break-word'
                  }}
                >
                  {submitError}
                </div>
              )}
            </div>
          )}
        </div>

        <div
          style={{
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'center',
            gap: '10px',
            marginTop: '14px'
          }}
        >
          <button
            type="button"
            onClick={goBack}
            disabled={submitting}
            style={{
              padding: '10px 16px',
              borderRadius: '8px',
              border: '1px solid var(--border-default)',
              background: 'transparent',
              color: 'var(--text-primary)',
              fontSize: '13px',
              cursor: submitting ? 'default' : 'pointer',
              opacity: submitting ? 0.7 : 1
            }}
          >
            {step === 0 ? t('setupWizard.button.cancel') : t('setupWizard.button.back')}
          </button>

          <div style={{ display: 'flex', gap: '10px' }}>
            {step < 3 ? (
              <button
                type="button"
                onClick={goNext}
                disabled={step === 2 && !canAdvanceFromConfig}
                style={{
                  padding: '10px 18px',
                  borderRadius: '8px',
                  border: 'none',
                  background: 'var(--accent)',
                  color: 'var(--on-accent)',
                  fontSize: '13px',
                  fontWeight: 600,
                  cursor: step === 2 && !canAdvanceFromConfig ? 'default' : 'pointer',
                  opacity: step === 2 && !canAdvanceFromConfig ? 0.65 : 1
                }}
              >
                {t('setupWizard.button.next')}
              </button>
            ) : (
              <button
                type="button"
                onClick={() => {
                  void handleSubmit()
                }}
                disabled={submitting}
                style={{
                  padding: '10px 18px',
                  borderRadius: '8px',
                  border: 'none',
                  background: 'var(--accent)',
                  color: 'var(--on-accent)',
                  fontSize: '13px',
                  fontWeight: 600,
                  cursor: submitting ? 'default' : 'pointer',
                  opacity: submitting ? 0.7 : 1
                }}
              >
                {submitting ? t('setupWizard.button.creating') : t('setupWizard.button.create')}
              </button>
            )}
          </div>
        </div>
      </div>
    </div>
  )
}

function SummaryRow({
  label,
  value,
  mono = false
}: {
  label: string
  value: string
  mono?: boolean
}): JSX.Element {
  return (
    <div
      style={{
        display: 'grid',
        gridTemplateColumns: '140px minmax(0, 1fr)',
        gap: '12px',
        alignItems: 'start'
      }}
    >
      <div style={{ color: 'var(--text-dimmed)' }}>{label}</div>
      <div
        style={{
          color: 'var(--text-primary)',
          wordBreak: 'break-word',
          fontFamily: mono ? 'var(--font-mono)' : undefined
        }}
      >
        {value}
      </div>
    </div>
  )
}
