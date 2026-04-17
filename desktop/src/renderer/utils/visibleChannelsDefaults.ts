/**
 * Machine-local cross-channel visibility: first run seeds `visibleChannels` with all
 * `builtin` channels from `channel/list` (specs/desktop-client.md §9.4.1).
 */

import { mergeAvailableChannels, seedVisibleChannelsFromAvailableChannels } from './availableChannels'

export async function ensureVisibleChannelsSeeded(
  current?: { visibleChannels?: string[] }
): Promise<string[]> {
  const s = current ?? (await window.api.settings.get())
  if (Object.prototype.hasOwnProperty.call(s, 'visibleChannels')) {
    return s.visibleChannels ?? []
  }
  try {
    const [channelListRes, modules] = await Promise.all([
      window.api.appServer.sendRequest('channel/list', {}),
      window.api.modules.list().catch(() => [])
    ])
    const r = channelListRes as { channels?: { name: string; category?: string }[] }
    const mergedChannels = mergeAvailableChannels(r.channels ?? [], modules)
    const seeded = seedVisibleChannelsFromAvailableChannels(mergedChannels)
    await window.api.settings.set({ visibleChannels: seeded })
    return seeded
  } catch {
    return []
  }
}
