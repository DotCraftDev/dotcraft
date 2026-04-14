import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";

import * as Lark from "@larksuiteoapi/node-sdk";

import type {
  AppConfig,
  FeishuBotInfo,
  FeishuCardActionEvent,
  FeishuMessageEvent,
  FeishuSendResult,
} from "./feishu-types.js";
import { errorMessage, logError, logInfo, logWarn } from "./logging.js";

type FeishuEventHandlers = {
  onMessage: (event: FeishuMessageEvent) => Promise<void>;
  onCardAction: (event: FeishuCardActionEvent) => Promise<void>;
};

function resolveBrand(brand?: string): string | Lark.Domain {
  if (!brand || brand === "feishu") return Lark.Domain.Feishu;
  if (brand === "lark") return Lark.Domain.Lark;
  return brand.replace(/\/+$/, "");
}

async function extractBufferFromResponse(
  response: unknown,
): Promise<{ buffer: Buffer; contentType?: string }> {
  if (Buffer.isBuffer(response)) {
    return { buffer: response };
  }
  if (response instanceof ArrayBuffer) {
    return { buffer: Buffer.from(response) };
  }
  if (!response || typeof response !== "object") {
    throw new Error("Unexpected Feishu binary response");
  }

  const resp = response as Record<string, unknown>;
  const headers = (resp.headers as Record<string, string> | undefined) ?? {};
  const contentType = headers["content-type"] ?? headers["Content-Type"];

  const data = resp.data;
  if (Buffer.isBuffer(data)) {
    return { buffer: data, contentType };
  }
  if (data instanceof ArrayBuffer) {
    return { buffer: Buffer.from(data), contentType };
  }
  if (data && typeof data === "object" && typeof (data as NodeJS.ReadableStream).pipe === "function") {
    return {
      buffer: await streamToBuffer(data as NodeJS.ReadableStream),
      contentType,
    };
  }
  if (typeof (resp as { getReadableStream?: () => Promise<NodeJS.ReadableStream> }).getReadableStream === "function") {
    const stream = await (resp as { getReadableStream: () => Promise<NodeJS.ReadableStream> }).getReadableStream();
    return { buffer: await streamToBuffer(stream), contentType };
  }

  throw new Error("Unable to read binary resource from Feishu response");
}

function streamToBuffer(stream: NodeJS.ReadableStream): Promise<Buffer> {
  return new Promise<Buffer>((resolve, reject) => {
    const chunks: Buffer[] = [];
    stream.on("data", (chunk: Buffer | Uint8Array) => chunks.push(Buffer.from(chunk)));
    stream.on("end", () => resolve(Buffer.concat(chunks)));
    stream.on("error", reject);
  });
}

export class FeishuClient {
  readonly sdk: Lark.Client;
  private wsClient: Lark.WSClient | null = null;
  private readonly appId: string;
  private readonly appSecret: string;
  private readonly domain: string | Lark.Domain;
  private readonly apiBaseUrl: string;
  private tenantAccessToken: string | null = null;
  private tenantAccessTokenExpiresAt = 0;

  constructor(private readonly config: AppConfig["feishu"]) {
    this.appId = config.appId;
    this.appSecret = config.appSecret;
    this.domain = resolveBrand(config.brand);
    this.apiBaseUrl = resolveApiBaseUrl(config.brand);
    this.sdk = new Lark.Client({
      appId: this.appId,
      appSecret: this.appSecret,
      appType: Lark.AppType.SelfBuild,
      domain: this.domain,
    });
  }

