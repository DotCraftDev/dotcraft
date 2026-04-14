import {
  readFile,
  stat,
} from "node:fs/promises";
import {
  basename,
  resolve,
} from "node:path";

import {
  ChannelAdapter,
  configureTextMergeDebug,
  DECISION_CANCEL,
  DECISION_DECLINE,
  DotCraftError,
  Thread,
  WebSocketTransport,
  mergeReplyTextFromDeltaAndSnapshot,
  textPart,
} from "dotcraft-wire";
import {
  buildApprovalCard,
  buildApprovalResolvedCard,
  buildApprovalTimeoutCard,
  buildErrorCard,
  buildFileCaptionCard,
  buildTranscriptCard,
} from "./card-builder.js";
import { createOrUpdateCard, sendReplyCards, sendSingleCard, updateCard } from "./card-sender.js";
import type { FeishuCardActionEvent, ParsedInboundMessage } from "./feishu-types.js";
import type { FeishuClient } from "./feishu-client.js";
import { errorMessage, logError, logInfo, logWarn, shortId } from "./logging.js";

export interface FeishuAdapterConfig {
  wsUrl: string;
  dotcraftToken?: string;
  approvalTimeoutMs: number;
  feishu: FeishuClient;
  /** Debug logging; pass `true` per flag to enable. */
  debug?: {
    adapterStream?: boolean;
    textMerge?: boolean;
  };
}

export class FeishuAdapter extends ChannelAdapter {
  private readonly feishu: FeishuClient;
  private readonly approvalTimeoutMs: number;
  private readonly threadContextMap = new Map<string, string>();
  private readonly turnTranscriptStates = new Map<
    string,
    {
      threadId: string;
      channelTarget: string;
      messageId: string;
      accumulatedText: string;
      isFinal: boolean;
    }
  >();
  private readonly activeTurnByThread = new Map<string, string>();
  private readonly activeTurnByChannelTarget = new Map<string, string>();
  private readonly approvalWaiters = new Map<
    string,
    {
      resolve: (decision: string) => void;
      timer: ReturnType<typeof setTimeout>;
      threadId: string;
      channelTarget: string;
      messageId: string;
      timeoutSeconds: number;
    }
  >();

  constructor(cfg: FeishuAdapterConfig) {
    const transport = new WebSocketTransport({
      url: cfg.wsUrl,
      token: cfg.dotcraftToken ?? "",
    });
    super(
      transport,
      "feishu",
      "dotcraft-feishu",
      "0.1.0",
      ["item/reasoning/delta", "subagent/progress", "item/usage/delta", "system/event", "plan/updated"],
      { debugStream: cfg.debug?.adapterStream },
    );
    this.feishu = cfg.feishu;
    this.approvalTimeoutMs = cfg.approvalTimeoutMs;
    configureTextMergeDebug(cfg.debug?.textMerge);
  }

  async onDeliver(target: string, content: string, _metadata: Record<string, unknown>): Promise<boolean> {
    logInfo("outbound.deliver.start", {
      target: shortId(target),
      contentChars: content.length,
    });
    try {
      await sendReplyCards(this.feishu, target, content);
      logInfo("outbound.deliver.success", {
        target: shortId(target),
      });
      return true;
    } catch (error) {
      logError("outbound.deliver.failed", {
        target: shortId(target),
        message: errorMessage(error),
      });
      return false;
    }
  }

  protected getDeliveryCapabilities(): Record<string, unknown> | null {
    return {
      structuredDelivery: true,
      media: {
        file: {
          maxBytes: 30 * 1024 * 1024,
          supportsHostPath: false,
          supportsUrl: false,
          supportsBase64: true,
          supportsCaption: true,
        },
      },
    };
  }

