import assert from "node:assert/strict";
import test from "node:test";

import { parseWeComApprovalDecision } from "./approval.js";
import { WeComPermissionService } from "./permission.js";
import { WE_COM_SEND_FILE_TOOL, WE_COM_SEND_VOICE_TOOL, WeComMediaTools } from "./wecom-media-tools.js";
import { parseWeComMessage, parseWeComParameters, WeComChatType } from "./wecom-types.js";

test("WeComPermissionService classifies admins, whitelisted users, chats, and unauthorized users", () => {
  const permissions = new WeComPermissionService({
    adminUsers: ["admin"],
    whitelistedUsers: ["user"],
    whitelistedChats: ["chat"],
  });

  assert.equal(permissions.getUserRole("admin"), "admin");
  assert.equal(permissions.getUserRole("user"), "whitelisted");
  assert.equal(permissions.getUserRole("someone", "chat"), "whitelisted");
  assert.equal(permissions.getUserRole("someone", "other"), "unauthorized");
});

test("parseWeComApprovalDecision accepts Chinese and English keywords", () => {
  assert.equal(parseWeComApprovalDecision("同意"), "accept");
  assert.equal(parseWeComApprovalDecision("yes all"), "acceptForSession");
  assert.equal(parseWeComApprovalDecision("拒绝"), "decline");
  assert.equal(parseWeComApprovalDecision("hello"), null);
});

test("parseWeComParameters strips leading mention in group chats", () => {
  assert.deepEqual(parseWeComParameters("@DotCraft hello world", WeComChatType.Group), ["hello", "world"]);
  assert.deepEqual(parseWeComParameters("@DotCraft hello", WeComChatType.Single), ["@DotCraft", "hello"]);
});

test("parseWeComMessage parses JSON mixed messages", () => {
  const message = parseWeComMessage(JSON.stringify({
    msgid: "m1",
    chattype: "group",
    msgtype: "mixed",
    chatid: "c1",
    webhook_url: "https://example.test/webhook?key=k",
    from: { userid: "u1", name: "User" },
    mixed: {
      msg_item: [
        { msgtype: "text", text: { content: "hello" } },
        { msgtype: "image", image: { url: "https://example.test/a.jpg" } },
      ],
    },
  }));
  assert.equal(message?.mixedMessage?.msgItems.length, 2);
  assert.equal(message?.mixedMessage?.msgItems[0]?.text?.content, "hello");
});

test("WeComMediaTools preserves legacy tool names and current-chat requirement", () => {
  const tools = new WeComMediaTools().getChannelTools();
  assert.deepEqual(tools.map((tool) => tool.name), [WE_COM_SEND_VOICE_TOOL, WE_COM_SEND_FILE_TOOL]);
  assert.ok(tools.every((tool) => tool.requiresChatContext === true));
  assert.equal((tools[0]?.display as Record<string, unknown> | undefined)?.icon, "🎤");
  assert.equal((tools[1]?.display as Record<string, unknown> | undefined)?.icon, "📁");
});
