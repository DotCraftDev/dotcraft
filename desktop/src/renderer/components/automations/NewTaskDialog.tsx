import { useEffect, useMemo, useState, type CSSProperties } from 'react'
import { useT } from '../../contexts/LocaleContext'
import {
  useAutomationsStore,
  type AutomationSchedule,
  type AutomationTemplate,
  type AutomationThreadBinding
} from '../../stores/automationsStore'
import { useThreadStore } from '../../stores/threadStore'
import { SchedulePicker } from './SchedulePicker'
import { ThreadPickerOverlay } from './ThreadPickerOverlay'
import { TemplateGalleryOverlay } from './TemplateGalleryOverlay'
import { PillSwitch } from '../ui/PillSwitch'

interface Props {
  onClose(): void
  /** Optional: pre-fill the dialog from a template (entry from the gallery strip). */
  initialTemplate?: AutomationTemplate
}

type TargetMode = 'project' | 'isolated' | 'bound'

export function NewTaskDialog({ onClose, initialTemplate }: Props): JSX.Element {
  const t = useT()
  const createTask = useAutomationsStore((s) => s.createTask)
  const threadList = useThreadStore((s) => s.threadList)

  const [title, setTitle] = useState(initialTemplate?.defaultTitle ?? '')
  const [description, setDescription] = useState(initialTemplate?.defaultDescription ?? '')
  const [schedule, setSchedule] = useState<AutomationSchedule | null>(
    initialTemplate?.defaultSchedule ?? null
  )
  const [binding, setBinding] = useState<AutomationThreadBinding | null>(null)
  const [workspaceMode, setWorkspaceMode] = useState<'project' | 'isolated'>(
    (initialTemplate?.defaultWorkspaceMode as 'project' | 'isolated' | undefined) ?? 'project'
  )
  const [approvalPolicy, setApprovalPolicy] = useState<'workspaceScope' | 'fullAuto'>(
    (initialTemplate?.defaultApprovalPolicy as 'workspaceScope' | 'fullAuto' | undefined) ??
      'workspaceScope'
  )
  const [requireApprovalOverride, setRequireApprovalOverride] = useState<boolean | null>(
    initialTemplate?.defaultRequireApproval ?? null
  )
  const [showAdvanced, setShowAdvanced] = useState(false)
  const [showThreadPicker, setShowThreadPicker] = useState(false)
  const [showTemplates, setShowTemplates] = useState(false)
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [templateId, setTemplateId] = useState<string | undefined>(initialTemplate?.id)
  const [workflowTemplate, setWorkflowTemplate] = useState<string | undefined>(
    initialTemplate?.workflowMarkdown
  )

  // When a template suggests thread binding, pop the thread picker after the dialog mounts
  // so the user sees the intent immediately (without forcing — they can still cancel).
  useEffect(() => {
    if (initialTemplate?.needsThreadBinding) setShowThreadPicker(true)
  }, [initialTemplate])

  const targetMode: TargetMode = binding ? 'bound' : workspaceMode
  const effectiveRequireApproval =
    requireApprovalOverride ?? (binding ? false : true)

  const boundThreadName = useMemo(() => {
    if (!binding) return null
    const match = threadList.find((t) => t.id === binding.threadId)
    return match?.displayName ?? binding.threadId
  }, [binding, threadList])

  const canSubmit = title.trim().length > 0 && description.trim().length > 0 && !submitting

  function applyTemplate(tpl: AutomationTemplate): void {
    setTemplateId(tpl.id)
    setWorkflowTemplate(tpl.workflowMarkdown)
    if (tpl.defaultTitle) setTitle(tpl.defaultTitle)
    if (tpl.defaultDescription) setDescription(tpl.defaultDescription)
    if (tpl.defaultSchedule !== undefined) setSchedule(tpl.defaultSchedule ?? null)
    if (tpl.defaultWorkspaceMode === 'project' || tpl.defaultWorkspaceMode === 'isolated')
      setWorkspaceMode(tpl.defaultWorkspaceMode)
    if (tpl.defaultApprovalPolicy === 'workspaceScope' || tpl.defaultApprovalPolicy === 'fullAuto')
      setApprovalPolicy(tpl.defaultApprovalPolicy)
    if (typeof tpl.defaultRequireApproval === 'boolean')
      setRequireApprovalOverride(tpl.defaultRequireApproval)
    if (tpl.needsThreadBinding && !binding) setShowThreadPicker(true)
  }

  async function handleSubmit(): Promise<void> {
    if (!canSubmit) return
    setSubmitting(true)
    setError(null)
    try {
      await createTask({
        title: title.trim(),
        description: description.trim(),
        approvalPolicy,
        workspaceMode,
        schedule: schedule && schedule.kind !== 'once' ? schedule : null,
        threadBinding: binding,
        requireApproval: effectiveRequireApproval,
        templateId,
        workflowTemplate
      })
      onClose()
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <>
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
            width: '560px',
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
              padding: '14px 20px',
              borderBottom: '1px solid var(--border-default)',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'space-between',
              gap: '8px'
            }}
          >
            <div style={{ fontSize: '15px', fontWeight: 600, color: 'var(--text-primary)' }}>
              {t('auto.newTask.title')}
            </div>
            <button
              type="button"
              onClick={() => setShowTemplates(true)}
              style={{
                padding: '5px 12px',
                borderRadius: '6px',
                border: '1px solid var(--border-default)',
                backgroundColor: 'transparent',
                color: 'var(--text-secondary)',
                fontSize: '12px',
                fontWeight: 500,
                cursor: 'pointer'
              }}
            >
              📚 {t('auto.newTask.useTemplate')}
            </button>
          </div>

          <div
            style={{
              padding: '16px 20px',
              display: 'flex',
              flexDirection: 'column',
              gap: '12px',
              overflow: 'auto'
            }}
          >
            <input
              type="text"
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              maxLength={200}
              placeholder={t('auto.newTask.namePlaceholder')}
              autoFocus
              style={{
                padding: '10px 12px',
                borderRadius: '8px',
                border: '1px solid var(--border-default)',
                backgroundColor: 'var(--bg-secondary)',
                color: 'var(--text-primary)',
                fontSize: '14px',
                fontWeight: 500,
                outline: 'none'
              }}
            />

            <textarea
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              rows={10}
              placeholder={t('auto.newTask.promptPlaceholder')}
              style={{
                padding: '10px 12px',
                borderRadius: '8px',
                border: '1px solid var(--border-default)',
                backgroundColor: 'var(--bg-secondary)',
                color: 'var(--text-primary)',
                fontSize: '13px',
                resize: 'vertical',
                outline: 'none',
                fontFamily: 'inherit',
                minHeight: '160px'
              }}
            />

            <div
              style={{
                display: 'flex',
                alignItems: 'center',
                flexWrap: 'wrap',
                gap: '8px',
                padding: '8px 0 0'
              }}
            >
              <TargetPill
                mode={targetMode}
                boundName={boundThreadName}
                onIsolated={() => {
                  setBinding(null)
                  setWorkspaceMode('isolated')
                }}
                onProject={() => {
                  setBinding(null)
                  setWorkspaceMode('project')
                }}
                onBind={() => setShowThreadPicker(true)}
                onUnbind={() => setBinding(null)}
              />
              <div style={{ flex: 1, minWidth: 0 }}>
                <SchedulePicker value={schedule} onChange={setSchedule} />
              </div>
            </div>

            <button
              type="button"
              onClick={() => setShowAdvanced((v) => !v)}
              style={{
                alignSelf: 'flex-start',
                fontSize: '12px',
                color: 'var(--text-secondary)',
                background: 'transparent',
                border: 'none',
                cursor: 'pointer',
                padding: 0,
                textDecoration: 'underline dotted'
              }}
            >
              {showAdvanced ? t('auto.newTask.hideDetails') : t('auto.newTask.advanced')} ▾
            </button>

            {showAdvanced && (
              <div
                style={{
                  display: 'flex',
                  flexDirection: 'column',
                  gap: '10px',
                  padding: '10px 12px',
                  borderRadius: '8px',
                  border: '1px solid var(--border-default)',
                  backgroundColor: 'var(--bg-secondary)'
                }}
              >
                {targetMode !== 'bound' && (
                  <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                    <label style={advancedLabelStyle}>
                      {t('auto.newTask.agentWorkspaceLabel')}
                    </label>
                    <select
                      value={workspaceMode}
                      onChange={(e) => setWorkspaceMode(e.target.value as 'project' | 'isolated')}
                      style={selectStyle}
                    >
                      <option value="project">{t('auto.newTask.workspaceProject')}</option>
                      <option value="isolated">{t('auto.newTask.workspaceIsolated')}</option>
                    </select>
                  </div>
                )}
                <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                  <label style={advancedLabelStyle}>{t('auto.newTask.toolPolicyLabel')}</label>
                  <select
                    value={approvalPolicy}
                    onChange={(e) =>
                      setApprovalPolicy(e.target.value as 'workspaceScope' | 'fullAuto')
                    }
                    style={selectStyle}
                  >
                    <option value="workspaceScope">{t('auto.newTask.policyWorkspace')}</option>
                    <option value="fullAuto">{t('auto.newTask.policyFullAuto')}</option>
                  </select>
                </div>
                <div
                  style={{
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'space-between',
                    gap: '12px'
                  }}
                >
                  <span style={{ fontSize: '12px', color: 'var(--text-primary)' }}>
                    {t('auto.newTask.requireApproval')}
                  </span>
                  <PillSwitch
                    checked={effectiveRequireApproval}
                    onChange={(v) => setRequireApprovalOverride(v)}
                    aria-label={t('auto.newTask.requireApproval')}
                    size="sm"
                  />
                </div>
              </div>
            )}

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
              onClick={() => void handleSubmit()}
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

      {showThreadPicker && (
        <ThreadPickerOverlay
          onClose={() => setShowThreadPicker(false)}
          onSelect={(th) =>
            setBinding({ threadId: th.id, mode: 'run-in-thread' })
          }
        />
      )}

      {showTemplates && (
        <TemplateGalleryOverlay
          onClose={() => setShowTemplates(false)}
          onSelect={(tpl) => {
            applyTemplate(tpl)
            setShowTemplates(false)
          }}
        />
      )}
    </>
  )
}

