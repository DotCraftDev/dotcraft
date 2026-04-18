import { useCallback, useEffect, useMemo, useState } from 'react'
import { useThreadStore } from '../../stores/threadStore'
import { selectLatestCreatePlanTurnId, useConversationStore } from '../../stores/conversationStore'
import { useConnectionStore } from '../../stores/connectionStore'
import { useModelCatalogStore } from '../../stores/modelCatalogStore'
import { addToast } from '../../stores/toastStore'
import { useUIStore } from '../../stores/uiStore'
import { ThreadHeader } from '../conversation/ThreadHeader'
import { MessageStream } from '../conversation/MessageStream'
import { TurnStatusIndicator } from '../conversation/TurnStatusIndicator'
import { InputComposer } from '../conversation/InputComposer'
import { PlanApprovalComposer } from '../conversation/PlanApprovalComposer'
import { ConversationWelcome } from '../conversation/ConversationWelcome'
import type { ThreadConfigurationWire } from '../../types/thread'
import { parseJsonConfig } from '../../../shared/jsonConfig'

interface ConversationPanelProps {
  workspacePath?: string
}

/**
 * Main conversation panel — M3 full implementation.
 * Composes: ThreadHeader, MessageStream, TurnStatusIndicator, InputComposer.
 * Spec §10
 */
