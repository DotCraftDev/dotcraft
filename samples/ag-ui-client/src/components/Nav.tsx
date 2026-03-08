"use client";

import Link from "next/link";

const DEFAULT_DASHBOARD_URL = "http://localhost:5101/dashboard";

function getDashboardUrl(): string {
  if (typeof window !== "undefined") {
    return process.env.NEXT_PUBLIC_DASHBOARD_URL ?? DEFAULT_DASHBOARD_URL;
  }
  return process.env.NEXT_PUBLIC_DASHBOARD_URL ?? DEFAULT_DASHBOARD_URL;
}

type NavProps = {
  onNewChat?: () => void;
};

export function Nav({ onNewChat }: NavProps) {
  const dashboardUrl = getDashboardUrl();

  return (
    <nav className="flex items-center gap-4 border-b border-slate-200 bg-slate-50 px-4 py-3 dark:border-slate-700 dark:bg-slate-900">
      <Link
        href="/"
        className="font-semibold text-slate-900 hover:text-slate-700 dark:text-slate-100 dark:hover:text-slate-300"
      >
        DotCraft Chat
      </Link>
      <Link
        href="/sidebar"
        className="text-sm text-slate-600 hover:text-slate-900 dark:text-slate-400 dark:hover:text-slate-100"
      >
        Sidebar
      </Link>
      {onNewChat && (
        <button
          type="button"
          onClick={onNewChat}
          className="rounded-md bg-slate-200 px-3 py-1.5 text-sm font-medium text-slate-800 hover:bg-slate-300 dark:bg-slate-700 dark:text-slate-200 dark:hover:bg-slate-600"
        >
          New chat
        </button>
      )}
      <a
        href={dashboardUrl}
        target="_blank"
        rel="noopener noreferrer"
        className="ml-auto text-sm text-slate-600 hover:text-slate-900 dark:text-slate-400 dark:hover:text-slate-100"
      >
        Dashboard
      </a>
    </nav>
  );
}
