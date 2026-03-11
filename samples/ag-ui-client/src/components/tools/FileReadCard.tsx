"use client";

import { ToolCardShell, FileIcon, type ToolStatus } from "./ToolCardShell";

interface FileReadCardProps {
  status: ToolStatus;
  path?: string;
  offset?: number;
  limit?: number;
  result?: string;
}

function isDirectoryListing(result: string): boolean {
  const lines = result.trim().split("\n");
  return lines.length > 1 && lines.some((l) => l.match(/^\s*(DIR|<DIR>|\[DIR\]|[drwx-]{10})/i));
}

function LineNumberedCode({ content }: { content: string }) {
  const lines = content.split("\n");
  return (
    <div className="overflow-x-auto max-h-72 overflow-y-auto">
      <table className="w-full border-collapse font-mono text-xs">
        <tbody>
          {lines.map((line, i) => (
            <tr key={i} className="hover:bg-slate-100 dark:hover:bg-slate-800">
              <td className="select-none w-10 pr-3 text-right text-slate-400 dark:text-slate-600 border-r border-slate-200 dark:border-slate-700 px-2 py-0">
                {i + 1}
              </td>
              <td className="pl-3 pr-3 py-0 whitespace-pre text-slate-700 dark:text-slate-300">
                {line || "\u00A0"}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function DirectoryListing({ content }: { content: string }) {
  const lines = content.trim().split("\n").filter(Boolean);
  return (
    <ul className="px-3 py-2 max-h-64 overflow-y-auto divide-y divide-slate-100 dark:divide-slate-800">
      {lines.map((line, i) => {
        const isDir = /[/\\]$/.test(line.trim()) || /\[DIR\]|<DIR>/i.test(line);
        return (
          <li key={i} className="flex items-center gap-2 py-0.5 text-xs font-mono text-slate-700 dark:text-slate-300">
            <span className="shrink-0">{isDir ? "📁" : "📄"}</span>
            <span className="truncate">{line.trim()}</span>
          </li>
        );
      })}
    </ul>
  );
}

export function FileReadCard({ status, path, offset, result }: FileReadCardProps) {
  const rangeLabel = offset != null ? `from L${offset}` : undefined;
  const content = result;
  const isDirList = content ? isDirectoryListing(content) : false;
  const long = (content?.split("\n").length ?? 0) > 20;

  return (
    <ToolCardShell
      icon={<FileIcon />}
      title={path ?? "ReadFile"}
      badge={rangeLabel ? (
        <span className="rounded px-1.5 py-0.5 text-xs bg-slate-200 dark:bg-slate-700 text-slate-600 dark:text-slate-400 font-mono">
          {rangeLabel}
        </span>
      ) : undefined}
      status={status}
      collapsible={long}
      defaultCollapsed={long}
    >
      {content ? (
        isDirList ? (
          <DirectoryListing content={content} />
        ) : (
          <LineNumberedCode content={content} />
        )
      ) : (
        <p className="px-3 py-2 text-xs text-slate-400 italic">
          {status === "complete" ? "No content returned." : "Reading file…"}
        </p>
      )}
    </ToolCardShell>
  );
}
