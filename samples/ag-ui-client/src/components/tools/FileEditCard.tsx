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

// --- LCS-based line diff ---

type DiffOp =
  | { type: "keep"; line: string; oldLn: number; newLn: number }
  | { type: "remove"; line: string; oldLn: number }
  | { type: "add"; line: string; newLn: number };

function computeLCS(a: string[], b: string[]): number[][] {
  const m = a.length;
  const n = b.length;
  // Use a flat Uint32Array for performance with large files
  const dp = new Uint32Array((m + 1) * (n + 1));
  for (let i = m - 1; i >= 0; i--) {
    for (let j = n - 1; j >= 0; j--) {
      if (a[i] === b[j]) {
        dp[i * (n + 1) + j] = dp[(i + 1) * (n + 1) + (j + 1)] + 1;
      } else {
        const down = dp[(i + 1) * (n + 1) + j];
        const right = dp[i * (n + 1) + (j + 1)];
        dp[i * (n + 1) + j] = down > right ? down : right;
      }
    }
  }
  // Convert to 2D for readability in backtrack
  const table: number[][] = [];
  for (let i = 0; i <= m; i++) {
    table.push(Array.from(dp.subarray(i * (n + 1), (i + 1) * (n + 1))));
  }
  return table;
}

function computeDiff(oldLines: string[], newLines: string[]): DiffOp[] {
  const lcs = computeLCS(oldLines, newLines);
  const ops: DiffOp[] = [];
  let i = 0;
  let j = 0;
  let oldLn = 1;
  let newLn = 1;

  while (i < oldLines.length || j < newLines.length) {
    if (
      i < oldLines.length &&
      j < newLines.length &&
      oldLines[i] === newLines[j]
    ) {
      ops.push({ type: "keep", line: oldLines[i], oldLn: oldLn++, newLn: newLn++ });
      i++;
      j++;
    } else if (
      j < newLines.length &&
      (i >= oldLines.length || lcs[i + 1][j] >= lcs[i][j + 1])
    ) {
      ops.push({ type: "add", line: newLines[j], newLn: newLn++ });
      j++;
    } else {
      ops.push({ type: "remove", line: oldLines[i], oldLn: oldLn++ });
      i++;
    }
  }

  return ops;
}

// Build hunks: groups of changed lines with up to CONTEXT_LINES of surrounding context.
const CONTEXT_LINES = 3;

type Hunk = { ops: DiffOp[]; hasGapBefore: boolean };

function buildHunks(ops: DiffOp[]): Hunk[] {
  if (ops.length === 0) return [];

  // Find indices of changed lines
  const changedIdx = ops
    .map((op, idx) => (op.type !== "keep" ? idx : -1))
    .filter((idx) => idx !== -1);

  if (changedIdx.length === 0) return [];

  // Merge changed ranges + context into windows
  const windows: [number, number][] = [];
  let start = Math.max(0, changedIdx[0] - CONTEXT_LINES);
  let end = Math.min(ops.length - 1, changedIdx[0] + CONTEXT_LINES);

  for (let k = 1; k < changedIdx.length; k++) {
    const nextStart = Math.max(0, changedIdx[k] - CONTEXT_LINES);
    const nextEnd = Math.min(ops.length - 1, changedIdx[k] + CONTEXT_LINES);
    if (nextStart <= end + 1) {
      end = nextEnd;
    } else {
      windows.push([start, end]);
      start = nextStart;
      end = nextEnd;
    }
  }
  windows.push([start, end]);

  return windows.map(([s, e], winIdx) => ({
    ops: ops.slice(s, e + 1),
    hasGapBefore: s > 0 || winIdx > 0,
  }));
}

// --- Rendering ---

