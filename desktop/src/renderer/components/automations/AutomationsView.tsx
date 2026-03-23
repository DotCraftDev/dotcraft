import { useState, useMemo, useEffect } from 'react'
import {
  useAutomationsStore,
  type SourceFilter
} from '../../stores/automationsStore'
import { TaskCard } from './TaskCard'
import { NewTaskDialog } from './NewTaskDialog'
import { TaskReviewPanel } from './TaskReviewPanel'
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
  const { tasks, loading, error, filterSource, setFilterSource, fetchTasks } =
    useAutomationsStore()
  const selectedTaskId = useAutomationsStore((s) => s.selectedTaskId)
  const [showNewTask, setShowNewTask] = useState(false)

  useEffect(() => {
    return () => {
      useReviewPanelStore.getState().destroyReviewPanel()
    }
  }, [])

  const filteredTasks = useMemo(() => {
    let list = tasks
    if (filterSource === 'local') list = list.filter((t) => t.sourceName === 'local')
    else if (filterSource === 'github') list = list.filter((t) => t.sourceName === 'github')
    return [...list].sort(
      (a, b) => new Date(b.updatedAt).getTime() - new Date(a.updatedAt).getTime()
    )
  }, [tasks, filterSource])

  const showNewButton = filterSource !== 'github'

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
      {/* Header */}
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
            marginBottom: '12px'
          }}
        >
          <h2 style={{ margin: 0, fontSize: '16px', fontWeight: 700, color: 'var(--text-primary)' }}>
            Automations
          </h2>
          {showNewButton && (
            <button
              type="button"
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

        {/* Filter tabs */}
        <div style={{ display: 'flex', gap: '2px' }}>
          {filterTabs.map((tab) => (
            <button
              key={tab.key}
              type="button"
              onClick={() => setFilterSource(tab.key)}
              style={{
                padding: '4px 12px',
                borderRadius: '6px',
                border: 'none',
                backgroundColor:
                  filterSource === tab.key ? 'var(--bg-tertiary)' : 'transparent',
                color:
                  filterSource === tab.key
                    ? 'var(--text-primary)'
                    : 'var(--text-tertiary)',
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
      </div>

      {/* Body */}
      <div style={{ flex: 1, overflow: 'auto', padding: '8px 6px' }}>
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
              onClick={() => fetchTasks()}
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
          filteredTasks.map((task) => <TaskCard key={`${task.sourceName}::${task.id}`} task={task} />)}
      </div>

      {showNewTask && <NewTaskDialog onClose={() => setShowNewTask(false)} />}
      </div>

      {selectedTaskId && <TaskReviewPanel />}
    </div>
  )
}
