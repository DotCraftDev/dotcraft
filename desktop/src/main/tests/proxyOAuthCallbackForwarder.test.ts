import http from 'http'
import { afterEach, describe, expect, it } from 'vitest'
import {
  ensureMacProxyOAuthCallbackForwarder,
  proxyOAuthCallbackDiagnostics,
  stopMacProxyOAuthCallbackForwarders
} from '../proxyOAuthCallbackForwarder'

async function getFreeIpv6LoopbackPort(): Promise<number | null> {
  return new Promise((resolve) => {
    const server = http.createServer()
    server.once('error', () => resolve(null))
    server.listen({ host: '::1', port: 0, ipv6Only: true }, () => {
      const address = server.address()
      const port = typeof address === 'object' && address ? address.port : null
      server.close(() => resolve(port))
    })
  })
}

async function requestRedirect(port: number, path: string): Promise<string | undefined> {
  return new Promise((resolve, reject) => {
    const req = http.get(
      {
        host: '::1',
        port,
        path,
        family: 6
      },
      (res) => {
        res.resume()
        resolve(res.headers.location)
      }
    )
    req.once('error', reject)
  })
}

describe('proxy OAuth callback forwarder', () => {
  afterEach(() => {
    stopMacProxyOAuthCallbackForwarders()
  })

  it('does nothing outside macOS', async () => {
    await expect(
      ensureMacProxyOAuthCallbackForwarder({
        provider: 'codex',
        proxyPort: 8317,
        authDir: '/tmp/auths',
        platform: 'win32'
      })
    ).resolves.toBeNull()
  })

  it('forwards IPv6 localhost OAuth callbacks back to the CLIProxyAPI server', async () => {
    const callbackPort = await getFreeIpv6LoopbackPort()
    if (callbackPort == null) {
      return
    }

    const details = await ensureMacProxyOAuthCallbackForwarder({
      provider: 'codex',
      proxyPort: 8317,
      authDir: '/tmp/auths',
      oauthUrl: `https://auth.example/authorize?redirect_uri=${encodeURIComponent(`http://localhost:${callbackPort}/auth/callback`)}`,
      platform: 'darwin',
      ttlMs: 30_000
    })

    expect(details).toMatchObject({
      provider: 'codex',
      callbackPort,
      targetUrl: 'http://127.0.0.1:8317/codex/callback',
      authDir: '/tmp/auths',
      active: true
    })
    await expect(
      requestRedirect(callbackPort, '/auth/callback?code=abc&state=s1')
    ).resolves.toBe('http://127.0.0.1:8317/codex/callback?code=abc&state=s1')
  })

  it('formats timeout diagnostics with callback port and auth directory', () => {
    expect(proxyOAuthCallbackDiagnostics('gemini', '/tmp/auths')).toBe(
      'callbackPort: 8085; authDir: /tmp/auths; localhost/IPv6 callback may be unreachable on macOS'
    )
  })
})
