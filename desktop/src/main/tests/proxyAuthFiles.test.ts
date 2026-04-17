import { describe, expect, it } from 'vitest'
import { isAuthenticatedProxyAuthFile, normalizeProxyAuthFiles } from '../proxyAuthFiles'

describe('normalizeProxyAuthFiles', () => {
  it('keeps known OAuth providers and normalizes status metadata', () => {
    expect(
      normalizeProxyAuthFiles({
        files: [
          {
            provider: 'CODEX',
            status: 'ready',
            status_message: 'ok',
            disabled: false,
            unavailable: false,
            runtime_only: true,
            modtime: '2026-04-17T03:00:00Z',
            email: '[email protected]',
            name: 'codex-user.json'
          },
          {
            provider: 'unknown',
            status: 'ready',
            name: 'ignored.json'
          }
        ]
      })
    ).toEqual([
      {
        provider: 'codex',
        status: 'ready',
        statusMessage: 'ok',
        disabled: false,
        unavailable: false,
        runtimeOnly: true,
        modtime: '2026-04-17T03:00:00Z',
        email: '[email protected]',
        name: 'codex-user.json'
      }
    ])
  })

  it('accepts fallback auth-dir scan entries by inferring provider from file name', () => {
    expect(
      normalizeProxyAuthFiles({
        files: [
          {
            name: 'codex-user.json',
            type: 'json',
            modtime: '2026-04-17T03:00:00Z',
            email: '[email protected]'
          }
        ]
      })
    ).toEqual([
      {
        provider: 'codex',
        status: 'ready',
        statusMessage: 'fallback auth-dir scan',
        disabled: false,
        unavailable: false,
        runtimeOnly: false,
        modtime: '2026-04-17T03:00:00Z',
        email: '[email protected]',
        name: 'codex-user.json'
      }
    ])
  })
})

describe('isAuthenticatedProxyAuthFile', () => {
  it('treats ready and active non-disabled files as authenticated', () => {
    expect(
      isAuthenticatedProxyAuthFile({
        provider: 'codex',
        status: 'ready',
        disabled: false,
        unavailable: false
      })
    ).toBe(true)
    expect(
      isAuthenticatedProxyAuthFile({
        provider: 'codex',
        status: 'active',
        disabled: false,
        unavailable: false
      })
    ).toBe(true)
  })

  it('rejects disabled and unavailable files', () => {
    expect(
      isAuthenticatedProxyAuthFile({
        provider: 'codex',
        status: 'active',
        disabled: true,
        unavailable: false
      })
    ).toBe(false)
    expect(
      isAuthenticatedProxyAuthFile({
        provider: 'codex',
        status: 'active',
        disabled: false,
        unavailable: true
      })
    ).toBe(false)
  })
})
