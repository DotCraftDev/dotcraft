import assert from "node:assert/strict";
import test from "node:test";

import { FeishuAdapter } from "./feishu-adapter.js";
import type { FeishuClient } from "./feishu-client.js";
import {
  APPEND_DOCX_TOOL_NAME,
  CREATE_DOCX_TOOL_NAME,
  extractDocxDocumentId,
  getFeishuDocxChannelTools,
  maybeExecuteFeishuDocxToolCall,
  READ_DOCX_TOOL_NAME,
  resolveDocxDocumentId,
  toFeishuDocxChildren,
  type FeishuSimpleBlock,
} from "./feishu-docx-tools.js";
import { FeishuApiError, type FeishuConfig } from "./feishu-types.js";
import {
  CREATE_WIKI_NODE_TOOL_NAME,
  GET_WIKI_NODE_INFO_TOOL_NAME,
  GET_WIKI_SPACE_TOOL_NAME,
  LIST_WIKI_NODES_TOOL_NAME,
  LIST_WIKI_SPACES_TOOL_NAME,
  MOVE_DOCX_TO_WIKI_TOOL_NAME,
  MOVE_WIKI_NODE_TOOL_NAME,
} from "./feishu-wiki-tools.js";

const DOC_ID = "DocxPlaceholder000000000001";
const LEGACY_DOC_ID = "doxABCDEFGHIJKLMNOPQRSTUVWX";
const WIKI_NODE_ID = "WikiNodePlaceholder00000001";
const WIKI_SPACE_ID = "1234567890123456789";

function validConfig(): FeishuConfig {
  return {
    dotcraft: {
      wsUrl: "ws://127.0.0.1:9100/ws",
    },
    feishu: {
      appId: "cli_test",
      appSecret: "test-secret",
      tools: {
        docs: {
          enabled: true,
        },
      },
    },
  };
}

test("docx channel tool registry only returns tools when enabled", () => {
  assert.deepEqual(getFeishuDocxChannelTools(false), []);
  const tools = getFeishuDocxChannelTools(true);
  assert.deepEqual(
    tools.map((tool) => String(tool.name ?? "")),
    [CREATE_DOCX_TOOL_NAME, READ_DOCX_TOOL_NAME, APPEND_DOCX_TOOL_NAME],
  );
});

test("docx mutating tools declare remoteResource approval and read tool does not", () => {
  const tools = getFeishuDocxChannelTools(true);
  const byName = new Map(tools.map((tool) => [String(tool.name ?? ""), tool]));

  const createTool = byName.get(CREATE_DOCX_TOOL_NAME) as Record<string, unknown> | undefined;
  assert.ok(createTool, "create tool is registered");
  assert.deepEqual(createTool!.approval, {
    kind: "remoteResource",
    targetArgument: "title",
    operation: "create",
  });
  const createSchema = createTool!.inputSchema as { required?: string[] };
  assert.deepEqual(createSchema.required, ["title"]);

  const appendTool = byName.get(APPEND_DOCX_TOOL_NAME) as Record<string, unknown> | undefined;
  assert.ok(appendTool, "append tool is registered");
  assert.deepEqual(appendTool!.approval, {
    kind: "remoteResource",
    targetArgument: "documentIdOrUrl",
    operation: "append",
  });

  const readTool = byName.get(READ_DOCX_TOOL_NAME) as Record<string, unknown> | undefined;
  assert.ok(readTool, "read tool is registered");
  assert.equal(readTool!.approval, undefined);
});

test("FeishuAdapter only registers docx tools when docs config is enabled", () => {
  const adapter = new FeishuAdapter();
  const getChannelTools = (adapter as unknown as { getChannelTools: () => Record<string, unknown>[] | null }).getChannelTools;

  Reflect.set(adapter as object, "loadedConfig", {
    ...validConfig(),
    feishu: {
      ...validConfig().feishu,
      tools: {
        docs: {
          enabled: false,
        },
      },
    },
  } satisfies FeishuConfig);
  const toolsWhenDisabled = getChannelTools.call(adapter) ?? [];
  assert.deepEqual(
    toolsWhenDisabled.map((tool) => String(tool.name ?? "")),
    ["FeishuSendFileToCurrentChat"],
  );

  Reflect.set(adapter as object, "loadedConfig", validConfig());
  const toolsWhenEnabled = getChannelTools.call(adapter) ?? [];
  assert.deepEqual(
    toolsWhenEnabled.map((tool) => String(tool.name ?? "")),
    [
      "FeishuSendFileToCurrentChat",
      CREATE_DOCX_TOOL_NAME,
      READ_DOCX_TOOL_NAME,
      APPEND_DOCX_TOOL_NAME,
      LIST_WIKI_NODES_TOOL_NAME,
      GET_WIKI_NODE_INFO_TOOL_NAME,
      MOVE_DOCX_TO_WIKI_TOOL_NAME,
      MOVE_WIKI_NODE_TOOL_NAME,
      LIST_WIKI_SPACES_TOOL_NAME,
      GET_WIKI_SPACE_TOOL_NAME,
      CREATE_WIKI_NODE_TOOL_NAME,
    ],
  );
});

