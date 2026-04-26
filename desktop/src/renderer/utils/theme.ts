/// <reference types="vite/client" />
import hljsDarkUrl from 'highlight.js/styles/github-dark.css?url'
import hljsLightUrl from 'highlight.js/styles/github.css?url'

export type ThemeMode = 'dark' | 'light'

const HLJS_LINK_ID = 'dotcraft-hljs-theme'

/**
 * Normalize persisted or unknown theme values to a valid mode.
 */
export function resolveTheme(raw: unknown): ThemeMode {
  return raw === 'light' ? 'light' : 'dark'
}

function getHljsHref(mode: ThemeMode): string {
  return mode === 'light' ? hljsLightUrl : hljsDarkUrl
}

/**
 * Sets `data-theme` on `<html>` and swaps the single highlight.js stylesheet.
 */
export function applyTheme(
  mode: ThemeMode,
  options: { syncTitleBarOverlay?: boolean } = {}
): void {
  document.documentElement.setAttribute('data-theme', mode)

  let link = document.getElementById(HLJS_LINK_ID) as HTMLLinkElement | null
  if (!link) {
    link = document.createElement('link')
    link.id = HLJS_LINK_ID
    link.rel = 'stylesheet'
    document.head.appendChild(link)
  }
  link.href = getHljsHref(mode)

  if (
    options.syncTitleBarOverlay !== false &&
    typeof window !== 'undefined' &&
    window.api?.platform !== 'darwin'
  ) {
    void window.api.window.setTitleBarOverlayTheme(mode)
  }
}
