import { describe, expect, it } from 'vitest'
import { mergeAvailableChannels, seedVisibleChannelsFromAvailableChannels } from '../utils/availableChannels'

describe('mergeAvailableChannels', () => {
  it('adds built-in social module channels that are missing from channel/list', () => {
    const merged = mergeAvailableChannels(
      [
        { name: 'qq', category: 'social' },
        { name: 'acp', category: 'builtin' }
      ],
      [
        { channelName: 'weixin' },
        { channelName: 'feishu' },
        { channelName: 'cron' }
      ]
    )

    expect(merged).toEqual([
      { name: 'qq', category: 'social' },
      { name: 'acp', category: 'builtin' },
      { name: 'weixin', category: 'social' },
      { name: 'feishu', category: 'social' }
    ])
  })

  it('does not duplicate channels already returned by channel/list', () => {
    const merged = mergeAvailableChannels(
      [{ name: 'feishu', category: 'social' }],
      [{ channelName: 'feishu' }]
    )

    expect(merged).toEqual([{ name: 'feishu', category: 'social' }])
  })
})

describe('seedVisibleChannelsFromAvailableChannels', () => {
  it('seeds builtin, social, and external channels', () => {
    expect(
      seedVisibleChannelsFromAvailableChannels([
        { name: 'acp', category: 'builtin' },
        { name: 'qq', category: 'social' },
        { name: 'feishu', category: 'social' },
        { name: 'feishu-adapter', category: 'external' },
        { name: 'cron', category: 'system' }
      ])
    ).toEqual(['acp', 'qq', 'feishu', 'feishu-adapter'])
  })
})
