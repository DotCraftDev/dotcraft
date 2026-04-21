import { randomUUID } from "node:crypto";

import type { FeishuClient } from "./feishu-client.js";
import type { FeishuConfig } from "./feishu-types.js";
import { extractWikiNodeToken, resolveWikiSpaceTarget } from "./feishu-wiki-tools.js";

export const CREATE_DOCX_TOOL_NAME = "FeishuCreateDocxAndShareToCurrentChat";
export const READ_DOCX_TOOL_NAME = "FeishuReadDocxContent";
export const APPEND_DOCX_TOOL_NAME = "FeishuAppendDocxContent";

const DOCX_ID_PATTERN = /^[A-Za-z0-9]{16,40}$/;
const SIMPLE_BLOCK_KINDS = [
  "paragraph",
  "heading1",
  "heading2",
  "bullet",
  "ordered",
  "todo",
  "quote",
  "code",
  "divider",
] as const;
const SIMPLE_BLOCK_KIND_SET = new Set<string>(SIMPLE_BLOCK_KINDS);
const CODE_LANGUAGE_IDS = new Map<string, number>([
  ["plain", 1],
  ["plaintext", 1],
  ["text", 1],
  ["abap", 2],
  ["ada", 3],
  ["apache", 4],
  ["apex", 5],
  ["asm", 6],
  ["assembly", 6],
  ["bash", 7],
  ["csharp", 8],
  ["c#", 8],
  ["cpp", 9],
  ["c++", 9],
  ["c", 10],
  ["cobol", 11],
  ["css", 12],
  ["coffeescript", 13],
  ["dart", 15],
  ["django", 17],
  ["dockerfile", 18],
  ["erlang", 19],
  ["fortran", 20],
  ["go", 22],
  ["groovy", 23],
  ["html", 24],
  ["http", 26],
  ["haskell", 27],
  ["json", 28],
  ["java", 29],
  ["javascript", 30],
  ["js", 30],
  ["julia", 31],
  ["kotlin", 32],
  ["latex", 33],
  ["lua", 36],
  ["matlab", 37],
  ["makefile", 38],
  ["markdown", 39],
  ["md", 39],
  ["nginx", 40],
  ["objective-c", 41],
  ["objc", 41],
  ["php", 43],
  ["perl", 44],
  ["powershell", 46],
  ["pwsh", 46],
  ["python", 49],
  ["py", 49],
  ["r", 50],
  ["ruby", 52],
  ["rb", 52],
  ["rust", 53],
  ["rs", 53],
  ["scss", 55],
  ["sql", 56],
  ["scala", 57],
  ["scheme", 58],
  ["shell", 60],
  ["sh", 60],
  ["swift", 61],
  ["thrift", 62],
  ["typescript", 63],
  ["ts", 63],
  ["vbscript", 64],
  ["xml", 66],
  ["yaml", 67],
  ["yml", 67],
  ["cmake", 68],
  ["diff", 69],
  ["gherkin", 70],
  ["graphql", 71],
  ["properties", 73],
  ["solidity", 74],
  ["toml", 75],
]);

export type FeishuSimpleBlockKind = (typeof SIMPLE_BLOCK_KINDS)[number];

export interface FeishuSimpleBlock {
  kind: FeishuSimpleBlockKind;
  text?: string;
  checked?: boolean;
  language?: string;
}

class DocxToolError extends Error {
  readonly code: string;

  constructor(code: string, message: string) {
    super(message);
    this.name = "DocxToolError";
    this.code = code;
  }
}

export function areFeishuDocxToolsEnabled(config: FeishuConfig | undefined): boolean {
  return config?.feishu.tools?.docs?.enabled === true;
}

