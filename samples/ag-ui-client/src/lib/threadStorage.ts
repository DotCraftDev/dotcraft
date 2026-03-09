const THREADS_KEY = "dotcraft-threads";
const LAST_THREAD_KEY = "dotcraft-last-thread";
const MESSAGES_KEY_PREFIX = "dotcraft-messages-";

const MAX_MESSAGES = 200;
const MAX_RESULT_BYTES = 2048;

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

function truncateMessageResults(messages: unknown[]): unknown[] {
  return messages.map((msg) => {
    if (
      msg == null ||
      typeof msg !== "object" ||
      (msg as Record<string, unknown>).role !== "tool"
    ) {
      return msg;
    }
    const m = msg as Record<string, unknown>;
    const content = m.content;
    if (typeof content === "string" && content.length > MAX_RESULT_BYTES) {
      return { ...m, content: content.slice(0, MAX_RESULT_BYTES) + "\n…[truncated]" };
    }
    return msg;
  });
}

export function loadMessages(threadId: string): unknown[] {
  if (typeof window === "undefined") return [];
  try {
    const raw = localStorage.getItem(MESSAGES_KEY_PREFIX + threadId);
    if (!raw) return [];
    const parsed = JSON.parse(raw);
    return Array.isArray(parsed) ? parsed : [];
  } catch {
    return [];
  }
}

export function saveMessages(threadId: string, messages: unknown[]): void {
  if (typeof window === "undefined") return;
  try {
    const capped = messages.slice(-MAX_MESSAGES);
    const truncated = truncateMessageResults(capped);
    localStorage.setItem(MESSAGES_KEY_PREFIX + threadId, JSON.stringify(truncated));
  } catch {
    // ignore QuotaExceededError and similar
  }
}

export function clearMessages(threadId: string): void {
  if (typeof window === "undefined") return;
  try {
    localStorage.removeItem(MESSAGES_KEY_PREFIX + threadId);
  } catch {
    // ignore
  }
}
