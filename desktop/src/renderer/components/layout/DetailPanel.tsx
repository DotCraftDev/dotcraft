import { useT } from '../../contexts/LocaleContext'
import { useUIStore, type DetailPanelTab } from '../../stores/uiStore'
import { useConversationStore } from '../../stores/conversationStore'
import { ChangesTab } from '../detail/ChangesTab'
import { PlanTab } from '../detail/PlanTab'
import { TerminalTab } from '../detail/TerminalTab'

interface DetailPanelProps {
  workspacePath?: string
}

/** Tool names treated as shell execution tools (must mirror TerminalTab) */
const SHELL_TOOLS = new Set(['Exec', 'RunCommand', 'BashCommand'])

/**
 * Detail Panel — three tabs: Changes, Plan, Terminal.
 * Tab bar shows a badge with file count on the Changes tab when changes exist.
 * Spec §11
 */
export function DetailPanel({ workspacePath = '' }: DetailPanelProps): JSX.Element {
  const t = useT()
  const { activeDetailTab, setActiveDetailTab, toggleDetailPanel } = useUIStore()
  const changedFiles = useConversationStore((s) => s.changedFiles)
  const turns = useConversationStore((s) => s.turns)

  const changedFileCount = changedFiles.size

  // Count terminal commands for badge
  const terminalCount = turns.reduce((acc, turn) => {
    return acc + turn.items.filter(
      (i) => i.type === 'toolCall' && SHELL_TOOLS.has(i.toolName ?? '') && i.status === 'completed'
    ).length
  }, 0)

  const tabs: { id: DetailPanelTab; label: string; badge?: number }[] = [
    {
      id: 'changes',
      label: t('detailPanel.tabChanges'),
      badge: changedFileCount > 0 ? changedFileCount : undefined
    },
    { id: 'plan', label: t('detailPanel.tabPlan') },
    {
      id: 'terminal',
      label: t('detailPanel.tabTerminal'),
      badge: terminalCount > 0 ? terminalCount : undefined
    }
  ]

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        height: '100%',
        backgroundColor: 'var(--bg-secondary)'
      }}
    >
      {/* Tab bar — fixed height matches ThreadHeader; tab underline uses inset shadow (no layout growth) */}
      <div
        style={{
          display: 'flex',
          alignItems: 'stretch',
          height: 'var(--chrome-header-height)',
          boxSizing: 'border-box',
          borderBottom: '1px solid var(--border-default)',
          flexShrink: 0,
          paddingLeft: '4px'
        }}
      >
        {tabs.map((tab) => (
          <button
            key={tab.id}
            onClick={() => setActiveDetailTab(tab.id)}
            style={{
              display: 'flex',
              alignItems: 'center',
              gap: '5px',
              height: '100%',
              padding: '0 14px',
              fontSize: '13px',
              fontWeight: activeDetailTab === tab.id ? 500 : 400,
              color:
                activeDetailTab === tab.id
                  ? 'var(--text-primary)'
                  : 'var(--text-secondary)',
              backgroundColor: 'transparent',
              border: 'none',
              boxSizing: 'border-box',
              boxShadow:
                activeDetailTab === tab.id
                  ? 'inset 0 -2px 0 var(--accent)'
                  : 'none',
              cursor: 'pointer',
              transition: 'color 100ms ease, box-shadow 100ms ease'
            }}
          >
            {tab.label}
            {tab.badge !== undefined && (
              <span
                style={{
                  display: 'inline-flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  minWidth: '16px',
                  height: '16px',
                  padding: '0 4px',
                  borderRadius: '8px',
                  background: activeDetailTab === tab.id ? 'var(--accent)' : 'var(--bg-tertiary)',
                  color: activeDetailTab === tab.id ? '#ffffff' : 'var(--text-secondary)',
                  fontSize: '10px',
                  fontWeight: 500
                }}
              >
                {tab.badge}
              </span>
            )}
          </button>
        ))}

        {/* Spacer */}
        <div style={{ flex: 1 }} />

        {/* Close button */}
        <button
          onClick={toggleDetailPanel}
          title={t('detailPanel.closeTitle')}
          style={{
            alignSelf: 'center',
            width: '28px',
            height: '28px',
            borderRadius: '4px',
            backgroundColor: 'transparent',
            border: 'none',
            color: 'var(--text-secondary)',
            cursor: 'pointer',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            fontSize: '16px',
            marginRight: '4px',
            flexShrink: 0
          }}
          aria-label={t('detailPanel.closeAria')}
        >
          ×
        </button>
      </div>

      {/* Tab content */}
      <div style={{ flex: 1, overflow: 'hidden', display: 'flex', flexDirection: 'column' }}>
        {activeDetailTab === 'changes' && (
          <ChangesTab workspacePath={workspacePath} />
        )}
        {activeDetailTab === 'plan' && (
          <PlanTab />
        )}
        {activeDetailTab === 'terminal' && (
          <TerminalTab />
        )}
      </div>
    </div>
  )
}
