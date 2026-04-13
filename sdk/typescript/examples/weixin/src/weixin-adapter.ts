/**
 * Weixin external channel: iLink HTTP + DotCraft WebSocket wire.
 */

import { randomUUID } from "node:crypto";
import {
  ChannelAdapter,
  DECISION_ACCEPT,
  DECISION_ACCEPT_ALWAYS,
  DECISION_ACCEPT_FOR_SESSION,
  DECISION_CANCEL,
  DECISION_DECLINE,
  WebSocketTransport,
} from "dotcraft-wire";
import { markdownToPlainText } from "./formatting.js";
import { buildTextMessageReq, sendMessage } from "./weixin-api.js";
import type { WeixinCredentials } from "./state.js";
import { WeixinState } from "./state.js";

/** Match QQ/WeCom keyword-style approval (plain text). */
function parseApprovalDecision(text: string): string | null {
  const t = text.trim().toLowerCase();
  const raw = text.trim();

  if (
    ["同意全部", "允许全部"].some((x) => raw.includes(x)) ||
    ["yes all", "approve all", "y all"].includes(t)
  ) {
    return DECISION_ACCEPT_FOR_SESSION;
  }
  if (
    ["同意", "允许", "yes", "y", "approve"].includes(t) ||
    ["同意", "允许"].some((x) => raw === x)
  ) {
    return DECISION_ACCEPT;
  }
  if (["拒绝", "不同意", "no", "n", "reject", "deny"].includes(t) || raw === "拒绝") {
    return DECISION_DECLINE;
  }
  return null;
}

/** Telegram-style /new: start a fresh thread (strict match, case-insensitive). */
function isNewCommand(text: string): boolean {
  return /^\s*\/new\s*$/i.test(text.trim());
}

export interface WeixinAdapterConfig {
  wsUrl: string;
  dotcraftToken?: string;
  apiBaseUrl: string;
  approvalTimeoutMs: number;
  state: WeixinState;
  credentials: WeixinCredentials;
}

export class WeixinAdapter extends ChannelAdapter {
  private readonly apiBaseUrl: string;
  private readonly botToken: string;
  private readonly approvalTimeoutMs: number;
  private readonly state: WeixinState;
  private contextTokens: Record<string, string> = {};
  /** userId -> waiter for active approval */
  private readonly approvalWaiters = new Map<
    string,
    { resolve: (v: string) => void; timer: ReturnType<typeof setTimeout> }
  >();

  constructor(cfg: WeixinAdapterConfig) {
    const transport = new WebSocketTransport({
      url: cfg.wsUrl,
      token: cfg.dotcraftToken ?? "",
    });
    super(transport, "weixin", "dotcraft-weixin", "0.1.0", [
      "item/reasoning/delta",
      "subagent/progress",
      "item/usage/delta",
      "system/event",
      "plan/updated",
    ]);
    this.apiBaseUrl = cfg.credentials.baseUrl || cfg.apiBaseUrl;
    this.botToken = cfg.credentials.botToken;
    this.approvalTimeoutMs = cfg.approvalTimeoutMs;
    this.state = cfg.state;
    this.contextTokens = cfg.state.loadContextTokens();
  }

  private findUserIdForThread(threadId: string): string | undefined {
    for (const [identityKey, tid] of this.threadMap) {
      if (tid === threadId) {
        const userId = identityKey.split(":")[0];
        return userId;
      }
    }
    return undefined;
  }

  private async sendWeixinText(toUserId: string, text: string): Promise<void> {
    const ctx = this.contextTokens[toUserId];
    const req = buildTextMessageReq({
      toUserId,
      text: markdownToPlainText(text),
      contextToken: ctx,
      clientId: `dotcraft-weixin-${randomUUID()}`,
    });
    await sendMessage({
      baseUrl: this.apiBaseUrl,
      token: this.botToken,
      body: req,
    });
  }

  /**
   * If message is an approval reply, consume it and return true (do not forward to agent).
   */
  tryHandleApprovalReply(fromUserId: string, text: string): boolean {
    const pending = this.approvalWaiters.get(fromUserId);
    if (!pending) return false;
    const decision = parseApprovalDecision(text);
    if (!decision) return false;
    clearTimeout(pending.timer);
    pending.resolve(decision);
    return true;
  }

  /** Resolve pending approval with cancel (e.g. user sent /new while a prompt was open). */
  private cancelPendingApprovalIfAny(userId: string): void {
    const pending = this.approvalWaiters.get(userId);
    if (!pending) return;
    clearTimeout(pending.timer);
    pending.resolve(DECISION_CANCEL);
  }

  async onDeliver(target: string, content: string, _metadata: Record<string, unknown>): Promise<boolean> {
    const userId = target.replace(/^user:/, "");
    try {
      await this.sendWeixinText(userId, content);
      return true;
    } catch (e) {
      console.error("onDeliver failed:", e);
      return false;
    }
  }

  protected override getDeliveryCapabilities(): Record<string, unknown> | null {
    return {
      structuredDelivery: true,
      media: {
        file: {
          supportsHostPath: false,
          supportsUrl: false,
          supportsBase64: true,
          supportsCaption: true,
          allowedMimeTypes: ["text/plain", "application/pdf"],
        },
      },
    };
  }

