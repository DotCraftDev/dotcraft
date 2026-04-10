import { buildErrorCard } from "./card-builder.js";
import { sendSingleCard } from "./card-sender.js";
import type { FeishuAdapter } from "./feishu-adapter.js";
import type { FeishuClient } from "./feishu-client.js";
import type {
  AppConfig,
  FeishuBotInfo,
  FeishuCardActionEvent,
  FeishuMessageEvent,
} from "./feishu-types.js";
import { shouldHandleMessageWithReason } from "./mention.js";
import { parseInboundMessage } from "./message-parser.js";
import { errorMessage, logInfo, logWarn, shortId } from "./logging.js";

export function createFeishuEventHandlers(params: {
  adapter: FeishuAdapter;
  client: FeishuClient;
  bot: FeishuBotInfo;
  config: AppConfig["feishu"];
}) {
  const dedup = new Map<string, number>();
  const dedupTtlMs = 5 * 60 * 1000;
  let loggedMissingBotIdentityWarning = false;

  const remember = (key: string): boolean => {
    const now = Date.now();
    for (const [existing, timestamp] of dedup) {
      if (now - timestamp > dedupTtlMs) dedup.delete(existing);
    }
    if (dedup.has(key)) return false;
    dedup.set(key, now);
    return true;
  };

  return {
    onMessage: async (event: FeishuMessageEvent): Promise<void> => {
      const messageId = event.message?.message_id ?? "";
      if (!messageId) return;
      logInfo("inbound.message.received", {
        messageId: shortId(messageId),
        chatType: event.message.chat_type,
        messageType: event.message.message_type,
      });
      if (!remember(`message:${messageId}`)) {
        logInfo("inbound.message.deduped", {
          messageId: shortId(messageId),
        });
        return;
      }
      const mentionDecision = shouldHandleMessageWithReason(
        event,
        params.bot.openId,
        params.bot.botName,
        params.config.groupMentionRequired !== false,
      );
      logInfo("gate.mention", {
        messageId: shortId(messageId),
        allow: mentionDecision.allow,
        reason: mentionDecision.reason,
      });
      if (!mentionDecision.allow) {
        if (mentionDecision.reason === "missing_bot_identity" && !loggedMissingBotIdentityWarning) {
          loggedMissingBotIdentityWarning = true;
          logWarn("gate.mention_missing_bot_identity", {
            hint:
              "Enable/publish Bot capability, or set groupMentionRequired=false to allow all group messages.",
          });
        }
        return;
      }

      const parsed = await parseInboundMessage(
        params.client,
        event,
        params.bot.openId,
        params.config.downloadDir,
      );
      if (!parsed) return;

      if (!parsed.text.trim() && parsed.kind === "text") {
        logInfo("inbound.message.empty_after_parse", {
          messageId: shortId(messageId),
          chatType: event.message.chat_type,
        });
        return;
      }

      if (!parsed.parts.length) {
        logWarn("inbound.message.unsupported", {
          messageId: shortId(messageId),
          messageType: event.message.message_type,
        });
        await sendSingleCard(
          params.client,
          parsed.channelContext,
          buildErrorCard("Unsupported Message", `Message type \`${event.message.message_type}\` is not supported yet.`),
        );
        return;
      }

      await params.adapter.handleInboundMessage(parsed);
    },
    onCardAction: async (event: FeishuCardActionEvent): Promise<void> => {
      const dedupKey = `action:${event.event_id ?? ""}:${event.token ?? ""}`;
      if (!remember(dedupKey)) {
        logInfo("approval.action.deduped", {
          eventId: shortId(event.event_id ?? ""),
        });
        return;
      }
      logInfo("approval.action.received", {
        eventId: shortId(event.event_id ?? ""),
        actionTag: event.action?.tag ?? "unknown",
      });
      params.adapter.handleCardAction(event);
    },
  };
}
