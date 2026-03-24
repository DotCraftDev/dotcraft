import { useState, useMemo, useEffect } from 'react'
import {
  useAutomationsStore,
  type SourceFilter
} from '../../stores/automationsStore'
import { useCronStore } from '../../stores/cronStore'
import { useConnectionStore } from '../../stores/connectionStore'
import { useUIStore } from '../../stores/uiStore'
import { TaskCard } from './TaskCard'
import { NewTaskDialog } from './NewTaskDialog'
import { TaskReviewPanel } from './TaskReviewPanel'
import { CronJobCard } from './CronJobCard'
import { CronReviewPanel } from './CronReviewPanel'
import { useReviewPanelStore } from '../../stores/reviewPanelStore'

const filterTabs: { key: SourceFilter; label: string }[] = [
  { key: 'all', label: 'All' },
  { key: 'local', label: 'Local' },
  { key: 'github', label: 'GitHub' }
]

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
  const capabilities = useConnectionStore((s) => s.capabilities)
  const hasTasks = capabilities?.automations === true
  const hasCron = capabilities?.cronManagement === true
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

  const showMainTabs = hasTasks && hasCron
  const activePanel: 'tasks' | 'cron' = showMainTabs
    ? automationsTab
    : hasTasks
      ? 'tasks'
      : 'cron'

  useEffect(() => {
    if (hasTasks && !hasCron) setAutomationsTab('tasks')
    else if (!hasTasks && hasCron) setAutomationsTab('cron')
  }, [hasTasks, hasCron, setAutomationsTab])

  useEffect(() => {
    startPolling()
    return () => {
      stopPolling()
      useReviewPanelStore.getState().destroyReviewPanel()
    }
  }, [startPolling, stopPolling])

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
              Automations
            </h2>
            <div style={{ display: 'flex', alignItems: 'center', gap: '8px', flexShrink: 0 }}>
              {activePanel === 'tasks' && (
                <button
                  type="button"
                  onClick={() => void fetchTasks()}
                  aria-label="Refresh task list"
                  title="Refresh task list"
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
                  Refresh
                </button>
              )}
              {activePanel === 'cron' && hasCron && (
                <button
                  type="button"
                  onClick={refreshCron}
                  aria-label="Refresh cron jobs"
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
                  Refresh
                </button>
              )}
              {activePanel === 'tasks' && showNewButton && (
                <button
                  type="button"
                  aria-label="Create new task"
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
                  + New Task
                </button>
              )}
            </div>
          </div>

          {showMainTabs && (
            <div role="tablist" aria-label="Automations" style={{ display: 'flex', gap: '2px' }}>
              <button
                type="button"
                role="tab"
                aria-selected={automationsTab === 'tasks'}
                onClick={() => setAutomationsTab('tasks')}
                style={{
                  padding: '4px 12px',
                  borderRadius: '6px',
                  border: 'none',
                  backgroundColor: automationsTab === 'tasks' ? 'var(--bg-tertiary)' : 'transparent',
                  color: automationsTab === 'tasks' ? 'var(--text-primary)' : 'var(--text-tertiary)',
                  fontSize: '12px',
                  fontWeight: 500,
                  cursor: 'pointer'
                }}
              >
                Tasks
              </button>
              <button
                type="button"
                role="tab"
                aria-selected={automationsTab === 'cron'}
                onClick={() => setAutomationsTab('cron')}
                style={{
                  padding: '4px 12px',
                  borderRadius: '6px',
                  border: 'none',
                  backgroundColor: automationsTab === 'cron' ? 'var(--bg-tertiary)' : 'transparent',
                  color: automationsTab === 'cron' ? 'var(--text-primary)' : 'var(--text-tertiary)',
                  fontSize: '12px',
                  fontWeight: 500,
                  cursor: 'pointer'
                }}
              >
                Cron
              </button>
            </div>
          )}

          {activePanel === 'tasks' && (
            <div
              role="tablist"
              aria-label="Filter tasks by source"
              style={{ display: 'flex', gap: '2px', marginTop: showMainTabs ? '10px' : '0' }}
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

        {activePanel === 'tasks' && (
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
                  Retry
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
                <p style={{ margin: 0 }}>No automation tasks yet.</p>
                {showNewButton && (
                  <p style={{ margin: '8px 0 0', fontSize: '12px' }}>
                    Click &quot;+ New Task&quot; to create one, or wait for GitHub tasks to be discovered.
                  </p>
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

        {activePanel === 'cron' && hasCron && (
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
                  Retry
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
                <p style={{ margin: 0 }}>No scheduled jobs yet.</p>
                <p style={{ margin: '8px 0 0', fontSize: '12px' }}>
                  Ask the agent in chat to create a cron job, e.g. “Remind me every hour…”
                </p>
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
