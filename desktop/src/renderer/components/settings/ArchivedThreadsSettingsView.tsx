import { useCallback, useEffect, useState, type CSSProperties, type JSX } from 'react'
import { useLocale, useT } from '../../contexts/LocaleContext'
import { addToast } from '../../stores/toastStore'
import { useThreadStore } from '../../stores/threadStore'
import type { SessionIdentity, ThreadSummary } from '../../types/thread'
import { formatRelativeTime } from '../../utils/relativeTime'
import { ensureVisibleChannelsSeeded } from '../../utils/visibleChannelsDefaults'

interface ArchivedThreadsSettingsViewProps {
  workspacePath?: string
  onThreadListRefreshRequested?: () => void
}

function cardStyle(): CSSProperties {
  return {
    border: '1px solid var(--border-default)',
    borderRadius: '12px',
    background: 'var(--bg-secondary)',
    padding: '14px 16px'
  }
}

function actionButtonStyle(disabled = false): CSSProperties {
  return {
    padding: '8px 12px',
    borderRadius: '999px',
    border: '1px solid var(--border-default)',
    background: 'transparent',
    color: 'var(--text-primary)',
    fontSize: '12px',
    fontWeight: 600,
    cursor: disabled ? 'default' : 'pointer',
    opacity: disabled ? 0.7 : 1
  }
}

export function ArchivedThreadsSettingsView({
  workspacePath,
  onThreadListRefreshRequested
}: ArchivedThreadsSettingsViewProps): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const [threads, setThreads] = useState<ThreadSummary[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [restoringIds, setRestoringIds] = useState<Set<string>>(new Set())

  const loadArchivedThreads = useCallback(async () => {
    if (!workspacePath) {
      setThreads([])
      setError(null)
      setLoading(false)
      return
    }

    setLoading(true)
    setError(null)
    try {
      const settings = await window.api.settings.get()
      const crossChannelOrigins = await ensureVisibleChannelsSeeded(settings)
      const identity: SessionIdentity = {
        channelName: 'dotcraft-desktop',
        userId: 'local',
        channelContext: `workspace:${workspacePath}`,
        workspacePath
      }
      const result = await window.api.appServer.sendRequest('thread/list', {
        identity,
        includeArchived: true,
        crossChannelOrigins
      })
      const archivedThreads = ((result as { data?: ThreadSummary[] }).data ?? []).filter(
        (thread) => thread.status === 'archived'
      )
      setThreads(archivedThreads)
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
      setThreads([])
    } finally {
      setLoading(false)
    }
  }, [workspacePath])

  useEffect(() => {
    void loadArchivedThreads()
  }, [loadArchivedThreads])

  useEffect(() => {
    const unsubscribe = window.api.appServer.onNotification((payload: { method: string; params: unknown }) => {
      const params = (payload.params ?? {}) as Record<string, unknown>

      switch (payload.method) {
        case 'thread/statusChanged': {
          const threadId = params.threadId as string | undefined
          const newStatus = params.newStatus as string | undefined
          if (!threadId) return

          if (newStatus === 'active') {
            setThreads((current) => current.filter((thread) => thread.id !== threadId))
            useThreadStore.getState().updateThreadStatus(threadId, 'active')
            onThreadListRefreshRequested?.()
          } else if (newStatus === 'archived') {
            void loadArchivedThreads()
          }
          break
        }
        case 'thread/deleted': {
          const threadId = params.threadId as string | undefined
          if (threadId) {
            setThreads((current) => current.filter((thread) => thread.id !== threadId))
          }
          break
        }
        case 'thread/renamed': {
          const threadId = params.threadId as string | undefined
          const displayName = params.displayName as string | undefined
          if (threadId && displayName) {
            setThreads((current) =>
              current.map((thread) => (thread.id === threadId ? { ...thread, displayName } : thread))
            )
          }
          break
        }
      }
    })

    return unsubscribe
  }, [loadArchivedThreads, onThreadListRefreshRequested])

  async function handleRestore(threadId: string): Promise<void> {
    setRestoringIds((current) => {
      const next = new Set(current)
      next.add(threadId)
      return next
    })

    try {
      await window.api.appServer.sendRequest('thread/unarchive', { threadId })
      setThreads((current) => current.filter((thread) => thread.id !== threadId))
      useThreadStore.getState().updateThreadStatus(threadId, 'active')
      onThreadListRefreshRequested?.()
      addToast(t('archivedThreads.restoreSuccess'), 'success')
    } catch (err) {
      addToast(
        t('archivedThreads.restoreFailed', {
          error: err instanceof Error ? err.message : String(err)
        }),
        'error'
      )
    } finally {
      setRestoringIds((current) => {
        const next = new Set(current)
        next.delete(threadId)
        return next
      })
    }
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
      <div>
        <div style={{ fontSize: '18px', fontWeight: 600, color: 'var(--text-primary)' }}>
          {t('archivedThreads.title')}
        </div>
        <div style={{ fontSize: '12px', color: 'var(--text-dimmed)', marginTop: '4px', lineHeight: 1.5 }}>
          {t('archivedThreads.description')}
        </div>
      </div>

      {loading && (
        <div style={cardStyle()}>
          <div style={{ fontSize: '13px', color: 'var(--text-dimmed)' }}>{t('archivedThreads.loading')}</div>
        </div>
      )}

      {!loading && error && (
        <div style={cardStyle()}>
          <div style={{ fontSize: '13px', color: '#f85149' }}>
            {t('archivedThreads.loadFailed', { error })}
          </div>
        </div>
      )}

      {!loading && !error && threads.length === 0 && (
        <div style={cardStyle()}>
          <div style={{ fontSize: '13px', color: 'var(--text-dimmed)' }}>{t('archivedThreads.empty')}</div>
        </div>
      )}

      {!loading && !error && threads.length > 0 && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
          {threads.map((thread) => {
            const displayName = thread.displayName?.trim() || t('sidebar.newConversation')
            const restoring = restoringIds.has(thread.id)
            return (
              <div
                key={thread.id}
                style={{
                  ...cardStyle(),
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'space-between',
                  gap: '14px'
                }}
              >
                <div style={{ minWidth: 0 }}>
                  <div
                    style={{
                      fontSize: '14px',
                      fontWeight: 600,
                      color: 'var(--text-primary)',
                      overflow: 'hidden',
                      textOverflow: 'ellipsis',
                      whiteSpace: 'nowrap'
                    }}
                    title={displayName}
                  >
                    {displayName}
                  </div>
                  <div style={{ fontSize: '12px', color: 'var(--text-dimmed)', marginTop: '6px' }}>
                    {formatRelativeTime(thread.lastActiveAt, new Date(), locale)}
                  </div>
                  <div style={{ fontSize: '12px', color: 'var(--text-dimmed)', marginTop: '4px' }}>
                    {t('archivedThreads.origin', { origin: thread.originChannel })}
                  </div>
                </div>

                <button
                  type="button"
                  onClick={() => {
                    void handleRestore(thread.id)
                  }}
                  disabled={restoring}
                  style={actionButtonStyle(restoring)}
                >
                  {restoring ? t('archivedThreads.restoring') : t('archivedThreads.restore')}
                </button>
              </div>
            )
          })}
        </div>
      )}
    </div>
  )
}
