import { afterEach, describe, expect, it, vi } from 'vitest'

const electronMocks = vi.hoisted(() => ({
  app: {
    isPackaged: false,
    setName: vi.fn(),
    setAppUserModelId: vi.fn()
  }
}))

vi.mock('electron', () => electronMocks)

describe('app identity', () => {
  afterEach(() => {
    electronMocks.app.isPackaged = false
    delete process.env.PORTABLE_EXECUTABLE_DIR
    vi.clearAllMocks()
  })

  it('uses the product name as the Windows notification identity in development', async () => {
    const { resolveWindowsAppUserModelId } = await import('../appIdentity')

    expect(resolveWindowsAppUserModelId()).toBe('DotCraft Desktop')
  })

  it('uses the product name for portable builds without installer registration', async () => {
    electronMocks.app.isPackaged = true
    process.env.PORTABLE_EXECUTABLE_DIR = 'E:/DotCraft'
    const { resolveWindowsAppUserModelId } = await import('../appIdentity')

    expect(resolveWindowsAppUserModelId()).toBe('DotCraft Desktop')
  })

  it('uses the package app id for installed builds', async () => {
    electronMocks.app.isPackaged = true
    const { resolveWindowsAppUserModelId } = await import('../appIdentity')

    expect(resolveWindowsAppUserModelId()).toBe('com.dotcraft.desktop')
  })
})
