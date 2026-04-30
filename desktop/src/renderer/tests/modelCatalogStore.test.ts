// @vitest-environment jsdom

import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useModelCatalogStore } from '../stores/modelCatalogStore'

const appServerListModels = vi.fn()

describe('modelCatalogStore', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    useModelCatalogStore.getState().reset()
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        appServer: { listModels: appServerListModels }
      }
    })
  })

  it('treats model/list failures as retryable errors', async () => {
    appServerListModels
      .mockResolvedValueOnce({
        success: false,
        errorCode: 'EndpointNotSupported',
        errorMessage: 'Endpoint does not support model listing.'
      })
      .mockResolvedValueOnce({
        success: true,
        models: [{ id: 'gpt-5' }]
      })

    await useModelCatalogStore.getState().loadIfNeeded()

    expect(appServerListModels).toHaveBeenCalledTimes(1)
    expect(useModelCatalogStore.getState()).toMatchObject({
      status: 'error',
      modelOptions: [],
      modelListUnsupportedEndpoint: true,
      errorCode: 'EndpointNotSupported',
      errorMessage: 'Endpoint does not support model listing.'
    })

    await useModelCatalogStore.getState().loadIfNeeded()

    expect(appServerListModels).toHaveBeenCalledTimes(2)
    expect(useModelCatalogStore.getState()).toMatchObject({
      status: 'ready',
      modelOptions: ['gpt-5'],
      modelListUnsupportedEndpoint: false,
      errorCode: null,
      errorMessage: null
    })
  })

  it('stores thrown model/list errors', async () => {
    appServerListModels.mockRejectedValueOnce(new Error('proxy unavailable'))

    await useModelCatalogStore.getState().loadIfNeeded()

    expect(useModelCatalogStore.getState()).toMatchObject({
      status: 'error',
      modelOptions: [],
      modelListUnsupportedEndpoint: false,
      errorMessage: 'proxy unavailable'
    })
  })
})
