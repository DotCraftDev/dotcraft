import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ChannelsView } from '../components/channels/ChannelsView'
import { useConnectionStore } from '../stores/connectionStore'
import type { DiscoveredModule } from '../../preload/api'

const settingsGet = vi.fn()
const modulesList = vi.fn()
const modulesReadConfig = vi.fn()
const modulesRunning = vi.fn()
const modulesQrStatus = vi.fn()
const appServerSendRequest = vi.fn()

function createWeComModule(): DiscoveredModule {
  return {
    moduleId: 'wecom-standard',
    channelName: 'wecom',
    displayName: 'WeCom',
    localizedDisplayName: {
      en: 'WeCom',
      'zh-Hans': '企业微信'
    },
    packageName: '@dotcraft/channel-wecom',
    configFileName: 'wecom.json',
    supportedTransports: ['websocket'],
    requiresInteractiveSetup: false,
    variant: 'standard',
    source: 'bundled',
    absolutePath: 'F:\\dotcraft\\sdk\\typescript\\packages\\channel-wecom',
    configDescriptors: []
  }
}

describe('ChannelsView module channel display', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    useConnectionStore.getState().reset()
    settingsGet.mockResolvedValue({
      locale: 'zh-Hans',
      connectionMode: 'websocket',
      activeModuleVariants: {}
    })
    modulesList.mockResolvedValue([createWeComModule()])
    modulesReadConfig.mockResolvedValue({ config: {} })
    modulesRunning.mockResolvedValue({})
    modulesQrStatus.mockResolvedValue({ active: false, qrDataUrl: null })
    appServerSendRequest.mockResolvedValue({ channels: [] })

    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: {
          get: settingsGet
        },
        appServer: {
          sendRequest: appServerSendRequest
        },
        modules: {
          list: modulesList,
          rescan: vi.fn().mockResolvedValue([createWeComModule()]),
          readConfig: modulesReadConfig,
          writeConfig: vi.fn().mockResolvedValue(undefined),
          running: modulesRunning,
          start: vi.fn().mockResolvedValue({ ok: true }),
          stop: vi.fn().mockResolvedValue({ ok: true }),
          setActiveVariant: vi.fn().mockResolvedValue({ ok: true }),
          getLogs: vi.fn().mockResolvedValue({ lines: [] }),
          qrStatus: modulesQrStatus,
          openFolder: vi.fn().mockResolvedValue({ ok: true }),
          pickDirectory: vi.fn().mockResolvedValue(null),
          onRescanSummary: vi.fn(() => vi.fn()),
          onStatusChanged: vi.fn(() => vi.fn()),
          onQrUpdate: vi.fn(() => vi.fn())
        }
      }
    })
  })

  it('shows WeCom as a localized module and does not render the old native group', async () => {
    render(
      <LocaleProvider>
        <ChannelsView />
      </LocaleProvider>
    )

    await waitFor(() => {
      expect(screen.getAllByText('企业微信').length).toBeGreaterThan(0)
    })

    expect(screen.queryByText('Native')).not.toBeInTheDocument()
    expect(screen.queryByText('启用此渠道')).not.toBeInTheDocument()
  })
})
