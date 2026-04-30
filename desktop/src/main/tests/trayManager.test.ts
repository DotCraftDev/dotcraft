import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { HubEvent } from '../HubClient'

const childProcessMocks = vi.hoisted(() => ({
  spawn: vi.fn(() => ({ unref: vi.fn() }))
}))

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

vi.mock('child_process', () => childProcessMocks)

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

describe('trayManager process launches', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('launches Desktop windows visibly with a workspace argument', async () => {
    const { spawnDesktopWindow } = await import('../trayManager')

    spawnDesktopWindow('E:/Git/dotcraft')

    const [, args, options] = childProcessMocks.spawn.mock.calls[0]
    expect(args).toEqual(expect.arrayContaining(['--workspace', 'E:/Git/dotcraft']))
    expect(options).toEqual({
      detached: true,
      stdio: 'ignore'
    })
  })

  it('launches default Desktop windows visibly', async () => {
    const { spawnDesktopWindow } = await import('../trayManager')

    spawnDesktopWindow()

    const [, args, options] = childProcessMocks.spawn.mock.calls[0]
    expect(args).not.toContain('--workspace')
    expect(args).not.toContain('--tray')
    expect(options).toEqual({
      detached: true,
      stdio: 'ignore'
    })
  })

  it('keeps the background tray process hidden', async () => {
    const { ensureTrayProcess } = await import('../trayManager')

    ensureTrayProcess()

    const [, args, options] = childProcessMocks.spawn.mock.calls[0]
    expect(args).toEqual(expect.arrayContaining(['--tray']))
    expect(options).toEqual({
      detached: true,
      stdio: 'ignore',
      windowsHide: true
    })
  })
})
