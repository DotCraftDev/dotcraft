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

test("Feishu client sends text messages through the shared message API", async () => {
  const client = createClient();
  const originalFetch = globalThis.fetch;
  const calls: Array<{ url: string; init?: RequestInit }> = [];
  globalThis.fetch = (async (input: string | URL | Request, init?: RequestInit) => {
    const url = String(input);
    calls.push({ url, init });
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
        code: 0,
        data: {
          message_id: "om_text_1",
          chat_id: "oc_chat_123",
        },
      }),
      { status: 200, headers: { "Content-Type": "application/json" } },
    );
  }) as typeof fetch;

  try {
    const result = await client.sendTextMessage("group:oc_chat_123", "hello text");
    assert.deepEqual(result, {
      messageId: "om_text_1",
      chatId: "oc_chat_123",
    });
    assert.equal(calls.length, 2);
    assert.match(calls[1]!.url, /receive_id_type=chat_id/);
    const body = JSON.parse(String(calls[1]!.init?.body ?? "{}")) as Record<string, string>;
    assert.equal(body.msg_type, "text");
    assert.deepEqual(JSON.parse(body.content), { text: "hello text" });
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("Feishu client rejects empty text sends before calling the API", async () => {
  const client = createClient();
  await assert.rejects(
    async () => await client.sendTextMessage("group:oc_chat_123", "   "),
    (error: unknown) => {
      assert.ok(error instanceof TypeError);
      assert.match(String((error as Error).message), /non-empty text/);
      return true;
    },
  );
});

test("Feishu client replies to a message with explicit token auth", async () => {
  const client = createClient();
  const originalFetch = globalThis.fetch;
  const calls: Array<{ url: string; init?: RequestInit }> = [];
  globalThis.fetch = (async (input: string | URL | Request, init?: RequestInit) => {
    const url = String(input);
    calls.push({ url, init });
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
        code: 0,
        data: {
          message_id: "om_reply_1",
          chat_id: "oc_chat_456",
        },
      }),
      { status: 200, headers: { "Content-Type": "application/json" } },
    );
  }) as typeof fetch;

  try {
    const result = await client.replyToMessage("om_origin_1", "reply text", {
      replyInThread: true,
      uuid: "uuid-1",
    });
    assert.deepEqual(result, {
      messageId: "om_reply_1",
      chatId: "oc_chat_456",
    });
    assert.equal(calls.length, 2);
    assert.match(calls[1]!.url, /\/messages\/om_origin_1\/reply$/);
    assert.equal((calls[1]!.init?.headers as Record<string, string>).Authorization, "Bearer tenant_token");
    const body = JSON.parse(String(calls[1]!.init?.body ?? "{}")) as Record<string, string | boolean>;
    assert.equal(body.msg_type, "text");
    assert.equal(body.reply_in_thread, true);
    assert.equal(body.uuid, "uuid-1");
    assert.deepEqual(JSON.parse(String(body.content)), { text: "reply text" });
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("Feishu client classifies reply failures with FeishuApiError", async () => {
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
        code: 99991403,
        msg: "permission denied",
      }),
      { status: 403, headers: { "Content-Type": "application/json" } },
    );
  }) as typeof fetch;

  try {
    await assert.rejects(
      async () => await client.replyToMessage("om_origin_1", "reply text"),
      (error: unknown) => {
        assert.ok(error instanceof FeishuApiError);
        assert.equal(error.kind, "permission");
        assert.equal(error.httpStatus, 403);
        return true;
      },
    );
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("Feishu client probes bot info with explicit tenant token and parses identity", async () => {
  const client = createClient();
  const originalFetch = globalThis.fetch;
  const calls: Array<{ url: string; init?: RequestInit }> = [];
  globalThis.fetch = (async (input: string | URL | Request, init?: RequestInit) => {
    const url = String(input);
    calls.push({ url, init });
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
        code: 0,
        data: {
          app_name: "DotCraft",
          tenant_key: "tenant-1",
          activate_status: 1,
        },
        bot: {
          open_id: "ou_bot_123",
          app_name: "DotCraft",
        },
      }),
      { status: 200, headers: { "Content-Type": "application/json" } },
    );
  }) as typeof fetch;

  try {
    const result = await client.probeBot();
    assert.equal(result.openId, "ou_bot_123");
    assert.equal(result.hasBotIdentity, true);
    assert.equal(result.appName, "DotCraft");
    assert.equal(result.diagnosticTag, undefined);
    assert.ok(result.rawFieldKeys?.includes("bot.open_id"));
    assert.match(calls[1]!.url, /\/bot\/v3\/info$/);
    assert.equal((calls[1]!.init?.headers as Record<string, string>).Authorization, "Bearer tenant_token");
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("Feishu client marks missing bot identity as capability disabled when status hints say so", async () => {
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
        code: 0,
        data: {
          app_name: "DotCraft",
          activate_status: 0,
        },
      }),
      { status: 200, headers: { "Content-Type": "application/json" } },
    );
  }) as typeof fetch;

  try {
    const result = await client.probeBot();
    assert.equal(result.hasBotIdentity, false);
    assert.equal(result.diagnosticTag, "botCapabilityDisabled");
    assert.match(result.diagnosticMessage ?? "", /disabled|unpublished/);
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("Feishu client marks missing bot identity as shape mismatch when capability hints are absent", async () => {
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
        code: 0,
        data: {
          app_name: "DotCraft",
        },
      }),
      { status: 200, headers: { "Content-Type": "application/json" } },
    );
  }) as typeof fetch;

  try {
    const result = await client.probeBot();
    assert.equal(result.hasBotIdentity, false);
    assert.equal(result.diagnosticTag, "identityFieldsMissing");
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("Feishu client raises auth errors when bot probe authorization fails", async () => {
  const client = createClient();
  const originalFetch = globalThis.fetch;
  globalThis.fetch = (async (_input: string | URL | Request) =>
    new Response(
      JSON.stringify({
        code: 99991663,
        msg: "token invalid",
      }),
      { status: 401, headers: { "Content-Type": "application/json" } },
    )) as typeof fetch;

  try {
    await assert.rejects(
      async () => await client.probeBot(),
      (error: unknown) => {
        assert.ok(error instanceof FeishuApiError);
        assert.equal(error.kind, "auth");
        return true;
      },
    );
  } finally {
    globalThis.fetch = originalFetch;
  }
});
