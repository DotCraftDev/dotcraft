"use client";

import { useEffect, useRef } from "react";
import { useAgent } from "@copilotkitnext/react";
import { loadMessages, saveMessages } from "@/lib/threadStorage";

/**
 * Persists the active agent's messages to localStorage keyed by threadId.
 *
 * Save strategy: whenever agent messages change and are non-empty, they are
 * written to localStorage. On a thread switch, the effect fires before
 * CopilotKit's connectAgent (which runs inside the child CopilotChat effect),
 * so we also do an eager save of the old thread's messages at that point.
 *
 * Restore strategy: after connectAgent calls agent.setMessages([]), the
 * onMessagesChanged subscription fires with an empty array. At that moment
 * we reload the cached messages for the new thread and call agent.setMessages
 * to restore them. A flag prevents the restore itself from triggering another save.
 */
export function useMessagePersistence(threadId: string): void {
  const { agent } = useAgent();
  const prevThreadIdRef = useRef<string>(threadId);
  const isRestoringRef = useRef(false);
  // Tracks whether we have already attempted restoration for this threadId,
  // preventing duplicate restores when multiple empty-message events fire.
  const restoredForRef = useRef<string | null>(null);

  useEffect(() => {
    const prevThreadId = prevThreadIdRef.current;

    // Eagerly save the previous thread's messages before connectAgent clears them.
    // This runs before CopilotChat's effect (parent effects fire first in React),
    // so agent.messages still contains the old thread's conversation.
    if (prevThreadId !== threadId && agent.messages.length > 0) {
      saveMessages(prevThreadId, agent.messages);
    }

    prevThreadIdRef.current = threadId;
    // Reset restoration tracking whenever we enter a new thread scope.
    restoredForRef.current = null;

    const subscription = agent.subscribe({
      onMessagesChanged: () => {
        if (isRestoringRef.current) return;

        const messages = agent.messages;

        if (messages.length === 0) {
          // Empty messages signal that connectAgent just cleared the state.
          // Attempt to restore cached messages for this thread (once per switch).
          if (restoredForRef.current !== threadId) {
            restoredForRef.current = threadId;
            const cached = loadMessages(threadId);
            if (cached.length > 0) {
              isRestoringRef.current = true;
              // eslint-disable-next-line @typescript-eslint/no-explicit-any
              agent.setMessages(cached as any);
              isRestoringRef.current = false;
            }
          }
        } else {
          // Non-empty messages — persist the latest state.
          saveMessages(threadId, messages);
        }
      },
    });

    return () => subscription.unsubscribe();
  }, [threadId, agent]);
}
