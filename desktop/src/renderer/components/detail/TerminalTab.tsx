import { useT } from '../../contexts/LocaleContext'
import { useConversationStore } from '../../stores/conversationStore'
import { TerminalCommandBlock } from './TerminalCommandBlock'

/** Tool names treated as shell execution tools */
const SHELL_TOOLS = new Set(['Exec', 'RunCommand', 'BashCommand'])

/**
 * Terminal tab — shows all Exec/shell tool call results across all turns.
 * Commands appear in chronological order.
 * Spec §11.5
 */
export function TerminalTab(): JSX.Element {
  const t = useT()
  const turns = useConversationStore((s) => s.turns)

  // Collect all completed shell tool calls across all turns
  const commands: Array<{ id: string; command: string; output: string; duration?: number }> = []

  for (const turn of turns) {
    for (const item of turn.items) {
      if (
        item.type === 'toolCall' &&
        SHELL_TOOLS.has(item.toolName ?? '') &&
        item.status === 'completed' &&
        item.result !== undefined
      ) {
        const args = item.arguments as Record<string, unknown> | undefined
        const command =
          (args?.command as string | undefined) ??
          (args?.cmd as string | undefined) ??
          (args?.bash as string | undefined) ??
          item.toolName ??
          'shell'
        commands.push({
          id: item.id,
          command,
          output: item.result ?? '',
          duration: item.duration
        })
      }
    }
  }

  if (commands.length === 0) {
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
          {t('terminal.empty')}
        </p>
      </div>
    )
  }

  return (
    <div style={{ overflowY: 'auto', height: '100%' }}>
      {commands.map((cmd) => (
        <TerminalCommandBlock
          key={cmd.id}
          command={cmd.command}
          output={cmd.output}
          duration={cmd.duration}
        />
      ))}
    </div>
  )
}
