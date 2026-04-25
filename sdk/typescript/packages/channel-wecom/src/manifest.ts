import { sdkContractVersion } from "dotcraft-wire";
import type { ModuleManifest } from "dotcraft-wire";

export const manifest: ModuleManifest = {
  moduleId: "wecom-standard",
  channelName: "wecom",
  displayName: "WeCom",
  localizedDisplayName: {
    en: "WeCom",
    "zh-Hans": "企业微信",
  },
  packageName: "@dotcraft/channel-wecom",
  configFileName: "wecom.json",
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
    bin: "dotcraft-channel-wecom",
    supportsWorkspaceFlag: true,
    supportsConfigOverrideFlag: true,
  },
};
