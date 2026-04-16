import { describe, expect, it } from 'vitest'
import type { AppSettings } from '../settings'
import {
  materializeProxyRuntimeSettings,
  resolveExistingProxyRuntimeSettings,
  resolveProxySettings
} from '../proxyRuntime'

const testDeps = {
  createSecret: (prefix: string) => `${prefix}_fixed`,
  getDefaultAuthDir: () => '/tmp/proxy-auth',
  getConfigPath: () => '/tmp/proxy-config.yaml'
}

describe('resolveProxySettings', () => {
  it('normalizes proxy settings without generating secrets', () => {
    expect(
      resolveProxySettings({
        proxy: {
          enabled: true,
          host: ' 127.0.0.1 ',
          port: 8317,
          binaryPath: ' C:/proxy.exe '
        }
      })
    ).toEqual({
      enabled: true,
      host: '127.0.0.1',
      port: 8317,
      binarySource: 'custom',
      binaryPath: 'C:/proxy.exe',
      authDir: undefined,
      apiKey: undefined,
      managementKey: undefined
    })
  })
})

describe('resolveExistingProxyRuntimeSettings', () => {
  it('returns runtime values from settings without mutating them', () => {
    const settings: AppSettings = {
      proxy: {
        enabled: true,
        host: '127.0.0.1',
        port: 8317,
        authDir: '/persisted/auth',
        apiKey: 'sk-proxy',
        managementKey: 'mgmt-proxy'
      }
    }
    const before = JSON.parse(JSON.stringify(settings)) as AppSettings

    const runtime = resolveExistingProxyRuntimeSettings(settings, testDeps)

    expect(runtime).toEqual({
      host: '127.0.0.1',
      port: 8317,
      binarySource: 'bundled',
      binaryPath: undefined,
      authDir: '/persisted/auth',
      apiKey: 'sk-proxy',
      managementKey: 'mgmt-proxy',
      configPath: '/tmp/proxy-config.yaml'
    })
    expect(settings).toEqual(before)
  })

  it('fails when management credentials are missing', () => {
    expect(() =>
      resolveExistingProxyRuntimeSettings(
        {
          proxy: {
            enabled: true,
            port: 8317
          }
        },
        testDeps
      )
    ).toThrow('API proxy credentials are missing. Restart or re-enable the API proxy to regenerate them.')
  })
})

describe('materializeProxyRuntimeSettings', () => {
  it('generates missing secrets and persists them into settings', () => {
    const settings: AppSettings = {
      proxy: {
        enabled: true,
        port: 8317
      }
    }

    const runtime = materializeProxyRuntimeSettings(settings, testDeps)

    expect(runtime).toEqual({
      host: '127.0.0.1',
      port: 8317,
      binarySource: 'bundled',
      binaryPath: undefined,
      authDir: '/tmp/proxy-auth',
      apiKey: 'dotcraft_proxy_api_fixed',
      managementKey: 'dotcraft_proxy_mgmt_fixed',
      configPath: '/tmp/proxy-config.yaml'
    })
    expect(settings.proxy).toEqual({
      enabled: true,
      port: 8317,
      host: '127.0.0.1',
      binarySource: 'bundled',
      binaryPath: undefined,
      authDir: '/tmp/proxy-auth',
      apiKey: 'dotcraft_proxy_api_fixed',
      managementKey: 'dotcraft_proxy_mgmt_fixed'
    })
  })

  it('preserves existing secrets instead of regenerating them', () => {
    const settings: AppSettings = {
      proxy: {
        enabled: true,
        host: '127.0.0.1',
        port: 8317,
        authDir: '/persisted/auth',
        apiKey: 'sk-proxy',
        managementKey: 'mgmt-proxy'
      }
    }

    const runtime = materializeProxyRuntimeSettings(settings, testDeps)

    expect(runtime.apiKey).toBe('sk-proxy')
    expect(runtime.managementKey).toBe('mgmt-proxy')
    expect(settings.proxy?.apiKey).toBe('sk-proxy')
    expect(settings.proxy?.managementKey).toBe('mgmt-proxy')
  })
})
