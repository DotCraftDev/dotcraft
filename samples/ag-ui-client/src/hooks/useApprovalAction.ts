"use client";

import { useHumanInTheLoop } from "@copilotkitnext/react";
import { ApprovalCardPending, ApprovalCardResult } from "@/components/ApprovalCard";

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

/**
 * Registers the request_approval frontend tool.
 * When the backend emits a request_approval tool call, CopilotKit renders
 * ApprovalCardPending and waits for the user to approve or reject.
 * The respond() callback sends back { approval_id, approved } as the tool result.
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
        return ApprovalCardPending({ request: safeReq, respond: respond! });
      }
      return ApprovalCardResult({ request: safeReq });
    },
  });
}
