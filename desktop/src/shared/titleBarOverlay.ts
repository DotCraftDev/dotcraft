/**
 * Single source of truth for Electron titleBarOverlay (Windows / Linux) and
 * renderer layout that must align (CustomMenuBar reserve, toast offset).
 * Colors mirror desktop/src/renderer/styles/tokens.css --bg-primary / --text-primary.
 */

export const TITLE_BAR_OVERLAY_HEIGHT = 36

/** Horizontal space reserved in CustomMenuBar so menu labels do not overlap caption buttons. */
export const TITLE_BAR_OVERLAY_RIGHT_RESERVE = 138

export type TitleBarOverlayTheme = 'dark' | 'light'

export const TITLE_BAR_OVERLAY_BY_THEME: Record<
  TitleBarOverlayTheme,
  { color: string; symbolColor: string }
> = {
  dark: { color: '#1a1a1a', symbolColor: '#e5e5e5' },
  light: { color: '#f7f8fa', symbolColor: '#1e3347' }
}