  protected override getChannelTools(): Record<string, unknown>[] | null {
    return [
      {
        name: "FeishuSendFileToCurrentChat",
        description: "Send a real file attachment to the current Feishu chat.",
        requiresChatContext: true,
        approval: {
          kind: "file",
          targetArgument: "filePath",
          operation: "read",
        },
        display: {
          icon: "\u{1F4CE}",
          title: "Send file to current Feishu chat",
        },
        inputSchema: {
          type: "object",
          properties: {
            filePath: { type: "string" },
            fileName: { type: "string" },
            caption: { type: "string" },
          },
          required: ["filePath"],
        },
      },
    ];
  }

  protected override async onSend(
    target: string,
    message: Record<string, unknown>,
    metadata: Record<string, unknown>,
  ): Promise<Record<string, unknown>> {
    const kind = String(message.kind ?? "");
    if (kind === "text") {
      return await super.onSend(target, message, metadata);
    }

    if (kind === "file") {
      const result = await this.deliverFileMessage(target, message, {
        source: "structured",
        metadata,
      });
      return result;
    }

    return {
      delivered: false,
      errorCode: "UnsupportedDeliveryKind",
      errorMessage: `Feishu example does not implement structured '${kind}' delivery yet.`,
    };
  }

  protected override async onToolCall(
    request: Record<string, unknown>,
  ): Promise<Record<string, unknown>> {
    const tool = String(request.tool ?? "");
    const args = (request.arguments as Record<string, unknown>) ?? {};
    const context = (request.context as Record<string, unknown>) ?? {};
    const target = String(context.channelContext ?? context.groupId ?? "");
    if (tool !== "FeishuSendFileToCurrentChat") {
      return {
        success: false,
        errorCode: "UnsupportedTool",
        errorMessage: `Unknown tool '${tool}'.`,
      };
    }

    if (!target) {
      return {
        success: false,
        errorCode: "MissingChatContext",
        errorMessage: "Current tool call does not contain a Feishu chat target.",
      };
    }

    const filePath = String(args.filePath ?? "");
    const fileName = String(args.fileName ?? "");
    const caption = String(args.caption ?? "");
    if (!filePath) {
      return {
        success: false,
        errorCode: "MissingFilePath",
        errorMessage: "Feishu file sending requires a filePath.",
      };
    }

    try {
      const resolvedPath = resolve(filePath);
      const fileStats = await stat(resolvedPath);
      if (!fileStats.isFile()) {
        return {
          success: false,
          errorCode: "InvalidFilePath",
          errorMessage: `Path '${resolvedPath}' is not a file.`,
        };
      }

      const data = await readFile(resolvedPath);
      const effectiveFileName = fileName || basename(resolvedPath);
      const sendResult = await this.feishu.sendFile(target, {
        fileName: effectiveFileName,
        data,
        mediaType: inferMediaType(effectiveFileName),
      });
      if (caption) {
        await this.sendCaptionCard(target, caption, {
          target,
          fileName: effectiveFileName,
          source: "tool",
        });
      }

      return {
        success: true,
        contentItems: [{ type: "text", text: `Sent ${effectiveFileName} to the current chat.` }],
        structuredResult: {
          delivered: true,
          fileName: effectiveFileName,
          remoteMessageId: sendResult.messageId,
          fileKey: sendResult.fileKey,
        },
      };
    } catch (error) {
      return {
        success: false,
        errorCode: "AdapterToolCallFailed",
        errorMessage: errorMessage(error),
      };
    }
  }

