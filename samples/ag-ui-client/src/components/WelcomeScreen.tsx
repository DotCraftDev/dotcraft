"use client";

import type React from "react";

// Mirrors the WelcomeScreenProps shape from CopilotChatView without requiring
// the internal type export.
type WelcomeScreenProps = {
  input?: React.ReactElement;
  suggestionView?: React.ReactElement;
} & React.HTMLAttributes<HTMLDivElement>;

const FEATURES = [
  {
    icon: (
      <svg className="h-6 w-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path
          strokeLinecap="round"
          strokeLinejoin="round"
          strokeWidth={1.5}
          d="M3 7v10a2 2 0 002 2h14a2 2 0 002-2V9a2 2 0 00-2-2h-6l-2-2H5a2 2 0 00-2 2z"
        />
      </svg>
    ),
    title: "文件操作",
    description: "读写、创建、删除工作区文件",
  },
  {
    icon: (
      <svg className="h-6 w-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path
          strokeLinecap="round"
          strokeLinejoin="round"
          strokeWidth={1.5}
          d="M8 9l3 3-3 3m5 0h3M5 20h14a2 2 0 002-2V6a2 2 0 00-2-2H5a2 2 0 00-2 2v12a2 2 0 002 2z"
        />
      </svg>
    ),
    title: "Shell 执行",
    description: "在工作区内运行任意命令",
  },
  {
    icon: (
      <svg className="h-6 w-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path
          strokeLinecap="round"
          strokeLinejoin="round"
          strokeWidth={1.5}
          d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z"
        />
      </svg>
    ),
    title: "搜索",
    description: "全局内容搜索与文件查找",
  },
] as const;

export function WelcomeScreen({ input, suggestionView }: WelcomeScreenProps) {
  return (
    <div className="flex h-full flex-col items-center justify-center gap-8 px-6 py-12">
      {/* Logo / title */}
      <div className="text-center">
        <h1 className="text-3xl font-bold tracking-tight text-slate-900 dark:text-slate-50">
          DotCraft
        </h1>
        <p className="mt-2 text-base text-slate-500 dark:text-slate-400">
          你好！我是 DotCraft，有什么可以帮你的？
        </p>
      </div>

      {/* Feature cards */}
      <div className="flex w-full max-w-xl flex-wrap justify-center gap-3">
        {FEATURES.map((f) => (
          <div
            key={f.title}
            className="flex w-44 flex-col items-center gap-2 rounded-xl border border-slate-200 bg-white p-4 text-center shadow-sm dark:border-slate-700 dark:bg-slate-800"
          >
            <span className="text-slate-500 dark:text-slate-400">{f.icon}</span>
            <span className="text-sm font-medium text-slate-800 dark:text-slate-100">{f.title}</span>
            <span className="text-xs text-slate-500 dark:text-slate-400">{f.description}</span>
          </div>
        ))}
      </div>

      {/* Input + suggestions provided by CopilotKit */}
      <div className="w-full max-w-2xl space-y-2">
        {suggestionView}
        {input}
      </div>
    </div>
  );
}
