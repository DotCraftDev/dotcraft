import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { ActionTooltip } from '../components/ui/ActionTooltip'
import { IconButton } from '../components/ui/IconButton'
import { ACTION_SHORTCUTS, formatShortcutParts } from '../components/ui/shortcutKeys'

describe('shortcut formatting', () => {
  it('formats Mod as Ctrl on Windows and Linux', () => {
    expect(formatShortcutParts(ACTION_SHORTCUTS.toggleDetailPanel, 'Win32')).toEqual([
      'Ctrl',
      'Shift',
      'B'
    ])
    expect(formatShortcutParts(ACTION_SHORTCUTS.settings, 'Linux x86_64')).toEqual(['Ctrl', ','])
  })

  it('formats Mod as Cmd on macOS', () => {
    expect(formatShortcutParts(ACTION_SHORTCUTS.quickOpen, 'MacIntel')).toEqual(['Cmd', 'P'])
  })
})

describe('ActionTooltip', () => {
  it('renders an action label with shortcut keycaps on hover', async () => {
    render(
      <ActionTooltip label="Show viewer panel" shortcut={ACTION_SHORTCUTS.toggleDetailPanel}>
        <button type="button">Open</button>
      </ActionTooltip>
    )

    fireEvent.mouseEnter(screen.getByRole('button').parentElement as HTMLElement)

    expect(await screen.findByRole('tooltip')).toHaveTextContent('Show viewer panel')
    expect(screen.getByRole('tooltip')).toHaveTextContent('Ctrl')
    expect(screen.getByRole('tooltip')).toHaveTextContent('Shift')
    expect(screen.getByRole('tooltip')).toHaveTextContent('B')
  })

  it('shows the disabled reason instead of shortcut keycaps', async () => {
    render(
      <ActionTooltip
        label="Commit file changes"
        shortcut={ACTION_SHORTCUTS.send}
        disabledReason="No changes to commit"
      >
        <button type="button" disabled>
          Commit
        </button>
      </ActionTooltip>
    )

    fireEvent.mouseEnter(screen.getByRole('button').parentElement as HTMLElement)

    expect(await screen.findByRole('tooltip')).toHaveTextContent('No changes to commit')
    expect(screen.getByRole('tooltip')).not.toHaveTextContent('Enter')
  })
})

describe('IconButton', () => {
  it('keeps the legacy label prop as the accessible name', () => {
    render(<IconButton icon={<span aria-hidden>R</span>} label="Refresh" />)

    expect(screen.getByRole('button', { name: 'Refresh' })).toBeInTheDocument()
  })
})
