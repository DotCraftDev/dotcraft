import assert from "node:assert/strict";
import test from "node:test";

import { FeishuClient } from "./feishu-client.js";
import { FeishuApiError } from "./feishu-types.js";

function createClient(): FeishuClient {
  return new FeishuClient({
    appId: "cli_test",
    appSecret: "secret_test",
  });
}

test("FeishuApiError preserves public fields and readable message", () => {
  const error = new FeishuApiError({
    kind: "rateLimited",
    retryable: true,
    code: 99991429,
    msg: "too many requests",
    httpStatus: 429,
    raw: { code: 99991429 },
    message: "Failed to send Feishu text message. too many requests code=99991429 httpStatus=429",
  });

  assert.equal(error.kind, "rateLimited");
  assert.equal(error.retryable, true);
  assert.equal(error.code, 99991429);
  assert.equal(error.msg, "too many requests");
  assert.equal(error.httpStatus, 429);
  assert.match(error.message, /too many requests/);
});

test("Feishu client classifies fetch failures with FeishuApiError", async () => {
  const client = createClient();
  const originalFetch = globalThis.fetch;
  globalThis.fetch = (async (input: string | URL | Request) => {
    const url = String(input);
    if (url.includes("/tenant_access_token/internal")) {
      return new Response(
        JSON.stringify({
          code: 0,
          tenant_access_token: "tenant_token",
          expire: 7200,
        }),
        { status: 200, headers: { "Content-Type": "application/json" } },
      );
    }

    return new Response(
      JSON.stringify({
        code: 99991429,
        msg: "rate limited",
      }),
      { status: 429, headers: { "Content-Type": "application/json" } },
    );
  }) as typeof fetch;

  try {
    await assert.rejects(
      async () =>
        await (client as unknown as {
          sendMessage: (
            receiveId: string,
            receiveIdType: "chat_id" | "open_id",
            msgType: string,
            content: Record<string, unknown>,
          ) => Promise<Record<string, unknown>>;
        }).sendMessage("oc_chat_123", "chat_id", "text", { text: "hello" }),
      (error: unknown) => {
        assert.ok(error instanceof FeishuApiError);
        assert.equal(error.kind, "rateLimited");
        assert.equal(error.retryable, true);
        assert.equal(error.code, 99991429);
        assert.equal(error.httpStatus, 429);
        assert.match(error.message, /rate limited/);
        return true;
      },
    );
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("Feishu client classifies SDK failures with FeishuApiError", async () => {
  const client = createClient();
  (client as unknown as { sdk: unknown }).sdk = {
    im: {
      messageReaction: {
        async create(): Promise<void> {
          throw {
            code: 99991403,
            msg: "permission denied",
          };
        },
      },
    },
  };

  await assert.rejects(
    async () => await client.addMessageReaction("om_test_1", "GLANCE"),
    (error: unknown) => {
      assert.ok(error instanceof FeishuApiError);
      assert.equal(error.kind, "permission");
      assert.equal(error.retryable, false);
      assert.equal(error.code, 99991403);
      assert.match(error.message, /permission denied/);
      return true;
    },
  );
});

test("Feishu client keeps successful SDK response shape", async () => {
  const client = createClient();
  (client as unknown as { sdk: unknown }).sdk = {
    im: {
      message: {
        async create(): Promise<{ data: { message_id: string; chat_id: string } }> {
          return {
            data: {
              message_id: "om_success_1",
              chat_id: "oc_chat_123",
            },
          };
        },
      },
    },
  };

  const result = await client.sendInteractiveCard("group:oc_chat_123", {
    schema: "1.0",
  });

  assert.deepEqual(result, {
    messageId: "om_success_1",
    chatId: "oc_chat_123",
  });
});