function DiffLineRow({ op }: { op: DiffOp }) {
  const oldLn = op.type !== "add" ? String(op.oldLn) : "";
  const newLn = op.type !== "remove" ? String(op.newLn) : "";

  const rowCls =
    op.type === "remove"
      ? "bg-red-50 dark:bg-red-950/30"
      : op.type === "add"
        ? "bg-emerald-50 dark:bg-emerald-950/30"
        : "hover:bg-slate-50 dark:hover:bg-slate-800/50";

  const prefixCls =
    op.type === "remove"
      ? "text-red-500 dark:text-red-400"
      : op.type === "add"
        ? "text-emerald-600 dark:text-emerald-400"
        : "text-slate-300 dark:text-slate-600";

  const textCls =
    op.type === "remove"
      ? "text-red-800 dark:text-red-300"
      : op.type === "add"
        ? "text-emerald-800 dark:text-emerald-300"
        : "text-slate-700 dark:text-slate-300";

  const prefix = op.type === "remove" ? "-" : op.type === "add" ? "+" : " ";

  return (
    <div className={`flex font-mono text-xs ${rowCls}`}>
      {/* Old line number */}
      <span className="select-none w-8 shrink-0 text-right pr-2 text-slate-400 dark:text-slate-600 border-r border-slate-200 dark:border-slate-700">
        {oldLn}
      </span>
      {/* New line number */}
      <span className="select-none w-8 shrink-0 text-right pr-2 text-slate-400 dark:text-slate-600 border-r border-slate-200 dark:border-slate-700">
        {newLn}
      </span>
      {/* +/- prefix */}
      <span className={`select-none w-5 shrink-0 text-center ${prefixCls}`}>
        {prefix}
      </span>
      {/* Line content */}
      <span className={`pl-1 pr-3 py-0.5 whitespace-pre-wrap break-all flex-1 ${textCls}`}>
        {op.line || "\u00A0"}
      </span>
    </div>
  );
}

function HunkSeparator({ label }: { label: string }) {
  return (
    <div className="flex font-mono text-xs bg-slate-100 dark:bg-slate-800 text-slate-500 dark:text-slate-400 px-3 py-0.5 border-y border-slate-200 dark:border-slate-700">
      {label}
    </div>
  );
}

function UnifiedDiff({ oldText, newText }: { oldText: string; newText: string }) {
  const oldLines = oldText.split("\n");
  const newLines = newText.split("\n");
  const ops = computeDiff(oldLines, newLines);
  const hunks = buildHunks(ops);

  if (hunks.length === 0) {
    return (
      <p className="px-3 py-2 text-xs text-slate-400 italic">No changes detected.</p>
    );
  }

  // Build hunk header labels using line numbers from the first/last op in each hunk
  function hunkLabel(hunk: Hunk): string {
    const first = hunk.ops[0];
    const last = hunk.ops[hunk.ops.length - 1];
    const oldStart = first.type !== "add" ? first.oldLn : (first as { newLn: number }).newLn;
    const newStart = first.type !== "remove" ? (first as { newLn?: number }).newLn ?? oldStart : oldStart;
    const oldEnd = last.type !== "add" ? last.oldLn : (last as { newLn: number }).newLn;
    const newEnd = last.type !== "remove" ? (last as { newLn?: number }).newLn ?? oldEnd : oldEnd;
    return `@@ -${oldStart},${oldEnd} +${newStart},${newEnd} @@`;
  }

  return (
    <div className="overflow-x-auto max-h-96 overflow-y-auto divide-y divide-slate-200 dark:divide-slate-700">
      {hunks.map((hunk, hi) => (
        <div key={hi}>
          {hunk.hasGapBefore && <HunkSeparator label={hunkLabel(hunk)} />}
          {hunk.ops.map((op, oi) => (
            <DiffLineRow key={oi} op={op} />
          ))}
        </div>
      ))}
    </div>
  );
}

export function FileEditCard({ status, path, oldText, newText, startLine, endLine, result }: FileEditCardProps) {
  const hasContent = oldText !== undefined || newText !== undefined;
  const rangeLabel = startLine != null ? `L${startLine}${endLine != null ? `–${endLine}` : ""}` : undefined;

  return (
    <ToolCardShell
      icon={<EditIcon />}
      title={path ?? "EditFile"}
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
        oldText !== undefined && newText !== undefined ? (
          <UnifiedDiff oldText={oldText} newText={newText} />
        ) : (
          // Only one side available — fallback to simple colored display
          <div className="overflow-x-auto max-h-72 overflow-y-auto divide-y divide-slate-200 dark:divide-slate-700">
            {oldText?.split("\n").map((line, i) => (
              <div key={`old-${i}`} className="flex gap-1 px-3 py-0.5 font-mono text-xs bg-red-50 text-red-800 dark:bg-red-950/30 dark:text-red-300">
                <span className="select-none shrink-0 w-4 text-center opacity-60">-</span>
                <span className="whitespace-pre-wrap break-all">{line}</span>
              </div>
            ))}
            {newText?.split("\n").map((line, i) => (
              <div key={`new-${i}`} className="flex gap-1 px-3 py-0.5 font-mono text-xs bg-emerald-50 text-emerald-800 dark:bg-emerald-950/30 dark:text-emerald-300">
                <span className="select-none shrink-0 w-4 text-center opacity-60">+</span>
                <span className="whitespace-pre-wrap break-all">{line}</span>
              </div>
            ))}
          </div>
        )
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
