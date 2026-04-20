import { type CSSProperties, useEffect, useState } from 'react'
import { useT } from '../../../contexts/LocaleContext'
import { MarkdownRenderer } from '../../conversation/MarkdownRenderer'
import { TextViewer } from './TextViewer'

const MAX_READ_BYTES = 5 * 1024 * 1024 // 5 MB

interface MarkdownViewerProps {
  absolutePath: string
}

interface MarkdownState {
  status: 'idle' | 'loading' | 'ok' | 'error'
  text: string
  truncated: boolean
  error?: string
}

export function MarkdownViewer({ absolutePath }: MarkdownViewerProps): JSX.Element {
  const t = useT()
  const [mode, setMode] = useState<'preview' | 'source'>('preview')
  const [state, setState] = useState<MarkdownState>({ status: 'idle', text: '', truncated: false })

  useEffect(() => {
    let cancelled = false
    setState({ status: 'loading', text: '', truncated: false })

    window.api.workspace.viewer.readText({ absolutePath, limitBytes: MAX_READ_BYTES })
      .then((result) => {
        if (cancelled) return
        setState({ status: 'ok', text: result.text, truncated: result.truncated })
      })
      .catch((err: unknown) => {
        if (cancelled) return
        const msg = err instanceof Error ? err.message : String(err)
        setState({ status: 'error', text: '', truncated: false, error: msg })
      })

    return () => {
      cancelled = true
    }
  }, [absolutePath])

  if (mode === 'source') {
    return (
      <div style={{ display: 'flex', flexDirection: 'column', height: '100%' }}>
        <ModeToolbar mode={mode} onModeChange={setMode} />
        <div style={{ flex: 1, overflow: 'hidden' }}>
          <TextViewer absolutePath={absolutePath} />
        </div>
      </div>
    )
  }

  if (state.status === 'loading') {
    return (
      <div style={{ display: 'flex', flexDirection: 'column', height: '100%' }}>
        <ModeToolbar mode={mode} onModeChange={setMode} />
        <div style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          height: '100%',
          color: 'var(--text-secondary)',
          fontSize: '13px'
        }}>
          {t('quickOpen.loading')}
        </div>
      </div>
    )
  }

  if (state.status === 'error') {
    return (
      <div style={{ display: 'flex', flexDirection: 'column', height: '100%' }}>
        <ModeToolbar mode={mode} onModeChange={setMode} />
        <div style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          height: '100%',
          color: 'var(--text-secondary)',
          fontSize: '13px',
          padding: '24px',
          textAlign: 'center'
        }}>
          {t('viewer.readFailed')} - {state.error}
        </div>
      </div>
    )
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', height: '100%' }}>
      <ModeToolbar mode={mode} onModeChange={setMode} />
      {state.truncated && (
        <div
          role="status"
          style={{
            padding: '4px 12px',
            backgroundColor: 'var(--bg-warning, rgba(255,200,0,0.12))',
            color: 'var(--text-warning, #e8c000)',
            fontSize: '12px',
            borderBottom: '1px solid var(--border-default)',
            flexShrink: 0
          }}
        >
          {t('viewer.truncatedNotice')}
        </div>
      )}
      <div style={{ flex: 1, overflow: 'auto', padding: '16px 24px' }}>
        <MarkdownRenderer content={state.text} />
      </div>
    </div>
  )
}

function ModeToolbar({
  mode,
  onModeChange
}: {
  mode: 'preview' | 'source'
  onModeChange: (next: 'preview' | 'source') => void
}): JSX.Element {
  const t = useT()
  const common: CSSProperties = {
    border: '1px solid var(--border-default)',
    background: 'transparent',
    color: 'var(--text-secondary)',
    fontSize: '12px',
    lineHeight: 1.2,
    borderRadius: '4px',
    padding: '4px 8px',
    cursor: 'pointer'
  }

  return (
    <div style={{
      display: 'flex',
      alignItems: 'center',
      gap: '6px',
      padding: '6px 10px',
      borderBottom: '1px solid var(--border-default)',
      flexShrink: 0
    }}>
      <button
        type="button"
        onClick={() => onModeChange('preview')}
        style={{
          ...common,
          backgroundColor: mode === 'preview' ? 'var(--bg-tertiary)' : 'transparent',
          color: mode === 'preview' ? 'var(--text-primary)' : 'var(--text-secondary)'
        }}
      >
        {t('viewer.preview')}
      </button>
      <button
        type="button"
        onClick={() => onModeChange('source')}
        style={{
          ...common,
          backgroundColor: mode === 'source' ? 'var(--bg-tertiary)' : 'transparent',
          color: mode === 'source' ? 'var(--text-primary)' : 'var(--text-secondary)'
        }}
      >
        {t('viewer.source')}
      </button>
    </div>
  )
}
