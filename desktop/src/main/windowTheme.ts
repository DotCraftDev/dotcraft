import type { AppSettings } from './settings'
import type { TitleBarOverlayTheme } from '../shared/titleBarOverlay'
import { resolveThemeMode } from '../shared/theme'

export function resolveInitialTheme(settings: Pick<AppSettings, 'theme'>): TitleBarOverlayTheme {
  return resolveThemeMode(settings.theme)
}

