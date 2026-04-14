/**
 * ChannelAdapter: high-level base class for external channel adapters.
 */

import { DotCraftClient, DotCraftError } from "./client.js";
import {
  ERR_THREAD_NOT_ACTIVE,
  ERR_TURN_IN_PROGRESS,
  JsonRpcMessage,
  Thread,
  Turn,
  textPart,
} from "./models.js";
import { Transport } from "./transport.js";
import { getDeliveredFrontier } from "./deliveredFrontier.js";
import {
  extractAgentReplyTextFromTurnCompletedParams,
  extractAgentReplyTextsFromTurnCompletedParams,
  mergeReplyTextFromDeltaAndSnapshot,
} from "./turnReply.js";
import { shouldFlushSegmentOnItemStarted } from "./segmentBoundaries.js";

function previewWireText(text: string, maxChars = 160): string {
  const s = text.replace(/\r\n/g, "\n");
  if (s.length <= maxChars) return s;
  return `${s.slice(0, maxChars)}…(+${s.length - maxChars} chars)`;
}

function shortWireId(id: string, max = 12): string {
  if (id.length <= max) return id;
  return `${id.slice(0, max)}…`;
}

/** Queued inbound message; skipCommand skips slash handling for expanded prompts. */
export type ChannelAdapterMessageOpts = {
  userId: string;
  userName: string;
  text: string;
  channelContext?: string;
  workspacePath?: string;
  senderExtra?: Record<string, unknown>;
  skipCommand?: boolean;
  /** When set, used as turn input instead of {@link textPart}(text). */
  inputParts?: Record<string, unknown>[];
  /**
   * When true, do not set `sender.groupId` from channelContext (e.g. Feishu DM vs group).
   * Default false: groupId is set whenever channelContext is non-empty.
   */
  omitSenderGroupId?: boolean;
};

/** Optional construction-time settings for {@link ChannelAdapter}. */
export type ChannelAdapterOptions = {
  /** When `true`, enables stderr trace logs for `consumeTurnEventStream`. Omitted or `false` disables. */
  debugStream?: boolean;
};

export abstract class ChannelAdapter {
  protected readonly client: DotCraftClient;
  protected readonly channelName: string;
  private readonly clientName: string;
  private readonly clientVersion: string;
  private readonly optOutNotifications: string[];
  /** See {@link ChannelAdapterOptions.debugStream}. */
  private readonly adapterStreamDebug: boolean | undefined;

  protected readonly threadMap = new Map<string, string>();
  private readonly forceFreshThreadIdentities = new Set<string>();
  private readonly threadQueues = new Map<string, Array<() => Promise<void>>>();
  private readonly runningWorkers = new Map<string, boolean>();
  private running = false;

  /** Default workspace path; override per instance or in handleMessage. */
  protected defaultWorkspacePath = "";

  constructor(
    transport: Transport,
    channelName: string,
    clientName: string,
    clientVersion: string,
    optOutNotifications: string[] = [],
    options?: ChannelAdapterOptions,
  ) {
    this.client = new DotCraftClient(transport);
    this.channelName = channelName;
    this.clientName = clientName;
    this.clientVersion = clientVersion;
    this.optOutNotifications = optOutNotifications;
    this.adapterStreamDebug = options?.debugStream;
  }

  /** Stream debug is enabled only when `ChannelAdapterOptions.debugStream` is `true`. */
  protected isAdapterStreamDebugEnabled(): boolean {
    return this.adapterStreamDebug === true;
  }

  protected debugAdapterStreamLog(
    message: string,
    data: Record<string, unknown> | (() => Record<string, unknown>),
  ): void {
    if (!this.isAdapterStreamDebugEnabled()) return;
    const payload = typeof data === "function" ? data() : data;
    console.error(`[dotcraft-wire:adapter-stream] ${message}`, payload);
  }

  abstract onDeliver(target: string, content: string, metadata: Record<string, unknown>): Promise<boolean>;

  abstract onApprovalRequest(request: Record<string, unknown>): Promise<string>;

