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
  DECISION_CANCEL,
  DECISION_DECLINE,
  DotCraftError,
  ERR_THREAD_NOT_ACTIVE,
  ERR_TURN_IN_PROGRESS,
  Thread,
  Turn,
  WebSocketTransport,
  extractAgentReplyTextFromTurnCompletedParams,
  mergeReplyTextFromDeltaAndSnapshot,
  textPart,
} from "dotcraft-wire";
import { buildApprovalCard, buildErrorCard } from "./card-builder.js";
import { sendProgressCard, sendReplyCards, sendSingleCard } from "./card-sender.js";
import type { FeishuCardActionEvent, ParsedInboundMessage } from "./feishu-types.js";
import type { FeishuClient } from "./feishu-client.js";
import { errorMessage, logError, logInfo, logWarn, shortId } from "./logging.js";

type InboundJob = ParsedInboundMessage & {
  workspacePath?: string;
  skipCommand?: boolean;
};

export interface FeishuAdapterConfig {
  wsUrl: string;
  dotcraftToken?: string;
  approvalTimeoutMs: number;
  feishu: FeishuClient;
}

export class FeishuAdapter extends ChannelAdapter {
  private readonly feishu: FeishuClient;
  private readonly approvalTimeoutMs: number;
  private readonly inboundQueues = new Map<string, Array<() => Promise<void>>>();
  private readonly activeWorkers = new Map<string, boolean>();
  private readonly threadContextMap = new Map<string, string>();
  private readonly approvalWaiters = new Map<
    string,
    { resolve: (decision: string) => void; timer: ReturnType<typeof setTimeout> }
  >();

  constructor(cfg: FeishuAdapterConfig) {
    const transport = new WebSocketTransport({
      url: cfg.wsUrl,
      token: cfg.dotcraftToken ?? "",
    });
    super(transport, "feishu", "dotcraft-feishu", "0.1.0", [
      "item/reasoning/delta",
      "subagent/progress",
      "item/usage/delta",
      "system/event",
      "plan/updated",
    ]);
    this.feishu = cfg.feishu;
    this.approvalTimeoutMs = cfg.approvalTimeoutMs;
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
        display: {
          icon: "📎",
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
        const captionDelivered = await this.onDeliver(target, caption, {});
        if (!captionDelivered) {
          logWarn("outbound.send.file.caption_failed", {
            target: shortId(target),
            fileName: effectiveFileName,
          });
        }
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
    await sendSingleCard(this.feishu, channelTarget, card);
    logInfo("approval.card_sent", {
      requestId: shortId(requestId),
      threadId: shortId(threadId),
      timeoutSec: timeoutSeconds,
    });

    return new Promise<string>((resolve) => {
      const timer = setTimeout(() => {
        this.approvalWaiters.delete(requestId);
        logWarn("approval.timeout", {
          requestId: shortId(requestId),
          decision: DECISION_CANCEL,
        });
        resolve(DECISION_CANCEL);
      }, this.approvalTimeoutMs);
      this.approvalWaiters.set(requestId, {
        resolve: (decision: string) => {
          clearTimeout(timer);
          this.approvalWaiters.delete(requestId);
          logInfo("approval.resolved", {
            requestId: shortId(requestId),
            decision,
          });
          resolve(decision);
        },
        timer,
      });
    });
  }

  async onTurnCompleted(
    _threadId: string,
    _turnId: string,
    replyText: string,
    channelContext: string,
  ): Promise<void> {
    if (!replyText.trim()) return;
    logInfo("turn.completed", {
      threadId: shortId(_threadId),
      turnId: shortId(_turnId),
      replyChars: replyText.length,
    });
    await sendReplyCards(this.feishu, channelContext, replyText);
  }

  async onProgressMessage(threadId: string, turnId: string, replyText: string, channelContext: string): Promise<void> {
    if (!replyText.trim()) return;
    logInfo("turn.progress", {
      threadId: shortId(threadId),
      turnId: shortId(turnId),
      replyChars: replyText.length,
    });
    await sendProgressCard(this.feishu, channelContext, replyText);
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

    this.enqueueInbound({
      ...message,
      workspacePath: this.defaultWorkspacePath,
    });
  }

  handleCardAction(event: FeishuCardActionEvent): boolean {
    const value = parseActionValue(event.action?.value);
    if (!value || value.kind !== "approval") return false;
    const requestId = String(value.requestId ?? "");
    const decision = String(value.decision ?? "");
    const waiter = this.approvalWaiters.get(requestId);
    if (!waiter) return false;
    waiter.resolve(decision || DECISION_CANCEL);
    logInfo("approval.action_resolved", {
      requestId: shortId(requestId),
      decision: decision || DECISION_CANCEL,
    });
    return true;
  }

  override async newThread(userId: string, channelContext = ""): Promise<void> {
    const identityKey = this.buildIdentityKey(userId, channelContext);
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
    }
  }