export function getFeishuDocxChannelTools(enabled: boolean): Record<string, unknown>[] {
  if (!enabled) return [];
  const simpleBlockSchema = {
    type: "object",
    properties: {
      kind: { type: "string", enum: [...SIMPLE_BLOCK_KINDS] },
      text: { type: "string" },
      checked: { type: "boolean" },
      language: { type: "string" },
    },
    required: ["kind"],
  };

  return [
    {
      name: CREATE_DOCX_TOOL_NAME,
      description: "Create a Feishu docx document and share the link to the current Feishu chat. Provide either folderToken or wiki, not both.",
      requiresChatContext: true,
      display: {
        icon: "\u{1F4C4}",
        title: "Create Feishu docx and share",
      },
      inputSchema: {
        type: "object",
        properties: {
          title: { type: "string" },
          folderToken: { type: "string" },
          wiki: {
            type: "object",
            properties: {
              spaceIdOrUrl: { type: "string" },
              parentNodeTokenOrUrl: { type: "string" },
            },
            required: ["spaceIdOrUrl"],
          },
          initialBlocks: {
            type: "array",
            items: simpleBlockSchema,
          },
        },
      },
    },
    {
      name: READ_DOCX_TOOL_NAME,
      description: "Read the raw text content of a Feishu docx document.",
      display: {
        icon: "\u{1F50D}",
        title: "Read Feishu docx content",
      },
      inputSchema: {
        type: "object",
        properties: {
          documentIdOrUrl: { type: "string" },
        },
        required: ["documentIdOrUrl"],
      },
    },
    {
      name: APPEND_DOCX_TOOL_NAME,
      description: "Append high-level text blocks to the root of a Feishu docx document.",
      display: {
        icon: "\u{270F}\u{FE0F}",
        title: "Append Feishu docx content",
      },
      inputSchema: {
        type: "object",
        properties: {
          documentIdOrUrl: { type: "string" },
          blocks: {
            type: "array",
            items: simpleBlockSchema,
          },
        },
        required: ["documentIdOrUrl", "blocks"],
      },
    },
  ];
}

export async function maybeExecuteFeishuDocxToolCall(params: {
  toolName: string;
  args: Record<string, unknown>;
  channelTarget?: string;
  client: FeishuClient;
}): Promise<Record<string, unknown> | null> {
  if (!isFeishuDocxToolName(params.toolName)) return null;

  try {
    if (params.toolName === CREATE_DOCX_TOOL_NAME) {
      return await executeCreateDocxTool(params);
    }
    if (params.toolName === READ_DOCX_TOOL_NAME) {
      return await executeReadDocxTool(params);
    }
    return await executeAppendDocxTool(params);
  } catch (error) {
    if (error instanceof DocxToolError) {
      return {
        success: false,
        errorCode: error.code,
        errorMessage: error.message,
      };
    }
    throw error;
  }
}

export function extractDocxDocumentId(documentIdOrUrl: string): string {
  const normalized = documentIdOrUrl.trim();
  if (!normalized) {
    throw new DocxToolError("MissingDocumentId", "Feishu docx tools require a documentIdOrUrl.");
  }
  if (DOCX_ID_PATTERN.test(normalized)) {
    return normalized;
  }

  let parsedUrl: URL;
  try {
    parsedUrl = new URL(normalized);
  } catch {
    throw new DocxToolError(
      "InvalidDocumentId",
      "Provide a Feishu docx document_id or a docx URL such as https://feishu.cn/docx/<document_id>.",
    );
  }

  if (parsedUrl.pathname.toLowerCase().includes("/wiki/")) {
    throw new DocxToolError(
      "UnsupportedWikiUrl",
      "Provide a docx document_id or docx URL, not a Feishu wiki node URL.",
    );
  }

  const lowerPath = parsedUrl.pathname.toLowerCase();
  if (/\/doc\//.test(lowerPath) && !lowerPath.includes("/docx/")) {
    throw new DocxToolError(
      "UnsupportedLegacyDoc",
      "Legacy /doc/ URLs point to old-style documents not supported by the docx v1 API; open the file in Feishu and copy the /docx/ URL instead.",
    );
  }

  const segments = parsedUrl.pathname.split("/").filter(Boolean);
  const docxIndex = segments.findIndex((segment) => segment.toLowerCase() === "docx");
  const maybeId = docxIndex >= 0 ? segments[docxIndex + 1] : undefined;
  if (maybeId && DOCX_ID_PATTERN.test(maybeId)) {
    return maybeId;
  }

  throw new DocxToolError(
    "InvalidDocumentId",
    "Provide a Feishu docx document_id or a docx URL such as https://feishu.cn/docx/<document_id>.",
  );
}

