/**
 * Phase 2 placeholder for the Automations sidebar entry (cron expansion later).
 */
export function AutomationsPlaceholder(): JSX.Element {
  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        height: '100%',
        padding: '32px',
        color: 'var(--text-secondary)',
        fontSize: '14px'
      }}
    >
      <p style={{ margin: 0 }}>Automations — expanded workflow UI is planned for a later release.</p>
      <p style={{ margin: '12px 0 0', fontSize: '13px' }}>Use the thread list to return to conversations.</p>
    </div>
  )
}
