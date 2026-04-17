import { promises as fs } from 'fs'
import { dirname, join } from 'path'
import { buildLocalProxyEndpoint } from './proxyConfig'

interface ProxyOverrideSnapshot {
  configExisted: boolean
  hadEndPoint: boolean
  originalEndPoint?: unknown
  hadApiKey: boolean
  originalApiKey?: unknown
  proxyEndPoint: string
  proxyApiKey: string
}

function getWorkspaceConfigPath(workspacePath: string): string {
  return join(workspacePath, '.craft', 'config.json')
}

function getWorkspaceProxySnapshotPath(workspacePath: string): string {
  return join(workspacePath, '.craft', 'proxy-overrides.json')
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return Boolean(value) && typeof value === 'object' && !Array.isArray(value)
}

async function readJsonObject(path: string): Promise<{ exists: boolean; value: Record<string, unknown> }> {
  try {
    const raw = await fs.readFile(path, 'utf8')
    const parsed = JSON.parse(raw) as unknown
    return {
      exists: true,
      value: isRecord(parsed) ? parsed : {}
    }
  } catch (error) {
    const code = (error as NodeJS.ErrnoException | undefined)?.code
    if (code === 'ENOENT') {
      return { exists: false, value: {} }
    }
    throw error
  }
}

async function writeJsonObject(path: string, value: Record<string, unknown>): Promise<void> {
  await fs.mkdir(dirname(path), { recursive: true })
  await fs.writeFile(path, `${JSON.stringify(value, null, 2)}\n`, 'utf8')
}

async function readSnapshot(workspacePath: string): Promise<ProxyOverrideSnapshot | null> {
  const snapshotPath = getWorkspaceProxySnapshotPath(workspacePath)
  const { exists, value } = await readJsonObject(snapshotPath)
  if (!exists) return null
  if (
    typeof value.configExisted !== 'boolean' ||
    typeof value.hadEndPoint !== 'boolean' ||
    typeof value.hadApiKey !== 'boolean' ||
    typeof value.proxyEndPoint !== 'string' ||
    typeof value.proxyApiKey !== 'string'
  ) {
    return null
  }
  return {
    configExisted: value.configExisted,
    hadEndPoint: value.hadEndPoint,
    originalEndPoint: value.originalEndPoint,
    hadApiKey: value.hadApiKey,
    originalApiKey: value.originalApiKey,
    proxyEndPoint: value.proxyEndPoint,
    proxyApiKey: value.proxyApiKey
  }
}

async function removeSnapshot(workspacePath: string): Promise<void> {
  const snapshotPath = getWorkspaceProxySnapshotPath(workspacePath)
  await fs.rm(snapshotPath, { force: true })
}

export async function applyWorkspaceProxyOverrides(workspacePath: string, port: number, apiKey: string): Promise<void> {
  const configPath = getWorkspaceConfigPath(workspacePath)
  const endpoint = buildLocalProxyEndpoint(port)
  const { exists, value } = await readJsonObject(configPath)
  const snapshot =
    (await readSnapshot(workspacePath)) ?? {
      configExisted: exists,
      hadEndPoint: Object.prototype.hasOwnProperty.call(value, 'EndPoint'),
      originalEndPoint: value.EndPoint,
      hadApiKey: Object.prototype.hasOwnProperty.call(value, 'ApiKey'),
      originalApiKey: value.ApiKey,
      proxyEndPoint: endpoint,
      proxyApiKey: apiKey
    }

  value.EndPoint = endpoint
  value.ApiKey = apiKey

  await writeJsonObject(configPath, value)
  await writeJsonObject(getWorkspaceProxySnapshotPath(workspacePath), snapshot as Record<string, unknown>)
}

export async function cleanupWorkspaceProxyOverrides(
  workspacePath: string,
  options?: {
    proxyPort?: number
    proxyApiKey?: string
  }
): Promise<void> {
  const configPath = getWorkspaceConfigPath(workspacePath)
  const { exists, value } = await readJsonObject(configPath)
  const snapshot = await readSnapshot(workspacePath)
  if (!exists) {
    await removeSnapshot(workspacePath)
    return
  }

  let changed = false
  let shouldDeleteConfig = false

  if (snapshot) {
    if (snapshot.hadEndPoint) {
      value.EndPoint = snapshot.originalEndPoint
    } else if (Object.prototype.hasOwnProperty.call(value, 'EndPoint')) {
      delete value.EndPoint
    }

    if (snapshot.hadApiKey) {
      value.ApiKey = snapshot.originalApiKey
    } else if (Object.prototype.hasOwnProperty.call(value, 'ApiKey')) {
      delete value.ApiKey
    }

    changed = true
    shouldDeleteConfig = !snapshot.configExisted && Object.keys(value).length === 0
  } else {
    const expectedEndpoint =
      typeof options?.proxyPort === 'number' ? buildLocalProxyEndpoint(options.proxyPort) : undefined
    const endpointMatches =
      typeof expectedEndpoint === 'string' &&
      typeof value.EndPoint === 'string' &&
      value.EndPoint.trim() === expectedEndpoint
    const apiKeyMatches =
      typeof options?.proxyApiKey === 'string' &&
      typeof value.ApiKey === 'string' &&
      value.ApiKey.trim() === options.proxyApiKey

    if (endpointMatches) {
      delete value.EndPoint
      changed = true
    }
    if (apiKeyMatches) {
      delete value.ApiKey
      changed = true
    }
    shouldDeleteConfig = changed && Object.keys(value).length === 0
  }

  if (changed) {
    if (shouldDeleteConfig) {
      await fs.rm(configPath, { force: true })
    } else {
      await writeJsonObject(configPath, value)
    }
  }

  await removeSnapshot(workspacePath)
}
