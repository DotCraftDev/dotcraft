import { describe, expect, it } from 'vitest'
import { getConfigReloadAffordance } from '../utils/configReloadAffordance'

describe('getConfigReloadAffordance', () => {
  it('returns live for hot fields', () => {
    expect(
      getConfigReloadAffordance({
        proxyActive: false,
        field: {
          key: 'DisabledSkills',
          sectionPath: ['Skills'],
          reload: 'hot'
        }
      })
    ).toEqual({ kind: 'live' })
  })

  it('returns subsystemRestart when subsystem key is present', () => {
    expect(
      getConfigReloadAffordance({
        proxyActive: false,
        field: {
          key: 'Enabled',
          sectionPath: ['Tools', 'Lsp'],
          reload: 'subsystemRestart',
          subsystemKey: 'lsp'
        }
      })
    ).toEqual({ kind: 'subsystemRestart', subsystemKey: 'lsp' })
  })

  it('falls back to processRestart for unknown reload values', () => {
    expect(
      getConfigReloadAffordance({
        proxyActive: false,
        field: {
          key: 'Model',
          reload: 'futureMode'
        }
      })
    ).toEqual({ kind: 'processRestart' })
  })

  it('locks ApiKey when proxy is active', () => {
    expect(
      getConfigReloadAffordance({
        proxyActive: true,
        field: {
          key: 'ApiKey',
          reload: 'processRestart'
        }
      })
    ).toEqual({ kind: 'lockedByProxy', reason: 'apiKey' })
  })

  it('locks EndPoint when proxy is active', () => {
    expect(
      getConfigReloadAffordance({
        proxyActive: true,
        field: {
          key: 'EndPoint',
          reload: 'processRestart'
        }
      })
    ).toEqual({ kind: 'lockedByProxy', reason: 'endpoint' })
  })

  it('does not lock fields outside AppConfig root', () => {
    expect(
      getConfigReloadAffordance({
        proxyActive: true,
        field: {
          key: 'ApiKey',
          sectionPath: ['Tools', 'Sandbox'],
          reload: 'processRestart'
        }
      })
    ).toEqual({ kind: 'processRestart' })
  })
})