  async onApprovalRequest(request: Record<string, unknown>): Promise<string> {
    const requestId = String(request.requestId ?? "");
    const threadId = String(request.threadId ?? "");
    const approvalType = String(request.approvalType ?? "");
    const operation = String(request.operation ?? "");
    const target = String(request.target ?? "");
    const reason = String(request.reason ?? "");
    const channelTarget = this.threadContextMap.get(threadId);
    if (!channelTarget || !requestId) {
      logWarn("approval.request.invalid_context", {
        requestId: shortId(requestId),
        threadId: shortId(threadId),
      });
      return DECISION_DECLINE;
    }

    const timeoutSeconds = Math.max(1, Math.round(this.approvalTimeoutMs / 1000));
    const card = buildApprovalCard({
      requestId,
      approvalType,
      operation,
      target,
      reason,
      timeoutSeconds,
    });
    const sent = await sendSingleCard(this.feishu, channelTarget, card);
    logInfo("approval.card_sent", {
      requestId: shortId(requestId),
      threadId: shortId(threadId),
      timeoutSec: timeoutSeconds,
      channelTarget: shortId(channelTarget),
      messageId: shortId(sent.messageId),
    });

    return new Promise<string>((resolve) => {
      const timer = setTimeout(() => {
        const waiter = this.approvalWaiters.get(requestId);
        this.approvalWaiters.delete(requestId);
        if (waiter?.messageId) {
          void this.tryUpdateApprovalCard(waiter.messageId, buildApprovalTimeoutCard({ requestId, timeoutSeconds }));
        }
        logWarn("approval.timeout", {
          requestId: shortId(requestId),
          decision: DECISION_CANCEL,
        });
        resolve(DECISION_CANCEL);
      }, this.approvalTimeoutMs);
      this.approvalWaiters.set(requestId, {
        resolve: (decision: string) => {
          clearTimeout(timer);
          const waiter = this.approvalWaiters.get(requestId);
          this.approvalWaiters.delete(requestId);
          if (waiter?.messageId) {
            void this.tryUpdateApprovalCard(
              waiter.messageId,
              buildApprovalResolvedCard({
                requestId,
                decision,
              }),
            );
          }
          logInfo("approval.resolved", {
            requestId: shortId(requestId),
            decision,
          });
          resolve(decision);
        },
        timer,
        threadId,
        channelTarget,
        messageId: sent.messageId,
        timeoutSeconds,
      });
    });
  }

  private async tryUpdateApprovalCard(messageId: string, card: Record<string, unknown>): Promise<void> {
    try {
      await updateCard(this.feishu, messageId, card);
      logInfo("approval.card_updated", {
        messageId: shortId(messageId),
      });
    } catch (error) {
      logWarn("approval.card_update_failed", {
        messageId: shortId(messageId),
        message: errorMessage(error),
      });
    }
  }

  private transcriptStateKey(threadId: string, turnId: string): string {
    return `${threadId}\u0000${turnId}`;
  }

  private getOrInitTurnTranscriptState(
    threadId: string,
    turnId: string,
    channelTarget: string,
  ): {
    threadId: string;
    channelTarget: string;
    messageId: string;
    accumulatedText: string;
    isFinal: boolean;
  } {
    const stateKey = this.transcriptStateKey(threadId, turnId);
    const existing = this.turnTranscriptStates.get(stateKey);
    if (existing) return existing;
    const created = {
      threadId,
      channelTarget,
      messageId: "",
      accumulatedText: "",
      isFinal: false,
    };
    this.turnTranscriptStates.set(stateKey, created);
    return created;
  }

  private async upsertTurnTranscriptCard(
    threadId: string,
    turnId: string,
    channelTarget: string,
    segmentText: string,
    isFinal: boolean,
  ): Promise<void> {
    const state = this.getOrInitTurnTranscriptState(threadId, turnId, channelTarget);
    if (segmentText) state.accumulatedText += segmentText;
    state.isFinal = isFinal;
    this.activeTurnByThread.set(threadId, turnId);
    this.activeTurnByChannelTarget.set(channelTarget, turnId);
    const card = buildTranscriptCard(state.accumulatedText, isFinal);
    const sent = await createOrUpdateCard(this.feishu, channelTarget, card, state.messageId);
    state.messageId = sent.messageId;
    if (isFinal) {
      this.clearTurnTranscriptState(threadId, turnId);
    }
  }

  private clearTurnTranscriptState(threadId: string, turnId: string): void {
    const stateKey = this.transcriptStateKey(threadId, turnId);
    const state = this.turnTranscriptStates.get(stateKey);
    if (!state) return;
    const activeTurnId = this.activeTurnByThread.get(state.threadId);
    if (activeTurnId === turnId) {
      this.activeTurnByThread.delete(state.threadId);
    }
    const activeTargetTurnId = this.activeTurnByChannelTarget.get(state.channelTarget);
    if (activeTargetTurnId === turnId) {
      this.activeTurnByChannelTarget.delete(state.channelTarget);
    }
    this.turnTranscriptStates.delete(stateKey);
  }

