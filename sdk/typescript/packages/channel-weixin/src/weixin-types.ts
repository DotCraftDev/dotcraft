/**
 * Weixin iLink protocol types (subset; mirrors openclaw-weixin api/types).
 */

export const MessageType = {
  NONE: 0,
  USER: 1,
  BOT: 2,
} as const;

export const MessageItemType = {
  NONE: 0,
  TEXT: 1,
  IMAGE: 2,
  VOICE: 3,
  FILE: 4,
  VIDEO: 5,
} as const;

export interface TextItem {
  text?: string;
}

export interface MessageItem {
  type?: number;
  text_item?: TextItem;
}

export interface WeixinMessage {
  seq?: number;
  message_id?: number;
  from_user_id?: string;
  to_user_id?: string;
  client_id?: string;
  message_type?: number;
  item_list?: MessageItem[];
  context_token?: string;
}

export interface GetUpdatesResp {
  ret?: number;
  errcode?: number;
  errmsg?: string;
  msgs?: WeixinMessage[];
  get_updates_buf?: string;
  longpolling_timeout_ms?: number;
}

export interface SendMessageReq {
  msg?: WeixinMessage;
}
