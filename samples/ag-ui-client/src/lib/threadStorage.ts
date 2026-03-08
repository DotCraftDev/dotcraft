const THREADS_KEY = "dotcraft-threads";
const LAST_THREAD_KEY = "dotcraft-last-thread";

export type ThreadEntry = {
  id: string;
  title?: string;
  createdAt: number;
};

export function loadThreads(): ThreadEntry[] {
  if (typeof window === "undefined") return [];
  try {
    const raw = localStorage.getItem(THREADS_KEY);
    if (!raw) return [];
    const parsed = JSON.parse(raw) as ThreadEntry[];
    return Array.isArray(parsed) ? parsed : [];
  } catch {
    return [];
  }
}

export function saveThreads(threads: ThreadEntry[]): void {
  if (typeof window === "undefined") return;
  try {
    localStorage.setItem(THREADS_KEY, JSON.stringify(threads));
  } catch {
    // ignore
  }
}

export function loadLastThreadId(): string | null {
  if (typeof window === "undefined") return null;
  return localStorage.getItem(LAST_THREAD_KEY);
}

export function saveLastThreadId(id: string): void {
  if (typeof window === "undefined") return;
  try {
    localStorage.setItem(LAST_THREAD_KEY, id);
  } catch {
    // ignore
  }
}

export function generateThreadId(): string {
  return `dotcraft-${Date.now().toString(36)}`;
}
