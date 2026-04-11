import { useState, useMemo, useEffect } from 'react'
import { useT } from '../../contexts/LocaleContext'
import {
  useAutomationsStore,
  type SourceFilter
} from '../../stores/automationsStore'
import { useCronStore } from '../../stores/cronStore'
import { useConnectionStore } from '../../stores/connectionStore'
import { useUIStore } from '../../stores/uiStore'
import { TaskCard } from './TaskCard'
import { NewTaskDialog } from './NewTaskDialog'
import { GitHubTrackerConfigPanel } from './GitHubTrackerConfigPanel'
import { TaskReviewPanel } from './TaskReviewPanel'
import { CronJobCard } from './CronJobCard'
import { CronReviewPanel } from './CronReviewPanel'
import { useReviewPanelStore } from '../../stores/reviewPanelStore'

function SkeletonCard(): JSX.Element {
  return (
    <div
      style={{
        display: 'flex',
        alignItems: 'center',
        gap: '12px',
        padding: '10px 14px',
        borderRadius: '8px'
      }}
    >
      <div
        style={{
          width: '60px',
          height: '16px',
          borderRadius: '4px',
          backgroundColor: 'var(--bg-tertiary)',
          animation: 'pulse 1.5s ease-in-out infinite'
        }}
      />
      <div style={{ flex: 1, display: 'flex', flexDirection: 'column', gap: '6px' }}>
        <div
          style={{
            width: '70%',
            height: '14px',
            borderRadius: '4px',
            backgroundColor: 'var(--bg-tertiary)',
            animation: 'pulse 1.5s ease-in-out infinite'
          }}
        />
        <div
          style={{
            width: '40%',
            height: '12px',
            borderRadius: '4px',
            backgroundColor: 'var(--bg-tertiary)',
            animation: 'pulse 1.5s ease-in-out infinite'
          }}
        />
      </div>
    </div>
  )
}

