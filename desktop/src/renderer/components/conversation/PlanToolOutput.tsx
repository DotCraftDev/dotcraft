import { MarkdownRenderer } from './MarkdownRenderer'
import { translate, type AppLocale } from '../../../shared/locales'

interface PlanToolOutputTodo {
  id: string
  content: string
  status: 'pending' | 'in_progress' | 'completed' | 'cancelled'
}

interface PlanToolOutputProps {
  itemId: string
  title: string
  overview: string
  content: string
  todos: PlanToolOutputTodo[]
  locale: AppLocale
}

const STATUS_ICON: Record<PlanToolOutputTodo['status'], string> = {
  pending: '○',
  in_progress: '◉',
  completed: '✓',
  cancelled: '✗'
}

export function PlanToolOutput({
  itemId,
  title,
  overview,
  content,
  todos,
  locale
}: PlanToolOutputProps): JSX.Element {
  return (
    <div
      className="selectable"
      data-plan-item-id={itemId}
      style={{ fontSize: '12px', lineHeight: 1.55, color: 'var(--text-secondary)' }}
    >
      {title && (
        <h3
          style={{
            margin: '0 0 6px',
            fontSize: '13px',
            fontWeight: 600,
            color: 'var(--text-primary)'
          }}
        >
          {title}
        </h3>
      )}

      {overview && (
        <div style={{ marginBottom: '10px' }}>
          <div style={{ fontSize: '11px', color: 'var(--text-dimmed)', marginBottom: '4px' }}>
            {translate(locale, 'toolCall.plan.overviewLabel')}
          </div>
          <p
            style={{
              margin: 0,
              whiteSpace: 'pre-wrap',
              color: 'var(--text-secondary)'
            }}
          >
            {overview}
          </p>
        </div>
      )}

      {content && (
        <div style={{ marginBottom: todos.length > 0 ? '12px' : 0 }}>
          <div style={{ fontSize: '11px', color: 'var(--text-dimmed)', marginBottom: '6px' }}>
            {translate(locale, 'toolCall.plan.contentLabel')}
          </div>
          <MarkdownRenderer content={content} />
        </div>
      )}

      {todos.length > 0 && (
        <div>
          <div style={{ fontSize: '11px', color: 'var(--text-dimmed)', marginBottom: '6px' }}>
            {translate(locale, 'toolCall.plan.todosLabel')}
          </div>
          <ul style={{ margin: 0, padding: 0, listStyle: 'none', display: 'grid', gap: '4px' }}>
            {todos.map((todo) => {
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
                <li key={todo.id} style={{ display: 'flex', alignItems: 'flex-start', gap: '8px' }}>
                  <span style={{ width: '16px', textAlign: 'center', color: iconColor, flexShrink: 0 }}>
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
            })}
          </ul>
        </div>
      )}
    </div>
  )
}
