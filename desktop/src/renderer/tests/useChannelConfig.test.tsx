import { describe, expect, it, vi, beforeEach } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { useState } from 'react'
import { useChannelConfig } from '../components/channels/useChannelConfig'

const fileReadFile = vi.fn<() => Promise<string>>()
const fileWriteFile = vi.fn<() => Promise<void>>()

function HookHarness({ workspacePath }: { workspacePath: string }): JSX.Element {
  const { config, error, reload, saveChannel } = useChannelConfig(workspacePath)
  const [saveError, setSaveError] = useState('')
  return (
    <div>
      <button onClick={() => void reload()}>reload</button>
      <button
        onClick={() => {
          void saveChannel('qq').catch((err) => {
            setSaveError(err instanceof Error ? err.message : String(err))
          })
        }}
      >
        save-qq
      </button>
      <pre data-testid="error">{error ?? ''}</pre>
      <pre data-testid="save-error">{saveError}</pre>
      <pre data-testid="config">{JSON.stringify(config)}</pre>
    </div>
  )
}

describe('useChannelConfig', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        file: {
          readFile: fileReadFile,
          writeFile: fileWriteFile
        }
      }
    })
  })

  it('reload parses workspace config encoded with UTF-8 BOM', async () => {
    fileReadFile.mockResolvedValue(
      '\uFEFF' +
        JSON.stringify({
          QQBot: {
            Enabled: true,
            Host: '127.0.0.1',
            Port: 6700,
            AccessToken: 'token'
          },
          WeComBot: {
            Enabled: true,
            Host: '0.0.0.0',
            Port: 9000,
            Robots: [{ Path: '/callback', Token: 'abc', AesKey: 'def' }]
          }
        })
    )

    render(<HookHarness workspacePath="E:/repo" />)
    fireEvent.click(screen.getByRole('button', { name: 'reload' }))

    await waitFor(() => {
      const parsed = JSON.parse(screen.getByTestId('config').textContent ?? '{}') as Record<string, unknown>
      const qq = parsed.qq as Record<string, unknown>
      const wecom = parsed.wecom as Record<string, unknown>
      expect(qq.Enabled).toBe(true)
      expect(qq.AccessToken).toBe('token')
      expect(wecom.Enabled).toBe(true)
      expect(Array.isArray(wecom.Robots)).toBe(true)
      expect((wecom.Robots as unknown[]).length).toBe(1)
    })
  })

  it('reload surfaces invalid JSON errors instead of silently resetting to defaults', async () => {
    fileReadFile.mockResolvedValue('{invalid-json')

    render(<HookHarness workspacePath="E:/repo" />)
    fireEvent.click(screen.getByRole('button', { name: 'reload' }))

    await waitFor(() => {
      expect(screen.getByTestId('error').textContent).not.toBe('')
    })
    expect(screen.getByTestId('config').textContent).toContain('"qq"')
  })

  it('saveChannel rejects invalid JSON and does not write the config file', async () => {
    fileReadFile.mockResolvedValue('{invalid-json')

    render(<HookHarness workspacePath="E:/repo" />)
    fireEvent.click(screen.getByRole('button', { name: 'save-qq' }))

    await waitFor(() => {
      expect(screen.getByTestId('save-error').textContent).not.toBe('')
      expect(fileWriteFile).not.toHaveBeenCalled()
    })
  })

  it('saveChannel treats non-object JSON as an empty root and writes channel config', async () => {
    fileReadFile.mockResolvedValue('[]')

    render(<HookHarness workspacePath="E:/repo" />)
    fireEvent.click(screen.getByRole('button', { name: 'save-qq' }))

    await waitFor(() => {
      expect(fileWriteFile).toHaveBeenCalledTimes(1)
    })

    const [, written] = fileWriteFile.mock.calls[0] as [string, string]
    expect(JSON.parse(written)).toEqual({
      QQBot: {
        Enabled: false,
        Host: '127.0.0.1',
        Port: 6700,
        AccessToken: ''
      }
    })
  })
})