  protected async onSend(
    target: string,
    message: Record<string, unknown>,
    metadata: Record<string, unknown>,
  ): Promise<Record<string, unknown>> {
    const kind = String(message.kind ?? "");
    if (kind === "text") {
      const ok = await this.onDeliver(target, String(message.text ?? ""), metadata);
      return { delivered: ok };
    }

    return {
      delivered: false,
      errorCode: "UnsupportedDeliveryKind",
      errorMessage: `Adapter does not implement structured '${kind}' delivery.`,
    };
  }

  protected getDeliveryCapabilities(): Record<string, unknown> | null {
    return null;
  }

  protected getChannelTools(): Record<string, unknown>[] | null {
    return null;
  }

  protected async onToolCall(
    _request: Record<string, unknown>,
  ): Promise<Record<string, unknown>> {
    return {
      success: false,
      errorCode: "UnsupportedTool",
      errorMessage: "Adapter does not implement channel tool calls.",
    };
  }

  /**
   * @param segmentsWereDelivered When true, progressive segments were already shown (e.g. tool
   *   boundaries); default implementation skips sending the full reply again to avoid duplicates.
   */
  protected async onTurnCompleted(
    threadId: string,
    turnId: string,
    replyText: string,
    channelContext: string,
    segmentsWereDelivered: boolean,
  ): Promise<void> {
    if (segmentsWereDelivered) return;
    if (replyText) await this.onDeliver(channelContext, replyText, {});
  }

  protected async onTurnFailed(threadId: string, turnId: string, error: string): Promise<void> {
    console.error(`Turn ${turnId} failed on thread ${threadId}: ${error}`);
  }

  protected async onTurnCancelled(threadId: string, turnId: string): Promise<void> {
    console.info(`Turn ${turnId} cancelled on thread ${threadId}`);
  }

  protected async onSegmentCompleted(
    _threadId: string,
    _turnId: string,
    _segmentText: string,
    _isFinal: boolean,
    _channelContext: string,
  ): Promise<void> {
    // Default no-op; adapters can override for progressive delivery.
  }

  /** Called after the thread is resolved for an inbound message (e.g. map threadId → chat target). */
  protected onThreadContextBound(_threadId: string, _channelContext: string): void {}

  async start(): Promise<void> {
    await this.client.connect();
    await this.client.start();
    await this.client.initialize({
      clientName: this.clientName,
      clientVersion: this.clientVersion,
      approvalSupport: true,
      streamingSupport: true,
      optOutNotifications: this.optOutNotifications,
      channelName: this.channelName,
      deliverySupport: true,
      deliveryCapabilities: this.getDeliveryCapabilities(),
      channelTools: this.getChannelTools(),
    });
    this.running = true;

    this.client.setApprovalHandler(async (_id, params) => {
      try {
        return await this.onApprovalRequest(params);
      } catch (e) {
        console.error("onApprovalRequest raised:", e);
        return "cancel";
      }
    });

    this.client.registerHandler("ext/channel/deliver", async (params) => {
      await this.handleDeliverRequest(params);
    });
    this.client.registerServerRequestHandler("ext/channel/deliver", async (_id, params) => {
      const target = String(params.target ?? "");
      const content = String(params.content ?? "");
      const metadata = (params.metadata as Record<string, unknown>) ?? {};
      try {
        const ok = await this.onDeliver(target, content, metadata);
        return { delivered: ok };
      } catch (e) {
        console.error("onDeliver raised:", e);
        return { delivered: false, error: String(e) };
      }
    });
    this.client.registerServerRequestHandler("ext/channel/send", async (_id, params) => {
      const target = String(params.target ?? "");
      const message = (params.message as Record<string, unknown>) ?? {};
      const metadata = (params.metadata as Record<string, unknown>) ?? {};
      try {
        return await this.onSend(target, message, metadata);
      } catch (e) {
        console.error("onSend raised:", e);
        return {
          delivered: false,
          errorCode: "AdapterDeliveryFailed",
          errorMessage: String(e),
        };
      }
    });
    this.client.registerServerRequestHandler("ext/channel/toolCall", async (_id, params) => {
      try {
        return await this.onToolCall((params as Record<string, unknown>) ?? {});
      } catch (e) {
        console.error("onToolCall raised:", e);
        return {
          success: false,
          errorCode: "AdapterToolCallFailed",
          errorMessage: String(e),
        };
      }
    });
    this.client.registerServerRequestHandler("ext/channel/heartbeat", async () => ({}));

    console.info(`ChannelAdapter '${this.channelName}' started (client: ${this.clientName} ${this.clientVersion})`);
  }

