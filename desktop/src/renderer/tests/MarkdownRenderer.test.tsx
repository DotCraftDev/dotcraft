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

  it('uses compact markdown block spacing', () => {
    const { container } = renderWithLocale('First paragraph\n\n- One\n- Two')
    const paragraph = container.querySelector('p')
    const list = container.querySelector('ul')
    const firstItem = container.querySelector('li')

    expect(paragraph).toHaveStyle({ margin: '0 0 6px' })
    expect(list?.getAttribute('style')).toContain('margin: 0px 0px 6px')
    expect(firstItem).toHaveStyle({ margin: '0 0 3px' })
  })

  it('marks markdown body for trailing block margin trim', () => {
    const { container } = renderWithLocale('Only paragraph')
    const markdownBody = container.querySelector('.markdown-body')
    const lastBlock = container.querySelector('.markdown-body > :last-child')

    expect(markdownBody).not.toBeNull()
    expect(lastBlock).not.toBeNull()
  })

  it('uses conversation code typography tokens', () => {
    const { container } = renderWithLocale('Inline `code`\n\n```ts\nconst x = 1\n```')
    const inlineCode = container.querySelector('p code')
    const block = container.querySelector('pre')

    expect(inlineCode?.getAttribute('style')).toContain('font-size: var(--text-code-size)')
    expect(block?.getAttribute('style')).toContain('font-size: var(--text-code-size)')
    expect(block?.getAttribute('style')).toContain('line-height: var(--text-code-line-height)')
    expect(block?.getAttribute('style')).toContain('padding: 12px 72px 12px 14px')
    expect(block).toHaveStyle({ paddingRight: '72px' })
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
    expect(link).toHaveStyle({ textDecoration: 'none' })
  })

  it('renders file links as inline reference pills', () => {
    renderWithLocale('[./docs/guide.md](./docs/guide.md)')
    const link = screen.getByRole('link', { name: /guide\.md/i })
    expect(link).toHaveAttribute('data-inline-reference-kind', 'file')
    expect(link).toHaveAttribute('title', './docs/guide.md')
  })

  it('shortens raw browser links into readable labels', () => {
    renderWithLocale('[https://docs.example.com/start](https://docs.example.com/start)')
    const link = screen.getByRole('link', { name: /docs\.example\.com\/start/i })
    expect(link).toHaveAttribute('data-inline-reference-kind', 'browser')
    expect(link.getAttribute('href')).toBe('https://docs.example.com/start')
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
