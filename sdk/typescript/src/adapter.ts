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

  async handleMessage(opts: {
    userId: string;
    userName: string;
    text: string;
    channelContext?: string;
    workspacePath?: string;
    senderExtra?: Record<string, unknown>;
  }): Promise<void> {
    this.enqueueMessage(opts);
  }

  /**
   * Schedule a message for serial processing (one turn at a time per identity).
   * Does not wait for the turn to complete (matches Python asyncio.Queue.put).
   */
  private enqueueMessage(opts: {
    userId: string;
    userName: string;
    text: string;
    channelContext?: string;
    workspacePath?: string;
    senderExtra?: Record<string, unknown>;
  }): void {
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
    opts: {
      userId: string;
      userName: string;
      text: string;
      channelContext?: string;
      workspacePath?: string;
      senderExtra?: Record<string, unknown>;
    },
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

    const replyParts: string[] = [];
    for await (const event of this.client.streamEvents(thread.id)) {
      if (event.method === "item/agentMessage/delta") {
        const delta = String((event.params as Record<string, unknown>)?.delta ?? "");
        replyParts.push(delta);
      } else if (event.method === "turn/completed") {
        const fullReply = replyParts.join("");
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
