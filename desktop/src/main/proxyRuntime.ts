import { app } from 'electron'
import { join } from 'path'
import type { AppSettings, ProxySettings } from './settings'
import type { ProxyBinarySource } from './ProxyProcessManager'
import { createLocalSecret } from './proxyConfig'

const DEFAULT_PROXY_HOST = '127.0.0.1'
const DEFAULT_PROXY_PORT = 8317

export interface ProxyRuntimeSettings {
  host: string
  port: number
  binarySource: ProxyBinarySource
  binaryPath?: string
  authDir: string
  apiKey: string
  managementKey: string
  configPath: string
}

interface ProxyRuntimeDeps {
  createSecret?: (prefix: string) => string
  getDefaultAuthDir?: () => string
  getConfigPath?: () => string
}

function getProxyConfigPath(): string {
  return join(app.getPath('userData'), 'proxy', 'config.yaml')
}

function getDefaultProxyAuthDir(): string {
  return join(app.getPath('userData'), 'proxy', 'auths')
}

export function resolveProxySettings(settings: AppSettings): Required<Pick<ProxySettings, 'enabled' | 'host' | 'port' | 'binarySource'>> &
  Pick<ProxySettings, 'binaryPath' | 'authDir' | 'apiKey' | 'managementKey'> {
  const raw = settings.proxy ?? {}
  const host = raw.host?.trim() || DEFAULT_PROXY_HOST
  const candidatePort = raw.port
  const port =
    typeof candidatePort === 'number' && Number.isInteger(candidatePort) && candidatePort > 0 && candidatePort <= 65535
      ? candidatePort
      : DEFAULT_PROXY_PORT
  const enabled = raw.enabled === true
  const binarySource: ProxyBinarySource =
    raw.binarySource === 'bundled' || raw.binarySource === 'path' || raw.binarySource === 'custom'
      ? raw.binarySource
      : raw.binaryPath?.trim()
        ? 'custom'
        : 'bundled'
  return {
    enabled,
    host,
    port,
    binarySource,
    binaryPath: raw.binaryPath?.trim() || undefined,
    authDir: raw.authDir?.trim() || undefined,
    apiKey: raw.apiKey?.trim() || undefined,
    managementKey: raw.managementKey?.trim() || undefined
  }
}

function buildRuntimeSettings(
  proxy: ReturnType<typeof resolveProxySettings>,
  authDir: string,
  apiKey: string,
  managementKey: string,
  deps?: ProxyRuntimeDeps
): ProxyRuntimeSettings {
  return {
    host: proxy.host,
    port: proxy.port,
    binarySource: proxy.binarySource,
    binaryPath: proxy.binaryPath,
    authDir,
    apiKey,
    managementKey,
    configPath: deps?.getConfigPath?.() ?? getProxyConfigPath()
  }
}

export function resolveExistingProxyRuntimeSettings(settings: AppSettings, deps?: ProxyRuntimeDeps): ProxyRuntimeSettings {
  const proxy = resolveProxySettings(settings)
  if (!proxy.apiKey || !proxy.managementKey) {
    throw new Error('API proxy credentials are missing. Restart or re-enable the API proxy to regenerate them.')
  }
  const authDir = proxy.authDir || deps?.getDefaultAuthDir?.() || getDefaultProxyAuthDir()
  return buildRuntimeSettings(proxy, authDir, proxy.apiKey, proxy.managementKey, deps)
}

export function materializeProxyRuntimeSettings(settings: AppSettings, deps?: ProxyRuntimeDeps): ProxyRuntimeSettings {
  const proxy = resolveProxySettings(settings)
  const createSecret = deps?.createSecret ?? createLocalSecret
  const apiKey = proxy.apiKey || createSecret('dotcraft_proxy_api')
  const managementKey = proxy.managementKey || createSecret('dotcraft_proxy_mgmt')
  const authDir = proxy.authDir || deps?.getDefaultAuthDir?.() || getDefaultProxyAuthDir()
  if (!settings.proxy) settings.proxy = {}
  settings.proxy.apiKey = apiKey
  settings.proxy.managementKey = managementKey
  settings.proxy.authDir = authDir
  settings.proxy.port = proxy.port
  settings.proxy.host = proxy.host
  settings.proxy.binarySource = proxy.binarySource
  settings.proxy.binaryPath = proxy.binaryPath
  return buildRuntimeSettings(proxy, authDir, apiKey, managementKey, deps)
}
