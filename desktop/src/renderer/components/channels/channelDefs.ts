export type ChannelId = 'qq' | 'wecom'

export interface ChannelDefinition {
  id: ChannelId
  nameKey: string
  logoPath?: string
  channelListName: string
}

export const CHANNEL_DEFS: ChannelDefinition[] = [
  {
    id: 'qq',
    nameKey: 'channels.channel.qq',
    logoPath: new URL('../../assets/channels/qq.svg', import.meta.url).toString(),
    channelListName: 'qq'
  },
  {
    id: 'wecom',
    nameKey: 'channels.channel.wecom',
    logoPath: new URL('../../assets/channels/wecom.svg', import.meta.url).toString(),
    channelListName: 'wecom'
  }
]
