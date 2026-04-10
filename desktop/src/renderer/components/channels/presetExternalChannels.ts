import type { ExternalChannelConfigWire } from './ExternalChannelConfigForm'

export interface PresetExternalChannel {
  name: string
  nameKey: string
  titleKey: string
  logoPath: string
  defaultDraft: ExternalChannelConfigWire
}

function channelLogo(filename: string): string {
  return new URL(`../../assets/channels/${filename}`, import.meta.url).toString()
}

export const PRESET_EXTERNAL_CHANNELS: PresetExternalChannel[] = [
  {
    name: 'weixin',
    nameKey: 'channels.channel.weixin',
    titleKey: 'channels.weixin.title',
    logoPath: channelLogo('weixin.svg'),
    defaultDraft: {
      name: 'weixin',
      enabled: false,
      transport: 'websocket',
      command: null,
      args: null,
      workingDirectory: null,
      env: null
    }
  },
  {
    name: 'telegram',
    nameKey: 'channels.channel.telegram',
    titleKey: 'channels.telegram.title',
    logoPath: channelLogo('telegram.svg'),
    defaultDraft: {
      name: 'telegram',
      enabled: false,
      transport: 'subprocess',
      command: 'python',
      args: ['-m', 'dotcraft_telegram'],
      workingDirectory: 'sdk/python/examples/telegram',
      env: { TELEGRAM_BOT_TOKEN: '' }
    }
  },
  {
    name: 'feishu',
    nameKey: 'channels.channel.feishu',
    titleKey: 'channels.feishu.title',
    logoPath: channelLogo('feishu.svg'),
    defaultDraft: {
      name: 'feishu',
      enabled: false,
      transport: 'websocket',
      command: null,
      args: null,
      workingDirectory: null,
      env: null
    }
  }
]

export const PRESET_EXTERNAL_CHANNELS_BY_NAME = new Map(
  PRESET_EXTERNAL_CHANNELS.map((item) => [item.name.toLowerCase(), item])
)

export function createPresetExternalDraft(name: string): ExternalChannelConfigWire {
  const preset = PRESET_EXTERNAL_CHANNELS_BY_NAME.get(name.toLowerCase())
  if (!preset) {
    return {
      name,
      enabled: false,
      transport: 'subprocess',
      command: '',
      args: [],
      workingDirectory: '',
      env: {}
    }
  }

  return {
    ...preset.defaultDraft,
    name: preset.name,
    args: [...(preset.defaultDraft.args ?? [])],
    env: preset.defaultDraft.env ? { ...preset.defaultDraft.env } : preset.defaultDraft.env
  }
}
