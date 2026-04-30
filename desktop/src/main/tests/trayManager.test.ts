import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { HubEvent } from '../HubClient'

const electronMocks = vi.hoisted(() => {
  const show = vi.fn()
  const on = vi.fn()
  const openExternal = vi.fn()
  return {
    show,
    on,
    openExternal,
    Notification: vi.fn().mockImplementation(() => ({ show, on }))
  }
})

vi.mock('electron', () => ({
  app: { isPackaged: false, resourcesPath: 'resources', quit: vi.fn(), on: vi.fn() },
  Menu: { buildFromTemplate: vi.fn((template) => ({ template })) },
  nativeImage: { createFromPath: vi.fn(), createEmpty: vi.fn() },
  Notification: Object.assign(electronMocks.Notification, { isSupported: vi.fn(() => true) }),
  shell: { openExternal: electronMocks.openExternal },
  Tray: vi.fn()
}))

vi.mock('fs', () => ({
  existsSync: vi.fn(() => true)
}))

describe('trayManager notifications', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('parses Hub notification events', async () => {
    const { parseHubNotificationPayload } = await import('../trayManager')
    const event: HubEvent = {
      kind: 'notification.requested',
      at: new Date().toISOString(),
      workspacePath: 'F:/dotcraft',
      data: {
        title: 'Done',
        body: 'Finished'
      }
    }

    expect(parseHubNotificationPayload(event)).toMatchObject({
      workspacePath: 'F:/dotcraft',
      title: 'Done',
      body: 'Finished'
    })
  })

  it('shows supported notification events', async () => {
    const { showHubNotification } = await import('../trayManager')
    const shown = showHubNotification({
      kind: 'notification.requested',
      at: new Date().toISOString(),
      workspacePath: 'F:/dotcraft',
      data: { title: 'Done', body: 'Finished' }
    })

    expect(shown).toBe(true)
    expect(electronMocks.Notification).toHaveBeenCalledWith({
      title: 'Done',
      body: 'Finished',
      icon: expect.any(String)
    })
    expect(electronMocks.show).toHaveBeenCalled()
  })
})
