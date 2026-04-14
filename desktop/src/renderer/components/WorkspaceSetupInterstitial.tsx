import { useT } from '../contexts/LocaleContext'
import { DotCraftLogo } from './ui/DotCraftLogo'

interface WorkspaceSetupInterstitialProps {
  workspacePath: string
  onStart: () => void
  onChooseDifferentWorkspace: () => void
}

export function WorkspaceSetupInterstitial({
  workspacePath,
  onStart,
  onChooseDifferentWorkspace
}: WorkspaceSetupInterstitialProps): JSX.Element {
  const t = useT()

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        height: '100%',
        padding: '40px 24px',
        background: 'var(--bg-primary)',
        color: 'var(--text-primary)',
        boxSizing: 'border-box'
      }}
    >
      <DotCraftLogo size={64} style={{ marginBottom: '20px' }} />
      <h1
        style={{
          margin: '0 0 10px',
          fontSize: '26px',
          fontWeight: 700,
          letterSpacing: '-0.4px'
        }}
      >
        {t('setupInterstitial.title')}
      </h1>
      <p
        style={{
          maxWidth: '560px',
          margin: '0 0 14px',
          fontSize: '14px',
          lineHeight: 1.6,
          color: 'var(--text-secondary)',
          textAlign: 'center'
        }}
      >
        {t('setupInterstitial.description')}
      </p>
      <div
        style={{
          maxWidth: '640px',
          width: '100%',
          padding: '12px 14px',
          borderRadius: '10px',
          border: '1px solid var(--border-default)',
          background: 'var(--bg-secondary)',
          color: 'var(--text-secondary)',
          fontSize: '12px',
          fontFamily: 'var(--font-mono)',
          marginBottom: '22px',
          wordBreak: 'break-all'
        }}
      >
        {workspacePath}
      </div>
      <div
        style={{
          display: 'flex',
          gap: '10px',
          alignItems: 'center'
        }}
      >
        <button
          type="button"
          onClick={onStart}
          style={{
            padding: '11px 24px',
            border: 'none',
            borderRadius: '8px',
            background: 'var(--accent)',
            color: 'var(--on-accent)',
            fontSize: '14px',
            fontWeight: 600,
            cursor: 'pointer'
          }}
        >
          {t('setupInterstitial.start')}
        </button>
        <button
          type="button"
          onClick={onChooseDifferentWorkspace}
          style={{
            padding: '11px 18px',
            borderRadius: '8px',
            border: '1px solid var(--border-default)',
            background: 'transparent',
            color: 'var(--text-primary)',
            fontSize: '14px',
            cursor: 'pointer'
          }}
        >
          {t('setupInterstitial.chooseDifferent')}
        </button>
      </div>
    </div>
  )
}
