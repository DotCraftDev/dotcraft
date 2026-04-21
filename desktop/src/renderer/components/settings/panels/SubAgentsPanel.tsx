import { useCallback, useEffect, useMemo, useRef, useState, type JSX } from 'react'
import { addToast } from '../../../stores/toastStore'
import { useT } from '../../../contexts/LocaleContext'
import { NativeProfileDetail } from './subAgents/NativeProfileDetail'
import { PresetProfileDetail } from './subAgents/PresetProfileDetail'
import { CustomProfileEditor } from './subAgents/CustomProfileEditor'
import { SubAgentList } from './subAgents/SubAgentList'
import {
  isPresetProfileName,
  type SubAgentProfileEntryWire,
  type SubAgentProfileListResult,
  type SubAgentProfileWriteWire
} from './subAgents/wire'
import {
  noticeStyle,
  pageDescriptionStyle,
  pageHeadingStyle,
  pageStyle
} from './subAgents/styles'

interface SubAgentsPanelProps {
  enabled: boolean
  refreshTick?: number
}

type ViewState =
  | { kind: 'list' }
  | { kind: 'preset'; name: string }
  | { kind: 'custom'; name: string }
  | { kind: 'new' }

const TEMPLATE_NAME = 'custom-cli-oneshot'

export function SubAgentsPanel({ enabled, refreshTick = 0 }: SubAgentsPanelProps): JSX.Element {
  const t = useT()
  const [profiles, setProfiles] = useState<SubAgentProfileEntryWire[]>([])
  const [view, setView] = useState<ViewState>({ kind: 'list' })
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [deleting, setDeleting] = useState(false)
  const [restoring, setRestoring] = useState(false)
  const [togglingName, setTogglingName] = useState<string | null>(null)
  const viewRef = useRef<ViewState>(view)

  useEffect(() => {
    viewRef.current = view
  }, [view])

  const visibleProfiles = useMemo(
    () => profiles.filter((profile) => profile.name !== TEMPLATE_NAME),
    [profiles]
  )

  const selectedProfile = useMemo<SubAgentProfileEntryWire | null>(() => {
    if (view.kind === 'preset' || view.kind === 'custom') {
      return profiles.find((profile) => profile.name === view.name) ?? null
    }
    return null
  }, [profiles, view])

  const loadProfiles = useCallback(
    async (preserveView?: ViewState) => {
    if (!enabled) {
      setProfiles([])
        setView({ kind: 'list' })
      return
    }
    setLoading(true)
    try {
        const result = (await window.api.appServer.sendRequest(
        'subagent/profiles/list',
        {}
        )) as SubAgentProfileListResult
        setProfiles(result.profiles)
        setError(null)
        const keepView = preserveView ?? viewRef.current
        if (keepView.kind === 'preset' || keepView.kind === 'custom') {
          const stillExists = result.profiles.some((profile) => profile.name === keepView.name)
          if (!stillExists) {
            setView({ kind: 'list' })
          } else {
            setView(keepView)
          }
        } else {
          setView(keepView)
        }
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err)
      setError(message)
    } finally {
      setLoading(false)
    }
    },
    [enabled]
  )

  useEffect(() => {
    void loadProfiles()
  }, [loadProfiles, refreshTick])

  const handleOpenProfile = useCallback((profile: SubAgentProfileEntryWire) => {
    if (profile.name === TEMPLATE_NAME) return
    if (profile.isBuiltIn && isPresetProfileName(profile.name)) {
      setView({ kind: 'preset', name: profile.name })
    } else {
      setView({ kind: 'custom', name: profile.name })
    }
  }, [])

  const handleBack = useCallback(() => {
    setView({ kind: 'list' })
  }, [])

  const handleAddCustom = useCallback(() => {
    setView({ kind: 'new' })
  }, [])

  const handleToggleEnabled = useCallback(
    async (profile: SubAgentProfileEntryWire, nextEnabled: boolean) => {
    setTogglingName(profile.name)
    try {
      await window.api.appServer.sendRequest('subagent/profiles/setEnabled', {
        name: profile.name,
        enabled: nextEnabled
      })
        await loadProfiles()
    } catch (err) {
      addToast(
        t('settings.subAgents.actionFailed', {
          error: err instanceof Error ? err.message : String(err)
        }),
        'error'
      )
    } finally {
      setTogglingName(null)
    }
    },
    [loadProfiles, t]
  )

  const handleSaveOverride = useCallback(
    async (profile: SubAgentProfileEntryWire, definition: SubAgentProfileWriteWire) => {
      setSaving(true)
      try {
        await window.api.appServer.sendRequest('subagent/profiles/upsert', {
          name: profile.name,
          definition
        })
        addToast(t('settings.subAgents.savedToast', { name: profile.name }), 'success')
        await loadProfiles({ kind: 'preset', name: profile.name })
    } catch (err) {
      addToast(
        t('settings.subAgents.actionFailed', {
          error: err instanceof Error ? err.message : String(err)
        }),
        'error'
      )
    } finally {
        setSaving(false)
      }
    },
    [loadProfiles, t]
  )

  const handleRestoreDefaults = useCallback(
    async (profile: SubAgentProfileEntryWire) => {
      setRestoring(true)
    try {
      await window.api.appServer.sendRequest('subagent/profiles/remove', {
          name: profile.name
      })
        addToast(t('settings.subAgents.restoredToast', { name: profile.name }), 'success')
        await loadProfiles({ kind: 'preset', name: profile.name })
    } catch (err) {
      addToast(
        t('settings.subAgents.actionFailed', {
          error: err instanceof Error ? err.message : String(err)
        }),
        'error'
      )
    } finally {
        setRestoring(false)
      }
    },
    [loadProfiles, t]
  )

  const handleSaveCustom = useCallback(
    async (name: string, definition: SubAgentProfileWriteWire) => {
    setSaving(true)
    try {
      await window.api.appServer.sendRequest('subagent/profiles/upsert', {
          name,
          definition
      })
        addToast(t('settings.subAgents.savedToast', { name }), 'success')
        await loadProfiles({ kind: 'custom', name })
    } catch (err) {
      addToast(
        t('settings.subAgents.actionFailed', {
          error: err instanceof Error ? err.message : String(err)
        }),
        'error'
      )
    } finally {
      setSaving(false)
    }
    },
    [loadProfiles, t]
  )

  const handleDeleteCustom = useCallback(
    async (profile: SubAgentProfileEntryWire) => {
      setDeleting(true)
      try {
        await window.api.appServer.sendRequest('subagent/profiles/remove', {
          name: profile.name
        })
        addToast(t('settings.subAgents.deletedToast', { name: profile.name }), 'success')
        await loadProfiles({ kind: 'list' })
      } catch (err) {
        addToast(
          t('settings.subAgents.actionFailed', {
            error: err instanceof Error ? err.message : String(err)
          }),
          'error'
        )
      } finally {
        setDeleting(false)
      }
    },
    [loadProfiles, t]
  )

  if (!enabled) {
  return (
      <div style={pageStyle()}>
        <div style={pageHeadingStyle()}>{t('settings.subAgents.title')}</div>
        <div style={noticeStyle('warning')}>{t('settings.subAgents.unsupported')}</div>
      </div>
    )
  }

  if (view.kind === 'preset' && selectedProfile) {
    if (selectedProfile.name === 'native') {
      return <NativeProfileDetail profile={selectedProfile} onBack={handleBack} />
    }
    return (
      <PresetProfileDetail
        profile={selectedProfile}
        toggling={togglingName === selectedProfile.name}
        saving={saving}
        restoring={restoring}
        onBack={handleBack}
        onToggleEnabled={handleToggleEnabled}
        onSaveOverride={handleSaveOverride}
        onRestoreDefaults={handleRestoreDefaults}
      />
    )
  }

  if (view.kind === 'custom' && selectedProfile) {
    return (
      <CustomProfileEditor
        mode="edit"
        profile={selectedProfile}
        saving={saving}
        deleting={deleting}
        onBack={handleBack}
        onSave={handleSaveCustom}
        onDelete={handleDeleteCustom}
      />
    )
  }

  if (view.kind === 'new') {
    return (
      <CustomProfileEditor
        mode="create"
        profile={null}
        saving={saving}
        deleting={false}
        onBack={handleBack}
        onSave={handleSaveCustom}
      />
    )
  }

  return (
    <div style={pageStyle()}>
      <div>
        <div style={pageHeadingStyle()}>{t('settings.subAgents.title')}</div>
        <div style={pageDescriptionStyle()}>{t('settings.subAgents.description')}</div>
      </div>

      {error && (
        <div style={noticeStyle('error')}>{t('settings.subAgents.loadFailed', { error })}</div>
      )}

      <SubAgentList
        profiles={visibleProfiles}
        loading={loading}
        togglingName={togglingName}
        onOpenProfile={handleOpenProfile}
        onToggleEnabled={handleToggleEnabled}
        onAddCustom={handleAddCustom}
      />
    </div>
  )
}
