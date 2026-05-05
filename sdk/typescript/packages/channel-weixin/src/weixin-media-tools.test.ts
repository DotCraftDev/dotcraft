import assert from "node:assert/strict";
import test from "node:test";
import { mkdtempSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { tmpdir } from "node:os";

import {
  WEIXIN_SEND_FILE_TOOL,
  WEIXIN_SEND_IMAGE_TOOL,
  WeixinMediaError,
  WeixinMediaTools,
  aesEcbPaddedSize,
  buildCdnUploadUrl,
  md5Hex,
  type WeixinMediaApi,
} from "./weixin-media-tools.js";
import type { GetUploadUrlReq, GetUploadUrlResp, SendMessageReq } from "./weixin-types.js";
import { MessageItemType } from "./weixin-types.js";
import { WeixinAdapter } from "./weixin-adapter.js";

class FakeWeixinApi implements WeixinMediaApi {
  readonly uploadBodies: GetUploadUrlReq[] = [];
  readonly sentBodies: SendMessageReq[] = [];

  constructor(private readonly uploadResponse: GetUploadUrlResp) {}

  async getUploadUrl(opts: { body: GetUploadUrlReq }): Promise<GetUploadUrlResp> {
    this.uploadBodies.push(opts.body);
    return this.uploadResponse;
  }

  async sendMessage(opts: { body: SendMessageReq }): Promise<void> {
    this.sentBodies.push(opts.body);
  }
}

class TestWeixinAdapter extends WeixinAdapter {
  exposeDeliveryCapabilities(): Record<string, unknown> | null {
    return this.getDeliveryCapabilities();
  }

  exposeChannelTools(): Record<string, unknown>[] | null {
    return this.getChannelTools();
  }

  async exposeSend(target: string, message: Record<string, unknown>): Promise<Record<string, unknown>> {
    return await this.onSend(target, message, {});
  }
}

test("crypto helpers calculate AES padded size and md5 metadata", () => {
  assert.equal(aesEcbPaddedSize(0), 16);
  assert.equal(aesEcbPaddedSize(15), 16);
  assert.equal(aesEcbPaddedSize(16), 32);
  assert.equal(md5Hex(Buffer.from("abc", "utf-8")), "900150983cd24fb0d6963f7d28e17f72");
});

test("buildCdnUploadUrl prefers full URL and falls back to documented query shape", () => {
  assert.equal(
    buildCdnUploadUrl({
      cdnBaseUrl: "https://cdn.example/c2c",
      uploadFullUrl: "https://upload.example/full",
      uploadParam: "ignored",
      filekey: "file",
    }),
    "https://upload.example/full",
  );
  assert.equal(
    buildCdnUploadUrl({
      cdnBaseUrl: "https://cdn.example/c2c",
      uploadParam: "a+b",
      filekey: "file name.txt",
    }),
    "https://cdn.example/c2c/upload?encrypted_query_param=a%2Bb&filekey=file%20name.txt",
  );
});

test("sendStructuredMessage sends a file_item after encrypted CDN upload", async () => {
  const originalFetch = globalThis.fetch;
  const api = new FakeWeixinApi({ upload_full_url: "https://upload.example/file" });
  let uploadedBytes = 0;
  globalThis.fetch = (async (_input: string | URL | Request, init?: RequestInit) => {
    uploadedBytes = Buffer.from((init?.body as Uint8Array) ?? []).length;
    return new Response("", { status: 200, headers: { "x-encrypted-param": "download-file" } });
  }) as typeof fetch;

  try {
    const tools = new WeixinMediaTools(api);
    const result = await tools.sendStructuredMessage({
      baseUrl: "https://ilink.example",
      token: "token",
      toUserId: "user@im.wechat",
      contextToken: "ctx",
      clientId: "client-1",
      message: {
        kind: "file",
        fileName: "report.txt",
        source: { kind: "dataBase64", dataBase64: Buffer.from("hello").toString("base64") },
      },
    });

    assert.equal(result.delivered, true);
    assert.equal(uploadedBytes, 16);
    assert.equal(api.uploadBodies[0]?.media_type, 3);
    assert.equal(api.uploadBodies[0]?.rawsize, 5);
    assert.equal(api.uploadBodies[0]?.filesize, 16);
    assert.equal(api.uploadBodies[0]?.no_need_thumb, true);
    const item = api.sentBodies[0]?.msg?.item_list?.[0];
    assert.equal(item?.type, MessageItemType.FILE);
    assert.equal(item?.file_item?.file_name, "report.txt");
    assert.equal(item?.file_item?.media?.encrypt_query_param, "download-file");
    assert.equal(item?.file_item?.len, "5");
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("sendStructuredMessage sends an image_item with thumbnail media", async () => {
  const originalFetch = globalThis.fetch;
  const api = new FakeWeixinApi({ upload_param: "image-upload", thumb_upload_param: "thumb-upload" });
  const uploadedUrls: string[] = [];
  globalThis.fetch = (async (input: string | URL | Request) => {
    uploadedUrls.push(String(input));
    const token = uploadedUrls.length === 1 ? "download-image" : "download-thumb";
    return new Response("", { status: 200, headers: { "x-encrypted-param": token } });
  }) as typeof fetch;

  try {
    const tools = new WeixinMediaTools(api, "https://cdn.example/c2c");
    await tools.sendStructuredMessage({
      baseUrl: "https://ilink.example",
      token: "token",
      toUserId: "user@im.wechat",
      contextToken: "ctx",
      clientId: "client-1",
      message: {
        kind: "image",
        fileName: "image.jpg",
        source: { kind: "dataBase64", dataBase64: Buffer.from("image-bytes").toString("base64") },
      },
    });

    assert.equal(api.uploadBodies[0]?.media_type, 1);
    assert.equal(api.uploadBodies[0]?.thumb_rawsize, 11);
    assert.equal(uploadedUrls[0]?.includes("encrypted_query_param=image-upload"), true);
    assert.equal(uploadedUrls[1]?.includes("encrypted_query_param=thumb-upload"), true);
    const item = api.sentBodies[0]?.msg?.item_list?.[0];
    assert.equal(item?.type, MessageItemType.IMAGE);
    assert.equal(item?.image_item?.media?.encrypt_query_param, "download-image");
    assert.equal(item?.image_item?.thumb_media?.encrypt_query_param, "download-thumb");
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("media tools reject invalid sources", async () => {
  const tools = new WeixinMediaTools(new FakeWeixinApi({ upload_full_url: "https://upload.example/file" }));

  await assert.rejects(
    async () =>
      await tools.sendStructuredMessage({
        baseUrl: "https://ilink.example",
        toUserId: "user@im.wechat",
        clientId: "client",
        message: { kind: "file", source: { kind: "dataBase64", dataBase64: "%%%" } },
      }),
    (error: unknown) => error instanceof WeixinMediaError && error.code === "InvalidArguments",
  );

  await assert.rejects(
    async () =>
      await tools.sendStructuredMessage({
        baseUrl: "https://ilink.example",
        toUserId: "user@im.wechat",
        clientId: "client",
        message: { kind: "file", source: { kind: "url", url: "https://example.com/a.pdf" } },
      }),
    (error: unknown) => error instanceof WeixinMediaError && error.code === "UnsupportedMediaSource",
  );
});

test("tool call resolves host path file names and exposes approval metadata", async () => {
  const tempDir = mkdtempSync(join(tmpdir(), "weixin-file-"));
  const filePath = join(tempDir, "report.txt");
  writeFileSync(filePath, "hello", "utf-8");

  const originalFetch = globalThis.fetch;
  const api = new FakeWeixinApi({ upload_full_url: "https://upload.example/file" });
  globalThis.fetch = (async () =>
    new Response("", { status: 200, headers: { "x-encrypted-param": "download-file" } })) as typeof fetch;

  try {
    const tools = new WeixinMediaTools(api);
    const toolDescriptors = tools.getChannelTools();
    assert.equal(toolDescriptors[0]?.name, WEIXIN_SEND_FILE_TOOL);
    assert.deepEqual((toolDescriptors[0]?.approval as Record<string, unknown>).targetArgument, "filePath");
    assert.equal(toolDescriptors[1]?.name, WEIXIN_SEND_IMAGE_TOOL);
    assert.deepEqual((toolDescriptors[1]?.approval as Record<string, unknown>).targetArgument, "imagePath");

    const result = await tools.executeToolCall({
      baseUrl: "https://ilink.example",
      toUserId: "user@im.wechat",
      clientId: "client",
      toolName: WEIXIN_SEND_FILE_TOOL,
      args: { filePath },
    });
    assert.equal(result.success, true);
    assert.equal(api.sentBodies[0]?.msg?.item_list?.[0]?.file_item?.file_name, "report.txt");
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("adapter advertises real file and image media capabilities", () => {
  const adapter = new TestWeixinAdapter();
  const capabilities = adapter.exposeDeliveryCapabilities() as Record<string, unknown>;
  const media = capabilities.media as Record<string, Record<string, unknown>>;
  assert.equal(media.file?.supportsHostPath, true);
  assert.equal(media.file?.supportsBase64, true);
  assert.equal(media.image?.supportsHostPath, true);
  assert.equal(media.image?.supportsBase64, true);
});

test("adapter sends caption as normalized follow-up text after media delivery", async () => {
  const originalFetch = globalThis.fetch;
  const adapter = new TestWeixinAdapter();
  const internals = adapter as unknown as {
    apiBaseUrl: string;
    botToken: string;
    contextTokens: Record<string, string>;
    mediaTools: {
      sendStructuredMessage(): Promise<Record<string, unknown>>;
      getDeliveryCapabilities(): Record<string, unknown>;
      getChannelTools(): Record<string, unknown>[];
    };
  };
  internals.apiBaseUrl = "https://ilink.example";
  internals.botToken = "token";
  internals.contextTokens = { "user@im.wechat": "ctx" };
  internals.mediaTools = {
    async sendStructuredMessage(): Promise<Record<string, unknown>> {
      return { delivered: true, remoteMediaId: "media-id" };
    },
    getDeliveryCapabilities(): Record<string, unknown> {
      return {};
    },
    getChannelTools(): Record<string, unknown>[] {
      return [];
    },
  };

  let followUpBody: Record<string, unknown> = {};
  globalThis.fetch = (async (_input: string | URL | Request, init?: RequestInit) => {
    followUpBody = JSON.parse(String(init?.body)) as Record<string, unknown>;
    return new Response("", { status: 200 });
  }) as typeof fetch;

  try {
    const result = await adapter.exposeSend("user@im.wechat", {
      kind: "file",
      caption: "**hello** [site](https://example.com)",
      source: { kind: "dataBase64", dataBase64: Buffer.from("x").toString("base64") },
    });
    assert.equal(result.delivered, true);
    const msg = followUpBody.msg as Record<string, unknown>;
    const item = (msg.item_list as Array<Record<string, { text?: string }>>)[0];
    assert.equal(item?.text_item?.text, "hello site");
    assert.equal(msg.context_token, "ctx");
  } finally {
    globalThis.fetch = originalFetch;
  }
});
