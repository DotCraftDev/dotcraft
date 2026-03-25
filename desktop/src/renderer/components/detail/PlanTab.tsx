import { useT } from '../../contexts/LocaleContext'
import { useConversationStore } from '../../stores/conversationStore'
import type { PlanTodoItem, PlanTodoStatus } from '../../stores/conversationStore'

/**
 * Plan tab — renders the agent's plan from plan/updated events.
 * Shows title, overview, and todo list with status icons.
 * Spec §11.4
 */
export function PlanTab(): JSX.Element {
  const t = useT()
  const plan = useConversationStore((s) => s.plan)

  if (!plan) {
    return (
      <div
        style={{
          flex: 1,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          padding: '16px'
        }}
      >
        <p
          style={{
            textAlign: 'center',
            color: 'var(--text-dimmed)',
            fontSize: '13px',
            lineHeight: 1.7,
            whiteSpace: 'pre-line'
          }}
        >
          {t('plan.empty')}
        </p>
      </div>
    )
  }

  return (
    <div
      style={{
        padding: '16px',
        overflowY: 'auto',
        height: '100%'
      }}
    >
      {/* Plan title */}
      {plan.title && (
        <h2
          style={{
            margin: '0 0 4px',
            fontSize: '14px',
            fontWeight: 600,
            color: 'var(--text-primary)'
          }}
        >
          {plan.title}
        </h2>
      )}

      {/* Separator */}
      {plan.title && (
        <hr
          style={{
            border: 'none',
            borderTop: '1px solid var(--border-default)',
            margin: '8px 0'
          }}
        />
      )}

      {/* Plan overview */}
      {plan.overview && (
        <p
          style={{
            margin: '0 0 12px',
            fontSize: '13px',
            color: 'var(--text-secondary)',
            lineHeight: 1.6
          }}
        >
          {plan.overview}
        </p>
      )}

      {/* Todo list */}
      {plan.todos.length > 0 && (
        <ul
          style={{
            listStyle: 'none',
            margin: 0,
            padding: 0,
            display: 'flex',
            flexDirection: 'column',
            gap: '4px'
          }}
        >
          {plan.todos.map((todo) => (
            <PlanTodoItemRow key={todo.id} todo={todo} />
          ))}
        </ul>
      )}
    </div>
  )
}

interface PlanTodoItemRowProps {
  todo: PlanTodoItem
}

const STATUS_ICON: Record<PlanTodoStatus, string> = {
  pending: '○',
  in_progress: '◉',
  completed: '✓',
  cancelled: '✗'
}

function PlanTodoItemRow({ todo }: PlanTodoItemRowProps): JSX.Element {
  const isCancelled = todo.status === 'cancelled'
  const isCompleted = todo.status === 'completed'
  const isInProgress = todo.status === 'in_progress'

  const iconColor = isCompleted
    ? 'var(--success)'
    : isInProgress
      ? 'var(--accent)'
      : isCancelled
        ? 'var(--text-dimmed)'
        : 'var(--text-secondary)'

  return (
    <li
      style={{
        display: 'flex',
        alignItems: 'flex-start',
        gap: '8px',
        fontSize: '13px',
        lineHeight: 1.5
      }}
    >
      {/* Status icon */}
      <span
        style={{
          flexShrink: 0,
          width: '16px',
          textAlign: 'center',
          color: iconColor,
          fontWeight: isInProgress ? 600 : 400
        }}
      >
        {STATUS_ICON[todo.status]}
      </span>
      {/* Content */}
      <span
        style={{
          color: isCancelled ? 'var(--text-dimmed)' : 'var(--text-primary)',
          textDecoration: isCancelled ? 'line-through' : 'none'
        }}
      >
        {todo.content}
      </span>
    </li>
  )
}
