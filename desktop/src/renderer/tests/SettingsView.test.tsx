import { describe, it, expect, beforeEach, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { SettingsView } from '../components/settings/SettingsView'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useToastStore } from '../stores/toastStore'
import { useUIStore } from '../stores/uiStore'

const settingsGet = vi.fn()
const settingsSet = vi.fn()
const appServerSendRequest = vi.fn()
const appServerRestartManaged = vi.fn()

function renderSettingsView(): void {
  render(
    <LocaleProvider>
      <SettingsView />
    </LocaleProvider>
  )
}

describe('SettingsView restart AppServer', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    useToastStore.setState({ toasts: [] })
    useUIStore.setState({ activeMainView: 'settings' })

    settingsGet.mockResolvedValue({
      connectionMode: 'stdio',
      webSocket: { host: '127.0.0.1', port: 9100 },
      locale: 'en'
    })
    settingsSet.mockResolvedValue(undefined)
    appServerSendRequest.mockResolvedValue({ channels: [] })
    appServerRestartManaged.mockResolvedValue(undefined)

    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: {
          get: settingsGet,
          set: settingsSet
        },
        appServer: {
          sendRequest: appServerSendRequest,
          restartManaged: appServerRestartManaged
        }
      }
    })
  })

  it('shows restart button for Desktop-managed connection modes', async () => {
    renderSettingsView()

    fireEvent.click(await screen.findByRole('button', { name: 'Connection' }))

    expect(await screen.findByRole('button', { name: 'Restart AppServer' })).toBeInTheDocument()
  })

  it('hides restart button for remote mode', async () => {
    settingsGet.mockResolvedValue({
      connectionMode: 'remote',
      remote: { url: 'ws://127.0.0.1:9100/ws' },
      locale: 'en'
    })

    renderSettingsView()

    fireEvent.click(await screen.findByRole('button', { name: 'Connection' }))

    await waitFor(() => {
      expect(screen.queryByRole('button', { name: 'Restart AppServer' })).not.toBeInTheDocument()
    })
  })

  it('restarts managed AppServer and adds a success toast', async () => {
    renderSettingsView()

    fireEvent.click(await screen.findByRole('button', { name: 'Connection' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Restart AppServer' }))

    await waitFor(() => {
      expect(appServerRestartManaged).toHaveBeenCalledOnce()
    })
    await waitFor(() => {
      expect(useToastStore.getState().toasts.some((toast) => toast.message === 'AppServer restarted')).toBe(true)
    })
  })

  it('shows an error toast when restart fails', async () => {
    appServerRestartManaged.mockRejectedValue(new Error('boom'))

    renderSettingsView()

    fireEvent.click(await screen.findByRole('button', { name: 'Connection' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Restart AppServer' }))

    await waitFor(() => {
      expect(
        useToastStore
          .getState()
          .toasts.some((toast) => toast.message === 'Failed to restart AppServer: boom')
      ).toBe(true)
    })
  })
})