  private clearThreadTranscriptState(threadId: string): void {
    const activeTurnId = this.activeTurnByThread.get(threadId);
    if (activeTurnId) this.clearTurnTranscriptState(threadId, activeTurnId);
  }

  private reconcileFinalTranscriptText(accumulatedText: string, replyText: string): string {
    return mergeReplyTextFromDeltaAndSnapshot(accumulatedText, replyText);
  }

  private async sendCaptionCard(
    channelTarget: string,
    caption: string,
    logContext: { target: string; fileName: string; source: "tool" | "structured" },
  ): Promise<void> {
    const normalized = caption.trim();
    if (!normalized) return;
    const card = buildFileCaptionCard(normalized, logContext.fileName);
    await sendSingleCard(this.feishu, channelTarget, card);
    logInfo("outbound.send.file.caption_card_sent", {
      source: logContext.source,
      target: shortId(logContext.target),
      fileName: logContext.fileName,
      captionChars: normalized.length,
    });
  }

  protected override async onSegmentCompleted(
    threadId: string,
    turnId: string,
    segmentText: string,
    isFinal: boolean,
    channelContext: string,
  ): Promise<void> {
    if (!segmentText.trim()) return;
    logInfo(isFinal ? "turn.completed_segment" : "turn.progress", {
      threadId: shortId(threadId),
      turnId: shortId(turnId),
      replyChars: segmentText.length,
      isFinal,
    });
    await this.upsertTurnTranscriptCard(threadId, turnId, channelContext, segmentText, isFinal);
  }

  protected override async onTurnCompleted(
    threadId: string,
    turnId: string,
    replyText: string,
    channelContext: string,
    segmentsWereDelivered: boolean,
  ): Promise<void> {
    if (!replyText.trim()) {
      this.clearTurnTranscriptState(threadId, turnId);
      return;
    }
    if (segmentsWereDelivered) {
      const state = this.turnTranscriptStates.get(this.transcriptStateKey(threadId, turnId));
      if (state && state.channelTarget === channelContext) {
        state.accumulatedText = this.reconcileFinalTranscriptText(state.accumulatedText, replyText);
        state.isFinal = true;
        const card = buildTranscriptCard(state.accumulatedText, true);
        const sent = await createOrUpdateCard(this.feishu, channelContext, card, state.messageId);
        state.messageId = sent.messageId;
      }
      this.clearTurnTranscriptState(threadId, turnId);
      return;
    }
    await this.upsertTurnTranscriptCard(threadId, turnId, channelContext, replyText, true);
  }

  protected override async onTurnFailed(threadId: string, turnId: string, error: string): Promise<void> {
    this.clearTurnTranscriptState(threadId, turnId);
    await super.onTurnFailed(threadId, turnId, error);
  }

  protected override async onTurnCancelled(threadId: string, turnId: string): Promise<void> {
    this.clearTurnTranscriptState(threadId, turnId);
    await super.onTurnCancelled(threadId, turnId);
  }

  protected override onThreadContextBound(threadId: string, channelContext: string): void {
    this.threadContextMap.set(threadId, channelContext);
  }

  async handleInboundMessage(message: ParsedInboundMessage): Promise<void> {
    logInfo("inbound.handle.start", {
      messageId: shortId(message.messageId),
      kind: message.kind,
      chatType: message.chatType,
    });
    if (isNewCommand(message.text)) {
      await this.newThread(message.threadUserId, message.channelContext);
      await sendSingleCard(
        this.feishu,
        message.channelContext,
        buildErrorCard("New Conversation", "Started a fresh DotCraft thread for this chat."),
      );
      logInfo("inbound.command.new_thread", {
        messageId: shortId(message.messageId),
        channelContext: shortId(message.channelContext),
      });
      return;
    }

    await this.handleMessage({
      userId: message.threadUserId,
      userName: message.userName,
      text: message.text,
      channelContext: message.channelContext,
      workspacePath: this.defaultWorkspacePath,
      inputParts: message.parts.length ? message.parts : undefined,
      omitSenderGroupId: message.chatType !== "group",
    });
  }

