import { app } from 'electron'

export const DOTCRAFT_DESKTOP_APP_NAME = 'DotCraft Desktop'
export const DOTCRAFT_DESKTOP_APP_ID = 'com.dotcraft.desktop'

function isPortableBuild(): boolean {
  return typeof process.env.PORTABLE_EXECUTABLE_DIR === 'string' &&
    process.env.PORTABLE_EXECUTABLE_DIR.length > 0
}

export function resolveWindowsAppUserModelId(): string {
  if (!app.isPackaged || isPortableBuild()) {
    return DOTCRAFT_DESKTOP_APP_NAME
  }

  return DOTCRAFT_DESKTOP_APP_ID
}

export function configureAppIdentity(): void {
  app.setName(DOTCRAFT_DESKTOP_APP_NAME)

  if (process.platform === 'win32') {
    app.setAppUserModelId(resolveWindowsAppUserModelId())
  }
}
