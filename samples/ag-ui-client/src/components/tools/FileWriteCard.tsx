"use client";

import { ToolCardShell, FileIcon, type ToolStatus } from "./ToolCardShell";

interface FileWriteCardProps {
  status: ToolStatus;
  path?: string;
  content?: string;
  result?: string;
}

export function FileWriteCard({ status, path, content, result }: FileWriteCardProps) {
  const lines = content?.split("\n") ?? [];
  const long = lines.length > 20;

  return (
    <ToolCardShell
      icon={<FileIcon />}
      title={path ?? "WriteFile"}
      status={status}
      collapsible={long}
      defaultCollapsed={long}
    >
      {content !== undefined ? (
        <pre className="overflow-x-auto p-3 text-xs font-mono text-slate-700 dark:text-slate-300 whitespace-pre-wrap break-all max-h-80 overflow-y-auto">
          {content}
        </pre>
      ) : (
        <p className="px-3 py-2 text-xs text-slate-400 italic">Preparing content…</p>
      )}
      {result && status === "complete" && (
        <div className="px-3 py-1.5 text-xs text-emerald-600 dark:text-emerald-400 border-t border-slate-200 dark:border-slate-700 bg-emerald-50 dark:bg-emerald-950/30">
          {result}
        </div>
      )}
    </ToolCardShell>
  );
}
