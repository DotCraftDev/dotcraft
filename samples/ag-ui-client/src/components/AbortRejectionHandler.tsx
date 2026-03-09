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
 * Suppresses unhandled promise rejections AND console.error calls for AbortError
 * (e.g. from CopilotChat effect cleanup when switching threads). The console.error
 * patch is needed because React re-emits the error via console.error in dev mode
 * after catching it during effect cleanup, bypassing the unhandledrejection path.
 */
export function AbortRejectionHandler() {
  useEffect(() => {
    const handleRejection = (event: PromiseRejectionEvent) => {
      if (isAbortRejection(event.reason)) {
        event.preventDefault();
        event.stopPropagation();
      }
    };
    window.addEventListener("unhandledrejection", handleRejection);

    const originalConsoleError = console.error.bind(console);
    console.error = (...args: unknown[]) => {
      const first = args[0];
      if (typeof first === "string" && first.includes("signal is aborted")) return;
      if (first instanceof Error && isAbortRejection(first)) return;
      originalConsoleError(...args);
    };

    return () => {
      window.removeEventListener("unhandledrejection", handleRejection);
      console.error = originalConsoleError;
    };
  }, []);
  return null;
}
