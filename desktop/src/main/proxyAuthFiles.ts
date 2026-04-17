import type { ProxyOAuthProvider } from './settings'

export interface RawProxyAuthFileSummary {
  provider?: string
  status?: string
  status_message?: string
  disabled?: boolean
  unavailable?: boolean
  runtime_only?: boolean
  modtime?: string
  email?: string
  name?: string
  type?: string
}

export interface ProxyAuthFileSummary {
  provider: ProxyOAuthProvider
  status: string
  statusMessage: string
  disabled: boolean
  unavailable: boolean
  runtimeOnly: boolean
  modtime?: string
  email?: string
  name: string
}

const PROXY_OAUTH_PROVIDERS: readonly ProxyOAuthProvider[] = ['codex', 'claude', 'gemini', 'qwen', 'iflow']
const AUTHENTICATED_PROXY_AUTH_STATUSES = new Set(['ready', 'active'])

export function isProxyOAuthProvider(value: string): value is ProxyOAuthProvider {
  return (PROXY_OAUTH_PROVIDERS as readonly string[]).includes(value)
}

function inferProxyProvider(file: RawProxyAuthFileSummary, name: string): ProxyOAuthProvider | null {
  const explicitProvider = typeof file.provider === 'string' ? file.provider.trim().toLowerCase() : ''
  if (isProxyOAuthProvider(explicitProvider)) {
    return explicitProvider
  }

  const type = typeof file.type === 'string' ? file.type.trim().toLowerCase() : ''
  if (isProxyOAuthProvider(type)) {
    return type
  }

  const lowerName = name.toLowerCase()
  const matched = PROXY_OAUTH_PROVIDERS.find((provider) => lowerName === `${provider}.json` || lowerName.startsWith(`${provider}-`))
  return matched ?? null
}

export function normalizeProxyAuthFiles(payload: { files?: RawProxyAuthFileSummary[] } | undefined): ProxyAuthFileSummary[] {
  const files = Array.isArray(payload?.files) ? payload.files : []
  return files.flatMap((file) => {
    const name = typeof file.name === 'string' ? file.name.trim() : ''
    const provider = inferProxyProvider(file, name)
    if (!provider || !name) {
      return []
    }
    const hasRuntimeStatus = typeof file.status === 'string' && file.status.trim().length > 0
    return [{
      provider,
      status: hasRuntimeStatus ? (file.status as string) : 'ready',
      statusMessage:
        typeof file.status_message === 'string' && file.status_message.trim().length > 0
          ? file.status_message
          : hasRuntimeStatus
            ? ''
            : 'fallback auth-dir scan',
      disabled: file.disabled === true,
      unavailable: file.unavailable === true,
      runtimeOnly: file.runtime_only === true,
      modtime: typeof file.modtime === 'string' ? file.modtime : undefined,
      email: typeof file.email === 'string' ? file.email : undefined,
      name
    }]
  })
}

export function isAuthenticatedProxyAuthFile(
  file: Pick<ProxyAuthFileSummary, 'provider' | 'status' | 'disabled' | 'unavailable'>,
  provider?: ProxyOAuthProvider
): boolean {
  return AUTHENTICATED_PROXY_AUTH_STATUSES.has(file.status) &&
    !file.disabled &&
    !file.unavailable &&
    (provider === undefined || file.provider === provider)
}
