"use client";

import { useThreads } from "@/contexts/ThreadsContext";
import { useRef, useEffect, useState } from "react";

export function ThreadList() {
  const { threads, currentThreadId, setCurrentThreadId, deleteThread, renameThread } =
    useThreads();
  const [open, setOpen] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    function handleClickOutside(e: MouseEvent) {
      if (ref.current && !ref.current.contains(e.target as Node)) {
        setOpen(false);
        setEditingId(null);
      }
    }
    document.addEventListener("mousedown", handleClickOutside);
    return () => document.removeEventListener("mousedown", handleClickOutside);
  }, []);

  const currentTitle =
    threads.find((t) => t.id === currentThreadId)?.title ?? currentThreadId;

  const sortedThreads = [...threads].sort(
    (a, b) => b.createdAt - a.createdAt
  );

  return (
    <div className="relative" ref={ref}>
      <button
        type="button"
        onClick={() => setOpen((o) => !o)}
        className="flex items-center gap-2 rounded-md border border-slate-200 bg-white px-3 py-2 text-left text-sm font-medium text-slate-800 hover:bg-slate-50 dark:border-slate-600 dark:bg-slate-800 dark:text-slate-200 dark:hover:bg-slate-700"
        aria-expanded={open}
        aria-haspopup="listbox"
      >
        <span className="min-w-0 truncate max-w-[180px]">{currentTitle}</span>
        <svg
          className="h-4 w-4 shrink-0 text-slate-500"
          fill="none"
          stroke="currentColor"
          viewBox="0 0 24 24"
        >
          <path
            strokeLinecap="round"
            strokeLinejoin="round"
            strokeWidth={2}
            d="M19 9l-7 7-7-7"
          />
        </svg>
      </button>

      {open && (
        <ul
          className="absolute left-0 top-full z-10 mt-1 max-h-64 w-56 overflow-auto rounded-md border border-slate-200 bg-white py-1 shadow-lg dark:border-slate-600 dark:bg-slate-800"
          role="listbox"
        >
          {threads.length === 0 ? (
            <li className="px-3 py-4 text-center text-sm text-slate-500 dark:text-slate-400">
              No threads yet. Use &quot;New chat&quot; to start.
            </li>
          ) : (
            sortedThreads.map((t) => (
              <li
                key={t.id}
                role="option"
                aria-selected={t.id === currentThreadId}
                className="group flex items-center justify-between gap-2 px-3 py-2 text-sm"
              >
                {editingId === t.id ? (
                  <input
                    type="text"
                    defaultValue={t.title ?? t.id}
                    className="min-w-0 flex-1 rounded border border-slate-200 bg-white px-2 py-1 text-slate-800 dark:border-slate-600 dark:bg-slate-700 dark:text-slate-100"
                    onKeyDown={(e) => {
                      if (e.key === "Enter") {
                        const v = (e.target as HTMLInputElement).value.trim();
                        if (v) renameThread(t.id, v);
                        setEditingId(null);
                      }
                      if (e.key === "Escape") setEditingId(null);
                    }}
                    onBlur={(e) => {
                      const v = e.target.value.trim();
                      if (v) renameThread(t.id, v);
                      setEditingId(null);
                    }}
                    autoFocus
                  />
                ) : (
                  <>
                    <button
                      type="button"
                      onClick={() => {
                        setCurrentThreadId(t.id);
                        setOpen(false);
                      }}
                      className={`min-w-0 flex-1 truncate text-left hover:bg-slate-100 dark:hover:bg-slate-700 ${
                        t.id === currentThreadId
                          ? "font-medium text-slate-900 dark:text-slate-100"
                          : "text-slate-700 dark:text-slate-300"
                      }`}
                    >
                      {t.title ?? t.id}
                    </button>
                    <div className="flex shrink-0 gap-1 opacity-0 group-hover:opacity-100">
                      <button
                        type="button"
                        onClick={() => setEditingId(t.id)}
                        className="rounded p-1 text-slate-400 hover:bg-slate-200 hover:text-slate-600 dark:hover:bg-slate-600 dark:hover:text-slate-300"
                        title="Rename"
                        aria-label="Rename thread"
                      >
                        <svg className="h-3.5 w-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15.232 5.232l3.536 3.536m-2.036-5.036a2.5 2.5 0 113.536 3.536L6.5 21.036H3v-3.572L16.732 3.732z" />
                        </svg>
                      </button>
                      <button
                        type="button"
                        onClick={() => deleteThread(t.id)}
                        className="rounded p-1 text-slate-400 hover:bg-red-100 hover:text-red-600 dark:hover:bg-red-900/30 dark:hover:text-red-400"
                        title="Delete"
                        aria-label="Delete thread"
                      >
                        <svg className="h-3.5 w-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                        </svg>
                      </button>
                    </div>
                  </>
                )}
              </li>
            ))
          )}
        </ul>
      )}
    </div>
  );
}
