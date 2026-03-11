"use client";

import { ToolCardShell, GlobeIcon, ExternalLinkIcon, type ToolStatus } from "./ToolCardShell";

interface WebFetchCardProps {
  status: ToolStatus;
  url?: string;
  extractMode?: string;
  result?: string;
}

interface FetchResultJson {
  url?: string;
  finalUrl?: string;
  status?: number;
  extractor?: string;
  truncated?: boolean;
  length?: number;
  text?: string;
  error?: string;
}

function tryParseResult(raw: string): FetchResultJson | null {
  try {
    let parsed: unknown = JSON.parse(raw);
    // The AG-UI SDK double-serializes string tool results; unwrap if needed.
    if (typeof parsed === "string") {
      parsed = JSON.parse(parsed);
    }
    return parsed as FetchResultJson;
  } catch {
    return null;
  }
}

function extractDomain(url: string): string {
  try {
    return new URL(url).hostname;
  } catch {
    return url;
  }
}

function StatusBadge({ code }: { code: number }) {
  const ok = code >= 200 && code < 300;
  return (
    <span
      className={
        "inline-flex items-center rounded px-1.5 py-0.5 text-xs font-mono font-medium " +
        (ok
          ? "bg-emerald-100 text-emerald-700 dark:bg-emerald-900/40 dark:text-emerald-400"
          : "bg-red-100 text-red-700 dark:bg-red-900/40 dark:text-red-400")
      }
    >
      {code}
    </span>
  );
}

export function WebFetchCard({ status, url: argUrl, extractMode, result }: WebFetchCardProps) {
  const parsed = result ? tryParseResult(result) : null;

  // Determine the effective URL to display
  const effectiveUrl = parsed?.finalUrl ?? parsed?.url ?? argUrl ?? "";
  const domain = effectiveUrl ? extractDomain(effectiveUrl) : "WebFetch";
  const title = domain || "WebFetch";

  const httpStatus = parsed?.status;
  const contentLength = parsed?.length;
  const isError = !!parsed?.error;

  return (
    <ToolCardShell
      icon={<GlobeIcon />}
      title={title}
      status={status}
      collapsible={false}
    >
      <div className="px-3 py-2 space-y-1.5">
        {/* Clickable URL row */}
        {effectiveUrl ? (
          <a
            href={effectiveUrl}
            target="_blank"
            rel="noopener noreferrer"
            className="inline-flex items-center gap-1.5 text-xs text-blue-600 dark:text-blue-400 hover:underline break-all"
          >
            <span>{effectiveUrl}</span>
            <ExternalLinkIcon className="shrink-0" />
          </a>
        ) : (
          <span className="text-xs text-slate-400 italic">URL unknown</span>
        )}

        {/* Metadata / error row */}
        {isError ? (
          <p className="text-xs text-red-600 dark:text-red-400">{parsed!.error}</p>
        ) : (parsed || status === "complete") ? (
          <div className="flex items-center gap-2 flex-wrap">
            {httpStatus !== undefined && <StatusBadge code={httpStatus} />}
            {extractMode && (
              <span className="text-xs text-slate-500 dark:text-slate-400 font-mono">
                {extractMode}
              </span>
            )}
            {contentLength !== undefined && (
              <span className="text-xs text-slate-500 dark:text-slate-400">
                {contentLength.toLocaleString()} chars
              </span>
            )}
            {parsed?.truncated && (
              <span className="text-xs text-amber-600 dark:text-amber-400">truncated</span>
            )}
          </div>
        ) : null}

        {/* In-progress placeholder */}
        {status !== "complete" && !isError && !parsed && (
          <p className="text-xs text-slate-400 italic">Fetching…</p>
        )}
      </div>
    </ToolCardShell>
  );
}
