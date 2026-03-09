"use client";

import { defineToolCallRenderer } from "@copilotkitnext/react";
import { FileWriteCard } from "@/components/tools/FileWriteCard";
import { FileEditCard } from "@/components/tools/FileEditCard";
import { TerminalCard } from "@/components/tools/TerminalCard";
import { FileReadCard } from "@/components/tools/FileReadCard";
import { GrepFilesCard, FindFilesCard } from "@/components/tools/SearchCard";
import { ApprovalCardResult } from "@/components/ApprovalCard";
import type { ToolStatus } from "@/components/tools/ToolCardShell";

type StatusString = "inProgress" | "executing" | "complete";

function toToolStatus(s: StatusString): ToolStatus {
  if (s === "complete") return "complete";
  return s as ToolStatus;
}

// Single wildcard renderer that dispatches to per-tool card components.
// The request_approval interactive case is handled separately by useApprovalAction
// (useHumanInTheLoop), which registers its own renderer while the component is mounted.
// This wildcard covers history display and all other tools.
const wildcardRenderer = defineToolCallRenderer({
  name: "*",
  render: ({ name, args, status, result }) => {
    const s = toToolStatus(status as StatusString);

    switch (name) {
      case "WriteFile":
        return (
          <FileWriteCard
            status={s}
            path={args?.path as string | undefined}
            content={args?.content as string | undefined}
            result={result}
          />
        );

      case "EditFile":
        return (
          <FileEditCard
            status={s}
            path={args?.path as string | undefined}
            oldText={args?.oldText as string | undefined}
            newText={args?.newText as string | undefined}
            startLine={args?.startLine as number | undefined}
            endLine={args?.endLine as number | undefined}
            result={result}
          />
        );

      case "Exec":
        return (
          <TerminalCard
            status={s}
            command={args?.command as string | undefined}
            workingDir={args?.workingDir as string | undefined}
            result={result}
          />
        );

      case "ReadFile":
        return (
          <FileReadCard
            status={s}
            path={args?.path as string | undefined}
            offset={args?.offset as number | undefined}
            limit={args?.limit as number | undefined}
            result={result}
          />
        );

      case "GrepFiles":
        return (
          <GrepFilesCard
            status={s}
            pattern={args?.pattern as string | undefined}
            path={args?.path as string | undefined}
            include={args?.include as string | undefined}
            result={result}
          />
        );

      case "FindFiles":
        return (
          <FindFilesCard
            status={s}
            pattern={args?.pattern as string | undefined}
            path={args?.path as string | undefined}
            result={result}
          />
        );

      case "request_approval": {
        // History display — the interactive renderer is registered by useApprovalAction.
        const req = args?.request as
          | { approval_id: string; function_name: string }
          | undefined;
        return (
          <ApprovalCardResult
            request={req ?? { approval_id: "", function_name: name }}
            result={result}
          />
        );
      }

      default:
        return (
          <div className="my-2 rounded-lg border border-slate-200 bg-slate-50 p-3 text-sm text-slate-700 shadow-sm dark:border-slate-600 dark:bg-slate-800 dark:text-slate-300">
            <strong className="block text-slate-900 dark:text-slate-100">{name}</strong>
            <div className="mt-2 space-y-1 text-xs">
              <div>
                <span className="font-medium text-slate-500 dark:text-slate-400">Status: </span>
                <span>{status}</span>
              </div>
              {args && Object.keys(args).length > 0 && (
                <pre className="max-h-32 overflow-auto whitespace-pre-wrap rounded bg-slate-100 p-2 font-mono text-slate-600 dark:bg-slate-700 dark:text-slate-300">
                  {JSON.stringify(args, null, 2)}
                </pre>
              )}
              {result !== undefined && result !== null && status === "complete" && (
                <details className="mt-2">
                  <summary className="cursor-pointer font-medium text-slate-500 dark:text-slate-400">
                    Result
                  </summary>
                  <pre className="mt-1 max-h-40 overflow-auto whitespace-pre-wrap break-words rounded bg-slate-100 p-2 font-mono text-slate-600 dark:bg-slate-700 dark:text-slate-300">
                    {typeof result === "string" ? result : JSON.stringify(result, null, 2)}
                  </pre>
                </details>
              )}
            </div>
          </div>
        );
    }
  },
});

export const toolRenderers = [wildcardRenderer];
