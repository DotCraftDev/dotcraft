import { afterEach, describe, expect, it, vi } from 'vitest'
import { mkdirSync, rmSync, writeFileSync } from 'fs'
import { join } from 'path'
import { tmpdir } from 'os'
import { mkdtempSync } from 'fs'
import { getWorkspaceStatus, listSetupModels } from '../workspaceSetup'

const tempDirs: string[] = []

function createTempWorkspace(): string {
  const dir = mkdtempSync(join(tmpdir(), 'dotcraft-workspace-'))
  tempDirs.push(dir)
  return dir
}

afterEach(() => {
  for (const dir of tempDirs.splice(0, tempDirs.length)) {
    rmSync(dir, { recursive: true, force: true })
  }
})

describe('getWorkspaceStatus', () => {
  it('returns no-workspace for empty paths', () => {
    expect(getWorkspaceStatus('', { userConfigPath: join(createTempWorkspace(), '.craft', 'config.json') })).toEqual({
      status: 'no-workspace',
      workspacePath: '',
      hasUserConfig: false
    })
  })

  it('returns needs-setup when .craft/config.json is missing', () => {
    const workspace = createTempWorkspace()
    mkdirSync(join(workspace, '.craft'), { recursive: true })

    expect(getWorkspaceStatus(workspace, { userConfigPath: join(createTempWorkspace(), '.craft', 'config.json') })).toEqual({
      status: 'needs-setup',
      workspacePath: workspace,
      hasUserConfig: false
    })
  })

  it('returns ready when .craft/config.json exists', () => {
    const workspace = createTempWorkspace()
    const configPath = join(workspace, '.craft', 'config.json')
    mkdirSync(join(workspace, '.craft'), { recursive: true })
    writeFileSync(configPath, '{}', 'utf8')

    expect(getWorkspaceStatus(workspace, { userConfigPath: join(createTempWorkspace(), '.craft', 'config.json') })).toEqual({
      status: 'ready',
      workspacePath: workspace,
      hasUserConfig: false
    })
  })

  it('includes safe defaults when user config exists', () => {
    const workspace = createTempWorkspace()
    const userHome = createTempWorkspace()
    const userConfigPath = join(userHome, '.craft', 'config.json')
    mkdirSync(join(userHome, '.craft'), { recursive: true })
    writeFileSync(
      userConfigPath,
      JSON.stringify({
        Language: 'English',
        EndPoint: 'https://example.com/v1',
        Model: 'gpt-4.1',
        ApiKey: 'sk-test'
      }),
      'utf8'
    )

    expect(getWorkspaceStatus(workspace, { userConfigPath })).toEqual({
      status: 'needs-setup',
      workspacePath: workspace,
      hasUserConfig: true,
      userConfigDefaults: {
        language: 'English',
        endpoint: 'https://example.com/v1',
        model: 'gpt-4.1',
        apiKeyPresent: true
      }
    })
  })

  it('re-reads updated user config on later calls', () => {
    const workspace = createTempWorkspace()
    const userHome = createTempWorkspace()
    const userConfigPath = join(userHome, '.craft', 'config.json')
    mkdirSync(join(userHome, '.craft'), { recursive: true })
    writeFileSync(
      userConfigPath,
      JSON.stringify({
        Language: 'English',
        EndPoint: 'https://example.com/v1',
        ApiKey: 'sk-old'
      }),
      'utf8'
    )

    expect(getWorkspaceStatus(workspace, { userConfigPath }).userConfigDefaults?.endpoint).toBe(
      'https://example.com/v1'
    )

    writeFileSync(
      userConfigPath,
      JSON.stringify({
        Language: 'English',
        EndPoint: 'https://litellm.example/v1',
        ApiKey: 'sk-new'
      }),
      'utf8'
    )

    expect(getWorkspaceStatus(workspace, { userConfigPath }).userConfigDefaults?.endpoint).toBe(
      'https://litellm.example/v1'
    )
  })

  it('reads core fields case-insensitively', () => {
    const workspace = createTempWorkspace()
    const userHome = createTempWorkspace()
    const userConfigPath = join(userHome, '.craft', 'config.json')
    mkdirSync(join(userHome, '.craft'), { recursive: true })
    writeFileSync(
      userConfigPath,
      JSON.stringify({
        language: 1,
        endpoint: 'https://lowercase.example/v1',
        model: 'gpt-4.1',
        apiKey: 'sk-test'
      }),
      'utf8'
    )

    expect(getWorkspaceStatus(workspace, { userConfigPath })).toEqual({
      status: 'needs-setup',
      workspacePath: workspace,
      hasUserConfig: true,
      userConfigDefaults: {
        language: 'English',
        endpoint: 'https://lowercase.example/v1',
        model: 'gpt-4.1',
        apiKeyPresent: true
      }
    })
  })

  it('parses user config encoded with UTF-8 BOM', () => {
    const workspace = createTempWorkspace()
    const userHome = createTempWorkspace()
    const userConfigPath = join(userHome, '.craft', 'config.json')
    mkdirSync(join(userHome, '.craft'), { recursive: true })
    writeFileSync(
      userConfigPath,
      '\uFEFF' +
        JSON.stringify({
          Language: 'English',
          EndPoint: 'https://bom.example/v1',
          Model: 'gpt-4.1',
          ApiKey: 'sk-bom'
        }),
      'utf8'
    )

    expect(getWorkspaceStatus(workspace, { userConfigPath })).toEqual({
      status: 'needs-setup',
      workspacePath: workspace,
      hasUserConfig: true,
      userConfigDefaults: {
        language: 'English',
        endpoint: 'https://bom.example/v1',
        model: 'gpt-4.1',
        apiKeyPresent: true
      }
    })
  })
})