export async function resolveDocxDocumentId(params: {
  client: FeishuClient;
  documentIdOrUrl: string;
}): Promise<string> {
  const normalized = params.documentIdOrUrl.trim();
  if (!normalized) {
    throw new DocxToolError("MissingDocumentId", "Feishu docx tools require a documentIdOrUrl.");
  }

  try {
    return extractDocxDocumentId(normalized);
  } catch (error) {
    if (
      !(error instanceof DocxToolError) ||
      (error.code !== "InvalidDocumentId" && error.code !== "UnsupportedWikiUrl")
    ) {
      throw error;
    }
  }

  let wikiNodeToken: string;
  try {
    wikiNodeToken = extractWikiNodeToken(normalized);
  } catch {
    throw new DocxToolError(
      "InvalidDocumentId",
      "Provide a Feishu docx document_id or a docx URL such as https://feishu.cn/docx/<document_id>.",
    );
  }
  const wikiNode = await params.client.getWikiNode(wikiNodeToken);
  if (wikiNode.objType !== "docx") {
    throw new DocxToolError(
      "UnsupportedDocxBacking",
      `Resolved wiki node '${wikiNode.nodeToken}' to obj_type '${wikiNode.objType}', not docx.`,
    );
  }
  const documentId = wikiNode.objToken.trim();
  if (!documentId) {
    throw new DocxToolError("InvalidDocumentId", "Resolved wiki node does not include a backing docx document_id.");
  }
  return documentId;
}

export function parseFeishuSimpleBlocks(value: unknown, argName: string): FeishuSimpleBlock[] {
  if (!Array.isArray(value) || value.length === 0) {
    throw new DocxToolError(
      "InvalidBlocks",
      `Feishu docx tool argument '${argName}' must be a non-empty array of simple blocks.`,
    );
  }
  if (value.length > 50) {
    throw new DocxToolError("TooManyBlocks", "Feishu docx append only supports up to 50 blocks per request.");
  }

  return value.map((item, index) => parseFeishuSimpleBlock(item, `${argName}[${index}]`));
}

export function toFeishuDocxChildren(blocks: FeishuSimpleBlock[]): Record<string, unknown>[] {
  return blocks.map((block) => toFeishuDocxBlock(block));
}

function isFeishuDocxToolName(toolName: string): boolean {
  return toolName === CREATE_DOCX_TOOL_NAME || toolName === READ_DOCX_TOOL_NAME || toolName === APPEND_DOCX_TOOL_NAME;
}

