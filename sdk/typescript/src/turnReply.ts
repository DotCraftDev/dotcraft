/**
 * Helpers to build the final assistant reply from turn/completed snapshots and streamed deltas.
 * turn/completed includes full turn items; delta aggregation can miss early chunks if handlers
 * attach after turn/start returns (race). Prefer the snapshot when it is at least as long as deltas.
 */

/**
 * Concatenates text from all agentMessage items in turn/completed params (wire order).
 */
export function extractAgentReplyTextFromTurnCompletedParams(
  params: Record<string, unknown> | null | undefined,
): string {
  return extractAgentReplyTextsFromTurnCompletedParams(params).join("");
}

/**
 * Extracts text from each agentMessage item in turn/completed params (wire order).
 */
export function extractAgentReplyTextsFromTurnCompletedParams(
  params: Record<string, unknown> | null | undefined,
): string[] {
  if (!params) return [];
  const turn = params.turn as Record<string, unknown> | undefined;
  if (!turn) return [];
  const items = turn.items;
  if (!Array.isArray(items)) return [];
  const parts: string[] = [];
  for (const raw of items) {
    if (!raw || typeof raw !== "object") continue;
    const item = raw as Record<string, unknown>;
    if (item.type !== "agentMessage") continue;
    const payload = item.payload as Record<string, unknown> | undefined;
    if (!payload) continue;
    const text = payload.text;
    if (typeof text === "string" && text.length > 0) parts.push(text);
  }
  return parts;
}

/**
 * Prefer the longer of snapshot (from turn/completed) vs streamed deltas (avoids truncated prefix).
 */
export function mergeReplyTextFromDeltaAndSnapshot(deltaText: string, snapshotText: string): string {
  if (snapshotText.length >= deltaText.length) return snapshotText;
  return deltaText;
}
