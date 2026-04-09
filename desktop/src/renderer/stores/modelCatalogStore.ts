import { create } from 'zustand'

export type ModelCatalogStatus = 'idle' | 'loading' | 'ready' | 'error'

interface ModelCatalogState {
  status: ModelCatalogStatus
  modelOptions: string[]
}

interface ModelCatalogActions {
  loadIfNeeded(force?: boolean): Promise<void>
  reset(): void
}

type ModelCatalogStore = ModelCatalogState & ModelCatalogActions

let inFlightLoad: Promise<void> | null = null

const initialState: ModelCatalogState = {
  status: 'idle',
  modelOptions: []
}

function parseModelOptions(payload: unknown): string[] {
  const typed = payload as {
    success?: boolean
    models?: Array<{ id?: string; Id?: string }>
  }
  if (!typed.success || !Array.isArray(typed.models)) return []
  return Array.from(
    new Set(
      typed.models
        .map((m) => String(m.id ?? m.Id ?? '').trim())
        .filter(Boolean)
    )
  ).sort((a, b) => a.localeCompare(b))
}

export const useModelCatalogStore = create<ModelCatalogStore>((set, get) => ({
  ...initialState,

  async loadIfNeeded(force = false) {
    const current = get()
    if (!force && (current.status === 'ready' || current.status === 'loading')) {
      if (inFlightLoad) await inFlightLoad
      return
    }
    if (inFlightLoad) {
      await inFlightLoad
      return
    }

    set({ status: 'loading' })
    inFlightLoad = (async () => {
      try {
        const result = await window.api.appServer.listModels()
        const options = parseModelOptions(result)
        set({ modelOptions: options, status: 'ready' })
      } catch {
        // Silent fallback by design.
        set({ modelOptions: [], status: 'error' })
      } finally {
        inFlightLoad = null
      }
    })()

    await inFlightLoad
  },

  reset() {
    inFlightLoad = null
    set({ ...initialState })
  }
}))
