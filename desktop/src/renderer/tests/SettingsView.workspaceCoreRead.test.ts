import { describe, expect, it, vi } from 'vitest'
import {
  readWorkspaceCoreSafeFromApi,
  readWorkspaceCoreStrictFromApi
} from '../components/settings/SettingsView'

describe('SettingsView workspace core readers', () => {
  it('returns empty workspace core from safe reader when api is unavailable', async () => {
    await expect(readWorkspaceCoreSafeFromApi(undefined)).resolves.toEqual({
      workspace: { apiKey: null, endPoint: null, welcomeSuggestionsEnabled: null, skillsSelfLearningEnabled: null },
      userDefaults: { apiKey: null, endPoint: null, welcomeSuggestionsEnabled: null, skillsSelfLearningEnabled: null }
    })
  })

  it('throws from strict reader when api is unavailable', async () => {
    await expect(readWorkspaceCoreStrictFromApi(undefined)).rejects.toThrow(
      'Workspace core API is unavailable'
    )
  })

  it('throws from strict reader when getCore fails', async () => {
    const getCore = vi.fn<() => Promise<unknown>>().mockRejectedValue(new Error('boom'))

    await expect(
      readWorkspaceCoreStrictFromApi({
        workspaceConfig: { getCore }
      })
    ).rejects.toThrow('boom')
  })

  it('normalizes self-learning config from workspace and user defaults', async () => {
    const getCore = vi.fn<() => Promise<unknown>>().mockResolvedValue({
      workspace: { skillsSelfLearningEnabled: true },
      userDefaults: { skillsSelfLearningEnabled: false }
    })

    await expect(
      readWorkspaceCoreStrictFromApi({
        workspaceConfig: { getCore }
      })
    ).resolves.toEqual({
      workspace: { apiKey: null, endPoint: null, welcomeSuggestionsEnabled: null, skillsSelfLearningEnabled: true },
      userDefaults: { apiKey: null, endPoint: null, welcomeSuggestionsEnabled: null, skillsSelfLearningEnabled: false }
    })
  })
})
