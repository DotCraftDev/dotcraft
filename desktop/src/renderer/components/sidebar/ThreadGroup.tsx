import type { ThreadSummary, ThreadGroup as ThreadGroupType } from '../../types/thread'
import { ThreadEntry } from './ThreadEntry'

interface ThreadGroupProps {
  label: ThreadGroupType
  threads: ThreadSummary[]
}

/**
 * Renders a time-group heading and its list of thread entries.
 * Spec §7.2, §9.5
 */
export function ThreadGroup({ label, threads }: ThreadGroupProps): JSX.Element {
  return (
    <div style={{ marginBottom: '4px' }}>
      {/* Group heading: 11px, semibold, uppercase, dimmed */}
      <div
        style={{
          padding: '6px 16px 2px',
          fontSize: '11px',
          fontWeight: 600,
          textTransform: 'uppercase',
          color: 'var(--text-dimmed)',
          letterSpacing: '0.04em'
        }}
      >
        {label}
      </div>

      {threads.map((thread) => (
        <ThreadEntry key={thread.id} thread={thread} />
      ))}
    </div>
  )
}
