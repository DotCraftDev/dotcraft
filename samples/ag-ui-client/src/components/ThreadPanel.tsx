"use client";

import { useRef, useEffect, useState } from "react";
import { useThreads } from "@/contexts/ThreadsContext";
import { useLocale } from "@/lib/i18n";

type ThreadPanelProps = {
  open: boolean;
  onClose: () => void;
};

export function ThreadPanel({ open, onClose }: ThreadPanelProps) {
  const { threads, currentThreadId, setCurrentThreadId, addThread, deleteThread, renameThread } =
    useThreads();
  const [editingId, setEditingId] = useState<string | null>(null);
  const overlayRef = useRef<HTMLDivElement>(null);
  const { t } = useLocale();

  const sortedThreads = [...threads].sort((a, b) => b.createdAt - a.createdAt);

  // Close panel on mobile when clicking the overlay backdrop
  useEffect(() => {
    function handleClickOutside(e: MouseEvent) {
      if (overlayRef.current && e.target === overlayRef.current) {
        onClose();
      }
    }
    document.addEventListener("mousedown", handleClickOutside);
    return () => document.removeEventListener("mousedown", handleClickOutside);
  }, [onClose]);

  function handleSelectThread(id: string) {
    setCurrentThreadId(id);
    onClose();
  }

  const panel = (
    <aside className="flex h-full w-64 shrink-0 flex-col border-r border-slate-200 bg-slate-50 dark:border-slate-700 dark:bg-slate-900">
      {/* New chat button */}
      <div className="p-3 border-b border-slate-200 dark:border-slate-700">
        <button
          type="button"
          onClick={() => { addThread(); onClose(); }}
          className="flex w-full items-center gap-2 rounded-md bg-slate-900 px-3 py-2 text-sm font-medium text-white hover:bg-slate-700 dark:bg-slate-700 dark:hover:bg-slate-600"
        >
          <svg className="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 4v16m8-8H4" />
          </svg>
          {t("newChat")}
        </button>
      </div>

      {/* Thread list */}
      <nav className="flex-1 overflow-y-auto py-2">
        {sortedThreads.length === 0 ? (
          <p className="px-4 py-6 text-center text-sm text-slate-400 dark:text-slate-500">
            {t("noChats")}
          </p>
        ) : (
          sortedThreads.map((thread) => {
            const isActive = thread.id === currentThreadId;
            return (
              <div
                key={thread.id}
                className={`group relative flex items-center gap-1 px-2 py-1 mx-1 rounded-md ${
                  isActive
                    ? "bg-slate-200 dark:bg-slate-700"
                    : "hover:bg-slate-100 dark:hover:bg-slate-800"
                }`}
              >
                {editingId === thread.id ? (
                  <input
                    type="text"
                    defaultValue={thread.title ?? thread.id}
                    className="min-w-0 flex-1 rounded border border-slate-300 bg-white px-2 py-0.5 text-sm text-slate-800 focus:outline-none dark:border-slate-600 dark:bg-slate-800 dark:text-slate-100"
                    onKeyDown={(e) => {
                      if (e.key === "Enter") {
                        const v = (e.target as HTMLInputElement).value.trim();
                        if (v) renameThread(thread.id, v);
                        setEditingId(null);
                      }
                      if (e.key === "Escape") setEditingId(null);
                    }}
                    onBlur={(e) => {
                      const v = e.target.value.trim();
                      if (v) renameThread(thread.id, v);
                      setEditingId(null);
                    }}
                    autoFocus
                    onClick={(e) => e.stopPropagation()}
                  />
                ) : (
                  <button
                    type="button"
                    onClick={() => handleSelectThread(thread.id)}
                    className={`min-w-0 flex-1 truncate py-1 text-left text-sm ${
                      isActive
                        ? "font-medium text-slate-900 dark:text-slate-100"
                        : "text-slate-700 dark:text-slate-300"
                    }`}
                  >
                    {thread.title ?? thread.id}
                  </button>
                )}

                {/* Action buttons — visible on hover */}
                {editingId !== thread.id && (
                  <div className="flex shrink-0 gap-0.5 opacity-0 transition-opacity group-hover:opacity-100">
                    <button
                      type="button"
                      onClick={(e) => { e.stopPropagation(); setEditingId(thread.id); }}
                      title={t("rename")}
                      className="rounded p-1 text-slate-400 hover:bg-slate-200 hover:text-slate-600 dark:hover:bg-slate-600 dark:hover:text-slate-300"
                    >
                      <svg className="h-3.5 w-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15.232 5.232l3.536 3.536m-2.036-5.036a2.5 2.5 0 113.536 3.536L6.5 21.036H3v-3.572L16.732 3.732z" />
                      </svg>
                    </button>
                    <button
                      type="button"
                      onClick={(e) => { e.stopPropagation(); deleteThread(thread.id); }}
                      title={t("delete")}
                      className="rounded p-1 text-slate-400 hover:bg-red-100 hover:text-red-600 dark:hover:bg-red-900/30 dark:hover:text-red-400"
                    >
                      <svg className="h-3.5 w-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                      </svg>
                    </button>
                  </div>
                )}
              </div>
            );
          })
        )}
      </nav>
    </aside>
  );

  return (
    <>
      {/* Desktop: always visible */}
      <div className="hidden lg:flex h-full">{panel}</div>

      {/* Mobile: slide-in overlay */}
      {open && (
        <div
          ref={overlayRef}
          className="fixed inset-0 z-40 bg-black/40 lg:hidden"
          aria-hidden="true"
        >
          <div className="h-full w-64 shadow-xl">{panel}</div>
        </div>
      )}
    </>
  );
}
