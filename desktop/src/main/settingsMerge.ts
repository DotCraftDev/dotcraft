import type { AppSettings } from './settings'
import { normalizeLocale } from '../shared/locales'

export function mergeUpdatedSettings(current: AppSettings, partial: Partial<AppSettings>): Partial<AppSettings> {
  const next: Partial<AppSettings> = { ...partial }

  if (partial.locale !== undefined) {
    next.locale = normalizeLocale(partial.locale)
  }

  if (partial.proxy !== undefined) {
    next.proxy = {
      ...(current.proxy ?? {}),
      ...partial.proxy
    }
  }

  if (partial.webSocket !== undefined) {
    next.webSocket = {
      ...(current.webSocket ?? {}),
      ...partial.webSocket
    }
  }

  if (partial.remote !== undefined) {
    next.remote = {
      ...(current.remote ?? {}),
      ...partial.remote
    }
  }

  return next
}