test("extractDocxDocumentId accepts raw docx IDs and docx URLs", () => {
  assert.equal(extractDocxDocumentId(DOC_ID), DOC_ID);
  assert.equal(extractDocxDocumentId(`https://feishu.cn/docx/${DOC_ID}`), DOC_ID);
  assert.equal(extractDocxDocumentId(`https://larksuite.com/docx/${DOC_ID}?from=wiki`), DOC_ID);
  assert.equal(extractDocxDocumentId(LEGACY_DOC_ID), LEGACY_DOC_ID);
  assert.equal(extractDocxDocumentId(`https://feishu.cn/docx/${LEGACY_DOC_ID}`), LEGACY_DOC_ID);
});

test("extractDocxDocumentId rejects legacy /doc/ URLs with UnsupportedLegacyDoc", () => {
  assert.throws(
    () => extractDocxDocumentId(`https://example.feishu.cn/doc/doccnABCDEFGHIJKLMNOPQRSTUV`),
    (error: unknown) => {
      const record = error as { code?: string; message?: string };
      return record.code === "UnsupportedLegacyDoc" && typeof record.message === "string";
    },
  );
});

test("resolveDocxDocumentId resolves wiki node tokens to backing docx IDs", async () => {
  const client = {
    async getWikiNode(nodeToken: string) {
      assert.equal(nodeToken, WIKI_NODE_ID);
      return {
        spaceId: WIKI_SPACE_ID,
        nodeToken,
        objToken: DOC_ID,
        objType: "docx",
        nodeType: "origin",
      };
    },
  } as unknown as FeishuClient;
  const resolved = await resolveDocxDocumentId({
    client,
    documentIdOrUrl: `https://example.feishu.cn/wiki/${WIKI_NODE_ID}`,
  });
  assert.equal(resolved, DOC_ID);
});

test("resolveDocxDocumentId rejects wiki nodes that are not backed by docx", async () => {
  const client = {
    async getWikiNode(nodeToken: string) {
      assert.equal(nodeToken, WIKI_NODE_ID);
      return {
        spaceId: WIKI_SPACE_ID,
        nodeToken,
        objToken: "shtcn123",
        objType: "sheet",
        nodeType: "origin",
      };
    },
  } as unknown as FeishuClient;
  await assert.rejects(
    async () =>
      await resolveDocxDocumentId({
        client,
        documentIdOrUrl: `https://feishu.cn/wiki/${WIKI_NODE_ID}`,
      }),
    (error: unknown) => String((error as Error).message).includes("not docx"),
  );
});

test("resolveDocxDocumentId resolves bare ambiguous tokens via wiki first", async () => {
  let calls = 0;
  const client = {
    async getWikiNode(nodeToken: string) {
      calls += 1;
      assert.equal(nodeToken, WIKI_NODE_ID);
      return {
        spaceId: WIKI_SPACE_ID,
        nodeToken,
        objToken: DOC_ID,
        objType: "docx",
        nodeType: "origin",
      };
    },
  } as unknown as FeishuClient;

  const resolved = await resolveDocxDocumentId({
    client,
    documentIdOrUrl: WIKI_NODE_ID,
  });
  assert.equal(resolved, DOC_ID);
  assert.equal(calls, 1);
});

test("resolveDocxDocumentId falls back to bare docx ID when wiki lookup fails", async () => {
  let calls = 0;
  const client = {
    async getWikiNode(nodeToken: string) {
      calls += 1;
      assert.equal(nodeToken, DOC_ID);
      throw new Error("node not found");
    },
  } as unknown as FeishuClient;

  const resolved = await resolveDocxDocumentId({
    client,
    documentIdOrUrl: DOC_ID,
  });
  assert.equal(resolved, DOC_ID);
  assert.equal(calls, 1);
});

