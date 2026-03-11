"use client";

import { useHumanInTheLoop } from "@copilotkitnext/react";
import {
  ApprovalCardPending,
  ApprovalCardAutoApproved,
  ApprovalCardResult,
} from "@/components/ApprovalCard";

interface ApprovalRequest {
  approval_id: string;
  function_name: string;
  function_arguments?: Record<string, unknown>;
  message?: string;
}

// Must satisfy Record<string, unknown> for useHumanInTheLoop constraint
type ApprovalArgs = Record<string, unknown> & {
  request: ApprovalRequest;
};

// Module-level set — lives for the duration of the browser session (until page reload).
// Tracks function names the user has approved for the entire session ("Allow All").
const sessionAllowed = new Set<string>();

/**
 * Registers the request_approval frontend tool.
 * When the backend emits a request_approval tool call, CopilotKit renders
 * ApprovalCardPending and waits for the user to approve or reject.
 * The respond() callback sends back { approval_id, approved } as the tool result.
 *
 * If the user clicks "Allow All", the function_name is recorded in sessionAllowed.
 * Subsequent requests for the same function_name are auto-approved via
 * ApprovalCardAutoApproved (which calls respond() inside useEffect).
 */
export function useApprovalAction() {
  useHumanInTheLoop<ApprovalArgs>({
    name: "request_approval",
    description: "Request user approval for a sensitive operation",
    render: ({ args, status, respond }) => {
      const req = args.request as ApprovalRequest | undefined;
      const safeReq: ApprovalRequest = req ?? { approval_id: "", function_name: "..." };

      if (status === "executing") {
        // respond is always defined when status is "executing" per ReactHumanInTheLoop types
        // eslint-disable-next-line @typescript-eslint/no-non-null-assertion
        const respondFn = respond!;

        // Auto-approve without prompting if the user already allowed this function for the session
        if (sessionAllowed.has(safeReq.function_name)) {
          return ApprovalCardAutoApproved({ request: safeReq, respond: respondFn });
        }

        return ApprovalCardPending({
          request: safeReq,
          respond: respondFn,
          onAllowAll: () => {
            sessionAllowed.add(safeReq.function_name);
          },
        });
      }
      return ApprovalCardResult({ request: safeReq });
    },
  });
}