  protected override getChannelTools(): Record<string, unknown>[] | null {
    return [
      {
        name: "WeixinSendFilePreviewToCurrentChat",
        description: "Send a file-style preview message to the current Weixin chat.",
        requiresChatContext: true,
        inputSchema: {
          type: "object",
          properties: {
            fileName: { type: "string" },
            caption: { type: "string" },
          },
          required: ["fileName"],
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

    if (kind !== "file") {
      return {
        delivered: false,
        errorCode: "UnsupportedDeliveryKind",
        errorMessage: `Weixin example does not implement structured '${kind}' delivery.`,
      };
    }

    const fileName = String(message.fileName ?? "attachment");
    const caption = String(message.caption ?? "");
    const preview = caption
      ? `[structured:file] ${fileName}\n${caption}`
      : `[structured:file] ${fileName}`;
    const delivered = await this.onDeliver(target, preview, metadata);
    return delivered
      ? { delivered: true }
      : {
          delivered: false,
          errorCode: "AdapterDeliveryFailed",
          errorMessage: `Failed to deliver structured preview for ${fileName}.`,
        };
  }

  protected override async onToolCall(
    request: Record<string, unknown>,
  ): Promise<Record<string, unknown>> {
    const tool = String(request.tool ?? "");
    if (tool !== "WeixinSendFilePreviewToCurrentChat") {
      return {
        success: false,
        errorCode: "UnsupportedTool",
        errorMessage: `Unknown tool '${tool}'.`,
      };
    }

    const args = (request.arguments as Record<string, unknown>) ?? {};
    const context = (request.context as Record<string, unknown>) ?? {};
    const target = String(context.channelContext ?? context.groupId ?? "");
    if (!target) {
      return {
        success: false,
        errorCode: "MissingChatContext",
        errorMessage: "Current tool call does not contain a Weixin chat target.",
      };
    }

    const fileName = String(args.fileName ?? "attachment");
    const caption = String(args.caption ?? "");
    const preview = caption ? `[tool:file] ${fileName}\n${caption}` : `[tool:file] ${fileName}`;
    const delivered = await this.onDeliver(target, preview, {});
    return delivered
      ? {
          success: true,
          contentItems: [
            {
              type: "text",
              text: `Sent a file preview for ${fileName} to the current Weixin chat.`,
            },
          ],
          structuredResult: { delivered: true, fileName },
        }
      : {
          success: false,
          errorCode: "AdapterToolCallFailed",
          errorMessage: `Failed to send preview for ${fileName}.`,
        };
  }

  async onApprovalRequest(request: Record<string, unknown>): Promise<string> {
    const threadId = String(request.threadId ?? "");
    const approvalType = String(request.approvalType ?? "");
    const operation = String(request.operation ?? "");
    const target = String(request.target ?? "");

    const userId = this.findUserIdForThread(threadId);
    if (!userId) {
      console.warn("No user for thread; declining approval");
      return DECISION_DECLINE;
    }

    // Chinese prompt aligned with QQ/WeCom session-protocol wording (WeChat is Chinese-first).
    const timeoutSeconds = Math.round(this.approvalTimeoutMs / 1000);
    const prompt =
      approvalType === "shell"
        ? `⚠️ 需要执行命令权限：\`${operation}\`\n回复 同意/yes 批准，同意全部/yes all 本会话放行同类操作，拒绝/no 拒绝（${timeoutSeconds}秒超时自动拒绝）`
        : `⚠️ 需要 ${operation} 文件权限：\`${target}\`\n回复 同意/yes 批准，同意全部/yes all 本会话放行同类操作，拒绝/no 拒绝（${timeoutSeconds}秒超时自动拒绝）`;

    await this.sendWeixinText(userId, prompt);

    return new Promise<string>((resolve) => {
      const timer = setTimeout(() => {
        this.approvalWaiters.delete(userId);
        resolve(DECISION_CANCEL);
      }, this.approvalTimeoutMs);
      this.approvalWaiters.set(userId, {
        resolve: (v: string) => {
          clearTimeout(timer);
          this.approvalWaiters.delete(userId);
          resolve(v);
        },
        timer,
      });
    });
  }

  protected override async onTurnCompleted(
    threadId: string,
    turnId: string,
    replyText: string,
    channelContext: string,
    segmentsWereDelivered: boolean,
  ): Promise<void> {
    if (segmentsWereDelivered) return;
    if (!replyText) return;
    try {
      await this.sendWeixinText(channelContext, replyText);
    } catch (e) {
      console.error("onTurnCompleted send failed:", e);
    }
  }

  updateContextToken(userId: string, token: string | undefined): void {
    if (!token) return;
    this.contextTokens[userId] = token;
    this.state.saveContextTokens(this.contextTokens);
  }

  async handleInboundUserMessage(msg: {
    from_user_id?: string;
    item_list?: { type?: number; text_item?: { text?: string } }[];
    context_token?: string;
  }): Promise<void> {
    const from = msg.from_user_id ?? "";
    if (!from) return;

    this.updateContextToken(from, msg.context_token);

    const text =
      msg.item_list
        ?.map((i) => (i.type === 1 ? i.text_item?.text ?? "" : ""))
        .join("")
        .trim() ?? "";

    if (isNewCommand(text)) {
      this.cancelPendingApprovalIfAny(from);
      await this.newThread(from, from);
      await this.sendWeixinText(from, "已开启新对话，请直接输入消息。");
      return;
    }

    if (this.tryHandleApprovalReply(from, text)) return;

    const name = from;
    // Fire-and-forget so the monitor loop can continue polling for approval replies
    // while the turn is in progress. Per-identity ordering is preserved by the
    // threadQueues/runWorker queue inside handleMessage.
    void this.handleMessage({
      userId: from,
      userName: name,
      text,
      channelContext: from,
      senderExtra: { senderRole: "admin" },
    }).catch((e) => console.error("handleMessage error:", e));
  }
}
