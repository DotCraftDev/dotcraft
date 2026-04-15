import { sdkContractVersion } from "dotcraft-wire";
import type { ModuleManifest } from "dotcraft-wire";

export const manifest: ModuleManifest = {
  moduleId: "feishu-standard",
  channelName: "feishu",
  displayName: "Feishu (Lark)",
  packageName: "@dotcraft/channel-feishu",
  configFileName: "feishu.json",
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
    bin: "dotcraft-channel-feishu",
    supportsWorkspaceFlag: true,
    supportsConfigOverrideFlag: true,
  },
};
