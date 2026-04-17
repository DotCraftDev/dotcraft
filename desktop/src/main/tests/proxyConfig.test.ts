import { describe, expect, it } from 'vitest'
import { buildProxyOAuthPath } from '../proxyConfig'

describe('buildProxyOAuthPath', () => {
  it('uses Web UI callback forwarding for documented providers', () => {
    expect(buildProxyOAuthPath('codex')).toBe('/codex-auth-url?is_webui=true')
    expect(buildProxyOAuthPath('claude')).toBe('/anthropic-auth-url?is_webui=true')
    expect(buildProxyOAuthPath('gemini')).toBe('/gemini-cli-auth-url?is_webui=true')
  })

  it('keeps legacy paths for providers without documented Web UI forwarding', () => {
    expect(buildProxyOAuthPath('qwen')).toBe('/qwen-auth-url')
    expect(buildProxyOAuthPath('iflow')).toBe('/iflow-auth-url')
  })
})
