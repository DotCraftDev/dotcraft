"use client";

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

export function Nav({ onMenuToggle, menuOpen }: NavProps) {
  const dashboardUrl = getDashboardUrl();

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
    </nav>
  );
}
