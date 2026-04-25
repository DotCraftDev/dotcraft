import type { ModuleInstance, WorkspaceContext } from "dotcraft-wire";

import { WeComAdapter } from "./wecom-adapter.js";

export function createModule(context: WorkspaceContext): ModuleInstance {
  const adapter = new WeComAdapter();
  return {
    start: () => adapter.startWithContext(context),
    stop: () => adapter.stop(),
    onStatusChange: (handler) => adapter.onStatusChange(handler),
    getStatus: () => adapter.getStatus(),
    getError: () => adapter.getError(),
  };
}

