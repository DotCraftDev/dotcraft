/**
 * ChannelAdapter: high-level base class for external channel adapters.
 */

import { DotCraftClient, DotCraftError } from "./client.js";
import {
  ERR_THREAD_NOT_ACTIVE,
  ERR_TURN_IN_PROGRESS,
  Thread,
  Turn,
  textPart,
} from "./models.js";
import { Transport } from "./transport.js";
import {
  extractAgentReplyTextFromTurnCompletedParams,
  extractAgentReplyTextsFromTurnCompletedParams,
  mergeReplyTextFromDeltaAndSnapshot,
} from "./turnReply.js";

/** Queued inbound message; skipCommand skips slash handling for expanded prompts. */
type ChannelAdapterMessageOpts = {
  userId: string;
  userName: string;
  text: string;
  channelContext?: string;
  workspacePath?: string;
  senderExtra?: Record<string, unknown>;
  skipCommand?: boolean;
};

export abstract class ChannelAdapter {
  protected readonly client: DotCraftClient;
  protected readonly channelName: string;
  private readonly clientName: string;
  private readonly clientVersion: string;
  private readonly optOutNotifications: string[];

  protected readonly threadMap = new Map<string, string>();
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
  ) {
    this.client = new DotCraftClient(transport);
    this.channelName = channelName;
    this.clientName = clientName;
    this.clientVersion = clientVersion;
    this.optOutNotifications = optOutNotifications;
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

  protected async onTurnCompleted(
    threadId: string,
    turnId: string,
    replyText: string,
    channelContext: string,
  ): Promise<void> {
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
        if (channelContext) sender.groupId = channelContext;
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
  private enqueueMessage(opts: ChannelAdapterMessageOpts): void {
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

  private identityKey(userId: string, channelContext: string): string {
    return `${userId}:${channelContext}`;
  }

  private async processMessage(
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

    const sender: Record<string, unknown> = {
      senderId: opts.userId,
      senderName: opts.userName,
      ...senderExtra,
    };
    if (channelContext) sender.groupId = channelContext;

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

    let turn: Turn;
    try {
      turn = await this.client.turnStart(thread.id, [textPart(opts.text)], sender);
    } catch (e) {
      if (e instanceof DotCraftError && e.code === ERR_TURN_IN_PROGRESS) {
        await new Promise((r) => setTimeout(r, 1000));
        this.enqueueMessage(opts);
        return;
      }
      if (e instanceof DotCraftError && e.code === ERR_THREAD_NOT_ACTIVE) {
        await this.client.threadResume(thread.id);
        turn = await this.client.turnStart(thread.id, [textPart(opts.text)], sender);
      } else {
        throw e;
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
          if (segmentText.trim()) {
            await this.onSegmentCompleted(
              thread.id,
              turn.id,
              segmentText,
              false,
              channelContext,
            );
          }
        }
      } else if (event.method === "turn/completed") {
        const params = (event.params as Record<string, unknown>) ?? {};
        const segmentTextFromDelta = currentSegmentParts.join("");
        const segmentText =
          segmentTextFromDelta.trim().length > 0
            ? segmentTextFromDelta
            : (() => {
                const snapshots = extractAgentReplyTextsFromTurnCompletedParams(params);
                return snapshots.length > 0 ? snapshots[snapshots.length - 1] : "";
              })();
        if (segmentText.trim()) {
          await this.onSegmentCompleted(
            thread.id,
            turn.id,
            segmentText,
            true,
            channelContext,
          );
        }
        const snapshotText = extractAgentReplyTextFromTurnCompletedParams(params);
        const deltaText = allDeltaParts.join("");
        const fullReply = mergeReplyTextFromDeltaAndSnapshot(deltaText, snapshotText);
        await this.onTurnCompleted(thread.id, turn.id, fullReply, channelContext);
        break;
      } else if (event.method === "turn/failed") {
        const err = String(
          ((event.params as Record<string, unknown>)?.turn as Record<string, unknown>)?.error ??
            "Unknown error",
        );
        await this.onTurnFailed(thread.id, turn.id, err);
        break;
      } else if (event.method === "turn/cancelled") {
        await this.onTurnCancelled(thread.id, turn.id);
        break;
      }
    }
  }

  private async getOrCreateThread(
    identityKey: string,
    userId: string,
    channelContext: string,
    workspacePath: string,
  ): Promise<Thread> {
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
    const identityKey = this.identityKey(userId, channelContext);
    const oldId = this.threadMap.get(identityKey);
    if (oldId) {
      try {
        await this.client.threadArchive(oldId);
        console.info(`Archived thread ${oldId} for ${identityKey}`);
      } catch (e) {
        console.warn(`Could not archive thread ${oldId}:`, e);
      }
      this.threadMap.delete(identityKey);
    }
  }
}
