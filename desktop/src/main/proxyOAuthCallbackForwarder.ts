import http from 'http'
import { URL } from 'url'
import type { ProxyOAuthProvider } from './settings'

interface CallbackForwarderConfig {
  callbackPort: number
  targetPath: string
}

export interface ProxyOAuthCallbackForwarderDetails {
  provider: ProxyOAuthProvider
  callbackPort: number
  targetUrl: string
  authDir: string
  active: boolean
}

export interface ProxyOAuthCallbackForwarderOptions {
  provider: ProxyOAuthProvider
  proxyPort: number
  authDir: string
  oauthUrl?: string
  platform?: NodeJS.Platform
  ttlMs?: number
}

interface ActiveCallbackForwarder {
  server: http.Server
  details: ProxyOAuthCallbackForwarderDetails
  timer: ReturnType<typeof setTimeout>
}

const CALLBACK_FORWARDERS: Partial<Record<ProxyOAuthProvider, CallbackForwarderConfig>> = {
  codex: {
    callbackPort: 1455,
    targetPath: '/codex/callback'
  },
  claude: {
    callbackPort: 54545,
    targetPath: '/anthropic/callback'
  },
  gemini: {
    callbackPort: 8085,
    targetPath: '/google/callback'
  }
}

const activeForwarders = new Map<ProxyOAuthProvider, ActiveCallbackForwarder>()
const DEFAULT_FORWARDER_TTL_MS = 10 * 60 * 1000

function callbackPortFromOAuthUrl(oauthUrl: string | undefined, fallbackPort: number): number {
  if (!oauthUrl?.trim()) {
    return fallbackPort
  }
  try {
    const parsed = new URL(oauthUrl)
    const redirectUri = parsed.searchParams.get('redirect_uri')
    if (!redirectUri) {
      return fallbackPort
    }
    const redirect = new URL(redirectUri)
    const parsedPort = Number.parseInt(redirect.port, 10)
    return Number.isInteger(parsedPort) && parsedPort > 0 && parsedPort <= 65535
      ? parsedPort
      : fallbackPort
  } catch {
    return fallbackPort
  }
}

function buildTargetUrl(proxyPort: number, targetPath: string): string {
  return `http://127.0.0.1:${proxyPort}${targetPath}`
}

function stopForwarder(provider: ProxyOAuthProvider): void {
  const existing = activeForwarders.get(provider)
  if (!existing) {
    return
  }
  activeForwarders.delete(provider)
  clearTimeout(existing.timer)
  existing.server.close()
}

function refreshForwarder(provider: ProxyOAuthProvider, ttlMs: number): ProxyOAuthCallbackForwarderDetails | null {
  const existing = activeForwarders.get(provider)
  if (!existing) {
    return null
  }
  clearTimeout(existing.timer)
  existing.timer = setTimeout(() => stopForwarder(provider), ttlMs)
  return existing.details
}

export async function ensureMacProxyOAuthCallbackForwarder(
  options: ProxyOAuthCallbackForwarderOptions
): Promise<ProxyOAuthCallbackForwarderDetails | null> {
  const platform = options.platform ?? process.platform
  if (platform !== 'darwin') {
    return null
  }

  const config = CALLBACK_FORWARDERS[options.provider]
  if (!config) {
    return null
  }

  const ttlMs = options.ttlMs ?? DEFAULT_FORWARDER_TTL_MS
  const callbackPort = callbackPortFromOAuthUrl(options.oauthUrl, config.callbackPort)
  const targetUrl = buildTargetUrl(options.proxyPort, config.targetPath)
  const existing = activeForwarders.get(options.provider)
  if (existing && existing.details.callbackPort === callbackPort && existing.details.targetUrl === targetUrl) {
    return refreshForwarder(options.provider, ttlMs)
  }
  stopForwarder(options.provider)

  const details: ProxyOAuthCallbackForwarderDetails = {
    provider: options.provider,
    callbackPort,
    targetUrl,
    authDir: options.authDir,
    active: true
  }

  const server = http.createServer((req, res) => {
    const rawQuery = req.url?.split('?')[1] ?? ''
    const location = rawQuery ? `${targetUrl}?${rawQuery}` : targetUrl
    res.writeHead(302, {
      Location: location,
      'Cache-Control': 'no-store'
    })
    res.end()
  })

  const timer = setTimeout(() => stopForwarder(options.provider), ttlMs)

  return new Promise((resolve, reject) => {
    let settled = false
    server.once('error', (error: NodeJS.ErrnoException) => {
      clearTimeout(timer)
      if (settled) {
        return
      }
      settled = true
      if (error.code === 'EADDRINUSE') {
        resolve({ ...details, active: false })
        return
      }
      reject(error)
    })
    server.listen(
      {
        host: '::1',
        port: callbackPort,
        ipv6Only: true
      },
      () => {
        settled = true
        activeForwarders.set(options.provider, {
          server,
          details,
          timer
        })
        resolve(details)
      }
    )
  })
}

export function stopMacProxyOAuthCallbackForwarders(): void {
  for (const provider of [...activeForwarders.keys()]) {
    stopForwarder(provider)
  }
}

export function proxyOAuthCallbackDiagnostics(provider: ProxyOAuthProvider, authDir: string): string {
  const config = CALLBACK_FORWARDERS[provider]
  if (!config) {
    return `authDir: ${authDir}`
  }
  return `callbackPort: ${config.callbackPort}; authDir: ${authDir}; localhost/IPv6 callback may be unreachable on macOS`
}
