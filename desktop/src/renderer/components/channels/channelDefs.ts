export type ChannelId = 'qq' | 'wecom' | 'weixin' | 'telegram'

export interface ChannelDefinition {
  id: ChannelId
  nameKey: string
  logoPath: string
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
  },
  {
    id: 'weixin',
    nameKey: 'channels.channel.weixin',
    logoPath: new URL('../../assets/channels/weixin.svg', import.meta.url).toString(),
    channelListName: 'weixin'
  },
  {
    id: 'telegram',
    nameKey: 'channels.channel.telegram',
    logoPath: new URL('../../assets/channels/telegram.svg', import.meta.url).toString(),
    channelListName: 'telegram'
  }
]
