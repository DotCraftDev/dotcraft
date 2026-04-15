/**
 * dotcraft-wire — TypeScript SDK for the DotCraft AppServer Wire Protocol.
 */

export { ChannelAdapter } from "./adapter.js";
export type { ChannelAdapterMessageOpts, ChannelAdapterOptions } from "./adapter.js";
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
export { getDeliveredFrontier } from "./deliveredFrontier.js";
export {
  configureTextMergeDebug,
  extractAgentReplyTextFromTurnCompletedParams,
  extractAgentReplyTextsFromTurnCompletedParams,
  mergeReplyTextFromDeltaAndSnapshot,
} from "./turnReply.js";
export { shouldFlushSegmentOnItemStarted } from "./segmentBoundaries.js";
export type {
  ModuleFactory,
  ModuleInstance,
  ModuleManifest,
  ModuleTransport,
  ModuleVariant,
  WorkspaceContext,
  LauncherDescriptor,
} from "./module.js";
export type { ConfigDescriptor, ConfigFieldKind } from "./config.js";
export type { LifecycleStatus, ModuleError, ModuleErrorCode } from "./lifecycle.js";
export type {
  CapabilitySummary,
  ChannelToolDescriptor,
  ToolApprovalDescriptor,
  DeliveryCapabilityDescriptor,
  ToolInvocationContext,
  ToolInvocationResult,
} from "./capability.js";

export const version = "0.1.0";
export const sdkContractVersion = "1.0.0";
