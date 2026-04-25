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
          fontSize: 'var(--type-title-size)',
          lineHeight: 'var(--type-title-line-height)',
          fontWeight: 'var(--type-title-weight)',
          letterSpacing: 0
        }}
      >
        {t('setupInterstitial.title')}
      </h1>
      <p
        style={{
          maxWidth: '560px',
          margin: '0 0 14px',
          fontSize: 'var(--type-body-size)',
          lineHeight: 'var(--type-body-line-height)',
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
          fontSize: 'var(--type-secondary-size)',
          lineHeight: 'var(--type-secondary-line-height)',
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
            fontSize: 'var(--type-body-size)',
            lineHeight: 'var(--type-body-line-height)',
            fontWeight: 'var(--type-ui-emphasis-weight)',
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
            fontSize: 'var(--type-body-size)',
            lineHeight: 'var(--type-body-line-height)',
            cursor: 'pointer'
          }}
        >
          {t('setupInterstitial.chooseDifferent')}
        </button>
      </div>
    </div>
  )
}
