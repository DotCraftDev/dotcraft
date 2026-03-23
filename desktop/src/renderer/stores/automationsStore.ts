import { create } from 'zustand'

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

  fetchTasks(): Promise<void>
  createTask(title: string, description: string, workflowTemplate?: string): Promise<void>
  approveTask(taskId: string, sourceName: string): Promise<void>
  rejectTask(taskId: string, sourceName: string, reason?: string): Promise<void>
  selectTask(taskId: string | null): void
  setFilterSource(filter: SourceFilter): void
  upsertTask(task: AutomationTask): void
}

export const useAutomationsStore = create<AutomationsState>((set, get) => ({
  tasks: [],
  loading: false,
  error: null,
  selectedTaskId: null,
  filterSource: 'all',

  async fetchTasks() {
    set({ loading: true, error: null })
    try {
      const result = (await window.api.appServer.sendRequest('automation/task/list', {})) as {
        tasks?: AutomationTask[]
      }
      set({ tasks: result.tasks ?? [], loading: false })
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : String(e)
      set({ error: msg, loading: false })
    }
  },

  async createTask(title: string, description: string, workflowTemplate?: string) {
    const params: Record<string, unknown> = { title, description }
    if (workflowTemplate) params.workflowTemplate = workflowTemplate
    await window.api.appServer.sendRequest('automation/task/create', params)
    await get().fetchTasks()
  },

  async approveTask(taskId: string, sourceName: string) {
    await window.api.appServer.sendRequest('automation/task/approve', { taskId, sourceName })
  },

  async rejectTask(taskId: string, sourceName: string, reason?: string) {
    const params: Record<string, unknown> = { taskId, sourceName }
    if (reason) params.reason = reason
    await window.api.appServer.sendRequest('automation/task/reject', params)
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
