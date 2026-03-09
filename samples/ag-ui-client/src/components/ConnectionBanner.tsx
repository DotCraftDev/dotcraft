"use client";

import { useLocale } from "@/lib/i18n";
import type { BackendHealth } from "@/hooks/useBackendHealth";

type ConnectionBannerProps = Pick<BackendHealth, "connected" | "retry">;

export function ConnectionBanner({ connected, retry }: ConnectionBannerProps) {
  const { t } = useLocale();

  if (connected !== false) return null;

  return (
    <div
      role="alert"
      className="flex items-center gap-3 border-b border-red-300 bg-red-50 px-4 py-3 text-sm text-red-800 dark:border-red-800 dark:bg-red-950/60 dark:text-red-300"
    >
      {/* Warning icon */}
      <svg
        className="h-5 w-5 shrink-0"
        fill="none"
        stroke="currentColor"
        viewBox="0 0 24 24"
        aria-hidden="true"
      >
        <path
          strokeLinecap="round"
          strokeLinejoin="round"
          strokeWidth={2}
          d="M12 9v4m0 4h.01M10.29 3.86L1.82 18a2 2 0 001.71 3h16.94a2 2 0 001.71-3L13.71 3.86a2 2 0 00-3.42 0z"
        />
      </svg>
      <span className="flex-1">{t("connectionError")}</span>
      <button
        type="button"
        onClick={retry}
        className="rounded-md border border-red-300 bg-white px-3 py-1 text-xs font-medium text-red-700 hover:bg-red-50 dark:border-red-700 dark:bg-red-900/40 dark:text-red-300 dark:hover:bg-red-900/60"
      >
        {t("retry")}
      </button>
    </div>
  );
}
