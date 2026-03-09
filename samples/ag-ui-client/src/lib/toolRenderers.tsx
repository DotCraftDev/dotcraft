"use client";

import { defineToolCallRenderer } from "@copilotkitnext/react";

const wildcardRenderer = defineToolCallRenderer({
  name: "*",
  render: ({ name, args, status, result }) => (
    <div className="my-2 rounded-lg border border-slate-200 bg-slate-50 p-3 text-sm text-slate-700 shadow-sm dark:border-slate-600 dark:bg-slate-800 dark:text-slate-300">
      <strong className="block text-slate-900 dark:text-slate-100">
        {name}
      </strong>
      <div className="mt-2 space-y-1 text-xs">
        <div>
          <span className="font-medium text-slate-500 dark:text-slate-400">
            Status:{" "}
          </span>
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
              {typeof result === "string"
                ? result
                : JSON.stringify(result, null, 2)}
            </pre>
          </details>
        )}
      </div>
    </div>
  ),
});

export const toolRenderers = [wildcardRenderer];