describe('listSetupModels', () => {
  it('uses explicit api key when provided', async () => {
    const fetchImpl = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ data: [{ id: 'gpt-4.1' }, { id: 'deepseek-chat' }] })
    })

    const result = await listSetupModels(
      {
        endpoint: 'https://example.com/v1',
        apiKey: 'sk-explicit',
        preferExistingUserConfig: true
      },
      { fetchImpl: fetchImpl as unknown as typeof fetch }
    )

    expect(result).toEqual({ kind: 'success', models: ['deepseek-chat', 'gpt-4.1'] })
    expect(fetchImpl).toHaveBeenCalledWith(
      'https://example.com/v1/models',
      expect.objectContaining({
        method: 'GET',
        headers: expect.objectContaining({
          Authorization: 'Bearer sk-explicit'
        })
      })
    )
  })

  it('uses inherited user api key when explicit key is empty', async () => {
    const userHome = createTempWorkspace()
    const userConfigPath = join(userHome, '.craft', 'config.json')
    mkdirSync(join(userHome, '.craft'), { recursive: true })
    writeFileSync(
      userConfigPath,
      '\uFEFF' +
        JSON.stringify({
          ApiKey: 'sk-inherited'
        }),
      'utf8'
    )
    const fetchImpl = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ data: [{ id: 'gpt-4.1' }] })
    })

    const result = await listSetupModels(
      {
        endpoint: 'https://example.com/v1',
        apiKey: ' ',
        preferExistingUserConfig: true
      },
      { userConfigPath, fetchImpl: fetchImpl as unknown as typeof fetch }
    )

    expect(result).toEqual({ kind: 'success', models: ['gpt-4.1'] })
    expect(fetchImpl).toHaveBeenCalledWith(
      'https://example.com/v1/models',
      expect.objectContaining({
        headers: expect.objectContaining({
          Authorization: 'Bearer sk-inherited'
        })
      })
    )
  })

  it('returns missing-key when no explicit or inherited key is available', async () => {
    const result = await listSetupModels(
      {
        endpoint: 'https://example.com/v1',
        apiKey: '',
        preferExistingUserConfig: true
      },
      {
        userConfigPath: join(createTempWorkspace(), '.craft', 'config.json'),
        fetchImpl: vi.fn() as unknown as typeof fetch
      }
    )

    expect(result).toEqual({ kind: 'missing-key' })
  })
})
