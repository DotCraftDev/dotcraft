"use client";

import { CopilotChat, CopilotKitProvider } from "@copilotkitnext/react";

export default function DotCraftChatPage() {
  return (
    <CopilotKitProvider runtimeUrl="/api/dotcraft" useSingleEndpoint>
      <div style={{ height: "100vh", display: "flex", flexDirection: "column" }}>
        <CopilotChat threadId="dotcraft-1" />
      </div>
    </CopilotKitProvider>
  );
}