async function executeCreateDocxTool(params: {
  args: Record<string, unknown>;
  channelTarget?: string;
  client: FeishuClient;
}): Promise<Record<string, unknown>> {
  const target = params.channelTarget?.trim() ?? "";
  if (!target) {
    throw new DocxToolError(
      "MissingChatContext",
      "Current tool call does not contain a Feishu chat target for docx sharing.",
    );
  }

  const title = optionalText(params.args.title);
  const folderToken = optionalText(params.args.folderToken);
  const wiki = await resolveWikiCreateTarget({
    client: params.client,
    value: params.args.wiki,
  });
  if (folderToken && wiki) {
    throw new DocxToolError("ConflictingTarget", "Provide either folderToken or wiki, not both.");
  }
  const initialBlocksValue = params.args.initialBlocks;
  const initialBlocks = Array.isArray(initialBlocksValue) && initialBlocksValue.length > 0
    ? parseFeishuSimpleBlocks(initialBlocksValue, "initialBlocks")
    : [];

  let document:
    | {
        documentId: string;
        revisionId: number;
        title: string;
        url: string;
      }
    | undefined;
  let createdWikiNode:
    | {
        spaceId: string;
        nodeToken: string;
        parentNodeToken?: string;
      }
    | undefined;
  if (wiki) {
    const node = await params.client.createWikiNode({
      spaceId: wiki.spaceId,
      parentNodeToken: wiki.parentNodeToken,
      objType: "docx",
      nodeType: "origin",
    });
    if (!node.objToken) {
      throw new DocxToolError("MissingDocumentId", "Feishu wiki node creation did not include a backing docx token.");
    }
    if (title) {
      await params.client.updateWikiNodeTitle(node.spaceId, node.nodeToken, title);
    }
    const wikiDocUrl = buildWikiUrl(node.nodeToken);
    document = {
      documentId: node.objToken,
      revisionId: 0,
      title: title ?? node.title ?? "",
      url: wikiDocUrl,
    };
    createdWikiNode = {
      spaceId: node.spaceId,
      nodeToken: node.nodeToken,
      parentNodeToken: node.parentNodeToken,
    };
  } else {
    document = await params.client.createDocxDocument({
      title,
      folderToken,
    });
  }
  if (!document) {
    throw new DocxToolError("CreateFailed", "Failed to create Feishu docx document.");
  }

  let appendResult:
    | {
        blocks: Array<{ blockId?: string; kind: FeishuSimpleBlockKind; text?: string }>;
        revisionId?: number;
      }
    | undefined;
  if (initialBlocks.length > 0) {
    const appended = await params.client.createDocxBlocks(document.documentId, document.documentId, {
      children: toFeishuDocxChildren(initialBlocks),
      index: -1,
      documentRevisionId: -1,
      clientToken: randomUUID(),
    });
    appendResult = {
      blocks: summarizeBlocks(initialBlocks, appended.blocks),
      revisionId: appended.revisionId,
    };
  }

  const shareText = buildCreateShareText(document.title || title || document.documentId, document.url);
  await params.client.sendTextMessage(target, shareText);

  return {
    success: true,
    contentItems: [{ type: "text", text: `Created Feishu docx ${document.documentId}.` }],
    structuredResult: {
      delivered: true,
      documentId: document.documentId,
      revisionId: appendResult?.revisionId ?? document.revisionId,
      title: document.title,
      url: document.url,
      sharedToCurrentChat: true,
      appendedBlocks: appendResult?.blocks ?? [],
      ...(createdWikiNode ? { wiki: createdWikiNode } : {}),
    },
  };
}

async function executeReadDocxTool(params: {
  args: Record<string, unknown>;
  client: FeishuClient;
}): Promise<Record<string, unknown>> {
  const documentId = await resolveDocxDocumentId({
    client: params.client,
    documentIdOrUrl: requiredText(params.args.documentIdOrUrl, "documentIdOrUrl"),
  });
  const result = await params.client.getDocxRawContent(documentId);
  const readableText = result.content || "(empty document)";
  return {
    success: true,
    contentItems: [{ type: "text", text: readableText }],
    structuredResult: {
      documentId: result.documentId,
      content: result.content,
      isEmpty: result.content.length === 0,
    },
  };
}

async function executeAppendDocxTool(params: {
  args: Record<string, unknown>;
  client: FeishuClient;
}): Promise<Record<string, unknown>> {
  const documentId = await resolveDocxDocumentId({
    client: params.client,
    documentIdOrUrl: requiredText(params.args.documentIdOrUrl, "documentIdOrUrl"),
  });
  const blocks = parseFeishuSimpleBlocks(params.args.blocks, "blocks");
  const result = await params.client.createDocxBlocks(documentId, documentId, {
    children: toFeishuDocxChildren(blocks),
    index: -1,
    documentRevisionId: -1,
    clientToken: randomUUID(),
  });
  const summary = summarizeBlocks(blocks, result.blocks);
  return {
    success: true,
    contentItems: [{ type: "text", text: `Appended ${summary.length} block(s) to Feishu docx ${documentId}.` }],
    structuredResult: {
      documentId,
      revisionId: result.revisionId,
      appendedBlocks: summary,
    },
  };
}

