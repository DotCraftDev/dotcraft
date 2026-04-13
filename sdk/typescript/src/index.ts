/**
 * dotcraft-wire — TypeScript SDK for the DotCraft AppServer Wire Protocol.
 */

export { ChannelAdapter } from "./adapter.js";
export type { ChannelAdapterMessageOpts } from "./adapter.js";
export { DotCraftClient, DotCraftError } from "./client.js";
export type { NotificationHandler, ServerRequestHandler } from "./client.js";
export {
  DECISION_ACCEPT,
  DECISION_ACCEPT_ALWAYS,
  DECISION_ACCEPT_FOR_SESSION,
  DECISION_CANCEL,
  DECISION_DECLINE,
  ERR_ALREADY_INITIALIZED,
  ERR_APPROVAL_TIMEOUT,
  ERR_CHANNEL_REJECTED,
  ERR_CRON_NOT_FOUND,
  ERR_NOT_INITIALIZED,
  ERR_THREAD_NOT_ACTIVE,
  ERR_THREAD_NOT_FOUND,
  ERR_TURN_IN_PROGRESS,
  ERR_TURN_NOT_FOUND,
  ERR_TURN_NOT_RUNNING,
  InitializeResult,
  JsonRpcMessage,
  ServerCapabilities,
  ServerInfo,
  Thread,
  Turn,
  imageUrlPart,
  localImagePart,
  textPart,
} from "./models.js";
export type { SessionIdentityWire } from "./models.js";
export {
  StdioTransport,
  TransportClosed,
  TransportError,
  WebSocketTransport,
} from "./transport.js";
export type { Transport, WebSocketTransportOptions } from "./transport.js";
export {
  extractAgentReplyTextFromTurnCompletedParams,
  extractAgentReplyTextsFromTurnCompletedParams,
  mergeReplyTextFromDeltaAndSnapshot,
} from "./turnReply.js";
export { shouldFlushSegmentOnItemStarted } from "./segmentBoundaries.js";

export const version = "0.1.0";