test("resolveDocxDocumentId keeps docx URL path and does not call wiki lookup", async () => {
  let calls = 0;
  const client = {
    async getWikiNode() {
      calls += 1;
      throw new Error("unexpected call");
    },
  } as unknown as FeishuClient;

  const resolved = await resolveDocxDocumentId({
    client,
    documentIdOrUrl: `https://feishu.cn/docx/${DOC_ID}`,
  });
  assert.equal(resolved, DOC_ID);
  assert.equal(calls, 0);
});

test("FeishuReadDocxContent accepts docx URLs and returns raw content", async () => {
  const client = {
    async getDocxRawContent(documentId: string) {
      return {
        documentId,
        content: "hello from docx",
      };
    },
  } as unknown as FeishuClient;

  const result = await maybeExecuteFeishuDocxToolCall({
    toolName: READ_DOCX_TOOL_NAME,
    args: {
      documentIdOrUrl: `https://feishu.cn/docx/${DOC_ID}`,
    },
    client,
  });

  assert.ok(result);
  assert.equal(result?.success, true);
  assert.deepEqual(result?.structuredResult, {
    documentId: DOC_ID,
    content: "hello from docx",
    isEmpty: false,
  });
});

test("FeishuReadDocxContent accepts wiki URLs by resolving node metadata", async () => {
  const client = {
    async getWikiNode(_nodeToken: string) {
      return {
        spaceId: WIKI_SPACE_ID,
        nodeToken: WIKI_NODE_ID,
        objToken: DOC_ID,
        objType: "docx",
        nodeType: "origin",
      };
    },
    async getDocxRawContent(documentId: string) {
      return {
        documentId,
        content: "hello from wiki-backed docx",
      };
    },
  } as unknown as FeishuClient;
  const result = await maybeExecuteFeishuDocxToolCall({
    toolName: READ_DOCX_TOOL_NAME,
    args: {
      documentIdOrUrl: `https://example.feishu.cn/wiki/${WIKI_NODE_ID}`,
    },
    client,
  });

  assert.ok(result);
  assert.equal(result?.success, true);
  assert.deepEqual(result?.structuredResult, {
    documentId: DOC_ID,
    content: "hello from wiki-backed docx",
    isEmpty: false,
  });
});