  private enqueueInbound(job: InboundJob): void {
    const identityKey = this.buildIdentityKey(job.threadUserId, job.channelContext);
    let queue = this.inboundQueues.get(identityKey);
    if (!queue) {
      queue = [];
      this.inboundQueues.set(identityKey, queue);
    }
    queue.push(async () => {
      await this.processInbound(identityKey, job);
    });
    logInfo("inbound.queue.enqueue", {
      identityKey: shortId(identityKey),
      queueSize: queue.length,
      messageId: shortId(job.messageId),
    });
    void this.drainInboundQueue(identityKey, queue);
  }

  private async drainInboundQueue(identityKey: string, queue: Array<() => Promise<void>>): Promise<void> {
    if (this.activeWorkers.get(identityKey)) return;
    this.activeWorkers.set(identityKey, true);
    logInfo("inbound.queue.drain_start", {
      identityKey: shortId(identityKey),
      queueSize: queue.length,
    });
    try {
      while (queue.length > 0) {
        const job = queue.shift();
        if (!job) continue;
        try {
          await job();
        } catch (error) {
          logError("inbound.queue.job_failed", {
            identityKey: shortId(identityKey),
            message: errorMessage(error),
          });
        }
      }
    } finally {
      this.activeWorkers.set(identityKey, false);
      logInfo("inbound.queue.drain_done", {
        identityKey: shortId(identityKey),
      });
      if (queue.length > 0) void this.drainInboundQueue(identityKey, queue);
    }
  }

