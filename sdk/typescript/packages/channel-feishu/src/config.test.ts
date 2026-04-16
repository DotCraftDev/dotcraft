import assert from "node:assert/strict";
import test from "node:test";

import { ConfigValidationError } from "dotcraft-wire";

import { validateFeishuConfig } from "./feishu-adapter.js";
import type { FeishuConfig } from "./feishu-types.js";

function validConfig(): FeishuConfig {
  return {
    dotcraft: {
      wsUrl: "ws://127.0.0.1:9100/ws",
    },
    feishu: {
      appId: "cli_test",
      appSecret: "test-secret",
    },
  };
}

test("throws ConfigValidationError when feishu.appId is missing", () => {
  const config = validConfig();
  config.feishu.appId = "";
  assert.throws(
    () => validateFeishuConfig(config),
    (error: unknown) =>
      error instanceof ConfigValidationError &&
      Array.isArray(error.fields) &&
      error.fields.includes("feishu.appId"),
  );
});

test("throws ConfigValidationError when feishu.appSecret is missing", () => {
  const config = validConfig();
  config.feishu.appSecret = "";
  assert.throws(
    () => validateFeishuConfig(config),
    (error: unknown) =>
      error instanceof ConfigValidationError &&
      Array.isArray(error.fields) &&
      error.fields.includes("feishu.appSecret"),
  );
});

test("throws ConfigValidationError when dotcraft.wsUrl is missing or invalid", () => {
  const missing = validConfig();
  missing.dotcraft.wsUrl = "";
  assert.throws(() => validateFeishuConfig(missing), ConfigValidationError);

  const invalid = validConfig();
  invalid.dotcraft.wsUrl = "http://127.0.0.1:9100/ws";
  assert.throws(() => validateFeishuConfig(invalid), ConfigValidationError);
});

test("accepts minimal valid config", () => {
  const config = validConfig();
  assert.doesNotThrow(() => validateFeishuConfig(config));
});
