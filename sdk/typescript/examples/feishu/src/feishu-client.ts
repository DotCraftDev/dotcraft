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

  constructor(private readonly config: AppConfig["feishu"]) {
    this.appId = config.appId;
    this.appSecret = config.appSecret;
    this.domain = resolveBrand(config.brand);
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

  async updateInteractiveCard(messageId: string, card: Record<string, unknown>): Promise<void> {
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
