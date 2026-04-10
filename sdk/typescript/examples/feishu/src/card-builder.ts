import {
  DECISION_ACCEPT,
  DECISION_ACCEPT_FOR_SESSION,
  DECISION_CANCEL,
  DECISION_DECLINE,
} from "dotcraft-wire";
import { chunkMarkdown, normalizeMarkdownForFeishu, summarizeApprovalOperation } from "./formatting.js";

export function buildReplyCards(replyText: string): Record<string, unknown>[] {
  const chunks = chunkMarkdown(replyText);
  return chunks.map((chunk, index) => ({
    config: {
      wide_screen_mode: true,
      update_multi: true,
    },
    header: {
      title: {
        tag: "plain_text",
        content: chunks.length > 1 ? `DotCraft Reply (${index + 1}/${chunks.length})` : "DotCraft Reply",
      },
      template: "blue",
    },
    elements: [
      {
        tag: "div",
        text: {
          tag: "lark_md",
          content: normalizeMarkdownForFeishu(chunk),
        },
      },
    ],
  }));
}

export function buildApprovalCard(params: {
  requestId: string;
  approvalType: string;
  operation: string;
  target: string;
  reason: string;
  timeoutSeconds: number;
}): Record<string, unknown> {
  const summary = summarizeApprovalOperation(params.approvalType, params.operation, params.target);
  const reasonBlock = params.reason ? `\nReason: ${params.reason}` : "";

  return {
    config: {
      wide_screen_mode: true,
      update_multi: true,
    },
    header: {
      title: {
        tag: "plain_text",
        content: "Approval Required",
      },
      template: "orange",
    },
    elements: [
      {
        tag: "div",
        text: {
          tag: "lark_md",
          content:
            `DotCraft needs approval before continuing.\n\n${summary}${reasonBlock}\n\n` +
            `Timeout: ${params.timeoutSeconds}s`,
        },
      },
      {
        tag: "action",
        actions: [
          buildApprovalButton("Approve", "primary", params.requestId, DECISION_ACCEPT),
          buildApprovalButton("Approve Session", "default", params.requestId, DECISION_ACCEPT_FOR_SESSION),
          buildApprovalButton("Decline", "danger", params.requestId, DECISION_DECLINE),
          buildApprovalButton("Cancel", "default", params.requestId, DECISION_CANCEL),
        ],
      },
    ],
  };
}

export function buildErrorCard(title: string, message: string): Record<string, unknown> {
  return {
    config: {
      wide_screen_mode: true,
      update_multi: true,
    },
    header: {
      title: {
        tag: "plain_text",
        content: title,
      },
      template: "red",
    },
    elements: [
      {
        tag: "div",
        text: {
          tag: "lark_md",
          content: normalizeMarkdownForFeishu(message),
        },
      },
    ],
  };
}

function buildApprovalButton(
  label: string,
  type: "default" | "primary" | "danger",
  requestId: string,
  decision: string,
): Record<string, unknown> {
  return {
    tag: "button",
    text: {
      tag: "plain_text",
      content: label,
    },
    type,
    value: {
      kind: "approval",
      requestId,
      decision,
    },
  };
}
