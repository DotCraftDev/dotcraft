import { MarkdownRenderer } from './MarkdownRenderer'

interface AgentMessageProps {
  text: string
  streaming?: boolean
}

/**
 * Renders agent message text as Markdown.
 * Spec §10.3.3
 */
export function AgentMessage({ text }: AgentMessageProps): JSX.Element {
  return (
    <div style={{ position: 'relative' }}>
      <MarkdownRenderer content={text} />
    </div>
  )
}