  async probeBot(): Promise<FeishuBotInfo> {
    logInfo("startup.bot_probe_request", { method: "GET", path: "/open-apis/bot/v3/info" });
    const response = (await (this.sdk as unknown as {
      request: (request: Record<string, unknown>) => Promise<Record<string, unknown>>;
    }).request({
      method: "GET",
      url: "/open-apis/bot/v3/info",
    })) as Record<string, unknown>;

    const data = (response.data as Record<string, unknown>) ?? {};
    const bot = (response.bot as Record<string, unknown> | undefined) ?? {};
    if ((response.code as number | undefined) && Number(response.code) !== 0) {
      throw new Error(String(response.msg ?? "Failed to query Feishu bot info"));
    }

    const responseKeys = Object.keys(response).map((key) => `response.${key}`);
    const dataKeys = Object.keys(data).map((key) => `data.${key}`);
    const botKeys = Object.keys(bot).map((key) => `bot.${key}`);
    const rawFieldKeys = [...responseKeys, ...dataKeys, ...botKeys].sort();

    // Some tenants / API versions may expose bot identity with different field names or nested shapes.
    const openIdCandidates = [
      bot.open_id,
      bot.bot_open_id,
      bot.id,
      data.open_id,
      data.bot_open_id,
      data.bot_id,
      (data.bot as Record<string, unknown> | undefined)?.open_id,
      (data.bot as Record<string, unknown> | undefined)?.id,
      (data.pingBotInfo as Record<string, unknown> | undefined)?.botID,
      (response.open_id as string | undefined),
      (response.bot_open_id as string | undefined),
    ]
      .map((value) => (value == null ? "" : String(value)))
      .filter((value) => value.length > 0);

    const appName = String(bot.app_name ?? data.app_name ?? data.bot_name ?? "");
    const botName = appName;
    const openId = openIdCandidates[0] ?? "";
    const hasBotIdentity = openId.length > 0;
    const diagnosticMessage = hasBotIdentity
      ? undefined
      : "Feishu bot info returned no bot identity field. " +
        "Bot capability may be disabled/unpublished, or SDK response shape differs. " +
        `Available fields: [${rawFieldKeys.join(", ")}]`;

    return {
      appName,
      botName,
      openId,
      hasBotIdentity,
      tenantKey: data.tenant_key ? String(data.tenant_key) : undefined,
      activateStatus: data.activate_status != null ? Number(data.activate_status) : undefined,
      rawFieldKeys,
      diagnosticMessage,
    };
  }

  async startEventStream(handlers: FeishuEventHandlers, abortSignal?: AbortSignal): Promise<void> {
    logInfo("feishu.ws.starting");
    const dispatcher = new Lark.EventDispatcher({
      encryptKey: "",
      verificationToken: "",
    });
    dispatcher.register({
      "im.message.receive_v1": (data: unknown) => handlers.onMessage(data as FeishuMessageEvent),
      "card.action.trigger": (data: unknown) => handlers.onCardAction(data as FeishuCardActionEvent),
    } as never);

    if (this.wsClient) {
      try {
        logWarn("feishu.ws.replacing_existing_client");
        this.wsClient.close({ force: true });
      } catch {
        // Ignore stale connection cleanup errors.
      }
    }

    this.wsClient = new Lark.WSClient({
      appId: this.appId,
      appSecret: this.appSecret,
      domain: this.domain,
      loggerLevel: Lark.LoggerLevel.info,
    });

    const wsClientAny = this.wsClient as unknown as {
      handleEventData: (data: Record<string, unknown>) => unknown;
    };
    const originalHandleEventData = wsClientAny.handleEventData.bind(wsClientAny);
    let loggedCardPatch = false;
    wsClientAny.handleEventData = (data: Record<string, unknown>) => {
      const headers = Array.isArray(data.headers) ? (data.headers as Array<Record<string, unknown>>) : [];
      const messageType = headers.find((header) => header.key === "type")?.value;
      if (messageType === "card") {
        if (!loggedCardPatch) {
          loggedCardPatch = true;
          logInfo("feishu.ws.card_event_patch_enabled");
        }
        const patchedHeaders = headers.map((header) =>
          header.key === "type" ? { ...header, value: "event" } : header,
        );
        return originalHandleEventData({ ...data, headers: patchedHeaders });
      }
      return originalHandleEventData(data);
    };

    const startPromise = this.wsClient.start({ eventDispatcher: dispatcher });
    if (!abortSignal) {
      await startPromise;
      logInfo("feishu.ws.started");
      return;
    }

    await new Promise<void>((resolve, reject) => {
      if (abortSignal.aborted) {
        logInfo("feishu.ws.abort_requested", { reason: "pre_aborted" });
        this.stopEventStream();
        resolve();
        return;
      }

      abortSignal.addEventListener(
        "abort",
        () => {
          logInfo("feishu.ws.abort_requested", { reason: "signal" });
          this.stopEventStream();
          resolve();
        },
        { once: true },
      );

      void startPromise.catch((error) => {
        logError("feishu.ws.start_failed", { message: errorMessage(error) });
        this.stopEventStream();
        reject(error);
      });
      void startPromise.then(() => {
        logInfo("feishu.ws.started");
      });
    });
  }

