"use client";

import {
  CopilotChat,
  CopilotKitProvider,
  useConfigureSuggestions,
  useAgentContext,
  CopilotChatConfigurationProvider,
} from "@copilotkitnext/react";
import type { ToolsMenuItem, CopilotChatLabels } from "@copilotkitnext/react";
import { useMemo, useState } from "react";
import { Nav } from "@/components/Nav";
import { ThreadPanel } from "@/components/ThreadPanel";
import { AbortErrorBoundary } from "@/components/AbortErrorBoundary";
import { ConnectionBanner } from "@/components/ConnectionBanner";
import { ThreadsProvider, useThreads } from "@/contexts/ThreadsContext";
import { LocaleProvider, useLocale } from "@/lib/i18n";
import { toolRenderers } from "@/lib/toolRenderers";
import { useMessagePersistence } from "@/hooks/useMessagePersistence";
import { useBackendHealth } from "@/hooks/useBackendHealth";
import { WelcomeScreen } from "@/components/WelcomeScreen";
import { useApprovalAction } from "@/hooks/useApprovalAction";

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


function ChatWithThreads() {
  const { currentThreadId, addThread } = useThreads();
  const [panelOpen, setPanelOpen] = useState(false);
  const { connected, retry } = useBackendHealth();
  const { t, locale } = useLocale();
  useMessagePersistence(currentThreadId);
  useApprovalAction();
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

  // Re-build labels when locale changes so CopilotKit UI updates immediately.
  const dotcraftLabels = useMemo<Partial<CopilotChatLabels>>(() => ({
    chatInputPlaceholder: t("chatPlaceholder"),
    assistantMessageToolbarCopyMessageLabel: t("copy"),
    assistantMessageToolbarThumbsUpLabel: t("helpful"),
    assistantMessageToolbarThumbsDownLabel: t("notHelpful"),
    assistantMessageToolbarRegenerateLabel: t("regenerate"),
    assistantMessageToolbarReadAloudLabel: t("readAloud"),
    assistantMessageToolbarCopyCodeLabel: t("copyCode"),
    assistantMessageToolbarCopyCodeCopiedLabel: t("copyCodeCopied"),
    userMessageToolbarCopyMessageLabel: t("copy"),
    userMessageToolbarEditMessageLabel: t("edit"),
    chatInputToolbarStartTranscribeButtonLabel: t("startTranscribe"),
    chatInputToolbarCancelTranscribeButtonLabel: t("cancelTranscribe"),
    chatInputToolbarFinishTranscribeButtonLabel: t("finishTranscribe"),
    chatInputToolbarAddButtonLabel: t("addAttachment"),
    chatInputToolbarToolsButtonLabel: t("toolsMenu"),
    chatToggleOpenLabel: t("chatToggleOpen"),
    chatToggleCloseLabel: t("chatToggleClose"),
    modalHeaderTitle: t("modalHeaderTitle"),
    welcomeMessageText: t("welcomeSubtitle"),
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }), [locale]); // re-compute when locale changes; t is stable within a locale

  const toolsMenu = useMemo<(ToolsMenuItem | "-")[]>(
    () => [
      {
        label: t("newChat"),
        action: () => addThread(),
      },
      "-",
      {
        label: t("suggestListFiles"),
        action: () => {
          const textarea = document.querySelector<HTMLTextAreaElement>(
            'textarea[placeholder*="message" i], textarea[placeholder*="Type" i], textarea[placeholder*="发送" i], textarea[placeholder*="Send" i]'
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
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [addThread, locale] // re-compute when locale changes
  );

  return (
    <div className="flex h-screen flex-col">
      <Nav onMenuToggle={() => setPanelOpen((v) => !v)} menuOpen={panelOpen} />
      <ConnectionBanner connected={connected} retry={retry} />
      <div className="flex min-h-0 flex-1">
        <ThreadPanel open={panelOpen} onClose={() => setPanelOpen(false)} />
        <main className="min-w-0 flex-1">
          <AbortErrorBoundary>
            <CopilotChatConfigurationProvider labels={dotcraftLabels}>
              <CopilotChat
                threadId={currentThreadId}
                input={{ toolsMenu }}
                // eslint-disable-next-line @typescript-eslint/no-explicit-any
                chatView={{ welcomeScreen: WelcomeScreen as any }}
              />
            </CopilotChatConfigurationProvider>
          </AbortErrorBoundary>
        </main>
      </div>
    </div>
  );
}

export default function DotCraftChatPage() {
  return (
    <CopilotKitProvider
      runtimeUrl="/api/dotcraft"
      useSingleEndpoint
      renderToolCalls={toolRenderers}
      showDevConsole="auto"
    >
      <LocaleProvider>
        <ThreadsProvider>
          <ChatWithThreads />
        </ThreadsProvider>
      </LocaleProvider>
    </CopilotKitProvider>
  );
}
