import { create } from 'zustand'

export type ToastType = 'info' | 'success' | 'warning' | 'error'

export interface Toast {
  id: string
  message: string
  type: ToastType
  duration: number
}

interface ToastState {
  toasts: Toast[]
}

interface ToastActions {
  addToast(message: string, type?: ToastType, duration?: number): void
  removeToast(id: string): void
}

type ToastStore = ToastState & ToastActions

const DEFAULT_DURATION_MS = 4000
const JOB_RESULT_DURATION_MS = 10000

export const useToastStore = create<ToastStore>((set, get) => ({
  toasts: [],

  addToast(message, type = 'info', duration = DEFAULT_DURATION_MS) {
    const id = `toast-${Date.now()}-${Math.random().toString(36).slice(2)}`
    set((s) => ({ toasts: [...s.toasts, { id, message, type, duration }] }))

    setTimeout(() => {
      if (get().toasts.some((t) => t.id === id)) {
        get().removeToast(id)
      }
    }, duration)
  },

  removeToast(id) {
    set((s) => ({ toasts: s.toasts.filter((t) => t.id !== id) }))
  }
}))

/** Convenience helpers for non-React callers */
export const addToast = (message: string, type?: ToastType, duration?: number): void =>
  useToastStore.getState().addToast(message, type, duration)

export const addJobResultToast = (message: string): void =>
  useToastStore.getState().addToast(message, 'info', JOB_RESULT_DURATION_MS)
