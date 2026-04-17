import { afterEach, describe, expect, it } from 'vitest'
import { existsSync, mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'fs'
import { join } from 'path'
import { tmpdir } from 'os'
import { applyWorkspaceProxyOverrides, cleanupWorkspaceProxyOverrides } from '../proxyWorkspaceConfig'

const tempDirs: string[] = []

function createTempWorkspace(): string {
  const dir = mkdtempSync(join(tmpdir(), 'dotcraft-proxy-config-'))
  tempDirs.push(dir)
  return dir
}

function readWorkspaceConfig(workspacePath: string): Record<string, unknown> {
  return JSON.parse(readFileSync(join(workspacePath, '.craft', 'config.json'), 'utf8')) as Record<string, unknown>
}

afterEach(() => {
  for (const dir of tempDirs.splice(0, tempDirs.length)) {
    rmSync(dir, { recursive: true, force: true })
  }
})

describe('proxyWorkspaceConfig', () => {
  it('restores original endpoint and api key after cleanup', async () => {
    const workspace = createTempWorkspace()
    const configPath = join(workspace, '.craft', 'config.json')
    mkdirSync(join(workspace, '.craft'), { recursive: true })
    writeFileSync(
      configPath,
      JSON.stringify({
        EndPoint: 'https://example.com/v1',
        ApiKey: 'sk-original',
        Model: 'gpt-4.1'
      }),
      'utf8'
    )

    await applyWorkspaceProxyOverrides(workspace, 8317, 'sk-proxy')

    expect(readWorkspaceConfig(workspace)).toEqual({
      EndPoint: 'http://127.0.0.1:8317/v1',
      ApiKey: 'sk-proxy',
      Model: 'gpt-4.1'
    })

    await cleanupWorkspaceProxyOverrides(workspace, {
      proxyPort: 8317,
      proxyApiKey: 'sk-proxy'
    })

    expect(readWorkspaceConfig(workspace)).toEqual({
      EndPoint: 'https://example.com/v1',
      ApiKey: 'sk-original',
      Model: 'gpt-4.1'
    })
  })

  it('removes proxy-created config when no original config existed', async () => {
    const workspace = createTempWorkspace()

    await applyWorkspaceProxyOverrides(workspace, 8317, 'sk-proxy')
    await cleanupWorkspaceProxyOverrides(workspace, {
      proxyPort: 8317,
      proxyApiKey: 'sk-proxy'
    })

    expect(existsSync(join(workspace, '.craft', 'config.json'))).toBe(true)
    expect(readWorkspaceConfig(workspace)).toEqual({})
  })

  it('cleans up stale proxy overrides from older builds without a snapshot', async () => {
    const workspace = createTempWorkspace()
    const configPath = join(workspace, '.craft', 'config.json')
    mkdirSync(join(workspace, '.craft'), { recursive: true })
    writeFileSync(
      configPath,
      JSON.stringify({
        EndPoint: 'http://127.0.0.1:8317/v1',
        ApiKey: 'sk-proxy',
        Model: 'gpt-4.1'
      }),
      'utf8'
    )

    await cleanupWorkspaceProxyOverrides(workspace, {
      proxyPort: 8317,
      proxyApiKey: 'sk-proxy'
    })

    expect(readWorkspaceConfig(workspace)).toEqual({
      Model: 'gpt-4.1'
    })
  })

  it('writes a valid empty JSON object instead of an empty file', async () => {
    const workspace = createTempWorkspace()
    const configPath = join(workspace, '.craft', 'config.json')

    await applyWorkspaceProxyOverrides(workspace, 8317, 'sk-proxy')
    await cleanupWorkspaceProxyOverrides(workspace, {
      proxyPort: 8317,
      proxyApiKey: 'sk-proxy'
    })

    const raw = readFileSync(configPath, 'utf8')
    expect(raw).toContain('{')
    expect(raw).toContain('}')
    expect(() => JSON.parse(raw)).not.toThrow()
    expect(JSON.parse(raw)).toEqual({})
  })

  it('applies proxy overrides when config is encoded with UTF-8 BOM', async () => {
    const workspace = createTempWorkspace()
    const configPath = join(workspace, '.craft', 'config.json')
    mkdirSync(join(workspace, '.craft'), { recursive: true })
    writeFileSync(
      configPath,
      '\uFEFF' +
        JSON.stringify({
          EndPoint: 'https://example.com/v1',
          ApiKey: 'sk-original'
        }),
      'utf8'
    )

    await applyWorkspaceProxyOverrides(workspace, 8317, 'sk-proxy')

    expect(readWorkspaceConfig(workspace)).toEqual({
      EndPoint: 'http://127.0.0.1:8317/v1',
      ApiKey: 'sk-proxy'
    })
  })
})
