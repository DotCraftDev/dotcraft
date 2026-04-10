import {
  DECISION_ACCEPT,
  DECISION_ACCEPT_FOR_SESSION,
  DECISION_CANCEL,
  DECISION_DECLINE,
} from "dotcraft-wire";
import { chunkMarkdown, normalizeMarkdownForFeishu, summarizeApprovalOperation } from "./formatting.js";

export function buildReplyCards(replyText: string): Record<string, unknown>[] {
  const chunks = chunkMarkdown(replyText);
  return chunks.map((chunk, index) =>
    buildV2Card(
      chunks.length > 1 ? `DotCraft Reply (${index + 1}/${chunks.length})` : "DotCraft Reply",
      "blue",
      [
        {
          tag: "markdown",
          content: normalizeMarkdownForFeishu(chunk),
        },
      ],
    ),
  );
}

export function buildProgressCard(text: string): Record<string, unknown> {
  return buildV2Card("DotCraft", "turquoise", [
    {
      tag: "markdown",
      content: normalizeMarkdownForFeishu(text),
    },
  ]);
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

  return buildV2Card("Approval Required", "orange", [
    {
      tag: "markdown",
      content:
        `DotCraft needs approval before continuing.\n\n${summary}${reasonBlock}\n\n` +
        `Timeout: ${params.timeoutSeconds}s`,
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
  ]);
}

export function buildErrorCard(title: string, message: string): Record<string, unknown> {
  return buildV2Card(title, "red", [
    {
      tag: "markdown",
      content: normalizeMarkdownForFeishu(message),
    },
  ]);
}

function buildV2Card(
  title: string,
  template: string,
  bodyElements: Array<Record<string, unknown>>,
): Record<string, unknown> {
  return {
    schema: "2.0",
    config: {
      update_multi: true,
      width_mode: "fill",
    },
    header: {
      title: {
        tag: "plain_text",
        content: title,
      },
      template,
    },
    body: {
      elements: bodyElements,
    },
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
