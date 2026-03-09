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

/** Returns a minimal AG-UI SSE stream (RUN_STARTED + RUN_FINISHED) so CopilotKit gets a valid response without running the agent. */
function emptyAgUiStream(threadId: string, runId: string): ReadableStream<Uint8Array> {
  const started = `data: ${JSON.stringify({ type: "RUN_STARTED", threadId, runId })}\n\n`;
  const finished = `data: ${JSON.stringify({ type: "RUN_FINISHED", threadId, runId })}\n\n`;
  return new ReadableStream({
    start(controller) {
      controller.enqueue(new TextEncoder().encode(started));
      controller.enqueue(new TextEncoder().encode(finished));
      controller.close();
    },
  });
}

/**
 * Tracks in-flight backend SSE connections keyed by threadId.
 * Aborting the controller closes the fetch to the backend, which causes
 * ASP.NET Core to fire HttpContext.RequestAborted and cancel the running agent.
 */
const activeRuns = new Map<string, AbortController>();

/** Proxy a run/connect request to the real backend, wired for abort. */
async function proxyAgUiRun(
  body: unknown,
): Promise<Response> {
  const runBody = body as RunBody;
  const threadId = runBody.threadId ?? "dotcraft-1";

  // Abort any previous run for this thread before starting a new one.
  activeRuns.get(threadId)?.abort();

  const abortController = new AbortController();
  activeRuns.set(threadId, abortController);

  const agUiUrl = getAgUiUrl();
  const res = await fetch(agUiUrl, {
    method: "POST",
    headers: getAgUiHeaders(),
    body: JSON.stringify(body),
    signal: abortController.signal,
  });

  const responseHeaders = new Headers();
  res.headers.forEach((value, key) => {
    if (key.toLowerCase() !== "content-encoding") {
      responseHeaders.set(key, value);
    }
  });

  // Pipe through a TransformStream so we can clean up the map when the stream ends
  // (naturally or via abort).
  const { readable, writable } = new TransformStream();
  res.body!.pipeTo(writable).catch(() => {
    // Suppress AbortError thrown when the run is stopped mid-stream.
  }).finally(() => {
    if (activeRuns.get(threadId) === abortController) {
      activeRuns.delete(threadId);
    }
  });

  return new Response(readable, { status: res.status, headers: responseHeaders });
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
      return new Response(emptyAgUiStream(threadId, runId), {
        status: 200,
        headers: { "Content-Type": "text/event-stream", "Cache-Control": "no-cache", Connection: "keep-alive" },
      });
    }
    return proxyAgUiRun(envelope.body);
  }

  if (method === "agent/connect" && envelope?.body != null) {
    if (!hasUserMessage(envelope.body)) {
      const body = envelope.body as RunBody;
      const threadId = body.threadId ?? "dotcraft-1";
      const runId = body.runId ?? `run_${Date.now()}`;
      return new Response(emptyAgUiStream(threadId, runId), {
        status: 200,
        headers: { "Content-Type": "text/event-stream", "Cache-Control": "no-cache", Connection: "keep-alive" },
      });
    }
    return proxyAgUiRun(envelope.body);
  }

  if (method === "agent/stop") {
    const threadId = envelope.params?.threadId as string | undefined;
    if (threadId) {
      const ctrl = activeRuns.get(threadId);
      if (ctrl) {
        ctrl.abort();
        activeRuns.delete(threadId);
      }
    }
    return Response.json({ ok: true });
  }

  return Response.json(
    { error: "unsupported_method", message: `Unsupported method: ${method}` },
    { status: 400 }
  );
}
