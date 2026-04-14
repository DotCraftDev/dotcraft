import { execFile } from 'child_process'
import { existsSync, readFileSync } from 'fs'
import { homedir } from 'os'
import { join } from 'path'
import { resolveBinaryLocation } from './AppServerManager'
import type { AppSettings } from './settings'

export type WorkspaceSetupState = 'no-workspace' | 'needs-setup' | 'ready'
export type WorkspaceBootstrapProfile = 'default' | 'developer' | 'personal-assistant'
export type WorkspaceLanguage = 'Chinese' | 'English'

export interface WorkspaceUserConfigDefaults {
  language?: WorkspaceLanguage
  endpoint?: string
  model?: string
  apiKeyPresent: boolean
}

export interface WorkspaceStatusPayload {
  status: WorkspaceSetupState
  workspacePath: string
  hasUserConfig: boolean
  userConfigDefaults?: WorkspaceUserConfigDefaults
}

export interface WorkspaceSetupRequest {
  language: WorkspaceLanguage
  model: string
  endpoint: string
  apiKey: string
  profile: WorkspaceBootstrapProfile
  saveToUserConfig: boolean
  preferExistingUserConfig: boolean
}

export interface WorkspaceSetupResult {
  stdout: string
  stderr: string
  exitCode: number
}

export interface WorkspaceSetupModelListRequest {
  endpoint: string
  apiKey: string
  preferExistingUserConfig: boolean
}

export type WorkspaceSetupModelListResult =
  | { kind: 'success'; models: string[] }
  | { kind: 'unsupported' }
  | { kind: 'missing-key' }
  | { kind: 'error' }

function buildBinaryResolutionError(settings: AppSettings): Error {
  const resolved = resolveBinaryLocation({
    binarySource: settings.binarySource,
    binaryPath: settings.appServerBinaryPath
  })

  if (resolved.path) {
    return new Error('DotCraft binary resolved unexpectedly.')
  }

  if (resolved.source === 'custom') {
    const configuredPath = settings.appServerBinaryPath?.trim()
    return new Error(
      configuredPath
        ? `Configured DotCraft binary not found: ${configuredPath}`
        : 'Custom DotCraft binary path is empty. Please choose a binary or switch to another source.'
    )
  }

  if (resolved.source === 'path') {
    return new Error(
      'DotCraft binary not found on PATH. Install dotcraft or switch to the bundled binary in Settings.'
    )
  }

  return new Error(
    'Bundled DotCraft binary not found. Reinstall DotCraft Desktop or switch to another binary source in Settings.'
  )
}

function resolveDesktopBinary(settings: AppSettings): string {
  const resolved = resolveBinaryLocation({
    binarySource: settings.binarySource,
    binaryPath: settings.appServerBinaryPath
  })

  if (resolved.path) {
    return resolved.path
  }

  throw buildBinaryResolutionError(settings)
}

function parseWorkspaceLanguage(value: unknown): WorkspaceLanguage | undefined {
  if (typeof value === 'number') {
    if (value === 0) return 'Chinese'
    if (value === 1) return 'English'
    return undefined
  }
  if (typeof value !== 'string') return undefined
  const trimmed = value.trim()
  if (trimmed === 'Chinese' || trimmed === 'English') {
    return trimmed
  }
  return undefined
}

function getConfigValueCaseInsensitive(
  config: Record<string, unknown>,
  key: string
): unknown {
  const loweredKey = key.toLowerCase()
  const matchedKey = Object.keys(config).find((candidate) => candidate.toLowerCase() === loweredKey)
  return matchedKey ? config[matchedKey] : undefined
}

function readJsonObject(path: string): Record<string, unknown> | null {
  if (!existsSync(path)) {
    return null
  }

  try {
    const rawContent = readFileSync(path, 'utf8')
    return JSON.parse(rawContent.replace(/^\uFEFF/, '')) as Record<string, unknown>
  } catch {
    return null
  }
}

function getUserConfigStatus(
  userConfigPath?: string
): Pick<WorkspaceStatusPayload, 'hasUserConfig' | 'userConfigDefaults'> {
  const globalConfigPath = userConfigPath ?? join(homedir(), '.craft', 'config.json')
  const parsed = readJsonObject(globalConfigPath)
  if (!parsed) {
    return {
      hasUserConfig: false
    }
  }

  const language = getConfigValueCaseInsensitive(parsed, 'Language')
  const endpoint = getConfigValueCaseInsensitive(parsed, 'EndPoint')
  const model = getConfigValueCaseInsensitive(parsed, 'Model')
  const apiKey = getConfigValueCaseInsensitive(parsed, 'ApiKey')
  return {
    hasUserConfig: true,
    userConfigDefaults: {
      language: parseWorkspaceLanguage(language),
      endpoint: typeof endpoint === 'string' && endpoint.trim() ? endpoint.trim() : undefined,
      model: typeof model === 'string' && model.trim() ? model.trim() : undefined,
      apiKeyPresent: typeof apiKey === 'string' && apiKey.trim().length > 0
    }
  }
}

