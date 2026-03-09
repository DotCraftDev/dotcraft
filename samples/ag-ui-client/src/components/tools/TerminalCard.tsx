"use client";

import { ToolCardShell, TerminalIcon, type ToolStatus } from "./ToolCardShell";

interface TerminalCardProps {
  status: ToolStatus;
  command?: string;
  workingDir?: string;
  result?: string;
}

export function TerminalCard({ status, command, workingDir, result }: TerminalCardProps) {
  return (
    <ToolCardShell
      icon={<TerminalIcon />}
      title={command ? `$ ${command}` : "exec"}
      badge={
        workingDir ? (
          <span className="rounded px-1.5 py-0.5 text-xs bg-slate-700 text-slate-300 font-mono">
            {workingDir}
          </span>
        ) : undefined
      }
      status={status}
      collapsible={!!result && result.split("\n").length > 15}
      defaultCollapsed={false}
    >
      {/* Always-dark terminal body */}
      <div className="bg-slate-900 text-slate-100 font-mono text-xs">
        {command && (
          <div className="px-3 py-2 border-b border-slate-700 flex items-center gap-2">
            <span className="text-emerald-400 shrink-0">$</span>
            <span className="whitespace-pre-wrap break-all">{command}</span>
          </div>
        )}
        {result ? (
          <pre className="px-3 py-2 overflow-x-auto whitespace-pre-wrap break-all max-h-64 overflow-y-auto text-slate-200">
            {result}
          </pre>
        ) : (
          status !== "complete" && (
            <div className="px-3 py-2 flex items-center gap-2 text-slate-400">
              <span className="inline-block h-2 w-2 rounded-full bg-amber-400 animate-pulse" />
              Running…
            </div>
          )
        )}
      </div>
    </ToolCardShell>
  );
}
