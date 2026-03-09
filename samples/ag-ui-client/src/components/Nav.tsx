"use client";

import { useTheme } from "@/hooks/useTheme";
import { useLocale, type Locale } from "@/lib/i18n";

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
  const { isDark, toggle: toggleTheme } = useTheme();
  const { t, locale, setLocale } = useLocale();

  function toggleLocale() {
    const next: Locale = locale === "zh" ? "en" : "zh";
    setLocale(next);
  }

  return (
    <nav className="flex items-center gap-3 border-b border-slate-200 bg-slate-50 px-4 py-3 dark:border-slate-700 dark:bg-slate-900">
      {onMenuToggle && (
        <button
          type="button"
          onClick={onMenuToggle}
          aria-label={menuOpen ? t("closeSidebar") : t("openSidebar")}
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
      {/* Locale toggle — ml-auto pushes controls to the right */}
      <button
        type="button"
        onClick={toggleLocale}
        title={t("switchLocale")}
        className="ml-auto min-w-[2.5rem] rounded-md px-2 py-1 text-xs font-medium text-slate-500 hover:bg-slate-200 hover:text-slate-700 dark:text-slate-400 dark:hover:bg-slate-700 dark:hover:text-slate-200"
      >
        {t("switchLocale")}
      </button>
      {/* Theme toggle */}
      <button
        type="button"
        onClick={toggleTheme}
        aria-label={isDark ? t("switchToLight") : t("switchToDark")}
        title={isDark ? t("switchToLight") : t("switchToDark")}
        className="rounded-md p-1.5 text-slate-500 hover:bg-slate-200 hover:text-slate-700 dark:text-slate-400 dark:hover:bg-slate-700 dark:hover:text-slate-200"
      >
        {isDark ? <SunIcon /> : <MoonIcon />}
      </button>
    </nav>
  );
}
