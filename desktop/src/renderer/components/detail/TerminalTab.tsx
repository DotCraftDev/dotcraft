import { useT } from '../../contexts/LocaleContext'
import { useConversationStore } from '../../stores/conversationStore'
import { TerminalCommandBlock } from './TerminalCommandBlock'

/**
 * Terminal tab - shows all commandExecution items across all turns.
 * Commands appear in chronological order and include in-progress output.
 */
export function TerminalTab(): JSX.Element {
  const t = useT()
  const turns = useConversationStore((s) => s.turns)

  const commands = turns.flatMap((turn) =>
    turn.items
      .filter((item) => item.type === 'commandExecution')
      .map((item) => ({
        id: item.id,
        command: item.command ?? 'shell',
        output: item.aggregatedOutput ?? '',
        duration: item.duration,
        running: item.status !== 'completed' || item.executionStatus === 'inProgress',
        exitCode: item.exitCode,
        source: item.commandSource
      }))
  )

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
          running={cmd.running}
          exitCode={cmd.exitCode}
          source={cmd.source}
        />
      ))}
    </div>
  )
}
