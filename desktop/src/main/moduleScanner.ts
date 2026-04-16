import { app } from 'electron'
import { promises as fs } from 'fs'
import * as path from 'path'
import type { AppSettings } from './settings'

export interface ConfigDescriptorWire {
  key: string
  displayLabel: string
  description: string
  required: boolean
  dataKind: string
  masked: boolean
  interactiveSetupOnly: boolean
  advanced?: boolean
  defaultValue?: unknown
  enumValues?: string[]
}

export interface DiscoveredModule {
  moduleId: string
  channelName: string
  displayName: string
  packageName: string
  configFileName: string
  supportedTransports: string[]
  requiresInteractiveSetup: boolean
  variant: string
  source: 'bundled' | 'user'
  absolutePath: string
  configDescriptors: ConfigDescriptorWire[]
}

export interface ChannelModuleGroup {
  channelName: string
  activeModuleId: string
  modules: DiscoveredModule[]
}

interface ManifestWire {
  moduleId: unknown
  channelName: unknown
  displayName: unknown
  packageName: unknown
  configFileName: unknown
  supportedTransports: unknown
  requiresInteractiveSetup: unknown
  variant: unknown
  configDescriptors: unknown
}

function asNonEmptyString(value: unknown): string | null {
  return typeof value === 'string' && value.trim() !== '' ? value : null
}

function asStringArray(value: unknown): string[] | null {
  if (!Array.isArray(value)) return null
  const parsed = value.filter((item): item is string => typeof item === 'string' && item.trim() !== '')
  return parsed.length === value.length ? parsed : null
}

function parseConfigDescriptor(value: unknown): ConfigDescriptorWire | null {
  if (value == null || typeof value !== 'object' || Array.isArray(value)) return null
  const item = value as Record<string, unknown>
  const key = asNonEmptyString(item.key)
  const displayLabel = asNonEmptyString(item.displayLabel)
  const description = typeof item.description === 'string' ? item.description : ''
  const dataKind = asNonEmptyString(item.dataKind)
  const enumValues = item.enumValues == null ? undefined : asStringArray(item.enumValues)
  if (
    key === null ||
    displayLabel === null ||
    dataKind === null ||
    typeof item.required !== 'boolean' ||
    typeof item.masked !== 'boolean' ||
    typeof item.interactiveSetupOnly !== 'boolean' ||
    (item.enumValues !== undefined && enumValues === null)
  ) {
    return null
  }
  return {
    key,
    displayLabel,
    description,
    required: item.required,
    dataKind,
    masked: item.masked,
    interactiveSetupOnly: item.interactiveSetupOnly,
    advanced: item.advanced === true,
    defaultValue: item.defaultValue,
    enumValues
  }
}

function parseManifest(
  manifest: ManifestWire,
  source: 'bundled' | 'user',
  modulePath: string
): DiscoveredModule | null {
  const moduleId = asNonEmptyString(manifest.moduleId)
  const channelName = asNonEmptyString(manifest.channelName)
  const displayName = asNonEmptyString(manifest.displayName)
  const packageName = asNonEmptyString(manifest.packageName)
  const configFileName = asNonEmptyString(manifest.configFileName)
  const supportedTransports = asStringArray(manifest.supportedTransports)
  const variant = asNonEmptyString(manifest.variant)
  const descriptorsRaw = manifest.configDescriptors
  if (
    moduleId === null ||
    channelName === null ||
    displayName === null ||
    packageName === null ||
    configFileName === null ||
    supportedTransports === null ||
    variant === null ||
    typeof manifest.requiresInteractiveSetup !== 'boolean' ||
    !Array.isArray(descriptorsRaw)
  ) {
    return null
  }

  const descriptors: ConfigDescriptorWire[] = []
  for (const descriptorRaw of descriptorsRaw) {
    const descriptor = parseConfigDescriptor(descriptorRaw)
    if (descriptor === null) return null
    descriptors.push(descriptor)
  }

  return {
    moduleId,
    channelName,
    displayName,
    packageName,
    configFileName,
    supportedTransports,
    requiresInteractiveSetup: manifest.requiresInteractiveSetup,
    variant,
    source,
    absolutePath: modulePath,
    configDescriptors: descriptors
  }
}

function bundledModulesDir(isDev: boolean): string {
  if (isDev) {
    return path.resolve(__dirname, '../../../sdk/typescript/packages')
  }
  return path.join(process.resourcesPath, 'modules')
}

function userModulesDir(settings: AppSettings): string {
  return settings.modulesDirectory ?? path.join(app.getPath('home'), '.craft', 'modules')
}

function activeVariantKey(channelName: string): string {
  return channelName.trim().toLowerCase()
}

export function groupModulesByChannel(
  modules: DiscoveredModule[],
  activeModuleVariants?: Record<string, string>
): ChannelModuleGroup[] {
  const byChannel = new Map<string, DiscoveredModule[]>()
  for (const module of modules) {
    const key = activeVariantKey(module.channelName)
    const list = byChannel.get(key)
    if (list) {
      list.push(module)
    } else {
      byChannel.set(key, [module])
    }
  }

  const groups: ChannelModuleGroup[] = []
  for (const [channelKey, channelModules] of byChannel) {
    const persistedActive = activeModuleVariants?.[channelKey]
    const persistedMatch =
      persistedActive == null
        ? undefined
        : channelModules.find((module) => module.moduleId === persistedActive)
    const userPreferred = channelModules.find((module) => module.source === 'user')
    const active = persistedMatch ?? userPreferred ?? channelModules[0]
    groups.push({
      channelName: active?.channelName ?? channelModules[0]?.channelName ?? channelKey,
      activeModuleId: active?.moduleId ?? channelModules[0]?.moduleId ?? '',
      modules: channelModules
    })
  }

  return groups
}

async function scanSingleRoot(
  rootDir: string,
  source: 'bundled' | 'user'
): Promise<DiscoveredModule[]> {
  const discovered: DiscoveredModule[] = []
  let entries: fs.Dirent[]
  try {
    entries = await fs.readdir(rootDir, { withFileTypes: true })
  } catch {
    return discovered
  }

  for (const entry of entries) {
    if (!entry.isDirectory()) continue
    const modulePath = path.join(rootDir, entry.name)
    const manifestPath = path.join(modulePath, 'manifest.json')
    try {
      const raw = await fs.readFile(manifestPath, 'utf-8')
      const parsed = JSON.parse(raw) as ManifestWire
      const module = parseManifest(parsed, source, modulePath)
      if (module === null) {
        console.warn(`[moduleScanner] invalid manifest: ${manifestPath}`)
        continue
      }
      discovered.push(module)
    } catch (error) {
      const code = (error as NodeJS.ErrnoException | null)?.code
      if (code !== 'ENOENT') {
        console.warn(`[moduleScanner] failed to load manifest: ${manifestPath}`, error)
      }
    }
  }

  return discovered
}

export async function scanModules(settings: AppSettings, isDev: boolean): Promise<DiscoveredModule[]> {
  const bundled = await scanSingleRoot(bundledModulesDir(isDev), 'bundled')
  const user = await scanSingleRoot(userModulesDir(settings), 'user')
  const merged = new Map<string, DiscoveredModule>()

  for (const module of bundled) {
    merged.set(module.moduleId, module)
  }
  for (const module of user) {
    merged.set(module.moduleId, module)
  }

  return [...merged.values()]
}