  private async handleDeliverRequest(params: Record<string, unknown>): Promise<void> {
    const target = String(params.target ?? "");
    const content = String(params.content ?? "");
    const metadata = (params.metadata as Record<string, unknown>) ?? {};
    try {
      await this.onDeliver(target, content, metadata);
    } catch (e) {
      console.error("onDeliver (notification) raised:", e);
    }
  }

  async stop(): Promise<void> {
    this.running = false;
    await this.client.stop();
    console.info(`ChannelAdapter '${this.channelName}' stopped`);
  }

  async handleMessage(opts: ChannelAdapterMessageOpts): Promise<void> {
    if (opts.skipCommand) {
      this.enqueueMessage(opts);
      return;
    }
    const trimmedText = opts.text.trim();
    if (trimmedText.startsWith("/")) {
      const channelContext = opts.channelContext ?? "";
      const identityKey = this.identityKey(opts.userId, channelContext);
      const threadId = this.threadMap.get(identityKey);
      if (threadId) {
        const sender: Record<string, unknown> = {
          senderId: opts.userId,
          senderName: opts.userName,
          ...(opts.senderExtra ?? {}),
        };
        if (channelContext && !opts.omitSenderGroupId) sender.groupId = channelContext;
        const parts = trimmedText.split(/\s+/);
        try {
          const commandResult = await this.client.commandExecute({
            threadId,
            command: parts[0],
            arguments: parts.length > 1 ? parts.slice(1) : undefined,
            sender,
          });
          const expanded = commandResult.expandedPrompt as string | undefined;
          if (expanded) {
            this.enqueueMessage({ ...opts, text: expanded, skipCommand: true });
            return;
          } else if (Boolean(commandResult.handled)) {
            await this.applyCommandResetResult(
              identityKey,
              opts.userId,
              channelContext,
              opts.workspacePath ?? this.defaultWorkspacePath,
              parts[0] ?? "",
              commandResult,
            );
            const commandMessage = commandResult.message as string | undefined;
            if (commandMessage) {
              await this.onDeliver(channelContext, commandMessage, {});
            }
            return;
          }
          // RPC consumed the line; do not re-enqueue or command_execute runs twice
          return;
        } catch (e) {
          if (e instanceof DotCraftError) {
            await this.onDeliver(channelContext, e.message || String(e), {});
            return;
          }
          throw e;
        }
      }
    }
    this.enqueueMessage(opts);
  }

  /**
   * Schedule a message for serial processing (one turn at a time per identity).
   * Does not wait for the turn to complete (matches Python asyncio.Queue.put).
   */
  protected enqueueMessage(opts: ChannelAdapterMessageOpts): void {
    const channelContext = opts.channelContext ?? "";
    const identityKey = this.identityKey(opts.userId, channelContext);

    let q = this.threadQueues.get(identityKey);
    if (!q) {
      q = [];
      this.threadQueues.set(identityKey, q);
    }

    q.push(async () => {
      await this.processMessage(identityKey, opts);
    });
    void this.runWorker(identityKey, q);
  }

  private async runWorker(
    identityKey: string,
    q: Array<() => Promise<void>>,
  ): Promise<void> {
    if (this.runningWorkers.get(identityKey)) return;
    this.runningWorkers.set(identityKey, true);
    try {
      while (this.running && q.length > 0) {
        const job = q.shift();
        if (job) {
          try {
            await job();
          } catch (e) {
            console.error(`Error processing message for ${identityKey}:`, e);
          }
        }
      }
    } finally {
      this.runningWorkers.set(identityKey, false);
      // If more work arrived while finishing, drain again
      if (q.length > 0 && this.running) void this.runWorker(identityKey, q);
    }
  }

