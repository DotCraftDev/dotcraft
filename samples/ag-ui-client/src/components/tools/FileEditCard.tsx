"use client";

import { ToolCardShell, EditIcon, type ToolStatus } from "./ToolCardShell";

interface FileEditCardProps {
  status: ToolStatus;
  path?: string;
  oldText?: string;
  newText?: string;
  startLine?: number;
  endLine?: number;
  result?: string;
}

function DiffLine({ prefix, text, className }: { prefix: string; text: string; className: string }) {
  return (
    <div className={"flex gap-1 px-3 py-0.5 font-mono text-xs " + className}>
      <span className="select-none shrink-0 w-4 text-center opacity-60">{prefix}</span>
      <span className="whitespace-pre-wrap break-all">{text}</span>
    </div>
  );
}

export function FileEditCard({ status, path, oldText, newText, startLine, endLine, result }: FileEditCardProps) {
  const hasContent = oldText !== undefined || newText !== undefined;
  const rangeLabel = startLine != null ? `L${startLine}${endLine != null ? `–${endLine}` : ""}` : undefined;

  return (
    <ToolCardShell
      icon={<EditIcon />}
      title={path ?? "edit_file"}
      badge={rangeLabel ? (
        <span className="rounded px-1.5 py-0.5 text-xs bg-slate-200 dark:bg-slate-700 text-slate-600 dark:text-slate-400 font-mono">
          {rangeLabel}
        </span>
      ) : undefined}
      status={status}
      collapsible={hasContent}
      defaultCollapsed={false}
    >
      {hasContent ? (
        <div className="overflow-x-auto max-h-72 overflow-y-auto divide-y divide-slate-200 dark:divide-slate-700">
          {oldText?.split("\n").map((line, i) => (
            <DiffLine
              key={`old-${i}`}
              prefix="-"
              text={line}
              className="bg-red-50 text-red-800 dark:bg-red-950/30 dark:text-red-300"
            />
          ))}
          {newText?.split("\n").map((line, i) => (
            <DiffLine
              key={`new-${i}`}
              prefix="+"
              text={line}
              className="bg-emerald-50 text-emerald-800 dark:bg-emerald-950/30 dark:text-emerald-300"
            />
          ))}
        </div>
      ) : (
        <p className="px-3 py-2 text-xs text-slate-400 italic">Preparing edit…</p>
      )}
      {result && status === "complete" && (
        <div className="px-3 py-1.5 text-xs text-emerald-600 dark:text-emerald-400 border-t border-slate-200 dark:border-slate-700 bg-emerald-50 dark:bg-emerald-950/30">
          {result}
        </div>
      )}
    </ToolCardShell>
  );
}
