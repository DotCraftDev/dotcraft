import type { AppSettings } from './settings'
import type { TitleBarOverlayTheme } from '../shared/titleBarOverlay'

export function resolveInitialTheme(settings: Pick<AppSettings, 'theme'>): TitleBarOverlayTheme {
  return settings.theme === 'light' ? 'light' : 'dark'
}

