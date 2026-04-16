/**
 * HTTP client for Tencent iLink Weixin bot API.
 */

import { randomBytes } from "node:crypto";
import type { GetUpdatesResp, SendMessageReq, WeixinMessage } from "./weixin-types.js";

const DEFAULT_LONG_POLL_MS = 35_000;
const DEFAULT_API_TIMEOUT_MS = 15_000;

export interface WeixinApiOptions {
  baseUrl: string;
  token?: string;
  timeoutMs?: number;
  longPollTimeoutMs?: number;
}

function ensureTrailingSlash(url: string): string {
  return url.endsWith("/") ? url : `${url}/`;
}

/** X-WECHAT-UIN: random uint32 as decimal string, base64-encoded (matches openclaw-weixin). */
function randomWechatUin(): string {
  const buf = randomBytes(4);
  const u32 = buf.readUInt32BE(0);
  return Buffer.from(String(u32), "utf-8").toString("base64");
}

function buildHeaders(token: string | undefined, body: string): Record<string, string> {
  const headers: Record<string, string> = {
    "Content-Type": "application/json",
    AuthorizationType: "ilink_bot_token",
    "Content-Length": String(Buffer.byteLength(body, "utf-8")),
    "X-WECHAT-UIN": randomWechatUin(),
  };
  if (token?.trim()) {
    headers.Authorization = `Bearer ${token.trim()}`;
  }
  return headers;
}

export function buildBaseInfo(): { channel_version: string } {
  return { channel_version: "0.1.0" };
}

async function apiFetch(params: {
  baseUrl: string;
  endpoint: string;
  body: string;
  token?: string;
  timeoutMs: number;
  label: string;
}): Promise<string> {
  const base = ensureTrailingSlash(params.baseUrl);
  const url = new URL(params.endpoint, base);
  const controller = new AbortController();
  const t = setTimeout(() => controller.abort(), params.timeoutMs);
  try {
    const res = await fetch(url.toString(), {
      method: "POST",
      headers: buildHeaders(params.token, params.body),
      body: params.body,
      signal: controller.signal,
    });
    clearTimeout(t);
    const rawText = await res.text();
    if (!res.ok) {
      throw new Error(`${params.label} ${res.status}: ${rawText}`);
    }
    return rawText;
  } catch (err) {
    clearTimeout(t);
    throw err;
  }
}

export async function getUpdates(params: {
  baseUrl: string;
  token?: string;
  get_updates_buf: string;
  timeoutMs?: number;
}): Promise<GetUpdatesResp> {
  const timeout = params.timeoutMs ?? DEFAULT_LONG_POLL_MS;
  try {
    const rawText = await apiFetch({
      baseUrl: params.baseUrl,
      endpoint: "ilink/bot/getupdates",
      body: JSON.stringify({
        get_updates_buf: params.get_updates_buf ?? "",
        base_info: buildBaseInfo(),
      }),
      token: params.token,
      timeoutMs: timeout,
      label: "getUpdates",
    });
    return JSON.parse(rawText) as GetUpdatesResp;
  } catch (err) {
    if (err instanceof Error && err.name === "AbortError") {
      return { ret: 0, msgs: [], get_updates_buf: params.get_updates_buf };
    }
    throw err;
  }
}

export async function sendMessage(
  opts: WeixinApiOptions & { body: SendMessageReq },
): Promise<void> {
  await apiFetch({
    baseUrl: opts.baseUrl,
    endpoint: "ilink/bot/sendmessage",
    body: JSON.stringify({ ...opts.body, base_info: buildBaseInfo() }),
    token: opts.token,
    timeoutMs: opts.timeoutMs ?? DEFAULT_API_TIMEOUT_MS,
    label: "sendMessage",
  });
}

export function buildTextMessageReq(params: {
  toUserId: string;
  text: string;
  contextToken?: string;
  clientId: string;
}): SendMessageReq {
  const item_list =
    params.text.length > 0
      ? [{ type: 1 as const, text_item: { text: params.text } }]
      : [];
  const msg: WeixinMessage = {
    from_user_id: "",
    to_user_id: params.toUserId,
    client_id: params.clientId,
    message_type: 2,
    item_list: item_list.length ? item_list : undefined,
    context_token: params.contextToken,
  };
  return { msg };
}

/** Session expired (openclaw-weixin session-guard). */
export const SESSION_EXPIRED_ERRCODE = -14;
