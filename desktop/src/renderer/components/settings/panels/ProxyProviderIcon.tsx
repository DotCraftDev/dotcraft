import type { CSSProperties, JSX } from 'react'
import { SquareTerminal } from 'lucide-react'
import type { ProxyOAuthProvider } from '../../../../preload/api'
import claudeIcon from '../../../assets/agents/claude.svg'
import geminiIcon from '../../../assets/agents/gemini.svg'
import iflytekIcon from '../../../assets/agents/iflytek.svg'
import openaiIcon from '../../../assets/agents/openai.svg'
import qwenIcon from '../../../assets/agents/qwen.svg'

interface ProxyProviderIconProps {
  provider: ProxyOAuthProvider
  size?: number
  framed?: boolean
}

const PROVIDER_ICON_MAP: Record<ProxyOAuthProvider, string> = {
  codex: openaiIcon,
  claude: claudeIcon,
  gemini: geminiIcon,
  qwen: qwenIcon,
  iflow: iflytekIcon
}

export function getProxyProviderIconSrc(provider: ProxyOAuthProvider): string | null {
  return PROVIDER_ICON_MAP[provider] ?? null
}

export function ProxyProviderIcon({
  provider,
  size = 28,
  framed = true
}: ProxyProviderIconProps): JSX.Element {
  const art = renderArt(provider, size)
  if (!framed) return <span style={inlineWrapperStyle(size)}>{art}</span>
  return <span style={frameStyle(size)}>{art}</span>
}

function renderArt(provider: ProxyOAuthProvider, size: number): JSX.Element {
  const iconSrc = getProxyProviderIconSrc(provider)
  if (iconSrc) {
    return <img src={iconSrc} alt="" width={size} height={size} style={IMG_STYLE} />
  }
  return <SquareTerminal size={Math.round(size * 0.7)} strokeWidth={1.8} aria-hidden="true" />
}

const IMG_STYLE: CSSProperties = {
  width: '100%',
  height: '100%',
  objectFit: 'contain',
  display: 'block'
}

function frameStyle(size: number): CSSProperties {
  return {
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    width: size + 12,
    height: size + 12,
    borderRadius: '10px',
    background: 'var(--bg-tertiary)',
    border: '1px solid var(--border-default)',
    color: 'var(--text-primary)',
    flexShrink: 0
  }
}

function inlineWrapperStyle(size: number): CSSProperties {
  return {
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    width: size,
    height: size,
    color: 'var(--text-primary)',
    flexShrink: 0
  }
}
