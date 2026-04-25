import { sdkContractVersion } from "dotcraft-wire";
import type { ModuleManifest } from "dotcraft-wire";

export const manifest: ModuleManifest = {
  moduleId: "qq-standard",
  channelName: "qq",
  displayName: "QQ",
  packageName: "@dotcraft/channel-qq",
  configFileName: "qq.json",
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
    bin: "dotcraft-channel-qq",
    supportsWorkspaceFlag: true,
    supportsConfigOverrideFlag: true,
  },
};