const selectStyle: CSSProperties = {
  padding: '7px 10px',
  borderRadius: '6px',
  border: '1px solid var(--border-default)',
  backgroundColor: 'var(--bg-primary)',
  color: 'var(--text-primary)',
  fontSize: '12px',
  outline: 'none',
  width: '100%',
  cursor: 'pointer'
}

const advancedLabelStyle: CSSProperties = {
  fontSize: '11px',
  fontWeight: 500,
  color: 'var(--text-secondary)',
  textTransform: 'uppercase',
  letterSpacing: '0.04em'
}

function TargetPill({
  mode,
  boundName,
  onIsolated,
  onProject,
  onBind,
  onUnbind
}: {
  mode: TargetMode
  boundName: string | null
  onIsolated(): void
  onProject(): void
  onBind(): void
  onUnbind(): void
}): JSX.Element {
  const t = useT()
  const [open, setOpen] = useState(false)

  const label =
    mode === 'bound'
      ? `💬 ${boundName ?? ''}`
      : mode === 'isolated'
        ? `📦 ${t('auto.newTask.targetIsolated')}`
        : `📁 ${t('auto.newTask.targetProject')}`

  return (
    <div style={{ position: 'relative' }}>
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        style={{
          padding: '5px 10px',
          borderRadius: '999px',
          border: mode === 'bound' ? '1px solid var(--accent)' : '1px solid var(--border-default)',
          backgroundColor:
            mode === 'bound'
              ? 'color-mix(in srgb, var(--accent) 12%, transparent)'
              : 'transparent',
          color: mode === 'bound' ? 'var(--accent)' : 'var(--text-secondary)',
          fontSize: '12px',
          fontWeight: 500,
          cursor: 'pointer',
          maxWidth: '220px',
          overflow: 'hidden',
          textOverflow: 'ellipsis',
          whiteSpace: 'nowrap'
        }}
        title={label}
      >
        {label} ▾
      </button>
      {open && (
        <div
          style={{
            position: 'absolute',
            top: '110%',
            left: 0,
            zIndex: 20,
            minWidth: '200px',
            padding: '4px',
            border: '1px solid var(--border-default)',
            borderRadius: '8px',
            backgroundColor: 'var(--bg-primary)',
            boxShadow: '0 6px 18px rgba(0,0,0,0.25)',
            display: 'flex',
            flexDirection: 'column'
          }}
          onMouseLeave={() => setOpen(false)}
        >
          <MenuItem
            active={mode === 'project'}
            onClick={() => {
              onProject()
              setOpen(false)
            }}
          >
            📁 {t('auto.newTask.targetProject')}
          </MenuItem>
          <MenuItem
            active={mode === 'isolated'}
            onClick={() => {
              onIsolated()
              setOpen(false)
            }}
          >
            📦 {t('auto.newTask.targetIsolated')}
          </MenuItem>
          <MenuItem
            onClick={() => {
              onBind()
              setOpen(false)
            }}
          >
            💬 {t('auto.newTask.targetBindThread')}
          </MenuItem>
          {mode === 'bound' && (
            <MenuItem
              onClick={() => {
                onUnbind()
                setOpen(false)
              }}
            >
              ✕ {t('auto.newTask.unbind')}
            </MenuItem>
          )}
        </div>
      )}
    </div>
  )
}

function MenuItem({
  active,
  onClick,
  children
}: {
  active?: boolean
  onClick(): void
  children: React.ReactNode
}): JSX.Element {
  return (
    <button
      type="button"
      onClick={onClick}
      style={{
        padding: '6px 10px',
        border: 'none',
        borderRadius: '6px',
        backgroundColor: active ? 'var(--bg-tertiary)' : 'transparent',
        color: 'var(--text-primary)',
        fontSize: '12px',
        textAlign: 'left',
        cursor: 'pointer'
      }}
      onMouseEnter={(e) => (e.currentTarget.style.backgroundColor = 'var(--bg-secondary)')}
      onMouseLeave={(e) =>
        (e.currentTarget.style.backgroundColor = active ? 'var(--bg-tertiary)' : 'transparent')
      }
    >
      {children}
    </button>
  )
}
