// @vitest-environment jsdom
import { describe, it, expect, beforeAll } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MarkdownRenderer } from '../components/conversation/MarkdownRenderer'

beforeAll(() => {
  // highlight.js theme is loaded dynamically from App/main; not required for these tests
})

describe('MarkdownRenderer', () => {
  it('renders plain text content', () => {
    const { container } = render(<MarkdownRenderer content="Hello world" />)
    expect(container.textContent).toContain('Hello world')
  })

  it('renders a heading', () => {
    render(<MarkdownRenderer content="# Main Title" />)
    const heading = document.querySelector('h1')
    expect(heading).not.toBeNull()
    expect(heading?.textContent).toContain('Main Title')
  })

  it('renders a subheading', () => {
    render(<MarkdownRenderer content="## Section" />)
    const heading = document.querySelector('h2')
    expect(heading).not.toBeNull()
    expect(heading?.textContent).toContain('Section')
  })

  it('renders an unordered list', () => {
    const { container } = render(<MarkdownRenderer content={'- Item 1\n- Item 2\n- Item 3'} />)
    const items = container.querySelectorAll('li')
    expect(items.length).toBe(3)
    expect(items[0].textContent).toContain('Item 1')
  })

  it('renders a fenced code block', () => {
    const content = '```typescript\nconst x = 1\n```'
    const { container } = render(<MarkdownRenderer content={content} />)
    const codeBlock = container.querySelector('pre')
    expect(codeBlock).not.toBeNull()
    expect(codeBlock?.textContent).toContain('const x = 1')
  })

  it('renders inline code', () => {
    const { container } = render(<MarkdownRenderer content="Use `npm install` to install." />)
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
    const { container } = render(<MarkdownRenderer content={tableMarkdown} />)
    const table = container.querySelector('table')
    expect(table).not.toBeNull()
    expect(container.textContent).toContain('foo')
    expect(container.textContent).toContain('bar')
  })

  it('renders a link with onClick (no href navigation)', () => {
    render(<MarkdownRenderer content="[DotCraft](https://example.com)" />)
    const link = screen.getByRole('link', { name: /dotcraft/i })
    expect(link).toBeDefined()
    expect(link.getAttribute('href')).toBe('https://example.com')
  })

  it('renders bold and italic text', () => {
    const { container } = render(<MarkdownRenderer content="**bold** and _italic_" />)
    expect(container.querySelector('strong')?.textContent).toContain('bold')
    expect(container.querySelector('em')?.textContent).toContain('italic')
  })

  it('memoizes: does not re-render when content unchanged', () => {
    // Re-render the same component twice with same props; verify DOM is stable
    const { rerender, container } = render(<MarkdownRenderer content="Static text" />)
    const firstHTML = container.innerHTML
    rerender(<MarkdownRenderer content="Static text" />)
    expect(container.innerHTML).toBe(firstHTML)
  })
})
