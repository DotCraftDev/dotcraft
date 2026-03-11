"use client";

import { ToolCardShell, SearchIcon, type ToolStatus } from "./ToolCardShell";

interface GrepFilesCardProps {
  status: ToolStatus;
  pattern?: string;
  path?: string;
  include?: string;
  result?: string;
}

interface FindFilesCardProps {
  status: ToolStatus;
  pattern?: string;
  path?: string;
  result?: string;
}

function highlightPattern(line: string, pattern: string): Array<{ text: string; highlight: boolean }> {
  if (!pattern) return [{ text: line, highlight: false }];
  try {
    const re = new RegExp(pattern, "gi");
    const parts: Array<{ text: string; highlight: boolean }> = [];
    let lastIdx = 0;
    let match: RegExpExecArray | null;
    while ((match = re.exec(line)) !== null) {
      if (match.index > lastIdx) parts.push({ text: line.slice(lastIdx, match.index), highlight: false });
      parts.push({ text: match[0], highlight: true });
      lastIdx = match.index + match[0].length;
      if (match[0].length === 0) { re.lastIndex++; }
    }
    if (lastIdx < line.length) parts.push({ text: line.slice(lastIdx), highlight: false });
    return parts;
  } catch {
    return [{ text: line, highlight: false }];
  }
}

function GrepResultLine({ line, pattern }: { line: string; pattern: string }) {
  const parts = highlightPattern(line, pattern);
  return (
    <div className="font-mono text-xs px-3 py-0.5 text-slate-700 dark:text-slate-300 whitespace-pre-wrap break-all hover:bg-slate-100 dark:hover:bg-slate-800">
      {parts.map((p, i) =>
        p.highlight ? (
          <mark key={i} className="bg-yellow-200 dark:bg-yellow-700/60 text-inherit rounded-sm px-0.5">
            {p.text}
          </mark>
        ) : (
          <span key={i}>{p.text}</span>
        )
      )}
    </div>
  );
}

export function GrepFilesCard({ status, pattern, path, include, result }: GrepFilesCardProps) {
  const lines = result?.split("\n").filter(Boolean) ?? [];
  const title = pattern ? `grep: ${pattern}` : "GrepFiles";
  const badge = (include ?? path) && (
    <span className="rounded px-1.5 py-0.5 text-xs bg-slate-200 dark:bg-slate-700 text-slate-600 dark:text-slate-400 font-mono">
      {include ?? path}
    </span>
  );

  return (
    <ToolCardShell
      icon={<SearchIcon />}
      title={title}
      badge={badge || undefined}
      status={status}
      collapsible={lines.length > 20}
      defaultCollapsed={lines.length > 20}
    >
      {lines.length > 0 ? (
        <div className="divide-y divide-slate-100 dark:divide-slate-800 max-h-72 overflow-y-auto">
          {lines.map((line, i) => (
            <GrepResultLine key={i} line={line} pattern={pattern ?? ""} />
          ))}
        </div>
      ) : (
        <p className="px-3 py-2 text-xs text-slate-400 italic">
          {status === "complete" ? "No matches found." : "Searching…"}
        </p>
      )}
    </ToolCardShell>
  );
}

export function FindFilesCard({ status, pattern, path, result }: FindFilesCardProps) {
  const lines = result?.split("\n").filter(Boolean) ?? [];
  const title = pattern ? `find: ${pattern}` : "FindFiles";

  return (
    <ToolCardShell
      icon={<SearchIcon />}
      title={title}
      badge={
        path ? (
          <span className="rounded px-1.5 py-0.5 text-xs bg-slate-200 dark:bg-slate-700 text-slate-600 dark:text-slate-400 font-mono">
            {path}
          </span>
        ) : undefined
      }
      status={status}
      collapsible={lines.length > 20}
      defaultCollapsed={lines.length > 20}
    >
      {lines.length > 0 ? (
        <ul className="max-h-64 overflow-y-auto divide-y divide-slate-100 dark:divide-slate-800">
          {lines.map((line, i) => {
            const isDir = /[/\\]$/.test(line.trim());
            return (
              <li key={i} className="flex items-center gap-2 px-3 py-0.5 text-xs font-mono text-slate-700 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800">
                <span className="shrink-0">{isDir ? "📁" : "📄"}</span>
                <span className="truncate">{line.trim()}</span>
              </li>
            );
          })}
        </ul>
      ) : (
        <p className="px-3 py-2 text-xs text-slate-400 italic">
          {status === "complete" ? "No files found." : "Searching…"}
        </p>
      )}
    </ToolCardShell>
  );
}
