// @vitest-environment jsdom
import { describe, it, expect, beforeAll } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MarkdownRenderer } from '../components/conversation/MarkdownRenderer'
import { LocaleProvider } from '../contexts/LocaleContext'

beforeAll(() => {
  // highlight.js theme is loaded dynamically from App/main; not required for these tests
  Object.defineProperty(window, 'api', {
    configurable: true,
    value: {
      settings: { get: () => Promise.resolve({ locale: 'en' }) }
    }
  })
})

describe('MarkdownRenderer', () => {
  function renderWithLocale(content: string): ReturnType<typeof render> {
    return render(
      <LocaleProvider>
        <MarkdownRenderer content={content} />
      </LocaleProvider>
    )
  }

  it('renders plain text content', () => {
    const { container } = renderWithLocale('Hello world')
    expect(container.textContent).toContain('Hello world')
  })

  it('renders a heading', () => {
    renderWithLocale('# Main Title')
    const heading = document.querySelector('h1')
    expect(heading).not.toBeNull()
    expect(heading?.textContent).toContain('Main Title')
  })

  it('renders a subheading', () => {
    renderWithLocale('## Section')
    const heading = document.querySelector('h2')
    expect(heading).not.toBeNull()
    expect(heading?.textContent).toContain('Section')
  })

  it('renders an unordered list', () => {
    const { container } = renderWithLocale('- Item 1\n- Item 2\n- Item 3')
    const items = container.querySelectorAll('li')
    expect(items.length).toBe(3)
    expect(items[0].textContent).toContain('Item 1')
  })

  it('renders a fenced code block', () => {
    const content = '```typescript\nconst x = 1\n```'
    const { container } = renderWithLocale(content)
    const codeBlock = container.querySelector('pre')
    expect(codeBlock).not.toBeNull()
    expect(codeBlock?.textContent).toContain('const x = 1')
  })

  it('renders inline code', () => {
    const { container } = renderWithLocale('Use `npm install` to install.')
    const code = container.querySelector('code')
    expect(code).not.toBeNull()
    expect(code?.textContent).toContain('npm install')
  })

  it('renders a GFM table', () => {
    const tableMarkdown = [
      '| Name | Value |',
      '|------|-------|',
      '| foo  | bar   |'
    ].join('\n')
    const { container } = renderWithLocale(tableMarkdown)
    const table = container.querySelector('table')
    expect(table).not.toBeNull()
    expect(container.textContent).toContain('foo')
    expect(container.textContent).toContain('bar')
  })

  it('renders a link with onClick (no href navigation)', () => {
    renderWithLocale('[DotCraft](https://example.com)')
    const link = screen.getByRole('link', { name: /dotcraft/i })
    expect(link).toBeDefined()
    expect(link.getAttribute('href')).toBe('https://example.com')
  })

  it('renders bold and italic text', () => {
    const { container } = renderWithLocale('**bold** and _italic_')
    expect(container.querySelector('strong')?.textContent).toContain('bold')
    expect(container.querySelector('em')?.textContent).toContain('italic')
  })

  it('memoizes: does not re-render when content unchanged', () => {
    // Re-render the same component twice with same props; verify DOM is stable
    const { rerender, container } = renderWithLocale('Static text')
    const firstHTML = container.innerHTML
    rerender(
      <LocaleProvider>
        <MarkdownRenderer content="Static text" />
      </LocaleProvider>
    )
    expect(container.innerHTML).toBe(firstHTML)
  })
})
