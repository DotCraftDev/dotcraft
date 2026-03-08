"use client";

import { useEffect } from "react";

function isAbortRejection(reason: unknown): boolean {
  if (reason instanceof Error) {
    if (reason.name === "AbortError") return true;
    const msg = reason.message?.toLowerCase() ?? "";
    if (msg.includes("abort") || msg.includes("signal is aborted")) return true;
  }
  return false;
}

/**
 * Suppresses unhandled promise rejections for AbortError (e.g. from CopilotChat
 * effect cleanup when switching threads) so they don't log or crash the app.
 */
export function AbortRejectionHandler() {
  useEffect(() => {
    const handle = (event: PromiseRejectionEvent) => {
      if (isAbortRejection(event.reason)) {
        event.preventDefault();
        event.stopPropagation();
      }
    };
    window.addEventListener("unhandledrejection", handle);
    return () => window.removeEventListener("unhandledrejection", handle);
  }, []);
  return null;
}
