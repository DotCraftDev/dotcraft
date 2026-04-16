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
