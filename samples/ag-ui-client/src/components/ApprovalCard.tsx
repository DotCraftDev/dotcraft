"use client";

import { useEffect } from "react";
import { ToolCardShell, ShieldIcon } from "./tools/ToolCardShell";

interface ApprovalRequest {
  approval_id: string;
  function_name: string;
  function_arguments?: Record<string, unknown>;
  message?: string;
}

// Shown while in "executing" phase — user can approve or reject
interface ApprovalCardPendingProps {
  request: ApprovalRequest;
  respond: (result: unknown) => Promise<void>;
  onAllowAll: () => void;
}

// Shown after decision in "complete" phase
interface ApprovalCardResultProps {
  request: ApprovalRequest;
  result?: string;
}

// Shown briefly while auto-responding for a session-allowed function
interface ApprovalCardAutoProps {
  request: ApprovalRequest;
  respond: (result: unknown) => Promise<void>;
}

function ArgPreview({ name, args }: { name: string; args?: Record<string, unknown> }) {
  if (!args) return null;

  if (name === "WriteFile") {
    const path = args.path as string | undefined;
    const content = args.content as string | undefined;
    return (
      <div className="text-xs font-mono space-y-1">
        {path && <div><span className="text-slate-400">path: </span>{path}</div>}
        {content && (
          <pre className="mt-1 rounded bg-slate-100 dark:bg-slate-800 p-2 overflow-x-auto max-h-32 whitespace-pre-wrap break-all">
            {content}
          </pre>
        )}
      </div>
    );
  }

  if (name === "EditFile") {
    const path = args.path as string | undefined;
    const oldText = args.oldText as string | undefined;
    const newText = args.newText as string | undefined;
    return (
      <div className="text-xs font-mono space-y-1">
        {path && <div><span className="text-slate-400">path: </span>{path}</div>}
        {oldText && (
          <pre className="rounded bg-red-50 dark:bg-red-950/30 p-2 text-red-800 dark:text-red-300 overflow-x-auto max-h-24 whitespace-pre-wrap break-all">
            - {oldText}
          </pre>
        )}
        {newText && (
          <pre className="rounded bg-emerald-50 dark:bg-emerald-950/30 p-2 text-emerald-800 dark:text-emerald-300 overflow-x-auto max-h-24 whitespace-pre-wrap break-all">
            + {newText}
          </pre>
        )}
      </div>
    );
  }

  if (name === "Exec") {
    const command = args.command as string | undefined;
    const workingDir = args.workingDir as string | undefined;
    return (
      <div className="rounded bg-slate-900 text-slate-100 p-2 text-xs font-mono">
        {workingDir && <div className="text-slate-400 mb-1">{workingDir}</div>}
        <div><span className="text-emerald-400">$ </span>{command}</div>
      </div>
    );
  }

  // Generic fallback
  return (
    <pre className="text-xs font-mono rounded bg-slate-100 dark:bg-slate-800 p-2 overflow-x-auto max-h-32 whitespace-pre-wrap break-all">
      {JSON.stringify(args, null, 2)}
    </pre>
  );
}

export function ApprovalCardPending({ request, respond, onAllowAll }: ApprovalCardPendingProps) {
  const handleApprove = () =>
    respond({ approval_id: request.approval_id, approved: true });
  const handleReject = () =>
    respond({ approval_id: request.approval_id, approved: false });
  const handleAllowAll = () => {
    onAllowAll();
    void respond({ approval_id: request.approval_id, approved: true });
  };

  return (
    <ToolCardShell
      icon={<ShieldIcon />}
      title={request.message ?? `Approve: ${request.function_name}`}
      status="executing"
    >
      <div className="p-3 space-y-3">
        <ArgPreview name={request.function_name} args={request.function_arguments} />
        <div className="flex gap-2 pt-1">
          <button
            onClick={handleApprove}
            className="flex-1 rounded-md bg-emerald-600 hover:bg-emerald-700 active:bg-emerald-800 text-white text-xs font-medium py-1.5 px-3 transition-colors"
          >
            Approve
          </button>
          <button
            onClick={handleAllowAll}
            className="flex-1 rounded-md bg-blue-600 hover:bg-blue-700 active:bg-blue-800 text-white text-xs font-medium py-1.5 px-3 transition-colors"
            title={`Always approve ${request.function_name} for this session`}
          >
            Allow All
          </button>
          <button
            onClick={handleReject}
            className="flex-1 rounded-md bg-red-600 hover:bg-red-700 active:bg-red-800 text-white text-xs font-medium py-1.5 px-3 transition-colors"
          >
            Reject
          </button>
        </div>
        <p className="text-xs text-slate-400 dark:text-slate-500 text-center">
          &ldquo;Allow All&rdquo; — skip approval for <span className="font-medium">{request.function_name}</span> for the rest of this session
        </p>
      </div>
    </ToolCardShell>
  );
}

// Auto-approves in useEffect so respond() is not called during render.
export function ApprovalCardAutoApproved({ request, respond }: ApprovalCardAutoProps) {
  useEffect(() => {
    void respond({ approval_id: request.approval_id, approved: true });
  // respond identity is stable within a tool call invocation; approval_id identifies the call
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [request.approval_id]);

  return (
    <ToolCardShell
      icon={<ShieldIcon />}
      title={request.function_name}
      status="complete"
      badge={
        <span className="rounded px-2 py-0.5 text-xs bg-blue-100 dark:bg-blue-950/50 text-blue-700 dark:text-blue-400 font-medium">
          Auto-approved
        </span>
      }
    >
      <div className="px-3 py-2 text-xs text-slate-500 dark:text-slate-400">
        Automatically approved for this session.
      </div>
    </ToolCardShell>
  );
}

export function ApprovalCardResult({ request, result }: ApprovalCardResultProps) {
  let approved: boolean | null = null;
  if (result) {
    try {
      const parsed = JSON.parse(result) as { approved?: boolean };
      approved = parsed.approved ?? null;
    } catch {
      // ignore
    }
  }

  return (
    <ToolCardShell
      icon={<ShieldIcon />}
      title={request.function_name}
      status="complete"
      badge={
        approved === true ? (
          <span className="rounded px-2 py-0.5 text-xs bg-emerald-100 dark:bg-emerald-950/50 text-emerald-700 dark:text-emerald-400 font-medium">
            Approved
          </span>
        ) : approved === false ? (
          <span className="rounded px-2 py-0.5 text-xs bg-red-100 dark:bg-red-950/50 text-red-700 dark:text-red-400 font-medium">
            Rejected
          </span>
        ) : undefined
      }
    >
      <div className="px-3 py-2 text-xs text-slate-500 dark:text-slate-400">
        {approved === true
          ? "Operation approved."
          : approved === false
          ? "Operation rejected."
          : result ?? ""}
      </div>
    </ToolCardShell>
  );
}
