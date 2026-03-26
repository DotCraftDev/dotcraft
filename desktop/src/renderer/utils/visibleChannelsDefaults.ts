/**
 * Machine-local cross-channel visibility: first run seeds `visibleChannels` with all
 * `builtin` channels from `channel/list` (specs/desktop-client.md §9.4.1).
 */

export async function ensureVisibleChannelsSeeded(
  current?: { visibleChannels?: string[] }
): Promise<string[]> {
  const s = current ?? (await window.api.settings.get())
  if (Object.prototype.hasOwnProperty.call(s, 'visibleChannels')) {
    return s.visibleChannels ?? []
  }
  try {
    const res = await window.api.appServer.sendRequest('channel/list', {})
    const r = res as { channels?: { name: string; category?: string }[] }
    const channels = r.channels ?? []
    const builtin = channels
      .filter((c) => (c.category || 'builtin') === 'builtin')
      .map((c) => c.name)
    await window.api.settings.set({ visibleChannels: builtin })
    return builtin
  } catch {
    return []
  }
}
