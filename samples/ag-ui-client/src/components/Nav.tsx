"use client";

import { useTheme } from "@/hooks/useTheme";

const DEFAULT_DASHBOARD_URL = "http://localhost:5101/dashboard";

function getDashboardUrl(): string {
  if (typeof window !== "undefined") {
    return process.env.NEXT_PUBLIC_DASHBOARD_URL ?? DEFAULT_DASHBOARD_URL;
  }
  return process.env.NEXT_PUBLIC_DASHBOARD_URL ?? DEFAULT_DASHBOARD_URL;
}

type NavProps = {
  onMenuToggle?: () => void;
  menuOpen?: boolean;
};

function SunIcon() {
  return (
    <svg className="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
        d="M12 3v1m0 16v1m9-9h-1M4 12H3m15.364-6.364l-.707.707M6.343 17.657l-.707.707M17.657 17.657l-.707-.707M6.343 6.343l-.707-.707M12 7a5 5 0 100 10A5 5 0 0012 7z" />
    </svg>
  );
}

function MoonIcon() {
  return (
    <svg className="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
        d="M21 12.79A9 9 0 1111.21 3 7 7 0 0021 12.79z" />
    </svg>
  );
}

export function Nav({ onMenuToggle, menuOpen }: NavProps) {
  const dashboardUrl = getDashboardUrl();
  const { isDark, toggle } = useTheme();

  return (
    <nav className="flex items-center gap-3 border-b border-slate-200 bg-slate-50 px-4 py-3 dark:border-slate-700 dark:bg-slate-900">
      {onMenuToggle && (
        <button
          type="button"
          onClick={onMenuToggle}
          aria-label={menuOpen ? "Close sidebar" : "Open sidebar"}
          className="rounded-md p-1.5 text-slate-500 hover:bg-slate-200 hover:text-slate-700 dark:text-slate-400 dark:hover:bg-slate-700 dark:hover:text-slate-200 lg:hidden"
        >
          {menuOpen ? (
            <svg className="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
            </svg>
          ) : (
            <svg className="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 6h16M4 12h16M4 18h16" />
            </svg>
          )}
        </button>
      )}
      <span className="font-semibold text-slate-900 dark:text-slate-100">
        DotCraft
      </span>
      <a
        href={dashboardUrl}
        target="_blank"
        rel="noopener noreferrer"
        className="ml-auto text-sm text-slate-600 hover:text-slate-900 dark:text-slate-400 dark:hover:text-slate-100"
      >
        Dashboard ↗
      </a>
      <button
        type="button"
        onClick={toggle}
        aria-label={isDark ? "切换到浅色模式" : "切换到深色模式"}
        title={isDark ? "切换到浅色模式" : "切换到深色模式"}
        className="rounded-md p-1.5 text-slate-500 hover:bg-slate-200 hover:text-slate-700 dark:text-slate-400 dark:hover:bg-slate-700 dark:hover:text-slate-200"
      >
        {isDark ? <SunIcon /> : <MoonIcon />}
      </button>
    </nav>
  );
}