  protected identityKey(userId: string, channelContext: string): string {
    return `${userId}:${channelContext}`;
  }

  protected async resetIdentityThreads(userId: string, channelContext = ""): Promise<string[]> {
    const identityKey = this.identityKey(userId, channelContext);
    const archivedIds = new Set<string>();
    const cachedThreadId = this.threadMap.get(identityKey);
    if (cachedThreadId) archivedIds.add(cachedThreadId);

    const threads = await this.client.threadList({
      channelName: this.channelName,
      userId,
      channelContext,
      workspacePath: this.defaultWorkspacePath,
    });
    for (const thread of threads) {
      if (thread.status === "active" || thread.status === "paused") archivedIds.add(thread.id);
    }

    for (const threadId of archivedIds) {
      try {
        await this.client.threadArchive(threadId);
        console.info(`Archived thread ${threadId} for ${identityKey}`);
      } catch (e) {
        console.warn(`Could not archive thread ${threadId}:`, e);
      }
    }

    this.threadMap.delete(identityKey);
    this.forceFreshThreadIdentities.add(identityKey);
    return [...archivedIds];
  }

  protected onThreadsArchived(_identityKey: string, _archivedThreadIds: string[]): void {
    // Default no-op for adapters without per-thread local state.
  }

  protected async applyCommandResetResult(
    identityKey: string,
    userId: string,
    channelContext: string,
    workspacePath: string,
    commandName: string,
    commandResult: Record<string, unknown>,
  ): Promise<void> {
    const normalizedCommand = commandName.trim().toLowerCase();
    const archivedThreadIds = Array.isArray(commandResult.archivedThreadIds)
      ? commandResult.archivedThreadIds.map((v) => String(v))
      : [];
    if (archivedThreadIds.length > 0) {
      this.onThreadsArchived(identityKey, archivedThreadIds);
    }

    const resetThreadWire = commandResult.thread as Record<string, unknown> | undefined;
    const sessionReset = Boolean(commandResult.sessionReset) || Boolean(resetThreadWire);
    if (sessionReset) {
      if (resetThreadWire) {
        const resetThread = Thread.fromWire(resetThreadWire);
        this.threadMap.set(identityKey, resetThread.id);
        this.forceFreshThreadIdentities.delete(identityKey);
        this.onThreadContextBound(resetThread.id, channelContext);
      } else {
        this.threadMap.delete(identityKey);
        this.forceFreshThreadIdentities.add(identityKey);
      }
      return;
    }

    // Backward compatibility: older servers may not return structured reset payload.
    if (normalizedCommand === "/new") {
      await this.resetIdentityThreads(userId, channelContext);
      const thread = await this.getOrCreateThread(identityKey, userId, channelContext, workspacePath);
      this.onThreadContextBound(thread.id, channelContext);
    }
  }

  protected async recoverThreadAfterNotActive(
    identityKey: string,
    userId: string,
    channelContext: string,
    workspacePath: string,
    staleThreadId: string,
  ): Promise<Thread> {
    try {
      const latest = await this.client.threadRead(staleThreadId);
      if (latest.status === "paused") {
        const resumed = await this.client.threadResume(staleThreadId);
        this.threadMap.set(identityKey, resumed.id);
        return resumed;
      }
      if (latest.status === "active") {
        this.threadMap.set(identityKey, latest.id);
        return latest;
      }
    } catch {
      // Continue with identity-level lookup.
    }

    this.threadMap.delete(identityKey);
    const threads = await this.client.threadList({
      channelName: this.channelName,
      userId,
      channelContext,
      workspacePath,
    });
    const reusable = threads.find((t) => t.status === "active" || t.status === "paused");
    if (reusable) {
      const thread =
        reusable.status === "paused"
          ? await this.client.threadResume(reusable.id)
          : await this.client.threadRead(reusable.id);
      this.threadMap.set(identityKey, thread.id);
      return thread;
    }

    const fresh = await this.client.threadStart({
      channelName: this.channelName,
      userId,
      channelContext,
      workspacePath,
    });
    this.threadMap.set(identityKey, fresh.id);
    return fresh;
  }

