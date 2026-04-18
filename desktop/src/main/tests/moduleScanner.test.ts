import { afterEach, describe, expect, it, vi } from 'vitest'
import { mkdtemp, mkdir, rm, writeFile } from 'fs/promises'
import { join } from 'path'
import { tmpdir } from 'os'

vi.mock('electron', () => ({
  app: {
    getPath: vi.fn(() => 'C:\\Users\\tester')
  }
}))

import { scanModules } from '../moduleScanner'

describe('scanModules', () => {
  let tempRoot = ''

  afterEach(async () => {
    if (tempRoot) {
      await rm(tempRoot, { recursive: true, force: true })
      tempRoot = ''
    }
  })

  it('parses localized descriptor fields and enum values from module manifest', async () => {
    tempRoot = await mkdtemp(join(tmpdir(), 'dotcraft-modules-'))
    const moduleDir = join(tempRoot, 'channel-feishu')
    await mkdir(moduleDir, { recursive: true })
    await writeFile(
      join(moduleDir, 'manifest.json'),
      JSON.stringify(
        {
          moduleId: 'feishu-standard',
          channelName: 'feishu',
          displayName: '飞书',
          packageName: '@dotcraft/channel-feishu',
          configFileName: 'feishu.json',
          supportedTransports: ['websocket'],
          requiresInteractiveSetup: false,
          variant: 'standard',
          configDescriptors: [
            {
              key: 'feishu.brand',
              displayLabel: 'Platform',
              description: 'Select the Feishu or Lark service environment.',
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
              enumValues: ['feishu', 'lark']
            }
          ]
        },
        null,
        2
      ),
      'utf-8'
    )

    const modules = await scanModules({ modulesDirectory: tempRoot }, true)
    const module = modules.find((entry) => entry.moduleId === 'feishu-standard')
    expect(module).toBeDefined()
    expect(module?.configDescriptors).toEqual([
      expect.objectContaining({
        key: 'feishu.brand',
        dataKind: 'enum',
        enumValues: ['feishu', 'lark'],
        localizedDisplayLabel: {
          en: 'Service Platform',
          'zh-Hans': '服务平台'
        },
        localizedDescription: {
          en: 'Select the Feishu or Lark service environment.',
          'zh-Hans': '选择接入的服务环境：飞书或 Lark。'
        }
      })
    ])
  })
})
