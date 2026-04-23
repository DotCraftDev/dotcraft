import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { AnsiPre } from '../components/conversation/AnsiPre'

describe('AnsiPre', () => {
  it('renders ansi segments as styled spans', () => {
    render(<AnsiPre text={'\u001b[1;32mRUN\u001b[0m plain'} />)

    const runSpan = screen.getByText('RUN')
    expect(runSpan).toHaveStyle({ fontWeight: '600' })
    expect(runSpan).toHaveStyle({ color: 'var(--ansi-green)' })
    expect(document.querySelector('pre')).toHaveTextContent('RUN plain')
  })

  it('truncates lines when threshold is exceeded', () => {
    render(<AnsiPre text={'a\nb\nc'} truncatedLinesOver={2} />)
    expect(screen.getByText('a')).toBeInTheDocument()
    expect(screen.getByText('b')).toBeInTheDocument()
    expect(screen.getByText('…')).toBeInTheDocument()
    expect(screen.queryByText('c')).toBeNull()
  })
})