  protected async processMessage(
    identityKey: string,
    opts: ChannelAdapterMessageOpts,
  ): Promise<void> {
    const channelContext = opts.channelContext ?? "";
    const workspacePath = opts.workspacePath ?? this.defaultWorkspacePath;
    const senderExtra = opts.senderExtra ?? {};

    const thread = await this.getOrCreateThread(
      identityKey,
      opts.userId,
      channelContext,
      workspacePath,
    );
    this.onThreadContextBound(thread.id, channelContext);

    const sender: Record<string, unknown> = {
      senderId: opts.userId,
      senderName: opts.userName,
      ...senderExtra,
    };
    if (channelContext && !opts.omitSenderGroupId) sender.groupId = channelContext;

    const trimmedText = opts.text.trim();
    if (trimmedText.startsWith("/") && !opts.skipCommand) {
      const commandParts = trimmedText.split(/\s+/);
      const commandName = commandParts[0];
      const commandArguments = commandParts.length > 1 ? commandParts.slice(1) : undefined;
      try {
        const commandResult = await this.client.commandExecute({
          threadId: thread.id,
          command: commandName,
          arguments: commandArguments,
          sender,
        });
        const expandedPrompt = commandResult.expandedPrompt as string | undefined;
        if (expandedPrompt) {
          opts.text = expandedPrompt;
        } else if (Boolean(commandResult.handled)) {
          await this.applyCommandResetResult(
            identityKey,
            opts.userId,
            channelContext,
            workspacePath,
            commandName,
            commandResult,
          );
          const commandMessage = commandResult.message as string | undefined;
          if (commandMessage) {
            await this.onDeliver(channelContext, commandMessage, {});
          }
          return;
        }
      } catch (e) {
        if (e instanceof DotCraftError) {
          await this.onDeliver(channelContext, e.message || String(e), {});
          return;
        }
        throw e;
      }
    }

    const input = opts.inputParts?.length ? opts.inputParts : [textPart(opts.text)];

    const eventStream = this.client.streamEvents(thread.id);
    let turn: Turn;
    try {
      turn = await this.client.turnStart(thread.id, input, sender);
    } catch (e) {
      await eventStream.return?.();
      if (e instanceof DotCraftError && e.code === ERR_TURN_IN_PROGRESS) {
        await new Promise((r) => setTimeout(r, 1000));
        this.enqueueMessage(opts);
        return;
      }
      if (e instanceof DotCraftError && e.code === ERR_THREAD_NOT_ACTIVE) {
        const recovered = await this.recoverThreadAfterNotActive(
          identityKey,
          opts.userId,
          channelContext,
          workspacePath,
          thread.id,
        );
        this.onThreadContextBound(recovered.id, channelContext);
        const stream2 = this.client.streamEvents(recovered.id);
        try {
          turn = await this.client.turnStart(recovered.id, input, sender);
        } catch (err) {
          await stream2.return?.();
          throw err;
        }
        await this.consumeTurnEventStream(stream2, recovered.id, turn.id, channelContext);
        return;
      }
      throw e;
    }

    await this.consumeTurnEventStream(eventStream, thread.id, turn.id, channelContext);
  }