export function ConversationPanel({ workspacePath = '' }: ConversationPanelProps): JSX.Element {
  const { activeThread, activeThreadId, loading } = useThreadStore()
  const turns = useConversationStore((s) => s.turns)
  const turnStatus = useConversationStore((s) => s.turnStatus)
  const threadMode = useConversationStore((s) => s.threadMode)
  const pendingApproval = useConversationStore((s) => s.pendingApproval)
  const latestCreatePlanTurnId = useConversationStore(selectLatestCreatePlanTurnId)
  const connectionStatus = useConnectionStore((s) => s.status)
  const connectionErrorMessage = useConnectionStore((s) => s.errorMessage)
  const capabilities = useConnectionStore((s) => s.capabilities)
  const planApprovalDismissed = useUIStore((s) => s.planApprovalDismissed)
  const resetPlanApprovalDismissed = useUIStore((s) => s.resetPlanApprovalDismissed)
  const modelOptions = useModelCatalogStore((s) => s.modelOptions)
  const modelCatalogStatus = useModelCatalogStore((s) => s.status)
  const modelListUnsupportedEndpoint = useModelCatalogStore((s) => s.modelListUnsupportedEndpoint)
  const [modelName, setModelName] = useState<string>('Default')
  const [modelApplying, setModelApplying] = useState(false)

  const showReconnectionBanner = connectionStatus === 'disconnected'
  const modelApiAvailable =
    capabilities?.modelCatalogManagement === true &&
    capabilities?.workspaceConfigManagement === true &&
    connectionStatus === 'connected' &&
    Boolean(activeThreadId)
  const modelLoading = modelApiAvailable && modelCatalogStatus === 'loading'
  const showPlanApproval = threadMode === 'plan'
    && turnStatus === 'idle'
    && pendingApproval == null
    && latestCreatePlanTurnId != null
    && planApprovalDismissed[latestCreatePlanTurnId] !== true

  const workspaceConfigPath = useMemo(() => {
    if (!workspacePath) return ''
    const normalized = workspacePath.replace(/[\\/]+$/, '')
    const sep = normalized.includes('\\') ? '\\' : '/'
    return `${normalized}${sep}.craft${sep}config.json`
  }, [workspacePath])

  const readWorkspaceConfig = useCallback(async (): Promise<Record<string, unknown>> => {
    if (!workspaceConfigPath) return {}
    const raw = await window.api.file.readFile(workspaceConfigPath)
    return parseJsonConfig<Record<string, unknown>>(raw, {})
  }, [workspaceConfigPath])

  const setCaseInsensitiveField = useCallback(
    (target: Record<string, unknown>, key: string, value: unknown): void => {
      const lower = key.toLowerCase()
      const existingKey = Object.keys(target).find((k) => k.toLowerCase() === lower)
      if (existingKey) {
        target[existingKey] = value
      } else {
        target[key] = value
      }
    },
    []
  )

  const deleteCaseInsensitiveField = useCallback((target: Record<string, unknown>, key: string): void => {
    const lower = key.toLowerCase()
    const existingKey = Object.keys(target).find((k) => k.toLowerCase() === lower)
    if (existingKey) delete target[existingKey]
  }, [])

  const resolveEffectiveModel = useCallback(
    (thread: typeof activeThread, workspaceCfg: Record<string, unknown>): string => {
      const workspaceModelRaw = workspaceCfg.Model ?? workspaceCfg.model
      const ws =
        typeof workspaceModelRaw === 'string' ? workspaceModelRaw.trim() : ''
      const workspaceModel =
        ws.length > 0 && ws !== 'Default' ? ws : null
      const threadRaw = thread?.configuration?.model ?? thread?.configuration?.Model
      const threadTrimmed = typeof threadRaw === 'string' ? threadRaw.trim() : ''
      if (threadTrimmed.length > 0 && threadTrimmed !== 'Default') {
        return threadTrimmed
      }
      return workspaceModel ?? 'Default'
    },
    []
  )

  useEffect(() => {
    let disposed = false
    const loadEffectiveModel = async (): Promise<void> => {
      try {
        const workspaceCfg = await readWorkspaceConfig()
        if (disposed) return
        setModelName(resolveEffectiveModel(activeThread, workspaceCfg))
      } catch {
        if (disposed) return
        const modelFromThread = activeThread?.configuration?.model ?? activeThread?.configuration?.Model
        const mt = typeof modelFromThread === 'string' ? modelFromThread.trim() : ''
        setModelName(mt.length > 0 && mt !== 'Default' ? mt : 'Default')
      }
    }

    void loadEffectiveModel()
    return () => {
      disposed = true
    }
  }, [
    activeThreadId,
    activeThread?.configuration?.Model,
    activeThread?.configuration?.model,
    readWorkspaceConfig,
    resolveEffectiveModel
  ])

  useEffect(() => {
    resetPlanApprovalDismissed()
  }, [activeThreadId, resetPlanApprovalDismissed])

  const handleModelChange = useCallback(
    async (nextModel: string): Promise<void> => {
      if (!activeThread || !nextModel || nextModel === modelName) return
      setModelApplying(true)
      const previousModel = modelName
      setModelName(nextModel)
      try {
        await window.api.appServer.sendRequest('workspace/config/update', {
          model: nextModel === 'Default' ? null : nextModel
        })

        const readRes = (await window.api.appServer.sendRequest('thread/read', {
          threadId: activeThread.id,
          includeTurns: false
        })) as { thread?: { configuration?: ThreadConfigurationWire | null } }
        const existingConfig =
          readRes.thread?.configuration && typeof readRes.thread.configuration === 'object'
            ? { ...(readRes.thread.configuration as Record<string, unknown>) }
            : {}
        if (nextModel === 'Default') {
          deleteCaseInsensitiveField(existingConfig, 'model')
        } else {
          setCaseInsensitiveField(existingConfig, 'model', nextModel)
        }

        await window.api.appServer.sendRequest('thread/config/update', {
          threadId: activeThread.id,
          config: existingConfig
        })
        const active = useThreadStore.getState().activeThread
        if (active && active.id === activeThread.id) {
          const mergedCfg: Record<string, unknown> = { ...(active.configuration ?? {}) }
          if (nextModel === 'Default') {
            deleteCaseInsensitiveField(mergedCfg, 'model')
          } else {
            mergedCfg.model = nextModel
          }
          useThreadStore.getState().setActiveThread({
            ...active,
            configuration: mergedCfg as typeof active.configuration
          })
        }
        addToast(
          nextModel === 'Default' ? 'Using workspace default model' : `Model switched to ${nextModel}`,
          'success'
        )
      } catch (err) {
        const msg = err instanceof Error ? err.message : String(err)
        setModelName(previousModel)
        addToast(`Failed to switch model: ${msg}`, 'error')
      } finally {
        setModelApplying(false)
      }
    },
    [
      activeThread,
      deleteCaseInsensitiveField,
      modelName,
      setCaseInsensitiveField,
    ]
  )

  // Loading state: thread selected but full data not yet fetched
  if (activeThreadId && !activeThread && loading) {
    return (
      <div style={centeredStyle}>
        <span style={{ color: 'var(--text-dimmed)', fontSize: '13px' }}>Loading thread...</span>
      </div>
    )
  }

  // No thread selected — show Codex-style welcome card
  if (!activeThread) {
    return <ConversationWelcome workspacePath={workspacePath} />
  }

  const threadName = activeThread.displayName ?? 'New conversation'
  const hasContent = turns.length > 0 || turnStatus === 'running'

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        height: '100%',
        backgroundColor: 'var(--bg-primary)',
        overflow: 'hidden'
      }}
    >
      {/* Fixed header */}
      <ThreadHeader threadName={threadName} threadId={activeThread.id} workspacePath={workspacePath} />

      {/* Reconnection banner */}
      {showReconnectionBanner && (
        <div
          role="status"
          aria-live="polite"
          style={{
            padding: '8px 16px',
            backgroundColor: 'rgba(220,38,38,0.1)',
            borderBottom: '1px solid var(--error)',
            color: 'var(--error)',
            fontSize: '12px',
            fontWeight: 500,
            display: 'flex',
            alignItems: 'center',
            gap: '8px',
            flexShrink: 0
          }}
        >
          <span style={{ width: '7px', height: '7px', borderRadius: '50%', background: 'var(--error)', flexShrink: 0, animation: 'pulse 1.5s ease-in-out infinite' }} />
          {connectionErrorMessage || 'Connection lost. Reconnecting...'}
        </div>
      )}

      {/* Archived thread notice — spec §18.2 */}
      {activeThread.status === 'archived' && (
        <div
          role="status"
          style={{
            padding: '8px 16px',
            backgroundColor: 'rgba(160,160,160,0.1)',
            borderBottom: '1px solid var(--border-default)',
            color: 'var(--text-secondary)',
            fontSize: '12px',
            fontWeight: 500,
            display: 'flex',
            alignItems: 'center',
            gap: '8px',
            flexShrink: 0
          }}
        >
          This thread has been archived.
        </div>
      )}

      {/* Message stream (fills remaining space) */}
      {hasContent ? (
        <MessageStream />
      ) : (
        <div style={centeredStyle}>
          <p style={{ fontSize: '14px', color: 'var(--text-dimmed)', margin: 0, textAlign: 'center' }}>
            Type a message below to get started.
          </p>
        </div>
      )}

      {/* Turn running indicator */}
      <TurnStatusIndicator threadId={activeThread.id} />

      {/* Input composer */}
      {showPlanApproval && latestCreatePlanTurnId ? (
        <PlanApprovalComposer
          threadId={activeThread.id}
          workspacePath={workspacePath}
          turnId={latestCreatePlanTurnId}
        />
      ) : (
        <InputComposer
          threadId={activeThread.id}
          workspacePath={workspacePath}
          modelName={modelName}
          modelOptions={modelOptions}
          modelLoading={modelLoading}
          modelDisabled={modelApplying || !modelApiAvailable}
          modelListUnsupportedEndpoint={modelListUnsupportedEndpoint}
          onModelChange={(m) => {
            void handleModelChange(m)
          }}
        />
      )}
    </div>
  )
}

const centeredStyle: React.CSSProperties = {
  display: 'flex',
  flex: 1,
  flexDirection: 'column',
  alignItems: 'center',
  justifyContent: 'center',
  backgroundColor: 'var(--bg-primary)'
}
