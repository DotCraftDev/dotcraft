"use client";

import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
} from "react";
import {
  loadThreads,
  saveThreads,
  loadLastThreadId,
  saveLastThreadId,
  generateThreadId,
  clearMessages,
  type ThreadEntry,
} from "@/lib/threadStorage";

type ThreadsContextValue = {
  threads: ThreadEntry[];
  currentThreadId: string;
  setCurrentThreadId: (id: string) => void;
  addThread: () => string;
  deleteThread: (id: string) => void;
  renameThread: (id: string, title: string) => void;
};

const ThreadsContext = createContext<ThreadsContextValue | null>(null);

export function ThreadsProvider({ children }: { children: React.ReactNode }) {
  const [threads, setThreadsState] = useState<ThreadEntry[]>([]);
  const [currentThreadId, setCurrentIdState] = useState<string>("dotcraft-1");
  const [hydrated, setHydrated] = useState(false);

  useEffect(() => {
    const stored = loadThreads();
    if (stored.length > 0) {
      setThreadsState(stored);
      const last = loadLastThreadId();
      const valid = last && stored.some((t) => t.id === last);
      setCurrentIdState(valid ? last! : stored[0].id);
    } else {
      const defaultId = "dotcraft-1";
      const initial: ThreadEntry[] = [
        { id: defaultId, title: "Chat 1", createdAt: Date.now() },
      ];
      saveThreads(initial);
      saveLastThreadId(defaultId);
      setThreadsState(initial);
      setCurrentIdState(defaultId);
    }
    setHydrated(true);
  }, []);

  const setThreads = useCallback((next: ThreadEntry[] | ((prev: ThreadEntry[]) => ThreadEntry[])) => {
    setThreadsState((prev) => {
      const nextList = typeof next === "function" ? next(prev) : next;
      saveThreads(nextList);
      return nextList;
    });
  }, []);

  const setCurrentThreadId = useCallback((id: string) => {
    setCurrentIdState(id);
    saveLastThreadId(id);
  }, []);

  const addThread = useCallback((): string => {
    const id = generateThreadId();
    setThreadsState((prev) => {
      const entry: ThreadEntry = {
        id,
        title: `Chat ${prev.length + 1}`,
        createdAt: Date.now(),
      };
      const next = [...prev, entry];
      saveThreads(next);
      return next;
    });
    setCurrentIdState(id);
    saveLastThreadId(id);
    return id;
  }, [setCurrentThreadId]);

  const deleteThread = useCallback(
    (id: string) => {
      clearMessages(id);
      const remaining = threads.filter((t) => t.id !== id);
      let nextList: ThreadEntry[];
      let nextCurrent: string;
      if (remaining.length === 0) {
        nextCurrent = generateThreadId();
        nextList = [
          { id: nextCurrent, title: "Chat 1", createdAt: Date.now() },
        ];
      } else {
        nextList = remaining;
        nextCurrent =
          currentThreadId === id ? remaining[0].id : currentThreadId;
      }
      saveThreads(nextList);
      saveLastThreadId(nextCurrent);
      setThreadsState(nextList);
      setCurrentIdState(nextCurrent);
    },
    [threads, currentThreadId]
  );

  const renameThread = useCallback((id: string, title: string) => {
    setThreadsState((prev) => {
      const next = prev.map((t) => (t.id === id ? { ...t, title } : t));
      saveThreads(next);
      return next;
    });
  }, []);

  const value = useMemo<ThreadsContextValue>(
    () => ({
      threads,
      currentThreadId,
      setCurrentThreadId,
      addThread,
      deleteThread,
      renameThread,
    }),
    [
      threads,
      currentThreadId,
      setCurrentThreadId,
      addThread,
      deleteThread,
      renameThread,
    ]
  );

  if (!hydrated) {
    return (
      <div className="flex h-screen items-center justify-center text-slate-500">
        Loading…
      </div>
    );
  }

  return (
    <ThreadsContext.Provider value={value}>{children}</ThreadsContext.Provider>
  );
}

export function useThreads(): ThreadsContextValue {
  const ctx = useContext(ThreadsContext);
  if (!ctx) throw new Error("useThreads must be used within ThreadsProvider");
  return ctx;
}
