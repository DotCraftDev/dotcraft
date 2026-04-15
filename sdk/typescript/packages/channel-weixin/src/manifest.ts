import { sdkContractVersion } from "dotcraft-wire";
import type { ModuleManifest } from "dotcraft-wire";

export const manifest: ModuleManifest = {
  moduleId: "weixin-standard",
  channelName: "weixin",
  displayName: "Weixin (iLink/企业微信)",
  packageName: "@dotcraft/channel-weixin",
  configFileName: "weixin.json",
  supportedTransports: ["websocket"],
  requiresInteractiveSetup: true,
  capabilitySummary: {
    hasChannelTools: true,
    hasStructuredDelivery: true,
    requiresInteractiveSetup: true,
    capabilitySetMayVaryByEnvironment: false,
  },
  sdkContractVersion,
  supportedProtocolVersions: ["0.2"],
  variant: "standard",
  launcher: {
    bin: "dotcraft-channel-weixin",
    supportsWorkspaceFlag: true,
    supportsConfigOverrideFlag: true,
  },
};
