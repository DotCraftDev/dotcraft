import { useT } from '../../contexts/LocaleContext'
import { useConversationStore, selectStreamingPlanDraft } from '../../stores/conversationStore'
import type {
  PlanTodoItem,
  PlanTodoStatus,
  StreamingPlanDraft
} from '../../stores/conversationStore'

/**
 * Plan tab — renders the agent's plan from plan/updated events.
 * While a `CreatePlan` tool call is streaming, the draft is rendered live
 * so the user can see the plan forming in real time.
 * Shows title, overview, and todo list with status icons.
 * Spec §11.4
 */
export function PlanTab(): JSX.Element {
  const t = useT()
  const plan = useConversationStore((s) => s.plan)
  const streamingDraft = useConversationStore(selectStreamingPlanDraft)

  if (streamingDraft) {
    return <StreamingPlanDraftView draft={streamingDraft} />
  }

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

      {plan.title && (
        <hr
          style={{
            border: 'none',
            borderTop: '1px solid var(--border-default)',
            margin: '8px 0'
          }}
        />
      )}

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

function StreamingPlanDraftView({ draft }: { draft: StreamingPlanDraft }): JSX.Element {
  const t = useT()

  return (
    <div
      style={{
        padding: '16px',
        overflowY: 'auto',
        height: '100%'
      }}
    >
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: '8px',
          marginBottom: '8px',
          fontSize: '11px',
          color: 'var(--accent)',
          fontWeight: 500
        }}
      >
        <span
          className="animate-spin-custom"
          style={{
            display: 'inline-block',
            width: '10px',
            height: '10px',
            borderRadius: '50%',
            border: '2px solid var(--border-active)',
            borderTopColor: 'var(--accent)'
          }}
        />
        <span>{t('plan.streamingDraftBadge')}</span>
      </div>

      {draft.title ? (
        <h2
          style={{
            margin: '0 0 4px',
            fontSize: '14px',
            fontWeight: 600,
            color: 'var(--text-primary)'
          }}
        >
          {draft.title}
        </h2>
      ) : (
        <div
          style={{
            height: '16px',
            width: '60%',
            borderRadius: '2px',
            background: 'var(--bg-tertiary)',
            marginBottom: '8px'
          }}
        />
      )}

      <hr
        style={{
          border: 'none',
          borderTop: '1px solid var(--border-default)',
          margin: '8px 0'
        }}
      />

      {draft.overview && (
        <p
          style={{
            margin: '0 0 12px',
            fontSize: '13px',
            color: 'var(--text-secondary)',
            lineHeight: 1.6,
            whiteSpace: 'pre-wrap'
          }}
        >
          {draft.overview}
        </p>
      )}

      {draft.plan && (
        <p
          style={{
            margin: '0 0 12px',
            fontSize: '12px',
            color: 'var(--text-dimmed)',
            lineHeight: 1.6,
            whiteSpace: 'pre-wrap'
          }}
        >
          {draft.plan}
        </p>
      )}

      {draft.todos.length > 0 && (
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
          {draft.todos.map((todo, idx) => (
            <StreamingPlanTodoRow
              key={todo.id ?? `draft-${idx}`}
              content={todo.content ?? ''}
              status={todo.status ?? 'pending'}
            />
          ))}
        </ul>
      )}
    </div>
  )
}

function StreamingPlanTodoRow({
  content,
  status
}: {
  content: string
  status: string
}): JSX.Element {
  const normalized: PlanTodoStatus = (['pending', 'in_progress', 'completed', 'cancelled'] as const).includes(
    status as PlanTodoStatus
  )
    ? (status as PlanTodoStatus)
    : 'pending'
  const icon = STATUS_ICON[normalized]
  const iconColor = normalized === 'completed'
    ? 'var(--success)'
    : normalized === 'in_progress'
      ? 'var(--accent)'
      : normalized === 'cancelled'
        ? 'var(--text-dimmed)'
        : 'var(--text-secondary)'

  return (
    <li
      style={{
        display: 'flex',
        alignItems: 'flex-start',
        gap: '8px',
        fontSize: '13px',
        lineHeight: 1.5,
        opacity: content ? 1 : 0.5
      }}
    >
      <span
        style={{
          flexShrink: 0,
          width: '16px',
          textAlign: 'center',
          color: iconColor,
          fontWeight: normalized === 'in_progress' ? 600 : 400
        }}
      >
        {icon}
      </span>
      <span
        style={{
          color: normalized === 'cancelled' ? 'var(--text-dimmed)' : 'var(--text-primary)',
          textDecoration: normalized === 'cancelled' ? 'line-through' : 'none'
        }}
      >
        {content}
      </span>
    </li>
  )
}
