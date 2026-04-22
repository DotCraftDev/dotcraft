import { describe, expect, it } from 'vitest'
import { render } from '@testing-library/react'
import { getProxyProviderIconSrc, ProxyProviderIcon } from '../components/settings/panels/ProxyProviderIcon'

describe('ProxyProviderIcon', () => {
  it('maps codex to the OpenAI brand asset', () => {
    expect(getProxyProviderIconSrc('codex')).toContain('OpenAI')
  })

  it('maps each provider to its expected asset', () => {
    expect(getProxyProviderIconSrc('claude')).toContain('Claude')
    expect(getProxyProviderIconSrc('gemini')).toContain('Gemini')
    expect(getProxyProviderIconSrc('qwen')).toContain('Qwen')
    expect(getProxyProviderIconSrc('iflow')).toContain('iFlyTek')
  })

  it('renders the provider icon as an image', () => {
    const { container } = render(<ProxyProviderIcon provider="codex" />)

    const image = container.querySelector('img')
    expect(image).not.toBeNull()
    expect(image).toHaveAttribute('src', expect.stringContaining('OpenAI'))
  })
})