  stopEventStream(): void {
    if (!this.wsClient) return;
    try {
      this.wsClient.close({ force: true });
    } catch {
      // Ignore close errors during shutdown.
    }
    this.wsClient = null;
    logInfo("feishu.ws.stopped");
  }

  async sendInteractiveCard(
    target: string,
    card: Record<string, unknown>,
  ): Promise<FeishuSendResult> {
    assertCardPayloadShape(card);
    const { receiveId, receiveIdType } = this.resolveTarget(target);
    const response = await this.sdk.im.message.create({
      params: {
        receive_id_type: receiveIdType as never,
      },
      data: {
        receive_id: receiveId,
        msg_type: "interactive",
        content: JSON.stringify(card),
      },
    });

    return {
      messageId: String(response.data?.message_id ?? ""),
      chatId: String(response.data?.chat_id ?? ""),
    };
  }

  async addMessageReaction(messageId: string, emojiType: string): Promise<void> {
    const normalizedMessageId = messageId.trim();
    const normalizedEmojiType = emojiType.trim();
    if (!normalizedMessageId) {
      throw new Error("Feishu message reaction requires a messageId.");
    }
    if (!normalizedEmojiType) {
      throw new Error("Feishu message reaction requires an emojiType.");
    }

    await this.sdk.im.messageReaction.create({
      path: {
        message_id: normalizedMessageId,
      },
      data: {
        reaction_type: {
          emoji_type: normalizedEmojiType,
        },
      },
    });
  }

  async updateInteractiveCard(messageId: string, card: Record<string, unknown>): Promise<void> {
    assertCardPayloadShape(card);
    await (this.sdk as unknown as {
      request: (request: Record<string, unknown>) => Promise<unknown>;
    }).request({
      method: "PATCH",
      url: `/open-apis/im/v1/messages/${messageId}`,
      data: {
        content: JSON.stringify(card),
      },
    });
  }

  async sendFile(
    target: string,
    file: {
      fileName: string;
      data: Buffer;
      mediaType?: string;
    },
  ): Promise<FeishuSendResult & { fileKey: string }> {
    if (!file.fileName.trim()) {
      throw new Error("Feishu file delivery requires a fileName.");
    }
    if (file.data.length === 0) {
      throw new Error("Feishu file delivery does not support empty files.");
    }
    if (file.data.length > 30 * 1024 * 1024) {
      throw new Error("Feishu file delivery only supports files up to 30 MB.");
    }

    const fileKey = await this.uploadFile(file.fileName, file.data, file.mediaType);
    const { receiveId, receiveIdType } = this.resolveTarget(target);
    const response = await this.sendMessage(receiveId, receiveIdType, "file", {
      file_key: fileKey,
    });
    const responseData = (response.data as Record<string, unknown> | undefined) ?? {};

    return {
      messageId: String(responseData.message_id ?? ""),
      chatId: String(responseData.chat_id ?? ""),
      fileKey,
    };
  }

