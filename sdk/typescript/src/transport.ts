/**
 * Transport layer: StdioTransport and WebSocketTransport.
 */

import { createInterface } from "node:readline";
import WebSocket from "ws";

export class TransportError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "TransportError";
  }
}

export class TransportClosed extends TransportError {
  constructor(message = "Transport closed") {
    super(message);
    this.name = "TransportClosed";
  }
}

/** Abstract transport that reads/writes JSON-RPC messages. */
export interface Transport {
  readMessage(): Promise<Record<string, unknown>>;
  writeMessage(msg: Record<string, unknown>): Promise<void>;
  close(): Promise<void>;
}

/**
 * Newline-delimited JSON (JSONL) on stdin/stdout.
 * Used when DotCraft spawns the adapter as a subprocess.
 */
export class StdioTransport implements Transport {
  private closed = false;
  private readonly rl: ReturnType<typeof createInterface>;
  private lineIter: AsyncIterator<string> | null = null;

  constructor() {
    this.rl = createInterface({ input: process.stdin, crlfDelay: Infinity });
  }

  async readMessage(): Promise<Record<string, unknown>> {
    if (this.closed) throw new TransportClosed();
    if (!this.lineIter) {
      this.lineIter = this.rl[Symbol.asyncIterator]();
    }
    // eslint-disable-next-line no-constant-condition
    while (true) {
      const { value, done } = await this.lineIter.next();
      if (done) throw new TransportClosed("stdin closed");
      const text = String(value).trim();
      if (!text) continue;
      return JSON.parse(text) as Record<string, unknown>;
    }
  }

  async writeMessage(msg: Record<string, unknown>): Promise<void> {
    if (this.closed) throw new TransportClosed();
    const line = `${JSON.stringify(msg)}\n`;
    await new Promise<void>((resolve, reject) => {
      process.stdout.write(line, (err) => (err ? reject(err) : resolve()));
    });
  }

  async close(): Promise<void> {
    this.closed = true;
    this.rl.close();
  }
}

export interface WebSocketTransportOptions {
  url: string;
  token?: string | null;
}

/**
 * One JSON-RPC message per WebSocket text frame.
 */
export class WebSocketTransport implements Transport {
  private readonly baseUrl: string;
  private readonly token: string | null | undefined;
  private ws: WebSocket | null = null;
  private closed = false;

  constructor(opts: WebSocketTransportOptions) {
    this.baseUrl = opts.url;
    this.token = opts.token;
  }

  private get urlWithToken(): string {
    if (!this.token?.trim()) return this.baseUrl;
    const sep = this.baseUrl.includes("?") ? "&" : "?";
    return `${this.baseUrl}${sep}token=${encodeURIComponent(this.token.trim())}`;
  }

  async connect(): Promise<void> {
    if (this.closed) throw new TransportClosed();
    this.ws = new WebSocket(this.urlWithToken);
    await new Promise<void>((resolve, reject) => {
      const w = this.ws!;
      w.once("open", () => resolve());
      w.once("error", (e) => reject(e));
    });
  }

  async readMessage(): Promise<Record<string, unknown>> {
    if (this.closed) throw new TransportClosed();
    if (this.ws === null) await this.connect();
    const raw = await new Promise<string | Buffer>((resolve, reject) => {
      const w = this.ws!;
      const onMsg = (data: WebSocket.RawData) => {
        cleanup();
        resolve(data as string | Buffer);
      };
      const onClose = () => {
        cleanup();
        reject(new TransportClosed("WebSocket closed"));
      };
      const onErr = (e: Error) => {
        cleanup();
        reject(e);
      };
      const cleanup = () => {
        w.off("message", onMsg);
        w.off("close", onClose);
        w.off("error", onErr);
      };
      w.on("message", onMsg);
      w.once("close", onClose);
      w.once("error", onErr);
    });
    const text = typeof raw === "string" ? raw : raw.toString("utf-8");
    return JSON.parse(text) as Record<string, unknown>;
  }

  async writeMessage(msg: Record<string, unknown>): Promise<void> {
    if (this.closed) throw new TransportClosed();
    if (this.ws === null) throw new TransportError("WebSocket not connected; call connect() first");
    const text = JSON.stringify(msg);
    await new Promise<void>((resolve, reject) => {
      this.ws!.send(text, (err) => (err ? reject(err) : resolve()));
    });
  }

  async close(): Promise<void> {
    this.closed = true;
    if (this.ws) {
      this.ws.close();
      this.ws = null;
    }
  }
}
