import type { CSSProperties } from 'react'

import { TITLE_BAR_OVERLAY_RIGHT_RESERVE } from '../../../shared/titleBarOverlay'
import { TOP_LEVEL_MENU_IDS, type TopLevelMenuId } from '../../../shared/locales'
import { useT } from '../../contexts/LocaleContext'

const dragRegion: CSSProperties = { WebkitAppRegion: 'drag' }
const noDrag: CSSProperties = { WebkitAppRegion: 'no-drag' }

/**
 * In-window menu strip for Windows / Linux when using a hidden OS title bar.
 * macOS uses the system menu bar only; this component is not rendered there.
 */
const MENU_LABEL_KEY: Record<TopLevelMenuId, 'menu.file' | 'menu.edit' | 'menu.view' | 'menu.window' | 'menu.help'> =
  {
    file: 'menu.file',
    edit: 'menu.edit',
    view: 'menu.view',
    window: 'menu.window',
    help: 'menu.help'
  }

export function CustomMenuBar(): JSX.Element {
  const height = window.api.titleBarOverlayHeight
  const t = useT()

  return (
    <div
      style={{
        ...dragRegion,
        height,
        flexShrink: 0,
        display: 'flex',
        flexDirection: 'row',
        alignItems: 'center',
        paddingLeft: 8,
        paddingRight: TITLE_BAR_OVERLAY_RIGHT_RESERVE,
        backgroundColor: 'var(--bg-primary)',
        borderBottom: 'none',
        fontSize: 13,
        color: 'var(--text-secondary)'
      }}
    >
      {TOP_LEVEL_MENU_IDS.map((menuId) => (
        <button
          key={menuId}
          type="button"
          style={{
            ...noDrag,
            marginRight: 2,
            padding: '2px 8px',
            border: 'none',
            borderRadius: 4,
            background: 'transparent',
            color: 'inherit',
            font: 'inherit',
            cursor: 'default'
          }}
          onMouseDown={(e) => {
            e.preventDefault()
            const r = e.currentTarget.getBoundingClientRect()
            void window.api.menu.popupTopLevel(menuId, r.left, r.bottom)
          }}
        >
          {t(MENU_LABEL_KEY[menuId])}
        </button>
      ))}
    </div>
  )
}