  handleCardAction(event: FeishuCardActionEvent): boolean {
    const value = parseActionValue(event.action?.value);
    if (!value || value.kind !== "approval") {
      const kindStr =
        value && typeof value === "object" && "kind" in value
          ? String((value as Record<string, unknown>).kind ?? "")
          : "";
      logWarn("approval.action_not_approval_kind", {
        kind: kindStr || "missing",
      });
      return false;
    }
    const requestId = String(value.requestId ?? "");
    const decision = String(value.decision ?? "");
    const waiter = this.approvalWaiters.get(requestId);
    if (!waiter) {
      logWarn("approval.action_no_waiter", {
        requestId: shortId(requestId),
        openMessageId: shortId(String(event.context?.open_message_id ?? "")),
      });
      return false;
    }
    const openMessageId = String(event.context?.open_message_id ?? "");
    if (openMessageId && waiter.messageId && openMessageId !== waiter.messageId) {
      logWarn("approval.action_message_mismatch", {
        requestId: shortId(requestId),
        expectedMessageId: shortId(waiter.messageId),
        actualMessageId: shortId(openMessageId),
      });
      return false;
    }
    waiter.resolve(decision || DECISION_CANCEL);
    logInfo("approval.action_resolved", {
      requestId: shortId(requestId),
      decision: decision || DECISION_CANCEL,
      messageId: shortId(openMessageId || waiter.messageId),
    });
    return true;
  }

  override async newThread(userId: string, channelContext = ""): Promise<void> {
    const identityKey = this.identityKey(userId, channelContext);
    const oldId = this.threadMap.get(identityKey);
    if (oldId) {
      try {
        await this.client.threadArchive(oldId);
        logInfo("thread.archive", {
          threadId: shortId(oldId),
          identityKey: shortId(identityKey),
        });
      } catch (error) {
        logWarn("thread.archive_failed", {
          threadId: shortId(oldId),
          message: errorMessage(error),
        });
      }
      this.threadMap.delete(identityKey);
      this.threadContextMap.delete(oldId);
      this.clearThreadTranscriptState(oldId);
    }
  }

  protected override async getOrCreateThread(
    identityKey: string,
    userId: string,
    channelContext: string,
    workspacePath: string,
  ): Promise<Thread> {
    let threadId = this.threadMap.get(identityKey);
    if (threadId) {
      try {
        const existing = await this.client.threadRead(threadId);
        if (existing.status === "active") {
          logInfo("thread.resolve_action", {
            action: "cache_hit",
            threadId: shortId(existing.id),
            identityKey: shortId(identityKey),
          });
          return existing;
        }
        if (existing.status === "paused") {
          const resumed = await this.client.threadResume(threadId);
          logInfo("thread.resolve_action", {
            action: "resumed_from_cache",
            threadId: shortId(resumed.id),
            identityKey: shortId(identityKey),
          });
          return resumed;
        }
      } catch {
        this.threadMap.delete(identityKey);
        logWarn("thread.cache_invalidated", {
          identityKey: shortId(identityKey),
          threadId: shortId(threadId),
        });
      }
    }

    const threads = await this.client.threadList({
      channelName: this.channelName,
      userId,
      channelContext,
      workspacePath,
    });
    const reusable = threads.find((thread) => thread.status === "active" || thread.status === "paused");
    if (reusable) {
      const active =
        reusable.status === "paused"
          ? await this.client.threadResume(reusable.id)
          : await this.client.threadRead(reusable.id);
      this.threadMap.set(identityKey, active.id);
      logInfo("thread.resolve_action", {
        action: reusable.status === "paused" ? "listed_resumed" : "listed_active",
        threadId: shortId(active.id),
        identityKey: shortId(identityKey),
      });
      return active;
    }

    const created = await this.client.threadStart({
      channelName: this.channelName,
      userId,
      channelContext,
      workspacePath,
    });
    this.threadMap.set(identityKey, created.id);
    logInfo("thread.resolve_action", {
      action: "created",
      threadId: shortId(created.id),
      identityKey: shortId(identityKey),
    });
    return created;
  }

