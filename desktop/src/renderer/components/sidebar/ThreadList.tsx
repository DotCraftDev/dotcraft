import { useShallow } from 'zustand/react/shallow'
import { useT } from '../../contexts/LocaleContext'
import { useThreadStore, selectFilteredThreads } from '../../stores/threadStore'
import { groupThreads } from '../../utils/threadGrouping'
import { ThreadGroup } from './ThreadGroup'
import type { ThreadGroup as ThreadGroupType } from '../../types/thread'
import { THREAD_GROUP_ORDER } from '../../types/thread'

/**
 * Scrollable container for the grouped thread list.
 * Handles empty states for "no threads" and "no search results".
 * Spec §9.5
 */
export function ThreadList(): JSX.Element {
  const t = useT()
  const { threadList, searchQuery, loading } = useThreadStore()
  // useShallow prevents infinite re-renders: selectFilteredThreads returns a new
  // array on every call (via .filter), so without shallow equality Zustand's
  // useSyncExternalStore sees a changed snapshot every render and loops.
  const filteredThreads = useThreadStore(useShallow(selectFilteredThreads))

  if (loading) {
    return (
      <div style={emptyStyle}>
        <span style={{ color: 'var(--text-dimmed)', fontSize: '13px' }}>{t('threadList.loading')}</span>
      </div>
    )
  }

  if (threadList.length === 0) {
    return (
      <div style={emptyStyle}>
        <span style={{ color: 'var(--text-dimmed)', fontSize: '13px', textAlign: 'center' }}>
          {t('threadList.empty')}
          <br />
          {t('threadList.emptyHint', { label: t('sidebar.newThreadLabel') })}
        </span>
      </div>
    )
  }

  if (filteredThreads.length === 0 && searchQuery) {
    return (
      <div style={emptyStyle}>
        <span style={{ color: 'var(--text-dimmed)', fontSize: '13px' }}>
          {t('threadList.noSearchResults')}
        </span>
      </div>
    )
  }

  const groups = groupThreads(filteredThreads)

  return (
    <div
      style={{
        flex: 1,
        overflowY: 'auto',
        overflowX: 'hidden',
        paddingBottom: '8px',
        scrollbarWidth: 'thin',
        scrollbarColor: 'var(--border-default) transparent'
      }}
    >
      {(THREAD_GROUP_ORDER as ThreadGroupType[])
        .filter((g) => groups.has(g))
        .map((group) => (
          <ThreadGroup key={group} label={group} threads={groups.get(group)!} />
        ))}
    </div>
  )
}

const emptyStyle: React.CSSProperties = {
  flex: 1,
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  padding: '24px 16px'
}
