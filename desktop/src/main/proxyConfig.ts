import { mkdirSync, writeFileSync } from 'fs'
import { dirname } from 'path'
import { randomBytes } from 'crypto'

export interface ProxyRuntimeConfig {
  host: string
  port: number
  authDir: string
  apiKey: string
  managementKey: string
}

function quoteYaml(value: string): string {
  return JSON.stringify(value)
}

function escapeSingleQuoted(value: string): string {
  return value.replace(/'/g, "''")
}

/**
 * Generates a minimal CLIProxyAPI config.yaml tuned for local Desktop usage.
 */
export function buildProxyConfigYaml(config: ProxyRuntimeConfig): string {
  return [
    'host: "127.0.0.1"',
    `port: ${config.port}`,
    'remote-management:',
    '  allow-remote: false',
    `  secret-key: ${quoteYaml(config.managementKey)}`,
    `auth-dir: ${quoteYaml(config.authDir)}`,
    'api-keys:',
    `  - ${quoteYaml(config.apiKey)}`,
    'usage-statistics-enabled: true',
    'request-log: false',
    `proxy-url: ''`,
    ''
  ].join('\n')
}

export function writeProxyConfig(path: string, config: ProxyRuntimeConfig): void {
  mkdirSync(dirname(path), { recursive: true })
  writeFileSync(path, buildProxyConfigYaml(config), 'utf8')
}

export function createLocalSecret(prefix: string): string {
  return `${prefix}_${randomBytes(24).toString('hex')}`
}

export function buildLocalProxyEndpoint(port: number): string {
  return `http://127.0.0.1:${port}/v1`
}

export function buildLocalProxyManagementBaseUrl(port: number): string {
  return `http://127.0.0.1:${port}/v0/management`
}

export function buildProxyOAuthPath(provider: ProxyOAuthProvider): string {
  switch (provider) {
    case 'codex':
      return '/codex-auth-url'
    case 'claude':
      return '/anthropic-auth-url'
    case 'gemini':
      return '/gemini-cli-auth-url'
    case 'qwen':
      return '/qwen-auth-url'
    case 'iflow':
      return '/iflow-auth-url'
  }
}

export type ProxyOAuthProvider = 'codex' | 'claude' | 'gemini' | 'qwen' | 'iflow'

export function buildManagementHeaders(managementKey: string): Record<string, string> {
  return {
    Authorization: `Bearer ${managementKey}`
  }
}

export function buildAuthFileNameHint(workspacePath: string): string {
  return escapeSingleQuoted(workspacePath.replace(/\\/g, '/'))
}