  private async deliverFileMessage(
    target: string,
    message: Record<string, unknown>,
    context: {
      source: "structured" | "tool";
      metadata: Record<string, unknown>;
    },
  ): Promise<Record<string, unknown>> {
    const caption = String(message.caption ?? "");
    const fileName = String(message.fileName ?? "attachment");

    try {
      const file = await resolveOutboundFilePayload(message, fileName);
      logInfo("outbound.send.file", {
        source: context.source,
        target: shortId(target),
        fileName: file.fileName,
        bytes: file.data.length,
      });
      const sendResult = await this.feishu.sendFile(target, file);
      if (caption) {
        await this.sendCaptionCard(target, caption, {
          target,
          fileName: file.fileName,
          source: context.source,
        });
      }

      return {
        delivered: true,
        remoteMessageId: sendResult.messageId,
        remoteMediaId: sendResult.fileKey,
      };
    } catch (error) {
      logError("outbound.send.file.failed", {
        source: context.source,
        target: shortId(target),
        fileName,
        message: errorMessage(error),
      });
      return {
        delivered: false,
        errorCode: "AdapterDeliveryFailed",
        errorMessage: errorMessage(error),
      };
    }
  }
}

function isNewCommand(text: string): boolean {
  return /^\s*\/new\s*$/i.test(text.trim());
}

function parseActionValue(value: Record<string, unknown> | string | undefined): Record<string, unknown> | null {
  if (!value) return null;
  if (typeof value === "object") return value;
  try {
    return JSON.parse(value) as Record<string, unknown>;
  } catch {
    return null;
  }
}

async function resolveOutboundFilePayload(
  message: Record<string, unknown>,
  fallbackFileName: string,
): Promise<{
  fileName: string;
  data: Buffer;
  mediaType?: string;
}> {
  const source = (message.source as Record<string, unknown> | undefined) ?? {};
  const sourceKind = String(source.kind ?? "");
  const fileName = String(message.fileName ?? fallbackFileName).trim() || "attachment";
  const mediaType = String(message.mediaType ?? inferMediaType(fileName));

  if (sourceKind === "dataBase64") {
    const base64 = String(source.dataBase64 ?? "");
    if (!base64) {
      throw new Error("Feishu file delivery requires source.dataBase64 for dataBase64 sources.");
    }
    try {
      return {
        fileName,
        data: Buffer.from(base64, "base64"),
        mediaType,
      };
    } catch {
      throw new Error("Feishu file delivery received invalid base64 data.");
    }
  }

  if (sourceKind === "hostPath") {
    const hostPath = String(source.hostPath ?? "");
    if (!hostPath) {
      throw new Error("Feishu file delivery requires source.hostPath for hostPath sources.");
    }
    const resolvedPath = resolve(hostPath);
    const fileData = await readFile(resolvedPath);
    return {
      fileName: fileName || basename(resolvedPath),
      data: fileData,
      mediaType,
    };
  }

  throw new Error(`Feishu file delivery does not support source kind '${sourceKind || "unknown"}'.`);
}

function inferMediaType(fileName: string): string {
  const lower = fileName.toLowerCase();
  if (lower.endsWith(".pdf")) return "application/pdf";
  if (lower.endsWith(".json")) return "application/json";
  if (lower.endsWith(".xml")) return "application/xml";
  if (lower.endsWith(".txt")) return "text/plain";
  if (lower.endsWith(".csv")) return "text/csv";
  if (lower.endsWith(".md")) return "text/markdown";
  return "application/octet-stream";
}

