import assert from "node:assert/strict";
import test from "node:test";

import { QQPermissionService } from "./permission.js";
import { channelContextForQQEvent, parseQQTarget } from "./target.js";

test("QQPermissionService classifies admins, users, groups, and unauthorized users", () => {
  const permissions = new QQPermissionService({
    adminUsers: [1],
    whitelistedUsers: [2],
    whitelistedGroups: [10],
  });

  assert.equal(permissions.getUserRole(1), "admin");
  assert.equal(permissions.getUserRole(2), "whitelisted");
  assert.equal(permissions.getUserRole(3, 10), "whitelisted");
  assert.equal(permissions.getUserRole(3, 11), "unauthorized");
});

test("QQ target parsing accepts group, user, and bare user ids", () => {
  assert.deepEqual(parseQQTarget("group:123"), { kind: "group", id: "123" });
  assert.deepEqual(parseQQTarget("user:456"), { kind: "user", id: "456" });
  assert.deepEqual(parseQQTarget("789"), { kind: "user", id: "789" });
  assert.equal(parseQQTarget("group:abc"), null);
});

test("channelContextForQQEvent preserves native QQ session semantics", () => {
  assert.equal(channelContextForQQEvent(true, 123, 456), "group:123");
  assert.equal(channelContextForQQEvent(false, undefined, 456), "user:456");
});