  /**
   * Runs the streaming loop for an already-started turn. Separated so callers can subscribe
   * to events before {@link DotCraftClient.turnStart}.
   */
  protected async consumeTurnEventStream(
    eventStream: AsyncIterableIterator<JsonRpcMessage>,
    threadId: string,
    turnId: string,
    channelContext: string,
  ): Promise<void> {
    const itemOrder: string[] = [];
    const perItemDelta = new Map<string, string>();
    const deliveredTextPerItem = new Map<string, string>();
    let activeAgentItemId: string | null = null;
    /** Last seen item id from agent deltas, used when item/started ordering is imperfect. */
    let lastDeltaAgentItemId: string | null = null;
    let orphanDeltaTail = "";
    let segmentsWereDelivered = false;

    const orderSeen = new Set<string>();
    const pushOrder = (itemId: string): void => {
      if (!itemId || orderSeen.has(itemId)) return;
      orderSeen.add(itemId);
      itemOrder.push(itemId);
    };
    const getCurrentItemText = (itemId: string | null): string => {
      if (!itemId) return "";
      return perItemDelta.get(itemId) ?? "";
    };
    const markSegmentDelivered = (itemId: string | null, segmentText: string): void => {
      if (!itemId || !segmentText) return;
      const delivered = deliveredTextPerItem.get(itemId) ?? "";
      deliveredTextPerItem.set(itemId, delivered + segmentText);
    };
    const getUnsentTail = (itemId: string | null, fallbackText = ""): string => {
      if (!itemId) return fallbackText;
      const current = getCurrentItemText(itemId) || fallbackText;
      const delivered = deliveredTextPerItem.get(itemId) ?? "";
      const frontier = getDeliveredFrontier(current, delivered);
      if (frontier >= current.length) return "";
      return current.slice(frontier);
    };
    const getUnsentFromMerged = (itemId: string | null, mergedText: string): string => {
      if (!itemId) return mergedText;
      const delivered = deliveredTextPerItem.get(itemId) ?? "";
      const frontier = getDeliveredFrontier(mergedText, delivered);
      if (frontier >= mergedText.length) return "";
      return mergedText.slice(frontier);
    };

    const snapshotStreamState = (): Record<string, unknown> => ({
      itemOrder: itemOrder.map((id) => shortWireId(id)),
      perItemChars: Object.fromEntries(
        [...perItemDelta.entries()].map(([id, t]) => [shortWireId(id), t.length]),
      ),
      deliveredChars: Object.fromEntries(
        [...deliveredTextPerItem.entries()].map(([id, t]) => [shortWireId(id), t.length]),
      ),
      activeAgentItemId: activeAgentItemId ? shortWireId(activeAgentItemId) : "",
      lastDeltaAgentItemId: lastDeltaAgentItemId ? shortWireId(lastDeltaAgentItemId) : "",
      orphanDeltaChars: orphanDeltaTail.length,
      segmentsWereDelivered,
    });

    for await (const event of eventStream) {
      this.debugAdapterStreamLog("event", () => ({
        method: event.method,
        threadId: shortWireId(threadId),
        turnId: shortWireId(turnId),
        channel: shortWireId(channelContext),
      }));
      if (event.method === "item/agentMessage/delta") {
        const params = (event.params as Record<string, unknown>) ?? {};
        const delta = String(params.delta ?? "");
        const explicitItemId = String(params.itemId ?? "");
        const resolvedItemId: string | null = explicitItemId || activeAgentItemId || lastDeltaAgentItemId || null;
        if (resolvedItemId) {
          pushOrder(resolvedItemId);
          const prev = perItemDelta.get(resolvedItemId) ?? "";
          perItemDelta.set(resolvedItemId, prev + delta);
          lastDeltaAgentItemId = resolvedItemId;
          if (!activeAgentItemId) activeAgentItemId = resolvedItemId;
        } else {
          orphanDeltaTail += delta;
        }
        this.debugAdapterStreamLog("event.item/agentMessage/delta", () => ({
          explicitItemId: explicitItemId ? shortWireId(explicitItemId) : "(empty)",
          resolvedItemId: resolvedItemId ? shortWireId(resolvedItemId) : "(orphan)",
          deltaChars: delta.length,
          deltaPreview: previewWireText(delta, 120),
          mergedAfterChars: resolvedItemId ? (perItemDelta.get(resolvedItemId) ?? "").length : orphanDeltaTail.length,
          ...snapshotStreamState(),
        }));
      } else if (event.method === "item/started") {
        const params = (event.params as Record<string, unknown>) ?? {};
        const item = (params.item as Record<string, unknown>) ?? {};
        const itemType = String(item.type ?? "");
        const itemId = String(item.id ?? "");
        if (itemType === "agentMessage" && itemId) {
          activeAgentItemId = itemId;
          lastDeltaAgentItemId = itemId;
          pushOrder(itemId);
        }
        if (shouldFlushSegmentOnItemStarted(itemType)) {
          const segmentItemId = activeAgentItemId ?? lastDeltaAgentItemId;
          let segmentText = "";
          if (segmentItemId) {
            const merged = perItemDelta.get(segmentItemId) ?? "";
            segmentText = getUnsentFromMerged(segmentItemId, merged);
          } else if (orphanDeltaTail) {
            segmentText = orphanDeltaTail;
            orphanDeltaTail = "";
          }
          if (segmentText.trim()) {
            segmentsWereDelivered = true;
            await this.onSegmentCompleted(threadId, turnId, segmentText, false, channelContext);
            markSegmentDelivered(segmentItemId, segmentText);
          }
          this.debugAdapterStreamLog("event.item/started.flush_segment", () => ({
            itemType,
            itemId: itemId ? shortWireId(itemId) : "",
            flushSegment: true,
            segmentItemId: segmentItemId ? shortWireId(segmentItemId) : "",
            segmentChars: segmentText.length,
            segmentPreview: previewWireText(segmentText),
            ...snapshotStreamState(),
          }));
        } else {
          this.debugAdapterStreamLog("event.item/started", () => ({
            itemType,
            itemId: itemId ? shortWireId(itemId) : "",
            flushSegment: false,
            ...snapshotStreamState(),
          }));
        }
      } else if (event.method === "item/completed") {
        const params = (event.params as Record<string, unknown>) ?? {};
        const item = (params.item as Record<string, unknown>) ?? {};
        const itemType = String(item.type ?? "");
        if (itemType !== "agentMessage") {
          this.debugAdapterStreamLog("event.item/completed.skipped", () => ({
            itemType,
            itemId: String(item.id ?? "") ? shortWireId(String(item.id ?? "")) : "",
            ...snapshotStreamState(),
          }));
          continue;
        }
        const itemId = String(item.id ?? "");
        const payload = (item.payload as Record<string, unknown>) ?? {};
        const snap = typeof payload.text === "string" ? payload.text : "";
        pushOrder(itemId);
        const fromD = perItemDelta.get(itemId) ?? "";
        const canon = mergeReplyTextFromDeltaAndSnapshot(fromD, snap);
        perItemDelta.set(itemId, canon);
        if (itemId === activeAgentItemId) {
          activeAgentItemId = null;
        }
        lastDeltaAgentItemId = itemId;
        this.debugAdapterStreamLog("event.item/completed.agentMessage", () => ({
          itemId: shortWireId(itemId),
          deltaCharsBeforeMerge: fromD.length,
          snapshotChars: snap.length,
          mergedCharsAfter: canon.length,
          deltaPreview: previewWireText(fromD),
          snapshotPreview: previewWireText(snap),
          mergedPreview: previewWireText(canon),
          ...snapshotStreamState(),
        }));
      } else if (event.method === "turn/completed") {
        const params = (event.params as Record<string, unknown>) ?? {};
        const snapshots = extractAgentReplyTextsFromTurnCompletedParams(params);
        const lastSnap = snapshots.length > 0 ? snapshots[snapshots.length - 1] ?? "" : "";
        const unsentParts: Array<{ itemId: string | null; text: string }> = [];
        for (const itemId of itemOrder) {
          const tail = getUnsentTail(itemId, "");
          if (tail.length > 0) unsentParts.push({ itemId, text: tail });
        }
        if (orphanDeltaTail.length > 0) {
          unsentParts.push({ itemId: null, text: orphanDeltaTail });
          orphanDeltaTail = "";
        }
        let segmentText = unsentParts.map((part) => part.text).join("");
        if (!segmentText.trim() && lastSnap && !segmentsWereDelivered && itemOrder.length === 0) {
          segmentText = lastSnap;
        }
        if (segmentText.trim()) {
          segmentsWereDelivered = true;
          await this.onSegmentCompleted(threadId, turnId, segmentText, true, channelContext);
          if (unsentParts.length > 0) {
            for (const part of unsentParts) {
              markSegmentDelivered(part.itemId, part.text);
            }
          } else {
            const lastItemId = itemOrder.length > 0 ? itemOrder[itemOrder.length - 1] : null;
            markSegmentDelivered(lastItemId, segmentText);
          }
        }
        const snapshotText = extractAgentReplyTextFromTurnCompletedParams(params);
        const deltaText = itemOrder.map((id) => perItemDelta.get(id) ?? "").join("");
        const fullReply = mergeReplyTextFromDeltaAndSnapshot(deltaText, snapshotText);
        this.debugAdapterStreamLog("event.turn/completed", () => ({
          unsentParts: unsentParts.map((p) => ({
            itemId: p.itemId ? shortWireId(p.itemId) : "(orphan)",
            chars: p.text.length,
            preview: previewWireText(p.text),
          })),
          finalSegmentChars: segmentText.length,
          finalSegmentPreview: previewWireText(segmentText),
          snapshotConcatChars: snapshotText.length,
          deltaConcatChars: deltaText.length,
          deltaConcatPreview: previewWireText(deltaText),
          snapshotConcatPreview: previewWireText(snapshotText),
          fullReplyChars: fullReply.length,
          fullReplyPreview: previewWireText(fullReply),
          segmentsWereDelivered,
          ...snapshotStreamState(),
        }));
        await this.onTurnCompleted(threadId, turnId, fullReply, channelContext, segmentsWereDelivered);
        break;
      } else if (event.method === "turn/failed") {
        const err = String(
          ((event.params as Record<string, unknown>)?.turn as Record<string, unknown>)?.error ??
            "Unknown error",
        );
        this.debugAdapterStreamLog("event.turn/failed", () => ({
          error: previewWireText(err, 500),
          ...snapshotStreamState(),
        }));
        await this.onTurnFailed(threadId, turnId, err);
        break;
      } else if (event.method === "turn/cancelled") {
        this.debugAdapterStreamLog("event.turn/cancelled", () => snapshotStreamState());
        await this.onTurnCancelled(threadId, turnId);
        break;
      }
    }
  }

