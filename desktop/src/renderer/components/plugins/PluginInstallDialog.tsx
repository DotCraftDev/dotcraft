import { useEffect, type CSSProperties } from 'react'
import { createPortal } from 'react-dom'
import { Box, Server, Wrench, X } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import type { PluginEntry } from '../../stores/pluginStore'
import { PluginIcon, pluginSubtitle, pluginTitle } from './PluginCatalogItem'

const dotharLogoUrl = new URL('../../assets/brand/dothar.svg', import.meta.url).href

export function PluginInstallDialog({
  plugin,
  installing,
  onInstall,
  onClose
}: {
  plugin: PluginEntry
  installing?: boolean
  onInstall: () => void
  onClose: () => void
}): JSX.Element {
  const t = useT()
  const title = pluginTitle(plugin)
  const capabilities = plugin.interface?.capabilities ?? []

  useEffect(() => {
    function handleKeyDown(event: KeyboardEvent): void {
      if (event.key === 'Escape') onClose()
    }
    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [onClose])

  const dialog = (
    <div role="dialog" aria-modal="true" aria-labelledby="plugin-install-title" style={backdrop} onMouseDown={(event) => {
      if (event.target === event.currentTarget) onClose()
    }}>
      <div style={dialogCard} onMouseDown={(event) => event.stopPropagation()}>
        <button type="button" aria-label={t('common.close')} onClick={onClose} style={closeButton}>
          <X size={16} aria-hidden />
        </button>
        <div style={logoRow}>
          <span style={brandLogo}><img src={dotharLogoUrl} alt="" style={logoImg} /></span>
          <span style={dotTrail}>•••</span>
          <PluginIcon plugin={plugin} size={56} />
        </div>
        <h2 id="plugin-install-title" style={titleStyle}>{t('plugins.installDialog.title', { name: title })}</h2>
        <div style={subtitleStyle}>{t('plugins.developedBy', { developer: plugin.interface?.developerName || 'DotHarness' })}</div>
        <div style={infoCard}>
          <div style={cardTitleLine}>
            <strong>{title}</strong>
            <span style={badge}>builtin</span>
          </div>
          <div style={muted}>{t('plugins.providedBy', { developer: plugin.interface?.developerName || 'DotHarness' })}</div>
          <div style={muted}>{t('plugins.detail.category')}: {plugin.interface?.category || 'Coding'}</div>
          <Divider />
          <SectionTitle>{t('plugins.detail.about')}</SectionTitle>
          <p style={description}>{plugin.interface?.longDescription || pluginSubtitle(plugin)}</p>
          <Divider />
          <SectionTitle>{t('plugins.detail.contents')}</SectionTitle>
          <div style={chips}>
            {plugin.skills.map((skill) => (
              <span key={`skill:${skill.name}`} style={chip}><Box size={12} aria-hidden />{skill.displayName || skill.name}</span>
            ))}
            {plugin.functions.map((fn) => (
              <span key={`tool:${fn.name}`} style={chip}><Wrench size={12} aria-hidden />{fn.name}</span>
            ))}
            {(plugin.mcpServers ?? []).map((server) => (
              <span key={`mcp:${server.runtimeName}`} style={chip}><Server size={12} aria-hidden />{server.runtimeName}</span>
            ))}
          </div>
          <Divider />
          <SectionTitle>{t('plugins.detail.capabilities')}</SectionTitle>
          <div style={chips}>
            {capabilities.map((capability) => <span key={capability} style={chip}>{capability}</span>)}
          </div>
        </div>
        <button type="button" onClick={onInstall} disabled={installing} style={primaryButton}>
          {installing ? t('plugins.installing') : t('plugins.installBrowserUse')}
        </button>
      </div>
    </div>
  )

  return createPortal(dialog, document.body) as JSX.Element
}

function Divider(): JSX.Element {
  return <div style={divider} />
}

function SectionTitle({ children }: { children: string }): JSX.Element {
  return <div style={sectionTitle}>{children}</div>
}

const backdrop: CSSProperties = {
  position: 'fixed',
  inset: 0,
  zIndex: 10000,
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  backgroundColor: 'var(--overlay-scrim)'
}
const dialogCard: CSSProperties = {
  position: 'relative',
  width: 600,
  maxWidth: 'calc(100vw - 48px)',
  maxHeight: 'calc(100vh - 48px)',
  overflow: 'auto',
  borderRadius: 18,
  backgroundColor: 'var(--bg-secondary)',
  boxShadow: 'var(--shadow-level-3)',
  padding: '32px 24px 24px'
}
const closeButton: CSSProperties = {
  position: 'absolute',
  top: 18,
  right: 18,
  width: 30,
  height: 30,
  border: 'none',
  borderRadius: 8,
  background: 'transparent',
  color: 'var(--text-secondary)',
  cursor: 'pointer'
}
const logoRow: CSSProperties = { display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 18 }
const brandLogo: CSSProperties = { width: 56, height: 56, borderRadius: 12, overflow: 'hidden', display: 'inline-flex' }
const logoImg: CSSProperties = { width: '100%', height: '100%' }
const dotTrail: CSSProperties = { color: 'var(--text-dimmed)', letterSpacing: 2 }
const titleStyle: CSSProperties = { margin: '18px 0 4px', textAlign: 'center', fontSize: 22, fontWeight: 700 }
const subtitleStyle: CSSProperties = { textAlign: 'center', color: 'var(--text-secondary)', fontSize: 13, marginBottom: 24 }
const infoCard: CSSProperties = { border: '1px solid var(--border-default)', borderRadius: 12, padding: 16, marginBottom: 24 }
const cardTitleLine: CSSProperties = { display: 'flex', alignItems: 'center', gap: 8, fontSize: 14 }
const badge: CSSProperties = { padding: '2px 7px', borderRadius: 999, backgroundColor: 'var(--bg-tertiary)', color: 'var(--text-secondary)', fontSize: 11 }
const muted: CSSProperties = { marginTop: 8, color: 'var(--text-secondary)', fontSize: 12 }
const divider: CSSProperties = { height: 1, backgroundColor: 'var(--border-subtle)', margin: '16px 0' }
const sectionTitle: CSSProperties = { fontSize: 13, fontWeight: 700, marginBottom: 8 }
const description: CSSProperties = { margin: 0, color: 'var(--text-secondary)', fontSize: 13, lineHeight: 1.5 }
const chips: CSSProperties = { display: 'flex', flexWrap: 'wrap', gap: 8 }
const chip: CSSProperties = { display: 'inline-flex', alignItems: 'center', gap: 5, height: 26, padding: '0 9px', borderRadius: 8, border: '1px solid var(--border-default)', fontSize: 12 }
const primaryButton: CSSProperties = { width: '100%', height: 38, border: 'none', borderRadius: 999, backgroundColor: 'var(--text-primary)', color: 'var(--bg-primary)', fontSize: 13, fontWeight: 700, cursor: 'pointer' }