test("FeishuCreateDocxAndShareToCurrentChat supports wiki target", async () => {
  let updateTitleCalls = 0;
  let sentShareText = "";
  let createWikiNodeOptions:
    | {
        spaceId: string;
        parentNodeToken?: string;
        objType?: "docx";
        nodeType?: "origin" | "shortcut";
      }
    | undefined;
  const client = {
    async getWikiNode(nodeToken: string) {
      assert.equal(nodeToken, WIKI_NODE_ID);
      return {
        spaceId: WIKI_SPACE_ID,
        nodeToken,
        objToken: DOC_ID,
        objType: "docx",
        nodeType: "origin",
      };
    },
    async createWikiNode(options: {
      spaceId: string;
      parentNodeToken?: string;
      objType?: "docx";
      nodeType?: "origin" | "shortcut";
    }) {
      createWikiNodeOptions = options;
      return {
        spaceId: WIKI_SPACE_ID,
        nodeToken: WIKI_NODE_ID,
        objToken: DOC_ID,
        objType: "docx",
        nodeType: "origin",
        parentNodeToken: WIKI_NODE_ID,
      };
    },
    async updateWikiNodeTitle(_spaceId: string, _nodeToken: string, _title: string) {
      updateTitleCalls += 1;
    },
    async createDocxBlocks() {
      return {
        documentId: DOC_ID,
        revisionId: 17,
        blocks: [{ blockId: "blk_1", blockType: 3 }],
      };
    },
    async sendTextMessage(_target: string, text: string) {
      sentShareText = text;
      return { messageId: "om_1", chatId: "oc_1" };
    },
  } as unknown as FeishuClient;

  const result = await maybeExecuteFeishuDocxToolCall({
    toolName: CREATE_DOCX_TOOL_NAME,
    args: {
      title: "Wiki Launch Notes",
      wiki: {
        spaceIdOrUrl: `https://example.feishu.cn/wiki/${WIKI_NODE_ID}`,
      },
      initialBlocks: [{ kind: "heading1", text: "Launch Notes" }],
    },
    channelTarget: "group:oc_123",
    client,
  });

  assert.deepEqual(createWikiNodeOptions, {
    spaceId: WIKI_SPACE_ID,
    parentNodeToken: WIKI_NODE_ID,
    objType: "docx",
    nodeType: "origin",
  });
  assert.equal(updateTitleCalls, 1);
  assert.match(sentShareText, /https:\/\/feishu\.cn\/wiki\//);
  assert.equal(result?.success, true);
  assert.deepEqual(result?.structuredResult, {
    delivered: true,
    documentId: DOC_ID,
    revisionId: 17,
    title: "Wiki Launch Notes",
    url: `https://feishu.cn/wiki/${WIKI_NODE_ID}`,
    sharedToCurrentChat: true,
    appendedBlocks: [{ blockId: "blk_1", kind: "heading1", text: "Launch Notes" }],
    wiki: {
      spaceId: WIKI_SPACE_ID,
      nodeToken: WIKI_NODE_ID,
      parentNodeToken: WIKI_NODE_ID,
    },
  });
});

test("FeishuCreateDocxAndShareToCurrentChat preserves wiki resolution API errors", async () => {
  const permissionError = new FeishuApiError({
    kind: "permission",
    retryable: false,
    code: 131006,
    msg: "permission denied: no destination parent node permission",
    message: "permission denied: no destination parent node permission code=131006",
  });
  const client = {
    async getWikiNode() {
      throw permissionError;
    },
  } as unknown as FeishuClient;

  await assert.rejects(
    async () =>
      await maybeExecuteFeishuDocxToolCall({
        toolName: CREATE_DOCX_TOOL_NAME,
        args: {
          title: "Wiki Target",
          wiki: {
            spaceIdOrUrl: `https://example.feishu.cn/wiki/${WIKI_NODE_ID}`,
          },
        },
        channelTarget: "group:oc_123",
        client,
      }),
    (error: unknown) => error === permissionError,
  );
});

test("FeishuCreateDocxAndShareToCurrentChat rejects folderToken and wiki together", async () => {
  const client = {} as FeishuClient;
  const result = await maybeExecuteFeishuDocxToolCall({
    toolName: CREATE_DOCX_TOOL_NAME,
    args: {
      folderToken: "fldcn123",
      wiki: {
        spaceIdOrUrl: WIKI_SPACE_ID,
      },
    },
    channelTarget: "group:oc_123",
    client,
  });
  assert.deepEqual(result, {
    success: false,
    errorCode: "ConflictingTarget",
    errorMessage: "Provide either folderToken or wiki, not both.",
  });
});

test("FeishuAppendDocxContent appends blocks at the root using translated docx children", async () => {
  const blocks: FeishuSimpleBlock[] = [
    { kind: "heading1", text: "Release Notes" },
    { kind: "bullet", text: "Added docx tools" },
    { kind: "todo", text: "Write docs", checked: true },
    { kind: "code", text: "console.log('hi')", language: "typescript" },
    { kind: "divider" },
  ];
  let captured:
    | {
        documentId: string;
        blockId: string;
        options: {
          children: Record<string, unknown>[];
          documentRevisionId?: number;
          index?: number;
          clientToken?: string;
        };
      }
    | undefined;
  const client = {
    async createDocxBlocks(
      documentId: string,
      blockId: string,
      options: {
        children: Record<string, unknown>[];
        documentRevisionId?: number;
        index?: number;
        clientToken?: string;
      },
    ) {
      captured = {
        documentId,
        blockId,
        options,
      };
      return {
        documentId,
        revisionId: 7,
        blocks: options.children.map((child, index) => ({
          blockId: `blk_${index + 1}`,
          blockType: Number((child.block_type as number | undefined) ?? 0),
        })),
      };
    },
  } as unknown as FeishuClient;

  const result = await maybeExecuteFeishuDocxToolCall({
    toolName: APPEND_DOCX_TOOL_NAME,
    args: {
      documentIdOrUrl: DOC_ID,
      blocks,
    },
    client,
  });

  assert.ok(captured);
  assert.equal(captured?.documentId, DOC_ID);
  assert.equal(captured?.blockId, DOC_ID);
  assert.equal(captured?.options.index, -1);
  assert.equal(captured?.options.documentRevisionId, -1);
  assert.equal(typeof captured?.options.clientToken, "string");
  assert.deepEqual(captured?.options.children, toFeishuDocxChildren(blocks));
  assert.equal(result?.success, true);
  assert.deepEqual(result?.structuredResult, {
    documentId: DOC_ID,
    revisionId: 7,
    appendedBlocks: [
      { blockId: "blk_1", kind: "heading1", text: "Release Notes" },
      { blockId: "blk_2", kind: "bullet", text: "Added docx tools" },
      { blockId: "blk_3", kind: "todo", text: "Write docs" },
      { blockId: "blk_4", kind: "code", text: "console.log('hi')" },
      { blockId: "blk_5", kind: "divider" },
    ],
  });
});
