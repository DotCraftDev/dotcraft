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
  toFeishuDocxChildren,
  type FeishuSimpleBlock,
} from "./feishu-docx-tools.js";
import type { FeishuConfig } from "./feishu-types.js";

const DOC_ID = "doxABCDEFGHIJKLMNOPQRSTUVWX";

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
    ],
  );
});

test("extractDocxDocumentId accepts raw docx IDs and docx URLs, but rejects wiki URLs", () => {
  assert.equal(extractDocxDocumentId(DOC_ID), DOC_ID);
  assert.equal(extractDocxDocumentId(`https://feishu.cn/docx/${DOC_ID}`), DOC_ID);
  assert.equal(extractDocxDocumentId(`https://larksuite.com/docx/${DOC_ID}?from=wiki`), DOC_ID);

  assert.throws(
    () => extractDocxDocumentId(`https://feishu.cn/wiki/AbCdEfGhIjKlMnOpQrStUvWx`),
    (error: unknown) => String((error as Error).message).includes("wiki node URL"),
  );
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

test("FeishuReadDocxContent rejects wiki URLs with a clear tool error", async () => {
  const client = {} as FeishuClient;
  const result = await maybeExecuteFeishuDocxToolCall({
    toolName: READ_DOCX_TOOL_NAME,
    args: {
      documentIdOrUrl: "https://feishu.cn/wiki/AbCdEfGhIjKlMnOpQrStUvWx",
    },
    client,
  });

  assert.deepEqual(result, {
    success: false,
    errorCode: "UnsupportedWikiUrl",
    errorMessage: "Provide a docx document_id or docx URL, not a Feishu wiki node URL.",
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
