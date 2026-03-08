"use client";

import {
  CopilotKitProvider,
  CopilotSidebar,
  useConfigureSuggestions,
  useAgentContext,
} from "@copilotkitnext/react";
import type { ToolsMenuItem } from "@copilotkitnext/react";
import { useMemo } from "react";
import Link from "next/link";
import { Nav } from "@/components/Nav";
import { ThreadList } from "@/components/ThreadList";
import { AbortErrorBoundary } from "@/components/AbortErrorBoundary";
import { ThreadsProvider, useThreads } from "@/contexts/ThreadsContext";
import { toolRenderers } from "@/lib/toolRenderers";

const DEFAULT_DASHBOARD_URL = "http://localhost:5101/dashboard";

type SuggestionsAvailable =
  | "always"
  | "after-first-message"
  | "before-first-message"
  | "disabled";

function getSuggestionsAvailable(): SuggestionsAvailable {
  const v = process.env.NEXT_PUBLIC_SUGGESTIONS_AVAILABLE?.toLowerCase();
  if (
    v === "always" ||
    v === "after-first-message" ||
    v === "before-first-message" ||
    v === "disabled"
  ) {
    return v;
  }
  return "always";
}

function getDashboardUrl(): string {
  if (typeof window !== "undefined") {
    return process.env.NEXT_PUBLIC_DASHBOARD_URL ?? DEFAULT_DASHBOARD_URL;
  }
  return process.env.NEXT_PUBLIC_DASHBOARD_URL ?? DEFAULT_DASHBOARD_URL;
}

function SidebarChat() {
  const { currentThreadId, addThread } = useThreads();
  const suggestionsAvailable = getSuggestionsAvailable();

  useConfigureSuggestions(
    suggestionsAvailable === "disabled"
      ? null
      : {
          instructions:
            "Suggest helpful follow-up questions or tasks based on the conversation.",
          available: suggestionsAvailable,
        }
  );

  useAgentContext({
    description: "Current thread ID:",
    value: currentThreadId,
  });

  const toolsMenu = useMemo<(ToolsMenuItem | "-")[]>(
    () => [
      { label: "New chat", action: () => addThread() },
      "-",
      {
        label: "Open DotCraft Dashboard",
        action: () =>
          window.open(getDashboardUrl(), "_blank", "noopener,noreferrer"),
      },
      "-",
      {
        label: "Suggest: List workspace",
        action: () => {
          const textarea = document.querySelector<HTMLTextAreaElement>(
            'textarea[placeholder*="message" i], textarea[placeholder*="Type" i]'
          );
          if (textarea) {
            const value = "List the files in the workspace root.";
            const nativeSetter = Object.getOwnPropertyDescriptor(
              window.HTMLTextAreaElement.prototype,
              "value"
            )?.set;
            nativeSetter?.call(textarea, value);
            textarea.dispatchEvent(new Event("input", { bubbles: true }));
            textarea.focus();
          }
        },
      },
    ],
    [addThread]
  );

  return (
      <AbortErrorBoundary>
        <CopilotSidebar
          threadId={currentThreadId}
          defaultOpen
          width="50%"
          input={{ toolsMenu }}
        />
      </AbortErrorBoundary>
  );
}

function SidebarLayout() {
  const { addThread } = useThreads();

  return (
    <div className="relative min-h-screen bg-gradient-to-br from-slate-100 via-white to-slate-200 dark:from-slate-900 dark:via-slate-800 dark:to-slate-900">
      <Nav onNewChat={addThread} />
      <div className="flex items-center gap-3 border-b border-slate-200 bg-white/80 px-4 py-2 backdrop-blur dark:border-slate-700 dark:bg-slate-900/80">
        <span className="text-sm text-slate-500 dark:text-slate-400">
          Thread:
        </span>
        <ThreadList />
        <Link
          href="/"
          className="ml-auto text-sm text-slate-600 hover:text-slate-900 dark:text-slate-400 dark:hover:text-slate-100"
        >
          Full chat
        </Link>
      </div>
      <main className="mx-auto flex max-w-5xl flex-col gap-8 px-6 py-12">
        <section className="space-y-4">
          <h1 className="text-3xl font-semibold tracking-tight text-slate-900 dark:text-slate-100">
            DotCraft Sidebar
          </h1>
          <p className="max-w-2xl text-slate-600 dark:text-slate-300">
            Chat with DotCraft in a right-aligned sidebar. Use the thread
            dropdown to switch or create conversations. Open the sidebar with
            the button in the corner.
          </p>
        </section>
        <section className="grid gap-6 md:grid-cols-2">
          {[1, 2, 3, 4].map((i) => (
            <article
              key={i}
              className="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm transition hover:shadow-md dark:border-slate-600 dark:bg-slate-800"
            >
              <h2 className="text-lg font-medium text-slate-900 dark:text-slate-100">
                Card {i}
              </h2>
              <p className="mt-2 text-sm text-slate-600 dark:text-slate-300">
                Placeholder content. The sidebar pushes this layout instead of
                overlapping it.
              </p>
            </article>
          ))}
        </section>
      </main>
      <SidebarChat />
    </div>
  );
}

export default function SidebarPage() {
  return (
    <CopilotKitProvider
      runtimeUrl="/api/dotcraft"
      useSingleEndpoint
      renderToolCalls={toolRenderers}
      showDevConsole="auto"
    >
      <ThreadsProvider>
        <SidebarLayout />
      </ThreadsProvider>
    </CopilotKitProvider>
  );
}
