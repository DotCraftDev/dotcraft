import { create } from 'zustand'

export type ModelCatalogStatus = 'idle' | 'loading' | 'ready' | 'error'

/** AppServer `model/list` error when the upstream endpoint does not support listing models. */
const MODEL_LIST_ERROR_ENDPOINT_NOT_SUPPORTED = 'EndpointNotSupported'

interface ModelCatalogState {
  status: ModelCatalogStatus
  modelOptions: string[]
  /** True when the server reports that the upstream API does not support model listing. */
  modelListUnsupportedEndpoint: boolean
  errorCode: string | null
  errorMessage: string | null
}

interface ModelCatalogActions {
  loadIfNeeded(force?: boolean): Promise<void>
  reset(): void
}

type ModelCatalogStore = ModelCatalogState & ModelCatalogActions

let inFlightLoad: Promise<void> | null = null

const initialState: ModelCatalogState = {
  status: 'idle',
  modelOptions: [],
  modelListUnsupportedEndpoint: false,
  errorCode: null,
  errorMessage: null
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

function parseModelListUnsupportedEndpoint(payload: unknown): boolean {
  const typed = payload as {
    success?: boolean
    errorCode?: string
    ErrorCode?: string
  }
  const errorCode = typed.errorCode ?? typed.ErrorCode
  return typed.success === false && errorCode === MODEL_LIST_ERROR_ENDPOINT_NOT_SUPPORTED
}

function parseModelListError(payload: unknown): { code: string | null; message: string | null } {
  const typed = payload as {
    success?: boolean
    errorCode?: string
    ErrorCode?: string
    errorMessage?: string
    ErrorMessage?: string
  }
  if (typed.success !== false) return { code: null, message: null }
  return {
    code: typed.errorCode ?? typed.ErrorCode ?? null,
    message: typed.errorMessage ?? typed.ErrorMessage ?? null
  }
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
        const error = parseModelListError(result)
        if (error.code || error.message) {
          set({
            modelOptions: [],
            status: 'error',
            modelListUnsupportedEndpoint: parseModelListUnsupportedEndpoint(result),
            errorCode: error.code,
            errorMessage: error.message
          })
          return
        }

        const options = parseModelOptions(result)
        set({
          modelOptions: options,
          status: 'ready',
          modelListUnsupportedEndpoint: false,
          errorCode: null,
          errorMessage: null
        })
      } catch (err) {
        set({
          modelOptions: [],
          status: 'error',
          modelListUnsupportedEndpoint: false,
          errorCode: null,
          errorMessage: err instanceof Error ? err.message : String(err)
        })
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
