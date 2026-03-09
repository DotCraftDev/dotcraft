"use client";

import { useEffect, useState, useCallback, useRef } from "react";

const POLL_INTERVAL_MS = 15_000;

export type BackendHealth = {
  connected: boolean | null; // null = initial check pending
  retry: () => void;
};

async function checkHealth(): Promise<boolean> {
  try {
    const res = await fetch("/api/dotcraft", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ method: "backend/health" }),
      // Short timeout so users don't wait long when the backend is down.
      signal: AbortSignal.timeout(5_000),
    });
    return res.ok;
  } catch {
    return false;
  }
}

export function useBackendHealth(): BackendHealth {
  const [connected, setConnected] = useState<boolean | null>(null);
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const run = useCallback(async () => {
    const ok = await checkHealth();
    setConnected(ok);
  }, []);

  const scheduleNext = useCallback(() => {
    timerRef.current = setTimeout(async () => {
      await run();
      scheduleNext();
    }, POLL_INTERVAL_MS);
  }, [run]);

  const retry = useCallback(() => {
    if (timerRef.current !== null) {
      clearTimeout(timerRef.current);
      timerRef.current = null;
    }
    run().then(scheduleNext);
  }, [run, scheduleNext]);

  useEffect(() => {
    run().then(scheduleNext);
    return () => {
      if (timerRef.current !== null) clearTimeout(timerRef.current);
    };
  }, [run, scheduleNext]);

  return { connected, retry };
}
