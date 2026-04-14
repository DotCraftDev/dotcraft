import assert from "node:assert/strict";
import { setTimeout as sleep } from "node:timers/promises";
import test from "node:test";

import WebSocket, { WebSocketServer } from "ws";

import { TransportClosed, WebSocketTransport } from "./transport.js";

async function withServer(
  onConnection: (socket: WebSocket) => void | Promise<void>,
  run: (url: string) => Promise<void>,
): Promise<void> {
  const server = new WebSocketServer({ port: 0 });
  server.on("connection", (socket) => {
    void onConnection(socket);
  });
  await new Promise<void>((resolve) => server.once("listening", () => resolve()));

  const address = server.address();
  if (!address || typeof address === "string") {
    throw new Error("WebSocket server did not expose a TCP port");
  }

  try {
    await run(`ws://127.0.0.1:${address.port}`);
  } finally {
    await new Promise<void>((resolve, reject) => {
      server.close((error) => {
        if (error) reject(error);
        else resolve();
      });
    });
  }
}

async function withTimeout<T>(promise: Promise<T>, timeoutMs = 400): Promise<T> {
  return await Promise.race([
    promise,
    (async () => {
      await sleep(timeoutMs);
      throw new Error(`Timed out after ${timeoutMs}ms`);
    })(),
  ]);
}

test("WebSocketTransport keeps ordered frames across burst and consumer gap", async () => {
  await withServer(
    async (socket) => {
      socket.send(JSON.stringify({ seq: 1 }));
      socket.send(JSON.stringify({ seq: 2 }));
      await sleep(10);
      socket.send(JSON.stringify({ seq: 3 }));
    },
    async (url) => {
      const transport = new WebSocketTransport({ url });
      await transport.connect();

      const first = await withTimeout(transport.readMessage());
      await sleep(25);
      const second = await withTimeout(transport.readMessage());
      const third = await withTimeout(transport.readMessage());

      assert.deepEqual(first, { seq: 1 });
      assert.deepEqual(second, { seq: 2 });
      assert.deepEqual(third, { seq: 3 });

      await transport.close();
    },
  );
});

test("WebSocketTransport rejects pending read on close", async () => {
  await withServer(
    async (socket) => {
      await sleep(20);
      socket.close();
    },
    async (url) => {
      const transport = new WebSocketTransport({ url });
      await transport.connect();

      await assert.rejects(withTimeout(transport.readMessage()), (error: unknown) => {
        return error instanceof TransportClosed;
      });

      await transport.close();
    },
  );
});

test("WebSocketTransport decodes binary UTF-8 frames", async () => {
  await withServer(
    async (socket) => {
      socket.send(Buffer.from(JSON.stringify({ mode: "binary" }), "utf-8"));
    },
    async (url) => {
      const transport = new WebSocketTransport({ url });
      await transport.connect();
      const message = await withTimeout(transport.readMessage());
      assert.deepEqual(message, { mode: "binary" });
      await transport.close();
    },
  );
});