export function AutomationsView(): JSX.Element {
  const t = useT()
  const capabilities = useConnectionStore((s) => s.capabilities)
  const hasTasks = capabilities?.automations === true
  const hasCron = capabilities?.cronManagement === true
  const hasGitHubTrackerConfig = capabilities?.gitHubTrackerConfig === true
  const automationsTab = useUIStore((s) => s.automationsTab)
  const setAutomationsTab = useUIStore((s) => s.setAutomationsTab)

  const { tasks, loading, error, filterSource, setFilterSource, fetchTasks } =
    useAutomationsStore()
  const selectedTaskId = useAutomationsStore((s) => s.selectedTaskId)
  const startPolling = useAutomationsStore((s) => s.startPolling)
  const stopPolling = useAutomationsStore((s) => s.stopPolling)

  const cronJobs = useCronStore((s) => s.jobs)
  const cronLoading = useCronStore((s) => s.loading)
  const cronError = useCronStore((s) => s.error)
  const fetchCronJobs = useCronStore((s) => s.fetchJobs)
  const startCronPolling = useCronStore((s) => s.startPolling)
  const stopCronPolling = useCronStore((s) => s.stopPolling)
  const selectedCronJobId = useCronStore((s) => s.selectedCronJobId)

  const [showNewTask, setShowNewTask] = useState(false)
  const [showGitHubConfig, setShowGitHubConfig] = useState(false)

  const filterTabs: { key: SourceFilter; label: string }[] = useMemo(
    () => [
      { key: 'all', label: t('auto.filterAll') },
      { key: 'local', label: t('auto.source.local') },
      { key: 'github', label: t('auto.source.github') }
    ],
    [t]
  )

  const activePanel: 'tasks' | 'cron' =
    automationsTab === 'tasks' && hasTasks
      ? 'tasks'
      : automationsTab === 'cron' && hasCron
        ? 'cron'
        : hasTasks
          ? 'tasks'
          : 'cron'

  const showTabBar = hasTasks || hasCron

  useEffect(() => {
    if (hasTasks && !hasCron) setAutomationsTab('tasks')
    else if (!hasTasks && hasCron) setAutomationsTab('cron')
  }, [hasTasks, hasCron, setAutomationsTab])

  // Only poll automation/task/list when the server advertises automations (cron-only workspaces have no handler).
  useEffect(() => {
    if (!hasTasks) {
      stopPolling()
      return
    }
    startPolling()
    return () => {
      stopPolling()
    }
  }, [hasTasks, startPolling, stopPolling])

  useEffect(() => {
    return () => {
      useReviewPanelStore.getState().destroyReviewPanel()
    }
  }, [])

  useEffect(() => {
    if (activePanel !== 'cron' || !hasCron) {
      stopCronPolling()
      return
    }
    void fetchCronJobs()
    startCronPolling()
    return () => {
      stopCronPolling()
    }
  }, [activePanel, hasCron, fetchCronJobs, startCronPolling, stopCronPolling])

  const filteredTasks = useMemo(() => {
    let list = tasks
    if (filterSource === 'local') list = list.filter((t) => t.sourceName === 'local')
    else if (filterSource === 'github') list = list.filter((t) => t.sourceName === 'github')
    return [...list].sort(
      (a, b) => new Date(b.updatedAt).getTime() - new Date(a.updatedAt).getTime()
    )
  }, [tasks, filterSource])

  const showNewButton = filterSource !== 'github'

  const refreshCron = () => void fetchCronJobs()

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'row',
        height: '100%',
        minHeight: 0,
        backgroundColor: 'var(--bg-primary)'
      }}
    >
      <div
        style={{
          display: 'flex',
          flexDirection: 'column',
          flex: 1,
          minWidth: 0,
          minHeight: 0
        }}
      >
        <div
          style={{
            padding: '16px 20px 12px',
            borderBottom: '1px solid var(--border-default)',
            flexShrink: 0
          }}
        >
          <div
            style={{
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'space-between',
              marginBottom: '12px',
              gap: '8px'
            }}
          >
            <h2 style={{ margin: 0, fontSize: '16px', fontWeight: 700, color: 'var(--text-primary)' }}>
              {t('auto.viewTitle')}
            </h2>
            <div style={{ display: 'flex', alignItems: 'center', gap: '8px', flexShrink: 0 }}>
              {hasGitHubTrackerConfig && (
                <button
                  type="button"
                  onClick={() => setShowGitHubConfig((v) => !v)}
                  aria-label={t('auto.githubConfig.open')}
                  title={t('auto.githubConfig.open')}
                  aria-pressed={showGitHubConfig}
                  style={{
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    width: '32px',
                    height: '32px',
                    borderRadius: '6px',
                    border: showGitHubConfig ? '1px solid var(--accent)' : '1px solid var(--border-default)',
                    backgroundColor: showGitHubConfig ? 'var(--bg-tertiary)' : 'transparent',
                    color: 'var(--text-secondary)',
                    cursor: 'pointer'
                  }}
                >
                  <svg
                    viewBox="0 0 16 16"
                    width="16"
                    height="16"
                    aria-hidden="true"
                    fill="currentColor"
                  >
                    <path d="M8 0C3.58 0 0 3.73 0 8.333c0 3.684 2.292 6.81 5.47 7.913.4.077.547-.179.547-.4 0-.197-.007-.845-.01-1.533-2.226.498-2.695-.98-2.695-.98-.364-.955-.89-1.209-.89-1.209-.727-.514.055-.504.055-.504.803.059 1.225.85 1.225.85.714 1.27 1.872.903 2.328.69.072-.533.279-.903.508-1.11-1.777-.209-3.644-.914-3.644-4.068 0-.899.31-1.635.818-2.211-.082-.209-.354-1.05.078-2.189 0 0 .668-.219 2.188.845A7.34 7.34 0 0 1 8 4.64c.68.003 1.366.095 2.006.279 1.52-1.064 2.186-.845 2.186-.845.434 1.139.162 1.98.08 2.189.51.576.818 1.312.818 2.211 0 3.162-1.87 3.857-3.652 4.062.287.256.543.759.543 1.53 0 1.104-.01 1.993-.01 2.264 0 .223.145.481.553.399C13.71 15.14 16 12.015 16 8.333 16 3.73 12.42 0 8 0Z" />
                  </svg>
                </button>
              )}
              {activePanel === 'tasks' && (
                <button
                  type="button"
                  onClick={() => void fetchTasks()}
                  aria-label={t('auto.refreshTasks')}
                  title={t('auto.refreshTasks')}
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
                  {t('common.refresh')}
                </button>
              )}
              {activePanel === 'cron' && hasCron && (
                <button
                  type="button"
                  onClick={refreshCron}
                  aria-label={t('auto.refreshCron')}
                  title={t('auto.refreshCron')}
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
                  {t('common.refresh')}
                </button>
              )}
              {activePanel === 'tasks' && showNewButton && (
                <button
                  type="button"
                  aria-label={t('auto.createTask')}
                  onClick={() => setShowNewTask(true)}
                  style={{
                    padding: '5px 14px',
                    borderRadius: '6px',
                    border: 'none',
                    backgroundColor: 'var(--accent)',
                    color: '#fff',
                    fontSize: '12px',
                    fontWeight: 600,
                    cursor: 'pointer'
                  }}
                >
                  {t('auto.newTaskButtonLabel')}
                </button>
              )}
            </div>
          </div>

          {showTabBar && (
            <div role="tablist" aria-label={t('auto.tablistAria')} style={{ display: 'flex', gap: '2px' }}>
              <button
                type="button"
                role="tab"
                disabled={!hasTasks}
                aria-disabled={!hasTasks}
                aria-selected={activePanel === 'tasks'}
                title={hasTasks ? undefined : t('auto.tabTasksUnavailable')}
                onClick={() => {
                  if (hasTasks) setAutomationsTab('tasks')
                }}
                style={{
                  padding: '4px 12px',
                  borderRadius: '6px',
                  border: 'none',
                  backgroundColor: activePanel === 'tasks' ? 'var(--bg-tertiary)' : 'transparent',
                  color: activePanel === 'tasks' ? 'var(--text-primary)' : 'var(--text-tertiary)',
                  fontSize: '12px',
                  fontWeight: 500,
                  cursor: hasTasks ? 'pointer' : 'not-allowed',
                  opacity: hasTasks ? 1 : 0.55
                }}
              >
                {t('auto.tabTasks')}
              </button>
              <button
                type="button"
                role="tab"
                disabled={!hasCron}
                aria-disabled={!hasCron}
                aria-selected={activePanel === 'cron'}
                title={hasCron ? undefined : t('auto.tabCronUnavailable')}
                onClick={() => {
                  if (hasCron) setAutomationsTab('cron')
                }}
                style={{
                  padding: '4px 12px',
                  borderRadius: '6px',
                  border: 'none',
                  backgroundColor: activePanel === 'cron' ? 'var(--bg-tertiary)' : 'transparent',
                  color: activePanel === 'cron' ? 'var(--text-primary)' : 'var(--text-tertiary)',
                  fontSize: '12px',
                  fontWeight: 500,
                  cursor: hasCron ? 'pointer' : 'not-allowed',
                  opacity: hasCron ? 1 : 0.55
                }}
              >
                {t('auto.tabCron')}
              </button>
            </div>
          )}

          {activePanel === 'tasks' && (
            <div
              role="tablist"
              aria-label={t('auto.filterSource')}
              style={{ display: 'flex', gap: '2px', marginTop: activePanel === 'tasks' ? '10px' : '0' }}
            >
              {filterTabs.map((tab) => (
                <button
                  key={tab.key}
                  type="button"
                  role="tab"
                  aria-selected={filterSource === tab.key}
                  aria-controls="automations-task-list"
                  id={`filter-tab-${tab.key}`}
                  tabIndex={filterSource === tab.key ? 0 : -1}
                  onClick={() => setFilterSource(tab.key)}
                  style={{
                    padding: '4px 12px',
                    borderRadius: '6px',
                    border: 'none',
                    backgroundColor: filterSource === tab.key ? 'var(--bg-tertiary)' : 'transparent',
                    color: filterSource === tab.key ? 'var(--text-primary)' : 'var(--text-tertiary)',
                    fontSize: '12px',
                    fontWeight: 500,
                    cursor: 'pointer',
                    transition: 'all 0.15s'
                  }}
                >
                  {tab.label}
                </button>
              ))}
            </div>
          )}
        </div>

        {showGitHubConfig && hasGitHubTrackerConfig && (
          <div
            style={{
              flex: 1,
              minHeight: 0,
              display: 'flex',
              flexDirection: 'column',
              overflow: 'hidden',
              padding: '20px'
            }}
          >
            <div style={{ width: '100%', flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column' }}>
              <GitHubTrackerConfigPanel onBack={() => setShowGitHubConfig(false)} />
            </div>
          </div>
        )}

        {!showGitHubConfig && activePanel === 'tasks' && (
          <div
            id="automations-task-list"
            role="tabpanel"
            style={{ flex: 1, overflow: 'auto', padding: '8px 6px' }}
          >
            {loading && (
              <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                <SkeletonCard />
                <SkeletonCard />
                <SkeletonCard />
              </div>
            )}

            {!loading && error && (
              <div
                style={{
                  display: 'flex',
                  flexDirection: 'column',
                  alignItems: 'center',
                  justifyContent: 'center',
                  padding: '48px 20px',
                  gap: '12px',
                  color: 'var(--text-secondary)',
                  fontSize: '13px',
                  textAlign: 'center'
                }}
              >
                <p style={{ margin: 0, color: 'var(--error)' }}>{error}</p>
                <button
                  type="button"
                  onClick={() => void fetchTasks()}
                  style={{
                    padding: '5px 14px',
                    borderRadius: '6px',
                    border: '1px solid var(--border-default)',
                    backgroundColor: 'transparent',
                    color: 'var(--text-secondary)',
                    fontSize: '12px',
                    cursor: 'pointer'
                  }}
                >
                  {t('common.retry')}
                </button>
              </div>
            )}

            {!loading && !error && filteredTasks.length === 0 && (
              <div
                style={{
                  display: 'flex',
                  flexDirection: 'column',
                  alignItems: 'center',
                  justifyContent: 'center',
                  padding: '48px 20px',
                  color: 'var(--text-tertiary)',
                  fontSize: '13px',
                  textAlign: 'center'
                }}
              >
                <p style={{ margin: 0 }}>{t('auto.emptyTasks')}</p>
                {showNewButton && (
                  <p style={{ margin: '8px 0 0', fontSize: '12px' }}>{t('auto.emptyTasksHint')}</p>
                )}
              </div>
            )}

            {!loading &&
              !error &&
              filteredTasks.map((task) => (
                <TaskCard key={`${task.sourceName}::${task.id}`} task={task} />
              ))}
          </div>
        )}

        {!showGitHubConfig && activePanel === 'cron' && hasCron && (
          <div
            id="automations-cron-list"
            role="tabpanel"
            style={{ flex: 1, overflow: 'auto', padding: '8px 6px' }}
          >
            {cronLoading && (
              <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                <SkeletonCard />
                <SkeletonCard />
              </div>
            )}

            {!cronLoading && cronError && (
              <div
                style={{
                  display: 'flex',
                  flexDirection: 'column',
                  alignItems: 'center',
                  padding: '48px 20px',
                  gap: '12px',
                  color: 'var(--text-secondary)',
                  fontSize: '13px',
                  textAlign: 'center'
                }}
              >
                <p style={{ margin: 0, color: 'var(--error)' }}>{cronError}</p>
                <button
                  type="button"
                  onClick={refreshCron}
                  style={{
                    padding: '5px 14px',
                    borderRadius: '6px',
                    border: '1px solid var(--border-default)',
                    backgroundColor: 'transparent',
                    color: 'var(--text-secondary)',
                    fontSize: '12px',
                    cursor: 'pointer'
                  }}
                >
                  {t('common.retry')}
                </button>
              </div>
            )}

            {!cronLoading && !cronError && cronJobs.length === 0 && (
              <div
                style={{
                  display: 'flex',
                  flexDirection: 'column',
                  alignItems: 'center',
                  padding: '48px 20px',
                  color: 'var(--text-tertiary)',
                  fontSize: '13px',
                  textAlign: 'center'
                }}
              >
                <p style={{ margin: 0 }}>{t('auto.emptyCron')}</p>
                <p style={{ margin: '8px 0 0', fontSize: '12px' }}>{t('auto.emptyCronHint')}</p>
              </div>
            )}

            {!cronLoading &&
              !cronError &&
              cronJobs.map((job) => <CronJobCard key={job.id} job={job} />)}
          </div>
        )}
      </div>

      {showNewTask && <NewTaskDialog onClose={() => setShowNewTask(false)} />}

      {selectedTaskId && <TaskReviewPanel />}
      {selectedCronJobId && <CronReviewPanel />}
    </div>
  )
}
