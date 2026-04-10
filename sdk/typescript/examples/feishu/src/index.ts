import { existsSync, readFileSync } from "node:fs";
import { mkdir } from "node:fs/promises";
import { join, resolve } from "node:path";

import { createFeishuEventHandlers } from "./event-handler.js";
import { FeishuAdapter } from "./feishu-adapter.js";
import { FeishuClient } from "./feishu-client.js";
import type { AppConfig } from "./feishu-types.js";
import { errorMessage, logError, logInfo, logWarn } from "./logging.js";

function loadConfig(): AppConfig {
  const configPath =
    process.argv[2] ||
    process.env.DOTCRAFT_FEISHU_CONFIG ||
    join(process.cwd(), "adapter_config.json");
  if (!existsSync(configPath)) {
    console.error(`Config not found: ${configPath}`);
    console.error("Copy config.example.json to adapter_config.json and fill in your Feishu app credentials.");
    process.exit(1);
  }

  const config = JSON.parse(readFileSync(configPath, "utf-8")) as AppConfig;
  if (!config.feishu?.appId || !config.feishu?.appSecret) {
    console.error("Missing feishu.appId or feishu.appSecret in adapter_config.json.");
    process.exit(1);
  }
  if (!config.dotcraft?.wsUrl) {
    console.error("Missing dotcraft.wsUrl in adapter_config.json.");
    process.exit(1);
  }
  return config;
}

async function main(): Promise<void> {
  const config = loadConfig();
  logInfo("startup.config_loaded", {
    brand: config.feishu.brand ?? "feishu",
    groupMentionRequired: config.feishu.groupMentionRequired !== false,
    hasDownloadDir: Boolean(config.feishu.downloadDir),
    hasDotcraftToken: Boolean(config.dotcraft.token),
  });
  if (config.feishu.downloadDir) {
    await mkdir(resolve(config.feishu.downloadDir), { recursive: true });
    logInfo("startup.download_dir_ready", {
      downloadDir: resolve(config.feishu.downloadDir),
    });
  }

  const feishuClient = new FeishuClient(config.feishu);
  const botInfo = await feishuClient.probeBot();
  logInfo("startup.bot_probe", {
    hasBotIdentity: botInfo.hasBotIdentity,
    appName: botInfo.appName || "(unnamed)",
    hasDiagnostic: Boolean(botInfo.diagnosticMessage),
  });
  if (!botInfo.hasBotIdentity) {
    logWarn("startup.bot_probe_fallback", {
      reason: "missing_bot_identity",
    });
    if (botInfo.diagnosticMessage) {
      logWarn("startup.bot_probe_diagnostic", {
        message: botInfo.diagnosticMessage,
      });
    }
    logWarn("startup.bot_probe_fallback_behavior", {
      dm: "allow",
      group:
        config.feishu.groupMentionRequired !== false
          ? "skip_when_mention_required"
          : "allow_when_mention_required_disabled",
    });
  } else {
    logInfo("startup.bot_ready", {
      appName: botInfo.appName || "(unnamed app)",
      openId: botInfo.openId,
      botName: botInfo.botName || "unknown",
    });
  }

  const adapter = new FeishuAdapter({
    wsUrl: config.dotcraft.wsUrl,
    dotcraftToken: config.dotcraft.token,
    approvalTimeoutMs: config.feishu.approvalTimeoutMs ?? 120000,
    feishu: feishuClient,
  });
  await adapter.start();
  logInfo("dotcraft.ws.connected");

  const handlers = createFeishuEventHandlers({
    adapter,
    client: feishuClient,
    bot: botInfo,
    config: config.feishu,
  });

  const abortController = new AbortController();
  process.on("SIGINT", () => {
    logInfo("shutdown.signal_received", { signal: "SIGINT" });
    abortController.abort();
  });
  process.on("SIGTERM", () => {
    logInfo("shutdown.signal_received", { signal: "SIGTERM" });
    abortController.abort();
  });

  try {
    await feishuClient.startEventStream(handlers, abortController.signal);
  } finally {
    feishuClient.stopEventStream();
    await adapter.stop();
    logInfo("shutdown.cleanup_done");
  }
}

main().catch((error) => {
  logError("startup.fatal", { message: errorMessage(error) });
  console.error(error);
  process.exit(1);
});