function parseModelIds(payload: unknown): string[] {
  const typed = payload as { data?: Array<{ id?: string; Id?: string }> }
  if (!Array.isArray(typed.data)) return []
  return Array.from(
    new Set(
      typed.data
        .map((item) => String(item.id ?? item.Id ?? '').trim())
        .filter(Boolean)
    )
  ).sort((a, b) => a.localeCompare(b))
}

function resolveEffectiveSetupApiKey(
  request: WorkspaceSetupModelListRequest,
  options?: { userConfigPath?: string }
): string {
  const explicitApiKey = request.apiKey.trim()
  if (explicitApiKey.length > 0) {
    return explicitApiKey
  }

  if (!request.preferExistingUserConfig) {
    return ''
  }

  const globalConfigPath = options?.userConfigPath ?? join(homedir(), '.craft', 'config.json')
  const parsed = readJsonObject(globalConfigPath)
  if (!parsed) {
    return ''
  }
  const inheritedApiKey = getConfigValueCaseInsensitive(parsed, 'ApiKey')
  return typeof inheritedApiKey === 'string' ? inheritedApiKey.trim() : ''
}

export async function listSetupModels(
  request: WorkspaceSetupModelListRequest,
  options?: {
    userConfigPath?: string
    fetchImpl?: typeof fetch
  }
): Promise<WorkspaceSetupModelListResult> {
  const endpoint = request.endpoint.trim()
  const apiKey = resolveEffectiveSetupApiKey(request, options)
  if (!apiKey) {
    return { kind: 'missing-key' }
  }

  let modelsUrl: string
  try {
    const normalizedEndpoint = endpoint.endsWith('/') ? endpoint : `${endpoint}/`
    modelsUrl = new URL('models', normalizedEndpoint).toString()
  } catch {
    return { kind: 'error' }
  }

  const fetchImpl = options?.fetchImpl ?? fetch
  try {
    const response = await fetchImpl(modelsUrl, {
      method: 'GET',
      headers: {
        Accept: 'application/json',
        Authorization: `Bearer ${apiKey}`
      }
    })
    if (!response.ok) {
      if (response.status === 404 || response.status === 405 || response.status === 501) {
        return { kind: 'unsupported' }
      }
      if (response.status === 401 || response.status === 403) {
        return { kind: 'missing-key' }
      }
      return { kind: 'error' }
    }

    const payload = (await response.json()) as unknown
    const models = parseModelIds(payload)
    if (models.length === 0) {
      return { kind: 'error' }
    }
    return { kind: 'success', models }
  } catch {
    return { kind: 'error' }
  }
}

export function getWorkspaceStatus(
  workspacePath: string | null | undefined,
  options?: { userConfigPath?: string }
): WorkspaceStatusPayload {
  const userConfigStatus = getUserConfigStatus(options?.userConfigPath)
  const trimmed = workspacePath?.trim() ?? ''
  if (!trimmed) {
    return {
      status: 'no-workspace',
      workspacePath: '',
      ...userConfigStatus
    }
  }

  const configPath = join(trimmed, '.craft', 'config.json')
  return {
    status: existsSync(configPath) ? 'ready' : 'needs-setup',
    workspacePath: trimmed,
    ...userConfigStatus
  }
}

export function runWorkspaceSetup(
  workspacePath: string,
  request: WorkspaceSetupRequest,
  settings: AppSettings
): Promise<WorkspaceSetupResult> {
  const binaryPath = resolveDesktopBinary(settings)
  const args = [
    'setup',
    '--language',
    request.language,
    '--model',
    request.model,
    '--endpoint',
    request.endpoint,
    '--profile',
    request.profile
  ]

  if (request.apiKey.trim().length > 0) {
    args.push('--api-key', request.apiKey)
  }

  if (request.saveToUserConfig) {
    args.push('--save-user-config')
  }

  if (request.preferExistingUserConfig) {
    args.push('--prefer-existing-user-config')
  }

  return new Promise<WorkspaceSetupResult>((resolve, reject) => {
    execFile(
      binaryPath,
      args,
      {
        cwd: workspacePath,
        windowsHide: true
      },
      (error: Error | null, stdout: string, stderr: string) => {
        if (error) {
          const detail = stderr?.trim() || stdout?.trim() || error.message
          reject(new Error(detail))
          return
        }

        resolve({
          stdout: stdout.trim(),
          stderr: stderr.trim(),
          exitCode: 0
        })
      }
    )
  })
}
