import { MarkdownRenderer } from './MarkdownRenderer'

interface AgentMessageProps {
  text: string
  /** True while the agent is still streaming this message */
  streaming?: boolean
}

/**
 * Renders agent message text as Markdown.
 * Shows a blinking cursor at the end during streaming.
 * Spec §10.3.3
 */
export function AgentMessage({ text, streaming = false }: AgentMessageProps): JSX.Element {
  return (
    <div style={{ position: 'relative' }}>
      <MarkdownRenderer content={text} />
      {streaming && (
        <span
          aria-hidden="true"
          style={{
            display: 'inline-block',
            width: '2px',
            height: '1em',
            backgroundColor: 'var(--text-primary)',
            verticalAlign: 'text-bottom',
            marginLeft: '1px',
            animation: 'cursor-blink 800ms step-end infinite'
          }}
        />
      )}
    </div>
  )
}
