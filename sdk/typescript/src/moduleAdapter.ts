/**
 * Module-aware ChannelAdapter runtime helpers and base class.
 */

import { readFile } from "node:fs/promises";
import { join } from "node:path";

import { ChannelAdapter, type ChannelAdapterOptions } from "./adapter.js";
import { DotCraftClient } from "./client.js";
import type { ModuleError, ModuleErrorCode } from "./lifecycle.js";
import type { WorkspaceContext } from "./module.js";
import { Transport, TransportClosed } from "./transport.js";

export type LoadJsonConfigResult = { found: true; data: unknown } | { found: false };

export function resolveConfigPath(context: WorkspaceContext, configFileName: string): string {
  if (context.configOverridePath) return context.configOverridePath;
  return join(context.craftPath, configFileName);
}

export async function loadJsonConfig(configPath: string): Promise<LoadJsonConfigResult> {
  try {
    const raw = await readFile(configPath, "utf-8");
    return { found: true, data: JSON.parse(raw) };
  } catch (error) {
    if (isNodeErrno(error, "ENOENT")) {
      return { found: false };
    }
    throw error;
  }
}

export function resolveModuleStatePath(context: WorkspaceContext): string {
  return join(context.craftPath, "state", context.moduleId);
}

export function resolveModuleTempPath(context: WorkspaceContext): string {
  return join(context.craftPath, "tmp", context.moduleId);
}

export class ConfigValidationError extends Error {
  readonly fields?: string[];

  constructor(message: string, fields?: string[]) {
    super(message);
    this.name = "ConfigValidationError";
    this.fields = fields;
  }
}

export abstract class ModuleChannelAdapter<TConfig = unknown> extends ChannelAdapter {
  protected context: WorkspaceContext | undefined;
  protected loadedConfig: TConfig | undefined;

  constructor(
    channelName: string,
    clientName: string,
    clientVersion: string,
    optOutNotifications: string[] = [],
    options?: ChannelAdapterOptions,
  ) {
    super(new PlaceholderTransport(), channelName, clientName, clientVersion, optOutNotifications, options);
  }

  protected getConfigFileName(context: WorkspaceContext): string {
    return `${context.channelName}.json`;
  }

  protected abstract validateConfig(rawConfig: unknown): asserts rawConfig is TConfig;

  protected abstract buildTransportFromConfig(config: TConfig): Transport;

  async startWithContext(context: WorkspaceContext): Promise<void> {
    this.context = context;
    this.setStatus("starting");

    const configFilePath = resolveConfigPath(context, this.getConfigFileName(context));
    const loaded = await loadJsonConfig(configFilePath);
    if (!loaded.found) {
      this.setStatus(
        "configMissing",
        this.buildModuleError("configMissing", `Config file not found: ${configFilePath}`),
      );
      return;
    }

    try {
      this.validateConfig(loaded.data);
      this.loadedConfig = loaded.data;
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      this.setStatus("configInvalid", this.buildModuleError("configInvalid", message));
      return;
    }

    try {
      const transport = this.buildTransportFromConfig(this.loadedConfig);
      this.client = new DotCraftClient(transport);
      await super.start();
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      this.setStatus("stopped", this.buildModuleError("startupFailed", message));
    }
  }

  protected signalAuthRequired(error?: Partial<ModuleError>): void {
    this.setStatus("authRequired", this.buildStatusError("authRequired", error));
  }

  protected signalAuthExpired(error?: Partial<ModuleError>): void {
    this.setStatus("authExpired", this.buildStatusError("authExpired", error));
  }

  private buildStatusError(code: "authRequired" | "authExpired", error?: Partial<ModuleError>): ModuleError {
    return {
      code,
      message: error?.message ?? (code === "authRequired" ? "Interactive authentication is required." : "Authentication has expired."),
      detail: error?.detail,
      timestamp: error?.timestamp ?? new Date().toISOString(),
    };
  }

  private buildModuleError(code: ModuleErrorCode, message: string): ModuleError {
    return {
      code,
      message,
      timestamp: new Date().toISOString(),
    };
  }
}

class PlaceholderTransport implements Transport {
  async readMessage(): Promise<Record<string, unknown>> {
    throw new TransportClosed("ModuleChannelAdapter transport not configured");
  }

  async writeMessage(_msg: Record<string, unknown>): Promise<void> {
    throw new TransportClosed("ModuleChannelAdapter transport not configured");
  }

  async close(): Promise<void> {}
}

function isNodeErrno(error: unknown, code: string): boolean {
  return (
    typeof error === "object" &&
    error !== null &&
    "code" in error &&
    String((error as { code?: unknown }).code) === code
  );
}