  async downloadMessageImage(messageId: string, imageKey: string, downloadDir?: string): Promise<string> {
    const response = await this.sdk.im.messageResource.get({
      path: {
        message_id: messageId,
        file_key: imageKey,
      },
      params: {
        type: "image",
      },
    });

    const { buffer, contentType } = await extractBufferFromResponse(response);
    const extension = extensionFromContentType(contentType);
    const dir = downloadDir ? path.resolve(downloadDir) : path.join(os.tmpdir(), "dotcraft-feishu");
    fs.mkdirSync(dir, { recursive: true });
    const filePath = path.join(dir, `feishu-${messageId}-${Date.now()}${extension}`);
    fs.writeFileSync(filePath, buffer);
    return filePath;
  }

  private resolveTarget(target: string): { receiveId: string; receiveIdType: "chat_id" | "open_id" } {
    if (target.startsWith("group:")) {
      return {
        receiveId: target.slice("group:".length),
        receiveIdType: "chat_id",
      };
    }
    if (target.startsWith("dm:")) {
      return {
        receiveId: target.slice("dm:".length),
        receiveIdType: "open_id",
      };
    }
    return {
      receiveId: target,
      receiveIdType: "chat_id",
    };
  }

  private async uploadFile(fileName: string, data: Buffer, mediaType?: string): Promise<string> {
    const token = await this.getTenantAccessToken();
    const formData = new FormData();
    formData.set("file_type", toFeishuFileType(fileName, mediaType));
    formData.set("file_name", fileName);
    formData.set("file", new Blob([data], { type: mediaType ?? inferMediaType(fileName) }), fileName);

    const response = await fetch(`${this.apiBaseUrl}/open-apis/im/v1/files`, {
      method: "POST",
      headers: {
        Authorization: `Bearer ${token}`,
      },
      body: formData,
    });
    const payload = (await response.json()) as Record<string, unknown>;
    if (!response.ok || Number(payload.code ?? 0) !== 0) {
      throw new Error(String(payload.msg ?? `Failed to upload file '${fileName}' to Feishu.`));
    }

    const dataNode = (payload.data as Record<string, unknown> | undefined) ?? {};
    const fileKey = String(dataNode.file_key ?? "");
    if (!fileKey) {
      throw new Error("Feishu upload response did not include file_key.");
    }
    return fileKey;
  }

  private async sendMessage(
    receiveId: string,
    receiveIdType: "chat_id" | "open_id",
    msgType: string,
    content: Record<string, unknown>,
  ): Promise<Record<string, unknown>> {
    const token = await this.getTenantAccessToken();
    const response = await fetch(
      `${this.apiBaseUrl}/open-apis/im/v1/messages?receive_id_type=${encodeURIComponent(receiveIdType)}`,
      {
        method: "POST",
        headers: {
          Authorization: `Bearer ${token}`,
          "Content-Type": "application/json",
        },
        body: JSON.stringify({
          receive_id: receiveId,
          msg_type: msgType,
          content: JSON.stringify(content),
        }),
      },
    );
    const payload = (await response.json()) as Record<string, unknown>;
    if (!response.ok || Number(payload.code ?? 0) !== 0) {
      throw new Error(String(payload.msg ?? `Failed to send Feishu '${msgType}' message.`));
    }
    return payload;
  }

  private async getTenantAccessToken(): Promise<string> {
    const now = Date.now();
    if (this.tenantAccessToken && now < this.tenantAccessTokenExpiresAt) {
      return this.tenantAccessToken;
    }

    const response = await fetch(`${this.apiBaseUrl}/open-apis/auth/v3/tenant_access_token/internal`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
      },
      body: JSON.stringify({
        app_id: this.appId,
        app_secret: this.appSecret,
      }),
    });
    const payload = (await response.json()) as Record<string, unknown>;
    if (!response.ok || Number(payload.code ?? 0) !== 0) {
      throw new Error(String(payload.msg ?? "Failed to obtain Feishu tenant access token."));
    }

    const token = String(payload.tenant_access_token ?? "");
    if (!token) {
      throw new Error("Feishu auth response did not include tenant_access_token.");
    }

    const expiresInSeconds = Math.max(60, Number(payload.expire ?? payload.expires_in ?? 7200));
    this.tenantAccessToken = token;
    this.tenantAccessTokenExpiresAt = now + (expiresInSeconds - 60) * 1000;
    return token;
  }
}