  protected async getOrCreateThread(
    identityKey: string,
    userId: string,
    channelContext: string,
    workspacePath: string,
  ): Promise<Thread> {
    if (this.forceFreshThreadIdentities.has(identityKey)) {
      const thread = await this.client.threadStart({
        channelName: this.channelName,
        userId,
        channelContext,
        workspacePath,
      });
      this.threadMap.set(identityKey, thread.id);
      this.forceFreshThreadIdentities.delete(identityKey);
      console.info(`Created fresh thread ${thread.id} for identity ${identityKey}`);
      return thread;
    }

    let threadId = this.threadMap.get(identityKey);
    if (threadId) {
      try {
        const thread = await this.client.threadRead(threadId);
        if (thread.status === "active") return thread;
        if (thread.status === "paused") return await this.client.threadResume(threadId);
      } catch {
        /* fall through */
      }
      this.threadMap.delete(identityKey);
    }

    const threads = await this.client.threadList({
      channelName: this.channelName,
      userId,
      channelContext,
      workspacePath,
    });
    const active = threads.filter((t) => t.status === "active" || t.status === "paused");
    if (active.length > 0) {
      let thread = active[0];
      if (thread.status === "paused") thread = await this.client.threadResume(thread.id);
      else thread = await this.client.threadRead(thread.id);
      this.threadMap.set(identityKey, thread.id);
      return thread;
    }

    const thread = await this.client.threadStart({
      channelName: this.channelName,
      userId,
      channelContext,
      workspacePath,
    });
    this.threadMap.set(identityKey, thread.id);
    console.info(`Created thread ${thread.id} for identity ${identityKey}`);
    return thread;
  }

  async newThread(userId: string, channelContext = ""): Promise<void> {
    await this.resetIdentityThreads(userId, channelContext);
  }
}
