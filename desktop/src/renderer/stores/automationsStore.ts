import { create } from 'zustand'
import { useReviewPanelStore } from './reviewPanelStore'

/** Polling interval for task list while Automations view is mounted (ms). */
const AUTOMATIONS_POLL_MS = 15_000

let pollTimer: ReturnType<typeof setInterval> | null = null

export type AutomationTaskStatus =
  | 'pending'
  | 'dispatched'
  | 'agent_running'
  | 'agent_completed'
  | 'awaiting_review'
  | 'approved'
  | 'rejected'
  | 'failed'

export interface AutomationTask {
  id: string
  title: string
  status: AutomationTaskStatus
  sourceName: string
  threadId: string | null
  description?: string
  agentSummary?: string | null
  /** Wire: `workspaceScope` (default) or `fullAuto`; legacy `autoApprove` / `default`. */
  approvalPolicy?: string | null
  createdAt: string
  updatedAt: string
}

export type SourceFilter = 'all' | 'local' | 'github'

interface AutomationsState {
  tasks: AutomationTask[]
  loading: boolean
  error: string | null
  selectedTaskId: string | null
  filterSource: SourceFilter

  /** Full refresh (shows loading). Use for initial load and explicit user refresh. */
  fetchTasks(options?: { silent?: boolean }): Promise<void>
  /** Starts periodic silent refresh; call from AutomationsView on mount. */
  startPolling(): void
  /** Stops periodic refresh; call on unmount. */
  stopPolling(): void
  createTask(
    title: string,
    description: string,
    workflowTemplate?: string,
    approvalPolicy?: 'workspaceScope' | 'fullAuto',
    workspaceMode?: 'project' | 'isolated'
  ): Promise<void>
  approveTask(taskId: string, sourceName: string): Promise<void>
  rejectTask(taskId: string, sourceName: string, reason?: string): Promise<void>
  deleteTask(task: AutomationTask): Promise<void>
  selectTask(taskId: string | null): void
  setFilterSource(filter: SourceFilter): void
  upsertTask(task: AutomationTask): void
  removeTask(taskId: string, sourceName: string): void
}

export const useAutomationsStore = create<AutomationsState>((set, get) => ({
  tasks: [],
  loading: false,
  error: null,
  selectedTaskId: null,
  filterSource: 'all',

  async fetchTasks(options?: { silent?: boolean }) {
    const silent = options?.silent === true
    if (!silent) set({ loading: true, error: null })
    try {
      const result = (await window.api.appServer.sendRequest('automation/task/list', {})) as {
        tasks?: AutomationTask[]
      }
      set({ tasks: result.tasks ?? [], loading: false })
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : String(e)
      if (!silent) set({ error: msg, loading: false })
      else set({ loading: false })
    }
  },

  startPolling() {
    if (pollTimer != null) return
    pollTimer = setInterval(() => {
      void useAutomationsStore.getState().fetchTasks({ silent: true })
    }, AUTOMATIONS_POLL_MS)
  },

  stopPolling() {
    if (pollTimer != null) {
      clearInterval(pollTimer)
      pollTimer = null
    }
  },

  async createTask(
    title: string,
    description: string,
    workflowTemplate?: string,
    approvalPolicy: 'workspaceScope' | 'fullAuto' = 'workspaceScope',
    workspaceMode: 'project' | 'isolated' = 'project'
  ) {
    const params: Record<string, unknown> = {
      title,
      description,
      approvalPolicy,
      workspaceMode
    }
    if (workflowTemplate) params.workflowTemplate = workflowTemplate
    await window.api.appServer.sendRequest('automation/task/create', params)
    await get().fetchTasks()
  },

  async approveTask(taskId: string, sourceName: string) {
    await window.api.appServer.sendRequest('automation/task/approve', { taskId, sourceName })
    await get().fetchTasks()
  },

  async rejectTask(taskId: string, sourceName: string, reason?: string) {
    const params: Record<string, unknown> = { taskId, sourceName }
    if (reason) params.reason = reason
    await window.api.appServer.sendRequest('automation/task/reject', params)
    await get().fetchTasks()
  },

  async deleteTask(task: AutomationTask) {
    await window.api.appServer.sendRequest('automation/task/delete', {
      taskId: task.id,
      sourceName: task.sourceName
    })
    if (task.threadId) {
      try {
        await window.api.appServer.sendRequest('thread/delete', { threadId: task.threadId })
      } catch {
        // Thread may already be gone; task folder is already removed.
      }
    }
    get().removeTask(task.id, task.sourceName)
    if (get().selectedTaskId === task.id) {
      set({ selectedTaskId: null })
      useReviewPanelStore.getState().destroyReviewPanel()
    }
  },

  removeTask(taskId: string, sourceName: string) {
    set((state) => ({
      tasks: state.tasks.filter(
        (t) => !(t.id === taskId && t.sourceName === sourceName)
      )
    }))
  },

  selectTask(taskId: string | null) {
    set({ selectedTaskId: taskId })
  },

  setFilterSource(filter: SourceFilter) {
    set({ filterSource: filter })
  },

  upsertTask(task: AutomationTask) {
    set((state) => {
      const idx = state.tasks.findIndex(
        (t) => t.id === task.id && t.sourceName === task.sourceName
      )
      if (idx >= 0) {
        const updated = [...state.tasks]
        updated[idx] = { ...updated[idx], ...task }
        return { tasks: updated }
      }
      return { tasks: [task, ...state.tasks] }
    })
  }
}))
