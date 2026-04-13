import assert from "node:assert/strict";
import test from "node:test";

import { ChannelAdapter } from "./adapter.js";
import { Thread, Turn } from "./models.js";
import { shouldFlushSegmentOnItemStarted } from "./segmentBoundaries.js";
import type { Transport } from "./transport.js";

class NoopTransport implements Transport {
  async readMessage(): Promise<Record<string, unknown>> {
    throw new Error("Not implemented in tests");
  }

  async writeMessage(_msg: Record<string, unknown>): Promise<void> {}

  async close(): Promise<void> {}
}

class RecordingAdapter extends ChannelAdapter {
  readonly segments: Array<{ text: string; isFinal: boolean; channelContext: string }> = [];
  readonly completedReplies: string[] = [];

  constructor() {
    super(new NoopTransport(), "test-channel", "test-client", "0.0.0");
  }

  async onDeliver(_target: string, _content: string, _metadata: Record<string, unknown>): Promise<boolean> {
    return true;
  }

  async onApprovalRequest(_request: Record<string, unknown>): Promise<string> {
    return "cancel";
  }

  protected override async onSegmentCompleted(
    _threadId: string,
    _turnId: string,
    segmentText: string,
    isFinal: boolean,
    channelContext: string,
  ): Promise<void> {
    this.segments.push({ text: segmentText, isFinal, channelContext });
  }

  protected override async onTurnCompleted(
    _threadId: string,
    _turnId: string,
    replyText: string,
    _channelContext: string,
  ): Promise<void> {
    this.completedReplies.push(replyText);
  }
}

test("should flush segments for external channel tool calls", () => {
  assert.equal(shouldFlushSegmentOnItemStarted("toolCall"), true);
  assert.equal(shouldFlushSegmentOnItemStarted("externalChannelToolCall"), true);
  assert.equal(shouldFlushSegmentOnItemStarted("agentMessage"), false);
});

test("ChannelAdapter flushes the current segment before external channel tool calls", async () => {
  const adapter = new RecordingAdapter();
  const client = (adapter as unknown as { client: Record<string, unknown> }).client;

  (adapter as unknown as {
    getOrCreateThread: (...args: unknown[]) => Promise<Thread>;
  }).getOrCreateThread = async () => new Thread("thread-1", "active");

  client.turnStart = async () => new Turn("turn-1", "thread-1", "running");
  client.streamEvents = async function* () {
    yield { method: "item/agentMessage/delta", params: { delta: "before " } };
    yield {
      method: "item/started",
      params: {
        threadId: "thread-1",
        item: { type: "externalChannelToolCall" },
      },
    };
    yield { method: "item/agentMessage/delta", params: { delta: "after" } };
    yield {
      method: "turn/completed",
      params: {
        threadId: "thread-1",
        turn: {
          items: [{ type: "agentMessage", payload: { text: "before after" } }],
        },
      },
    };
  };

  await (adapter as unknown as {
    processMessage: (identityKey: string, opts: Record<string, unknown>) => Promise<void>;
  }).processMessage("user-1:chat-1", {
    userId: "user-1",
    userName: "Tester",
    text: "hello",
    channelContext: "chat-1",
  });

  assert.deepEqual(adapter.segments, [
    { text: "before ", isFinal: false, channelContext: "chat-1" },
    { text: "after", isFinal: true, channelContext: "chat-1" },
  ]);
  assert.deepEqual(adapter.completedReplies, ["before after"]);
});
