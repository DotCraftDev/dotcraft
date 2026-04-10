import { buildReplyCards } from "./card-builder.js";
import type { FeishuClient } from "./feishu-client.js";

export async function sendReplyCards(
  client: FeishuClient,
  target: string,
  replyText: string,
): Promise<void> {
  const cards = buildReplyCards(replyText);
  for (const card of cards) {
    await client.sendInteractiveCard(target, card);
  }
}

export async function sendSingleCard(
  client: FeishuClient,
  target: string,
  card: Record<string, unknown>,
): Promise<void> {
  await client.sendInteractiveCard(target, card);
}

export async function updateCard(
  client: FeishuClient,
  messageId: string,
  card: Record<string, unknown>,
): Promise<void> {
  await client.updateInteractiveCard(messageId, card);
}