function assertCardPayloadShape(card: Record<string, unknown>): void {
  const schema = String(card.schema ?? "");
  const body = card.body as Record<string, unknown> | undefined;
  const elements = body?.elements;
  if (schema === "2.0" && Array.isArray(elements)) {
    const forbiddenTagPath = findForbiddenV2TagPath(elements);
    if (!forbiddenTagPath) return;

    throw new Error(
      `Invalid Feishu card payload: schema 2.0 does not support 'action' tags (found at ${forbiddenTagPath}).`,
    );
  }

  logWarn("feishu.card.payload.shape_unexpected", {
    schema,
    hasBody: Boolean(body),
    hasElements: Array.isArray(elements),
  });
}

function findForbiddenV2TagPath(
  elements: unknown[],
  path = "body.elements",
): string | null {
  for (const [index, element] of elements.entries()) {
    if (!element || typeof element !== "object") continue;

    const record = element as Record<string, unknown>;
    const currentPath = `${path}[${index}]`;
    if (record.tag === "action") return currentPath;

    const nestedElements = record.elements;
    if (Array.isArray(nestedElements)) {
      const nestedPath = findForbiddenV2TagPath(nestedElements, `${currentPath}.elements`);
      if (nestedPath) return nestedPath;
    }

    const nestedColumns = record.columns;
    if (Array.isArray(nestedColumns)) {
      const columnPath = findForbiddenV2ColumnsPath(nestedColumns, `${currentPath}.columns`);
      if (columnPath) return columnPath;
    }
  }

  return null;
}

function findForbiddenV2ColumnsPath(
  columns: unknown[],
  path: string,
): string | null {
  for (const [index, column] of columns.entries()) {
    if (!column || typeof column !== "object") continue;

    const record = column as Record<string, unknown>;
    const currentPath = `${path}[${index}]`;
    const nestedElements = record.elements;
    if (!Array.isArray(nestedElements)) continue;

    const nestedPath = findForbiddenV2TagPath(nestedElements, `${currentPath}.elements`);
    if (nestedPath) return nestedPath;
  }

  return null;
}

function extensionFromContentType(contentType?: string): string {
  switch (contentType) {
    case "image/png":
      return ".png";
    case "image/gif":
      return ".gif";
    case "image/webp":
      return ".webp";
    case "image/jpeg":
    case "image/jpg":
    default:
      return ".jpg";
  }
}

function resolveApiBaseUrl(brand?: string): string {
  if (!brand || brand === "feishu") return "https://open.feishu.cn";
  if (brand === "lark") return "https://open.larksuite.com";

  const normalized = brand.replace(/\/+$/, "");
  return /^https?:\/\//i.test(normalized) ? normalized : `https://${normalized}`;
}

function toFeishuFileType(fileName: string, mediaType?: string): string {
  const extension = path.extname(fileName).toLowerCase();
  if (mediaType === "application/pdf" || extension === ".pdf") return "pdf";
  if (extension === ".doc" || extension === ".docx") return "doc";
  if (extension === ".xls" || extension === ".xlsx" || extension === ".csv") return "xls";
  if (extension === ".ppt" || extension === ".pptx") return "ppt";
  if (extension === ".opus") return "opus";
  if (mediaType === "video/mp4" || extension === ".mp4") return "mp4";
  return "stream";
}

function inferMediaType(fileName: string): string {
  const extension = path.extname(fileName).toLowerCase();
  return extension === ".pdf"
    ? "application/pdf"
    : extension === ".json"
      ? "application/json"
      : extension === ".xml"
        ? "application/xml"
        : extension === ".txt"
          ? "text/plain"
          : extension === ".csv"
            ? "text/csv"
            : extension === ".md"
              ? "text/markdown"
              : "application/octet-stream";
}
