import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ModuleConfigForm } from '../components/channels/ModuleConfigForm'
import type { DiscoveredModule } from '../../preload/api'

const settingsGet = vi.fn()

function createModule(): DiscoveredModule {
  return {
    moduleId: 'feishu-standard',
    channelName: 'feishu',
    displayName: '飞书',
    packageName: '@dotcraft/channel-feishu',
    configFileName: 'feishu.json',
    supportedTransports: ['websocket'],
    requiresInteractiveSetup: false,
    variant: 'standard',
    source: 'bundled',
    absolutePath: 'F:\\dotcraft\\sdk\\typescript\\packages\\channel-feishu',
    configDescriptors: [
      {
        key: 'feishu.brand',
        displayLabel: 'Platform',
        description: 'Base platform description',
        localizedDisplayLabel: {
          en: 'Service Platform',
          'zh-Hans': '服务平台'
        },
        localizedDescription: {
          en: 'Select the Feishu or Lark service environment.',
          'zh-Hans': '选择接入的服务环境：飞书或 Lark。'
        },
        required: false,
        dataKind: 'enum',
        masked: false,
        interactiveSetupOnly: false,
        defaultValue: 'feishu',
        enumValues: ['feishu', 'lark']
      },
      {
        key: 'feishu.downloadDir',
        displayLabel: 'Download Directory',
        description: 'Fallback description',
        localizedDisplayLabel: {
          en: 'Download Directory',
          'zh-Hans': '下载目录'
        },
        required: false,
        dataKind: 'path',
        masked: false,
        interactiveSetupOnly: false
      }
    ]
  }
}

function renderForm(locale: 'en' | 'zh-Hans') {
  settingsGet.mockResolvedValue({ locale })
  return render(
    <LocaleProvider>
      <ModuleConfigForm
        module={createModule()}
        config={{ feishu: { brand: 'feishu' } }}
        onChange={vi.fn()}
        onSave={vi.fn()}
        saving={false}
        persistedEnabled={false}
        wsAvailable={true}
        onStart={vi.fn()}
        onStop={vi.fn()}
        starting={false}
        qrDataUrl={null}
        qrPhase="idle"
        moduleLogLines={[]}
        logsLoading={false}
        onLoadLogs={vi.fn()}
      />
    </LocaleProvider>
  )
}

describe('ModuleConfigForm localization', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: {
          get: settingsGet
        },
        modules: {
          pickDirectory: vi.fn().mockResolvedValue(null)
        }
      }
    })
  })

  it('renders channel-provided zh-Hans labels and descriptions and keeps brand as enum select', async () => {
    renderForm('zh-Hans')

    await waitFor(() => {
      expect(screen.getByText('服务平台')).toBeInTheDocument()
    })
    expect(screen.getByText('选择接入的服务环境：飞书或 Lark。')).toBeInTheDocument()
    expect(screen.getByText('下载目录')).toBeInTheDocument()
    expect(screen.getByText('Fallback description')).toBeInTheDocument()
    const select = screen.getByDisplayValue('feishu') as HTMLSelectElement
    expect(select.tagName).toBe('SELECT')
    expect([...select.options].map((option) => option.value)).toEqual(['', 'feishu', 'lark'])
    expect([...select.options].map((option) => option.text)).toEqual(['', 'feishu', 'lark'])
  })

  it('prefers channel-provided english localization over base labels', async () => {
    renderForm('en')

    await waitFor(() => {
      expect(screen.getByText('Service Platform')).toBeInTheDocument()
    })
    expect(screen.getByText('Select the Feishu or Lark service environment.')).toBeInTheDocument()
    expect(screen.getByText('Download Directory')).toBeInTheDocument()
    expect(screen.getByText('Fallback description')).toBeInTheDocument()
  })
})
