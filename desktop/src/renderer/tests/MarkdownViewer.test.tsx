// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { MarkdownViewer } from '../components/detail/viewers/MarkdownViewer'

const readTextMock = vi.fn()

describe('MarkdownViewer', () => {
  beforeEach(() => {
    readTextMock.mockReset()
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: { get: () => Promise.resolve({ locale: 'en' }) },
        workspace: {
          viewer: {
            readText: readTextMock
          }
        }
      }
    })
  })

  function renderViewer(): ReturnType<typeof render> {
    return render(
      <LocaleProvider>
        <MarkdownViewer absolutePath="C:/repo/README.md" />
      </LocaleProvider>
    )
  }

  it('renders markdown preview without preview/source mode buttons', async () => {
    readTextMock.mockResolvedValue({ text: '# Hello Preview', truncated: false })
    renderViewer()

    expect(screen.queryByRole('button', { name: /preview/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /source/i })).not.toBeInTheDocument()
    expect(await screen.findByRole('heading', { name: /hello preview/i })).toBeInTheDocument()
  })

  it('shows truncated notice when markdown content is clipped', async () => {
    readTextMock.mockResolvedValue({ text: 'Body', truncated: true })
    renderViewer()

    expect(await screen.findByRole('status')).toHaveTextContent('File is large')
  })
})
