export interface AppConfig {
  dotcraft: {
    wsUrl: string;
    token?: string;
  };
  feishu: {
    appId: string;
    appSecret: string;
    brand?: "feishu" | "lark" | string;
    approvalTimeoutMs?: number;
    groupMentionRequired?: boolean;
    ackReactionEmoji?: string;
    downloadDir?: string;
    /** Debug logging to stderr; only keys set to `true` enable tracing. */
    debug?: {
      /** Verbose `consumeTurnEventStream` traces (adapter stream). */
      adapterStream?: boolean;
      /** Traces inside `mergeReplyTextFromDeltaAndSnapshot`. */
      textMerge?: boolean;
    };
  };
}

export interface FeishuSenderId {
  open_id?: string;
  user_id?: string;
  union_id?: string;
}

export interface FeishuMention {
  key: string;
  id: FeishuSenderId;
  name: string;
  tenant_key?: string;
}

export interface FeishuMessageEvent {
  app_id?: string;
  sender: {
    sender_id: FeishuSenderId;
    sender_type?: string;
    tenant_key?: string;
  };
  message: {
    message_id: string;
    root_id?: string;
    parent_id?: string;
    chat_id: string;
    thread_id?: string;
    chat_type: "p2p" | "group";
    message_type: string;
    content: string;
    create_time?: string;
    mentions?: FeishuMention[];
  };
}

export interface FeishuCardActionEvent {
  app_id?: string;
  event_id?: string;
  token?: string;
  operator?: {
    open_id?: string;
    user_id?: string;
    union_id?: string;
    tenant_key?: string;
  };
  action?: {
    tag?: string;
    value?: Record<string, unknown> | string;
  };
  context?: {
    open_message_id?: string;
    open_chat_id?: string;
  };
}

export interface FeishuBotInfo {
  appName: string;
  botName: string;
  openId: string;
  hasBotIdentity: boolean;
  tenantKey?: string;
  activateStatus?: number;
  rawFieldKeys?: string[];
  diagnosticMessage?: string;
}

export interface FeishuSendResult {
  messageId: string;
  chatId: string;
}

export interface ParsedInboundMessage {
  kind: "text" | "parts";
  userId: string;
  userName: string;
  threadUserId: string;
  channelContext: string;
  chatId: string;
  chatType: "p2p" | "group";
  text: string;
  parts: Record<string, unknown>[];
  messageId: string;
}
