import { useT } from '../../contexts/LocaleContext'
import { useUIStore, type DetailPanelTab } from '../../stores/uiStore'
import { useConversationStore } from '../../stores/conversationStore'
import { ChangesTab } from '../detail/ChangesTab'
import { PlanTab } from '../detail/PlanTab'
import { TerminalTab } from '../detail/TerminalTab'

interface DetailPanelProps {
  workspacePath?: string
}

/**
 * Detail Panel - three tabs: Changes, Plan, Terminal.
 * Tab bar shows a badge with file count on the Changes tab when changes exist.
 */
export function DetailPanel({ workspacePath = '' }: DetailPanelProps): JSX.Element {
  const t = useT()
  const { activeDetailTab, setActiveDetailTab, toggleDetailPanel } = useUIStore()
  const changedFiles = useConversationStore((s) => s.changedFiles)
  const turns = useConversationStore((s) => s.turns)

  const changedFileCount = changedFiles.size
  const terminalCount = turns.reduce(
    (acc, turn) => acc + turn.items.filter((i) => i.type === 'commandExecution').length,
    0
  )

  const tabs: { id: DetailPanelTab; label: string; icon: JSX.Element; badge?: number }[] = [
    {
      id: 'changes',
      label: t('detailPanel.tabChanges'),
      icon: <ChangesIcon />,
      badge: changedFileCount > 0 ? changedFileCount : undefined
    },
    { id: 'plan', label: t('detailPanel.tabPlan'), icon: <PlanIcon /> },
    {
      id: 'terminal',
      label: t('detailPanel.tabTerminal'),
      icon: <TerminalIcon />,
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
            title={tab.label}
            aria-label={tab.label}
            style={{
              display: 'flex',
              alignItems: 'center',
              gap: '5px',
              height: '100%',
              padding: '0 10px',
              fontSize: '13px',
              fontWeight: activeDetailTab === tab.id ? 500 : 400,
              color: activeDetailTab === tab.id
                ? 'var(--text-primary)'
                : 'var(--text-secondary)',
              backgroundColor: 'transparent',
              border: 'none',
              boxSizing: 'border-box',
              boxShadow: activeDetailTab === tab.id
                ? 'inset 0 -2px 0 var(--accent)'
                : 'none',
              cursor: 'pointer',
              transition: 'color 100ms ease, box-shadow 100ms ease'
            }}
          >
            {tab.icon}
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

        <div style={{ flex: 1 }} />

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

      <div style={{ flex: 1, overflow: 'hidden', display: 'flex', flexDirection: 'column' }}>
        {activeDetailTab === 'changes' && <ChangesTab workspacePath={workspacePath} />}
        {activeDetailTab === 'plan' && <PlanTab />}
        {activeDetailTab === 'terminal' && <TerminalTab />}
      </div>
    </div>
  )
}

function ChangesIcon(): JSX.Element {
  return (
    <svg
      width="16"
      height="16"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden
      style={{ display: 'block' }}
    >
      <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" />
      <path d="M14 2v6h6" />
      <path d="M10 14h4" />
      <path d="M12 12v4" />
    </svg>
  )
}

function PlanIcon(): JSX.Element {
  return (
    <svg
      width="16"
      height="16"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden
      style={{ display: 'block' }}
    >
      <path d="m3 17 2 2 4-4" />
      <path d="m3 7 2 2 4-4" />
      <path d="M13 6h8" />
      <path d="M13 12h8" />
      <path d="M13 18h8" />
    </svg>
  )
}

function TerminalIcon(): JSX.Element {
  return (
    <svg
      width="16"
      height="16"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden
      style={{ display: 'block' }}
    >
      <rect x="3" y="4" width="18" height="16" rx="2" />
      <path d="m7 8 4 4-4 4" />
      <path d="M13 16h4" />
    </svg>
  )
}
