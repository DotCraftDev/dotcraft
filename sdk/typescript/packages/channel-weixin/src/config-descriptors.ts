import type { ConfigDescriptor } from "dotcraft-wire";

export const configDescriptors: ConfigDescriptor[] = [
  {
    key: "dotcraft.wsUrl",
    displayLabel: "AppServer WebSocket URL",
    description: "DotCraft AppServer WebSocket endpoint (ws:// or wss://).",
    required: true,
    dataKind: "string",
    masked: false,
    interactiveSetupOnly: false,
  },
  {
    key: "dotcraft.token",
    displayLabel: "AppServer Auth Token",
    description: "Optional token used by DotCraft AppServer WebSocket transport.",
    required: false,
    dataKind: "secret",
    masked: true,
    interactiveSetupOnly: false,
  },
  {
    key: "weixin.apiBaseUrl",
    displayLabel: "Weixin API Base URL",
    description: "Tencent iLink API base URL.",
    required: true,
    dataKind: "string",
    masked: false,
    interactiveSetupOnly: false,
  },
  {
    key: "weixin.pollIntervalMs",
    displayLabel: "Poll Interval (ms)",
    description: "Delay between polling cycles after each response.",
    required: false,
    dataKind: "number",
    masked: false,
    interactiveSetupOnly: false,
  },
  {
    key: "weixin.pollTimeoutMs",
    displayLabel: "Poll Timeout (ms)",
    description: "Long-poll timeout for getUpdates requests.",
    required: false,
    dataKind: "number",
    masked: false,
    interactiveSetupOnly: false,
  },
];
