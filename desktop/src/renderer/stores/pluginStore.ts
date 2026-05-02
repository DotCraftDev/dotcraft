import { create } from 'zustand'

export interface PluginInterface {
  displayName?: string | null
  shortDescription?: string | null
  longDescription?: string | null
  developerName?: string | null
  category?: string | null
  capabilities?: string[]
  defaultPrompt?: string | null
  brandColor?: string | null
  composerIconDataUrl?: string | null
  logoDataUrl?: string | null
  websiteUrl?: string | null
  privacyPolicyUrl?: string | null
  termsOfServiceUrl?: string | null
}

export interface PluginFunctionInfo {
  name: string
  namespace?: string | null
  description: string
}

export interface PluginSkillInfo {
  name: string
  description: string
  displayName?: string | null
  shortDescription?: string | null
  enabled: boolean
}

export interface PluginEntry {
  id: string
  displayName: string
  description?: string | null
  version?: string | null
  enabled: boolean
  source: string
  rootPath: string
  interface?: PluginInterface | null
  functions: PluginFunctionInfo[]
  skills: PluginSkillInfo[]
  diagnostics?: Array<{ severity: string; code: string; message: string; pluginId?: string; path?: string }>
}

interface PluginState {
  plugins: PluginEntry[]
  loading: boolean
  error: string | null
  selectedPluginId: string | null
  selectedPlugin: PluginEntry | null
  detailLoading: boolean

  fetchPlugins(): Promise<void>
  selectPlugin(id: string): Promise<void>
  clearSelection(): void
  togglePluginEnabled(id: string, enabled: boolean): Promise<void>
}

export const usePluginStore = create<PluginState>((set, get) => ({
  plugins: [],
  loading: false,
  error: null,
  selectedPluginId: null,
  selectedPlugin: null,
  detailLoading: false,

  async fetchPlugins() {
    set({ loading: true, error: null })
    try {
      const result = (await window.api.appServer.sendRequest('plugin/list', {
        includeDisabled: true
      })) as { plugins?: PluginEntry[] }
      const plugins = result.plugins ?? []
      set((state) => ({
        plugins,
        selectedPlugin: state.selectedPluginId
          ? plugins.find((plugin) => plugin.id === state.selectedPluginId) ?? state.selectedPlugin
          : state.selectedPlugin,
        loading: false
      }))
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : String(e)
      set({ error: msg, loading: false })
    }
  },

  async selectPlugin(id: string) {
    set({ selectedPluginId: id, selectedPlugin: null, detailLoading: true })
    try {
      const result = (await window.api.appServer.sendRequest('plugin/view', { id })) as { plugin?: PluginEntry }
      const plugin = result.plugin ?? null
      set({ selectedPlugin: plugin, detailLoading: false })
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : String(e)
      set({ error: msg, detailLoading: false })
    }
  },

  clearSelection() {
    set({ selectedPluginId: null, selectedPlugin: null, detailLoading: false })
  },

  async togglePluginEnabled(id: string, enabled: boolean) {
    try {
      const result = (await window.api.appServer.sendRequest('plugin/setEnabled', {
        id,
        enabled
      })) as { plugin?: PluginEntry }
      const updated = result.plugin
      if (updated) {
        set((state) => ({
          plugins: state.plugins.map((plugin) => (plugin.id === updated.id ? updated : plugin)),
          selectedPlugin: state.selectedPlugin?.id === updated.id ? updated : state.selectedPlugin
        }))
      } else {
        await get().fetchPlugins()
      }
    } catch (e: unknown) {
      console.error('plugin/setEnabled failed:', e)
      throw e
    }
  }
}))
