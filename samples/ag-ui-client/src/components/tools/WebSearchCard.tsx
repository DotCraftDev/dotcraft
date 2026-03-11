"use client";

import { ToolCardShell, SearchIcon, ExternalLinkIcon, type ToolStatus } from "./ToolCardShell";

interface WebSearchCardProps {
  status: ToolStatus;
  query?: string;
  maxResults?: number;
  result?: string;
}

interface SearchResultItem {
  title: string;
  url: string;
  snippet?: string;
  author?: string;
  publishedDate?: string;
}

interface SearchOutput {
  query?: string;
  provider?: string;
  results?: SearchResultItem[];
  // Error / no-results shapes
  error?: string;
  message?: string;
}

function tryParseOutput(raw: string): SearchOutput | null {
  try {
    let parsed: unknown = JSON.parse(raw);
    // The AG-UI SDK double-serializes string tool results; unwrap if needed.
    if (typeof parsed === "string") {
      parsed = JSON.parse(parsed);
    }
    return parsed as SearchOutput;
  } catch {
    return null;
  }
}

function formatDate(iso: string): string {
  try {
    return new Date(iso).toLocaleDateString(undefined, { year: "numeric", month: "short", day: "numeric" });
  } catch {
    return iso;
  }
}

function ResultRow({ item, index }: { item: SearchResultItem; index: number }) {
  let displayUrl: string;
  try {
    const u = new URL(item.url);
    displayUrl = u.hostname + (u.pathname !== "/" ? u.pathname : "");
  } catch {
    displayUrl = item.url;
  }

  return (
    <div className="px-3 py-2 hover:bg-slate-100 dark:hover:bg-slate-800 transition-colors">
      <div className="flex items-start gap-2">
        <span className="text-xs text-slate-400 dark:text-slate-600 shrink-0 mt-0.5 w-4 text-right font-mono select-none">
          {index + 1}.
        </span>
        <div className="min-w-0 flex-1 space-y-0.5">
          {/* Title */}
          <p className="text-sm font-medium text-slate-800 dark:text-slate-200 truncate">
            {item.title}
          </p>

          {/* Clickable URL */}
          <a
            href={item.url}
            target="_blank"
            rel="noopener noreferrer"
            className="inline-flex items-center gap-1 text-xs text-blue-600 dark:text-blue-400 hover:underline max-w-full"
          >
            <span className="truncate">{displayUrl}</span>
            <ExternalLinkIcon />
          </a>

          {/* Snippet */}
          {item.snippet && (
            <p className="text-xs text-slate-500 dark:text-slate-400 line-clamp-2">
              {item.snippet}
            </p>
          )}

          {/* Optional metadata: author / published date */}
          {(item.author || item.publishedDate) && (
            <p className="text-xs text-slate-400 dark:text-slate-600">
              {[
                item.author,
                item.publishedDate ? formatDate(item.publishedDate) : undefined,
              ]
                .filter(Boolean)
                .join(" · ")}
            </p>
          )}
        </div>
      </div>
    </div>
  );
}

export function WebSearchCard({ status, query, result }: WebSearchCardProps) {
  const parsed = result ? tryParseOutput(result) : null;
  const results = parsed?.results;
  const hasResults = results && results.length > 0;

  // Use query from args, fall back to what the backend echoed back
  const displayQuery = query ?? parsed?.query;
  const title = displayQuery ? `"${displayQuery}"` : "WebSearch";
  const collapsible = (results?.length ?? 0) > 5;

  return (
    <ToolCardShell
      icon={<SearchIcon />}
      title={title}
      status={status}
      collapsible={collapsible}
      defaultCollapsed={false}
    >
      {hasResults ? (
        <div className="divide-y divide-slate-100 dark:divide-slate-800">
          {results.map((r, i) => (
            <ResultRow key={i} item={r} index={i} />
          ))}
        </div>
      ) : parsed?.error ? (
        <p className="px-3 py-2 text-xs text-red-600 dark:text-red-400">{parsed.error}</p>
      ) : parsed?.message ? (
        <p className="px-3 py-2 text-xs text-slate-400 italic">{parsed.message}</p>
      ) : result ? (
        // Non-JSON fallback (should not occur with unified backend)
        <pre className="px-3 py-2 text-xs font-mono text-slate-600 dark:text-slate-400 whitespace-pre-wrap break-all max-h-48 overflow-y-auto">
          {result}
        </pre>
      ) : (
        <p className="px-3 py-2 text-xs text-slate-400 italic">
          {status === "complete" ? "No results." : "Searching…"}
        </p>
      )}
    </ToolCardShell>
  );
}
