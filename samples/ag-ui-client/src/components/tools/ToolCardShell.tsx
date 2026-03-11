"use client";

import { useState, type ReactNode } from "react";

export type ToolStatus = "inProgress" | "executing" | "complete" | "error";

interface ToolCardShellProps {
  icon: ReactNode;
  title: string;
  badge?: ReactNode;
  status: ToolStatus;
  collapsible?: boolean;
  defaultCollapsed?: boolean;
  children: ReactNode;
}

function StatusDot({ status }: { status: ToolStatus }) {
  if (status === "inProgress" || status === "executing") {
    return (
      <span className="inline-block h-2 w-2 rounded-full bg-amber-400 animate-pulse" />
    );
  }
  if (status === "complete") {
    return (
      <span className="inline-block h-2 w-2 rounded-full bg-emerald-500" />
    );
  }
  return <span className="inline-block h-2 w-2 rounded-full bg-red-500" />;
}

export function ToolCardShell({
  icon,
  title,
  badge,
  status,
  collapsible = false,
  defaultCollapsed = false,
  children,
}: ToolCardShellProps) {
  const [collapsed, setCollapsed] = useState(defaultCollapsed);

  return (
    <div className="my-2 rounded-lg border border-slate-200 bg-slate-50 text-sm text-slate-700 shadow-sm dark:border-slate-700 dark:bg-slate-900 dark:text-slate-300 overflow-hidden">
      {/* Header */}
      <div
        className={
          "flex items-center gap-2 px-3 py-2 border-b border-slate-200 dark:border-slate-700 " +
          (collapsible ? "cursor-pointer select-none hover:bg-slate-100 dark:hover:bg-slate-800" : "")
        }
        onClick={collapsible ? () => setCollapsed((v) => !v) : undefined}
      >
        <span className="text-slate-500 dark:text-slate-400 shrink-0">{icon}</span>
        <span className="font-medium text-slate-900 dark:text-slate-100 truncate flex-1">{title}</span>
        {badge && <span className="shrink-0">{badge}</span>}
        <StatusDot status={status} />
        {collapsible && (
          <ChevronIcon collapsed={collapsed} />
        )}
      </div>

      {/* Body */}
      {!collapsed && <div>{children}</div>}
    </div>
  );
}

function ChevronIcon({ collapsed }: { collapsed: boolean }) {
  return (
    <svg
      className={"h-3.5 w-3.5 text-slate-400 transition-transform " + (collapsed ? "" : "rotate-180")}
      viewBox="0 0 16 16"
      fill="none"
      stroke="currentColor"
      strokeWidth={2}
    >
      <path d="M4 6l4 4 4-4" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}

export function FileIcon() {
  return (
    <svg className="h-3.5 w-3.5" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth={1.5}>
      <path d="M9 2H4a1 1 0 0 0-1 1v10a1 1 0 0 0 1 1h8a1 1 0 0 0 1-1V6L9 2z" strokeLinejoin="round" />
      <path d="M9 2v4h4" strokeLinejoin="round" />
    </svg>
  );
}

export function TerminalIcon() {
  return (
    <svg className="h-3.5 w-3.5" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth={1.5}>
      <rect x="1" y="2" width="14" height="12" rx="1.5" />
      <path d="M4 6l2.5 2L4 10" strokeLinecap="round" strokeLinejoin="round" />
      <path d="M8.5 10h3" strokeLinecap="round" />
    </svg>
  );
}

export function SearchIcon() {
  return (
    <svg className="h-3.5 w-3.5" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth={1.5}>
      <circle cx="6.5" cy="6.5" r="4" />
      <path d="M11 11l3 3" strokeLinecap="round" />
    </svg>
  );
}

export function EditIcon() {
  return (
    <svg className="h-3.5 w-3.5" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth={1.5}>
      <path d="M11.5 2.5l2 2L5 13H3v-2L11.5 2.5z" strokeLinejoin="round" />
    </svg>
  );
}

export function ShieldIcon() {
  return (
    <svg className="h-3.5 w-3.5" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth={1.5}>
      <path d="M8 2L3 4v4c0 3 2.5 5.5 5 6.5C11 13.5 13 11 13 8V4L8 2z" strokeLinejoin="round" />
    </svg>
  );
}

export function GlobeIcon() {
  return (
    <svg className="h-3.5 w-3.5" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth={1.5}>
      <circle cx="8" cy="8" r="6" />
      <path d="M2 8h12M8 2c-2 2-3 4-3 6s1 4 3 6M8 2c2 2 3 4 3 6s-1 4-3 6" strokeLinecap="round" />
    </svg>
  );
}

export function ExternalLinkIcon({ className }: { className?: string }) {
  return (
    <svg
      className={"h-3 w-3 shrink-0 " + (className ?? "")}
      viewBox="0 0 16 16"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.5}
    >
      <path d="M6 3H3a1 1 0 0 0-1 1v9a1 1 0 0 0 1 1h9a1 1 0 0 0 1-1v-3" strokeLinecap="round" strokeLinejoin="round" />
      <path d="M9 2h5v5M14 2L8 8" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}
