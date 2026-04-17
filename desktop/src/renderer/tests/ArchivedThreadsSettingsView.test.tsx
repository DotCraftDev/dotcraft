import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ConfirmDialogHost } from '../components/ui/ConfirmDialog'
import { ArchivedThreadsSettingsView } from '../components/settings/ArchivedThreadsSettingsView'

const settingsGet = vi.fn()
const appServerSendRequest = vi.fn()

function renderView(): void {
  render(
    <LocaleProvider>
      <ConfirmDialogHost />
      <ArchivedThreadsSettingsView workspacePath="E:\\Git\\dotcraft" />
    </LocaleProvider>
  )
}

describe('ArchivedThreadsSettingsView deletion actions', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    settingsGet.mockResolvedValue({})
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/list') {
        return {
          data: [
            {
              id: 'arch-1',
              displayName: 'Archived One',
              status: 'archived',
              originChannel: 'appserver',
              createdAt: new Date().toISOString(),
              lastActiveAt: new Date().toISOString()
            },
            {
              id: 'arch-2',
              displayName: 'Archived Two',
              status: 'archived',
              originChannel: 'appserver',
              createdAt: new Date().toISOString(),
              lastActiveAt: new Date().toISOString()
            }
          ]
        }
      }
      return {}
    })
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: { get: settingsGet },
        appServer: { sendRequest: appServerSendRequest },
        workspace: { saveImageToTemp: vi.fn() }
      }
    })
  })

  it('deletes a single archived thread after confirmation', async () => {
    renderView()

    await screen.findByText('Archived One')
    const rowDeleteButtons = screen.getAllByLabelText('Delete')
    fireEvent.click(rowDeleteButtons[0]!)
    const dialog = await screen.findByRole('dialog')
    expect(dialog).toBeDefined()
    fireEvent.click(within(dialog).getByRole('button', { name: 'Delete' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/delete', { threadId: 'arch-1' })
    })
  })

  it('deletes all archived threads after confirmation', async () => {
    renderView()

    await screen.findByText('Archived One')
    fireEvent.click(screen.getByRole('button', { name: 'Delete all' }))
    const dialog = await screen.findByRole('dialog')
    fireEvent.click(within(dialog).getByRole('button', { name: 'Delete all' }))

    await waitFor(() => {
      const deleteCalls = appServerSendRequest.mock.calls.filter((call) => call[0] === 'thread/delete')
      expect(deleteCalls).toHaveLength(2)
    })
  })
})
