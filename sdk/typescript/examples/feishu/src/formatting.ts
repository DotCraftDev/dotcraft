const CARD_TEXT_LIMIT = 3200;

export function normalizeMarkdownForFeishu(markdown: string): string {
  return markdown
    .replace(/\r\n/g, "\n")
    .replace(/\t/g, "  ")
    .replace(/\n{3,}/g, "\n\n")
    .trim();
}

export function chunkMarkdown(markdown: string, limit = CARD_TEXT_LIMIT): string[] {
  const text = normalizeMarkdownForFeishu(markdown);
  if (!text) return [];
  if (text.length <= limit) return [text];

  const chunks: string[] = [];
  let remaining = text;
  while (remaining.length > limit) {
    let splitAt = remaining.lastIndexOf("\n\n", limit);
    if (splitAt < Math.floor(limit * 0.5)) {
      splitAt = remaining.lastIndexOf("\n", limit);
    }
    if (splitAt < Math.floor(limit * 0.5)) {
      splitAt = limit;
    }
    chunks.push(remaining.slice(0, splitAt).trim());
    remaining = remaining.slice(splitAt).trim();
  }
  if (remaining) chunks.push(remaining);
  return chunks;
}

export function summarizeApprovalOperation(
  approvalType: string,
  operation: string,
  target: string,
): string {
  if (approvalType === "shell") {
    return `Command: \`${operation}\``;
  }
  return `Operation: \`${operation}\`\nTarget: \`${target || "(not provided)"}\``;
}
