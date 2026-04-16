import { describe, expect, it } from 'vitest'
import { mergeUpdatedSettings } from '../settingsMerge'
import type { AppSettings } from '../settings'

describe('mergeUpdatedSettings', () => {
  it('preserves server-side proxy secrets when renderer updates proxy settings', () => {
    const current: AppSettings = {
      proxy: {
        enabled: true,
        host: '127.0.0.1',
        port: 8317,
        apiKey: 'sk-proxy',
        managementKey: 'mgmt-key'
      }
    }

    const next = mergeUpdatedSettings(current, {
      proxy: {
        enabled: false,
        port: 9000
      }
    })

    expect(next.proxy).toEqual({
      enabled: false,
      host: '127.0.0.1',
      port: 9000,
      apiKey: 'sk-proxy',
      managementKey: 'mgmt-key'
    })
  })

  it('merges other nested transport settings without dropping unspecified fields', () => {
    const current: AppSettings = {
      webSocket: {
        host: '127.0.0.1',
        port: 9100
      },
      remote: {
        url: 'wss://example.test/ws',
        token: 'abc'
      }
    }

    const next = mergeUpdatedSettings(current, {
      webSocket: {
        port: 9200
      },
      remote: {
        url: 'wss://other.test/ws'
      }
    })

    expect(next.webSocket).toEqual({
      host: '127.0.0.1',
      port: 9200
    })
    expect(next.remote).toEqual({
      url: 'wss://other.test/ws',
      token: 'abc'
    })
  })
})