  private async processInbound(identityKey: string, job: InboundJob): Promise<void> {
    const thread = await this.resolveThread(
      identityKey,
      job.threadUserId,
      job.channelContext,
      job.workspacePath ?? "",
    );
    this.threadContextMap.set(thread.id, job.channelContext);
    logInfo("thread.resolve", {
      threadId: shortId(thread.id),
      identityKey: shortId(identityKey),
      status: thread.status,
    });

    const sender: Record<string, unknown> = {
      senderId: job.userId,
      senderName: job.userName,
    };
    if (job.chatType === "group") {
      sender.groupId = job.channelContext;
    }

    if (job.kind === "text" && job.text.trim().startsWith("/") && !job.skipCommand) {
      const commandParts = job.text.trim().split(/\s+/);
      try {
        logInfo("command.execute", {
          threadId: shortId(thread.id),
          command: commandParts[0],
        });
        const commandResult = await this.client.commandExecute({
          threadId: thread.id,
          command: commandParts[0],
          arguments: commandParts.length > 1 ? commandParts.slice(1) : undefined,
          sender,
        });
        const expandedPrompt = commandResult.expandedPrompt as string | undefined;
        if (expandedPrompt) {
          this.enqueueInbound({
            ...job,
            text: expandedPrompt,
            parts: [textPart(expandedPrompt)],
            kind: "text",
            skipCommand: true,
          });
          return;
        }
        if (Boolean(commandResult.handled)) {
          const message = String(commandResult.message ?? "");
          if (message) {
            await this.onDeliver(job.channelContext, message, {});
          }
          return;
        }
      } catch (error) {
        if (error instanceof DotCraftError) {
          await this.onDeliver(job.channelContext, error.message || String(error), {});
          return;
        }
        throw error;
      }
    }

    let turn: Turn;
    try {
      logInfo("turn.start", {
        threadId: shortId(thread.id),
        inputParts: job.parts.length,
      });
      turn = await this.client.turnStart(thread.id, job.parts, sender);
      logInfo("turn.started", {
        threadId: shortId(thread.id),
        turnId: shortId(turn.id),
      });
    } catch (error) {
      if (error instanceof DotCraftError && error.code === ERR_TURN_IN_PROGRESS) {
        logWarn("turn.retry", {
          reason: "turn_in_progress",
          threadId: shortId(thread.id),
        });
        await delay(1000);
        this.enqueueInbound(job);
        return;
      }
      if (error instanceof DotCraftError && error.code === ERR_THREAD_NOT_ACTIVE) {
        logWarn("turn.resume_then_retry", {
          threadId: shortId(thread.id),
        });
        await this.client.threadResume(thread.id);
        turn = await this.client.turnStart(thread.id, job.parts, sender);
        logInfo("turn.started", {
          threadId: shortId(thread.id),
          turnId: shortId(turn.id),
        });
      } else {
        throw error;
      }
    }

    const allDeltaParts: string[] = [];
    const currentSegmentParts: string[] = [];
    for await (const event of this.client.streamEvents(thread.id)) {
      if (event.method === "item/agentMessage/delta") {
        const delta = String((event.params as Record<string, unknown>)?.delta ?? "");
        allDeltaParts.push(delta);
        currentSegmentParts.push(delta);
      } else if (event.method === "item/started") {
        const params = (event.params as Record<string, unknown>) ?? {};
        const item = (params.item as Record<string, unknown>) ?? {};
        const itemType = String(item.type ?? "");
        if (itemType === "toolCall") {
          const segmentText = currentSegmentParts.join("");
          currentSegmentParts.length = 0;
          await this.onProgressMessage(thread.id, turn.id, segmentText, job.channelContext);
        }
      } else if (event.method === "turn/completed") {
        const params = (event.params as Record<string, unknown>) ?? {};
        const snapshotText = extractAgentReplyTextFromTurnCompletedParams(params);
        const deltaText = allDeltaParts.join("");
        const fullReply = mergeReplyTextFromDeltaAndSnapshot(deltaText, snapshotText);
        await this.onTurnCompleted(thread.id, turn.id, fullReply, job.channelContext);
        return;
      } else if (event.method === "turn/failed") {
        const errorMessage = String(
          ((event.params as Record<string, unknown>)?.turn as Record<string, unknown>)?.error ??
            "Unknown error",
        );
        logError("turn.failed", {
          threadId: shortId(thread.id),
          turnId: shortId(turn.id),
          message: errorMessage,
        });
        await sendSingleCard(
          this.feishu,
          job.channelContext,
          buildErrorCard("DotCraft Turn Failed", errorMessage),
        );
        return;
      } else if (event.method === "turn/cancelled") {
        logWarn("turn.cancelled", {
          threadId: shortId(thread.id),
          turnId: shortId(turn.id),
        });
        return;
      }
    }
  }

  private buildIdentityKey(userId: string, channelContext: string): string {
    return `${userId}:${channelContext}`;
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
        const captionDelivered = await this.onDeliver(target, caption, context.metadata);
        if (!captionDelivered) {
          logWarn("outbound.send.file.caption_failed", {
            target: shortId(target),
            fileName: file.fileName,
          });
        }
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

  private async resolveThread(
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
          this.threadContextMap.set(resumed.id, channelContext);
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
      this.threadContextMap.set(active.id, channelContext);
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
    this.threadContextMap.set(created.id, channelContext);
    logInfo("thread.resolve_action", {
      action: "created",
      threadId: shortId(created.id),
      identityKey: shortId(identityKey),
    });
    return created;
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

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
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