function parseFeishuSimpleBlock(value: unknown, path: string): FeishuSimpleBlock {
  if (!value || typeof value !== "object" || Array.isArray(value)) {
    throw new DocxToolError("InvalidBlocks", `${path} must be an object.`);
  }
  const record = value as Record<string, unknown>;
  const kind = String(record.kind ?? "").trim();
  if (!SIMPLE_BLOCK_KIND_SET.has(kind)) {
    throw new DocxToolError(
      "InvalidBlocks",
      `${path}.kind must be one of: ${SIMPLE_BLOCK_KINDS.join(", ")}.`,
    );
  }

  return {
    kind: kind as FeishuSimpleBlockKind,
    text: optionalText(record.text),
    checked: typeof record.checked === "boolean" ? record.checked : undefined,
    language: optionalText(record.language),
  };
}

function toFeishuDocxBlock(block: FeishuSimpleBlock): Record<string, unknown> {
  const textElements = buildTextElements(block.text ?? "");
  switch (block.kind) {
    case "paragraph":
      return { block_type: 2, text: { elements: textElements } };
    case "heading1":
      return { block_type: 3, heading1: { elements: textElements } };
    case "heading2":
      return { block_type: 4, heading2: { elements: textElements } };
    case "bullet":
      return { block_type: 12, bullet: { elements: textElements } };
    case "ordered":
      return { block_type: 13, ordered: { elements: textElements } };
    case "todo":
      return { block_type: 17, todo: { elements: textElements, style: { done: block.checked === true } } };
    case "quote":
      return { block_type: 15, quote: { elements: textElements } };
    case "code":
      return {
        block_type: 14,
        code: {
          elements: textElements,
          style: {
            language: resolveCodeLanguageId(block.language),
            wrap: false,
          },
        },
      };
    case "divider":
      return { block_type: 22, divider: {} };
  }
}

function buildTextElements(text: string): Record<string, unknown>[] {
  return [
    {
      text_run: {
        content: text,
      },
    },
  ];
}

function resolveCodeLanguageId(language: string | undefined): number {
  const normalized = (language ?? "").trim().toLowerCase();
  if (!normalized) return 1;
  return CODE_LANGUAGE_IDS.get(normalized) ?? 1;
}

function summarizeBlocks(
  requestedBlocks: FeishuSimpleBlock[],
  createdBlocks: Array<{ blockId: string; blockType: number }>,
): Array<{ blockId?: string; kind: FeishuSimpleBlockKind; text?: string }> {
  return requestedBlocks.map((block, index) => ({
    blockId: createdBlocks[index]?.blockId,
    kind: block.kind,
    ...(block.text ? { text: block.text.slice(0, 120) } : {}),
  }));
}

function buildCreateShareText(title: string, url: string): string {
  return `Created Feishu docx: ${title}\n${url}`;
}

function buildWikiUrl(nodeToken: string): string {
  return `https://feishu.cn/wiki/${nodeToken}`;
}

function requiredText(value: unknown, fieldName: string): string {
  const text = String(value ?? "").trim();
  if (!text) {
    throw new DocxToolError("InvalidArguments", `Feishu docx tool requires a non-empty '${fieldName}'.`);
  }
  return text;
}

function optionalText(value: unknown): string | undefined {
  if (typeof value !== "string") return undefined;
  const normalized = value.trim();
  return normalized ? normalized : undefined;
}

async function resolveWikiCreateTarget(params: {
  client: FeishuClient;
  value: unknown;
}): Promise<{ spaceId: string; parentNodeToken?: string } | undefined> {
  if (params.value == null) return undefined;
  if (!params.value || typeof params.value !== "object" || Array.isArray(params.value)) {
    throw new DocxToolError("InvalidArguments", "Feishu docx tool 'wiki' must be an object.");
  }
  const record = params.value as Record<string, unknown>;
  const spaceIdOrUrl = requiredText(record.spaceIdOrUrl, "wiki.spaceIdOrUrl");
  const parentNodeTokenOrUrl = optionalText(record.parentNodeTokenOrUrl);
  const target = await resolveWikiSpaceTarget({
    client: params.client,
    spaceIdOrUrl,
    parentNodeTokenOrUrl,
  });
  return {
    spaceId: target.spaceId,
    parentNodeToken: target.parentNodeToken,
  };
}
