/**
 * DotCraft Weixin external channel adapter (CLI).
 *
 * Requires DotCraft AppServer with WebSocket and ExternalChannels.weixin enabled.
 */

import { readFileSync, existsSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

import { waitForQrLogin } from "./auth.js";
import { runMonitorLoop } from "./monitor.js";
import type { WeixinCredentials } from "./state.js";
import { WeixinState } from "./state.js";
import { WeixinAdapter } from "./weixin-adapter.js";

const __dirname = dirname(fileURLToPath(import.meta.url));

interface AppConfig {
  dotcraft: { wsUrl: string; token?: string };
  weixin: {
    apiBaseUrl: string;
    dataDir: string;
    approvalTimeoutMs?: number;
    botType?: string;
  };
}

function loadConfig(): AppConfig {
  const path =
    process.argv[2] ||
    process.env.DOTCRAFT_WEIXIN_CONFIG ||
    join(process.cwd(), "adapter_config.json");
  if (!existsSync(path)) {
    console.error(`Config not found: ${path}`);
    console.error("Create adapter_config.json next to this process (see repo) or pass a path.");
    process.exit(1);
  }
  return JSON.parse(readFileSync(path, "utf-8")) as AppConfig;
}

async function ensureCredentials(
  state: WeixinState,
  apiBaseUrl: string,
  botType: string,
): Promise<WeixinCredentials> {
  const existing = state.loadCredentials();
  if (existing?.botToken && existing.ilinkBotId) {
    console.log("Using saved Weixin credentials.");
    return existing;
  }

  console.log("Starting Weixin QR login...");
  const qrcodeTerminal = await import("qrcode-terminal");

  const creds = await waitForQrLogin({
    apiBaseUrl,
    botType,
    onQrUrl: (url) => {
      console.log("\nScan this QR with WeChat:\n", url, "\n");
      try {
        qrcodeTerminal.default.generate(url, { small: true });
      } catch {
        /* ignore */
      }
    },
    deadlineMs: 480_000,
  });

  state.saveCredentials(creds);
  return creds;
}

async function main(): Promise<void> {
  const cfg = loadConfig();
  const dataDir = cfg.weixin.dataDir.startsWith(".")
    ? join(process.cwd(), cfg.weixin.dataDir)
    : cfg.weixin.dataDir;
  const state = new WeixinState(dataDir);

  const botType = cfg.weixin.botType ?? "3";
  const creds = await ensureCredentials(state, cfg.weixin.apiBaseUrl, botType);

  const adapter = new WeixinAdapter({
    wsUrl: cfg.dotcraft.wsUrl,
    dotcraftToken: cfg.dotcraft.token,
    apiBaseUrl: cfg.weixin.apiBaseUrl,
    approvalTimeoutMs: cfg.weixin.approvalTimeoutMs ?? 120_000,
    state,
    credentials: creds,
  });

  await adapter.start();
  console.log("Connected to DotCraft; starting Weixin monitor...");

  const ac = new AbortController();
  process.on("SIGINT", () => ac.abort());

  await runMonitorLoop({
    baseUrl: creds.baseUrl || cfg.weixin.apiBaseUrl,
    token: creds.botToken,
    getInitialBuf: () => state.loadSyncBuf(),
    saveBuf: (buf) => state.saveSyncBuf(buf),
    abortSignal: ac.signal,
    callbacks: {
      onMessage: async (msg) => {
        await adapter.handleInboundUserMessage(msg);
      },
      onSessionExpired: async () => {
        console.log("Session expired — run again to scan QR.");
      },
    },
  });

  await adapter.stop();
}

main().catch((e) => {
  console.error(e);
  process.exit(1);
});
