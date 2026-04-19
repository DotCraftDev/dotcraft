import { sdkContractVersion } from "dotcraft-wire";
import type { ModuleManifest } from "dotcraft-wire";

export const manifest: ModuleManifest = {
  moduleId: "telegram-standard",
  channelName: "telegram",
  displayName: "Telegram",
  packageName: "@dotcraft/channel-telegram",
  configFileName: "telegram.json",
  supportedTransports: ["websocket"],
  requiresInteractiveSetup: false,
  capabilitySummary: {
    hasChannelTools: true,
    hasStructuredDelivery: true,
    requiresInteractiveSetup: false,
    capabilitySetMayVaryByEnvironment: false,
  },
  sdkContractVersion,
  supportedProtocolVersions: ["0.2"],
  variant: "standard",
  launcher: {
    bin: "dotcraft-channel-telegram",
    supportsWorkspaceFlag: true,
    supportsConfigOverrideFlag: true,
  },
};
