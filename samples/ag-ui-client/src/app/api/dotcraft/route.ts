/**
 * Proxy from CopilotKit (single-endpoint) to DotCraft AG-UI.
 * DotCraft must be running with AgUi.Enabled: true (default http://localhost:5100/ag-ui).
 * Optional env: DOTCRAFT_AGUI_URL, DOTCRAFT_AGUI_API_KEY (when RequireAuth).
 */

const DEFAULT_AGUI_URL = "http://localhost:5100/ag-ui";

function getAgUiUrl(): string {
  return process.env.DOTCRAFT_AGUI_URL ?? DEFAULT_AGUI_URL;
}

function getAgUiHeaders(): Record<string, string> {
  const headers: Record<string, string> = {
    "Content-Type": "application/json",
    Accept: "text/event-stream",
  };
  const apiKey = process.env.DOTCRAFT_AGUI_API_KEY;
  if (apiKey) {
    headers.Authorization = `Bearer ${apiKey}`;
  }
  return headers;
}

function runtimeInfo() {
  return {
    version: "1.0",
    agents: {
      default: {
        name: "default",
        className: "DotCraft",
        description: "DotCraft AG-UI agent",
      },
    },
    audioFileTranscriptionEnabled: false,
  };
}

/** AG-UI request body shape (threadId, runId, messages, ...). */
type RunBody = { threadId?: string; runId?: string; messages?: Array<{ role?: string; content?: string }> };

/** Returns true if body has at least one user message so we should run the agent. */
function hasUserMessage(body: unknown): boolean {
  if (body == null || typeof body !== "object") return false;
  const b = body as RunBody;
  const messages = b.messages;
  if (!Array.isArray(messages) || messages.length === 0) return false;
  return messages.some((m) => m && String(m.role).toLowerCase() === "user");
}

/**
 * For agent/connect requests, we need to always forward to the backend
 * to restore session history. This is the key fix for session recovery.
 * 
 * The connect request is sent when:
 * 1. Page loads and needs to restore previous conversation
 * 2. User switches between threads
 * 
 * The backend should return historical events via SSE if the thread exists.
 */
async function handleConnect(reqBody: unknown): Promise<Response> {
  const body = reqBody as RunBody;
  const threadId = body.threadId ?? "dotcraft-1";
  const runId = body.runId ?? `run_${Date.now()}`;

  const agUiUrl = getAgUiUrl();

  try {
    const res = await fetch(agUiUrl, {
      method: "POST",
      headers: getAgUiHeaders(),
      body: JSON.stringify(reqBody),
    });

    // If backend returns 404 or no content, it means thread doesn't exist
    // Return empty stream so frontend initializes fresh
    if (!res.ok || !res.body) {
      return new Response(createEmptyStream(threadId, runId), {
        status: 200,
        headers: {
          "Content-Type": "text/event-stream",
          "Cache-Control": "no-cache",
          Connection: "keep-alive",
        },
      });
    }

    // Forward the response from backend (including historical messages)
    const responseHeaders = new Headers();
    res.headers.forEach((value, key) => {
      if (key.toLowerCase() !== "content-encoding") {
        responseHeaders.set(key, value);
      }
    });

    return new Response(res.body, {
      status: res.status,
      headers: responseHeaders,
    });
  } catch (error) {
    // If connection fails, return empty stream
    console.error("AG-UI connect failed:", error);
    return new Response(createEmptyStream(threadId, runId), {
      status: 200,
      headers: {
        "Content-Type": "text/event-stream",
        "Cache-Control": "no-cache",
        Connection: "keep-alive",
      },
    });
  }
}

/**
 * Creates an empty AG-UI SSE stream with RUN_STARTED and RUN_FINISHED events.
 * Used when no backend is available or thread doesn't exist.
 */
function createEmptyStream(threadId: string, runId: string): ReadableStream<Uint8Array> {
  const started = `event: message_added\ndata: ${JSON.stringify({ type: "RUN_STARTED", threadId, runId })}\n\n`;
  const finished = `event: message_added\ndata: ${JSON.stringify({ type: "RUN_FINISHED", threadId, runId })}\n\n`;
  
  return new ReadableStream({
    start(controller) {
      controller.enqueue(new TextEncoder().encode(started));
      controller.enqueue(new TextEncoder().encode(finished));
      controller.close();
    },
  });
}

export async function POST(req: Request): Promise<Response> {
  let envelope: { method?: string; params?: Record<string, unknown>; body?: unknown };
  try {
    envelope = await req.json();
  } catch {
    return Response.json(
      { error: "invalid_request", message: "Invalid JSON body" },
      { status: 400 }
    );
  }

  const method = envelope?.method;

  if (method === "info") {
    return Response.json(runtimeInfo(), {
      headers: { "Content-Type": "application/json" },
    });
  }

  if (method === "agent/run" && envelope?.body != null) {
    if (!hasUserMessage(envelope.body)) {
      const body = envelope.body as RunBody;
      const threadId = body.threadId ?? "dotcraft-1";
      const runId = body.runId ?? `run_${Date.now()}`;
      return new Response(createEmptyStream(threadId, runId), {
        status: 200,
        headers: { 
          "Content-Type": "text/event-stream", 
          "Cache-Control": "no-cache", 
          Connection: "keep-alive" 
        },
      });
    }
    const agUiUrl = getAgUiUrl();
    const res = await fetch(agUiUrl, {
      method: "POST",
      headers: getAgUiHeaders(),
      body: JSON.stringify(envelope.body),
    });

    const responseHeaders = new Headers();
    res.headers.forEach((value, key) => {
      if (key.toLowerCase() !== "content-encoding") {
        responseHeaders.set(key, value);
      }
    });

    return new Response(res.body, {
      status: res.status,
      headers: responseHeaders,
    });
  }

  if (method === "agent/connect" && envelope?.body != null) {
    // Always forward connect requests to backend for session recovery
    // This is the key fix - previously it checked hasUserMessage and returned empty stream
    return handleConnect(envelope.body);
  }

  if (method === "agent/stop") {
    return Response.json({ ok: true });
  }

  return Response.json(
    { error: "unsupported_method", message: `Unsupported method: ${method}` },
    { status: 400 }
  );
}
