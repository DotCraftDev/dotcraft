"use client";

import { defineToolCallRenderer } from "@copilotkitnext/react";
import { FileWriteCard } from "@/components/tools/FileWriteCard";
import { FileEditCard } from "@/components/tools/FileEditCard";
import { TerminalCard } from "@/components/tools/TerminalCard";
import { FileReadCard } from "@/components/tools/FileReadCard";
import { GrepFilesCard, FindFilesCard } from "@/components/tools/SearchCard";
import { WebSearchCard } from "@/components/tools/WebSearchCard";
import { WebFetchCard } from "@/components/tools/WebFetchCard";
import { ApprovalCardResult } from "@/components/ApprovalCard";
import type { ToolStatus } from "@/components/tools/ToolCardShell";

type StatusString = "inProgress" | "executing" | "complete";

function toToolStatus(s: StatusString): ToolStatus {
  if (s === "complete") return "complete";
  return s as ToolStatus;
}

// Normalize literal escape sequences (e.g. "\\n", "\\uXXXX") that the backend may send
// as two-character sequences due to double-serialization through the AG-UI SSE pipeline.
function normalizeResult(s: string | undefined): string | undefined {
  if (!s) return s;
  return s
    .replace(/\\r\\n/g, "\n")
    .replace(/\\n/g, "\n")
    .replace(/\\r/g, "\r")
    .replace(/\\t/g, "\t")
    // Decode surrogate pairs (\uD800-\uDBFF followed by \uDC00-\uDFFF) before BMP codepoints
    .replace(/\\u([dD][89aAbB][0-9a-fA-F]{2})\\u([dD][cCdDeEfF][0-9a-fA-F]{2})/g, (_, hi, lo) => {
      const h = parseInt(hi, 16);
      const l = parseInt(lo, 16);
      return String.fromCodePoint(((h - 0xD800) << 10) + (l - 0xDC00) + 0x10000);
    })
    // Decode remaining BMP Unicode escapes
    .replace(/\\u([0-9a-fA-F]{4})/g, (_, code) =>
      String.fromCharCode(parseInt(code, 16))
    );
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
            result={normalizeResult(result)}
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
            result={normalizeResult(result)}
          />
        );

      case "Exec":
        return (
          <TerminalCard
            status={s}
            command={args?.command as string | undefined}
            workingDir={args?.workingDir as string | undefined}
            result={normalizeResult(result)}
          />
        );

      case "ReadFile":
        return (
          <FileReadCard
            status={s}
            path={args?.path as string | undefined}
            offset={args?.offset as number | undefined}
            limit={args?.limit as number | undefined}
            result={normalizeResult(result)}
          />
        );

      case "GrepFiles":
        return (
          <GrepFilesCard
            status={s}
            pattern={args?.pattern as string | undefined}
            path={args?.path as string | undefined}
            include={args?.include as string | undefined}
            result={normalizeResult(result)}
          />
        );

      case "FindFiles":
        return (
          <FindFilesCard
            status={s}
            pattern={args?.pattern as string | undefined}
            path={args?.path as string | undefined}
            result={normalizeResult(result)}
          />
        );

      case "WebSearch":
        // Result is JSON — do NOT apply normalizeResult (it would corrupt JSON escape sequences)
        return (
          <WebSearchCard
            status={s}
            query={args?.query as string | undefined}
            maxResults={args?.maxResults as number | undefined}
            result={result}
          />
        );

      case "WebFetch":
        // Result is JSON — do NOT apply normalizeResult
        return (
          <WebFetchCard
            status={s}
            url={args?.url as string | undefined}
            extractMode={args?.extractMode as string | undefined}
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
