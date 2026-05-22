import { PROTOCOL_VERSION } from "@hotrepl/protocol";
import { HotReplError } from "./errors";
import type { RuntimeTransport } from "./session";
import { Session } from "./session";
import { WebSocketTransport } from "./websocket-transport";

export interface ConnectOptions {
  runtime?: RuntimeTransport;
  url?: string;
  env?: Record<string, string | undefined>;
}

export function resolveHotReplUrl(options: ConnectOptions = {}): string {
  return options.url ?? options.env?.HOTREPL_URL ?? "ws://127.0.0.1:18590";
}

export async function connect(options: ConnectOptions = {}): Promise<Session> {
  const runtime = options.runtime ?? (await WebSocketTransport.connect(resolveHotReplUrl(options)));

  const handshake = await runtime.handshake();
  if (handshake.protocolVersion !== PROTOCOL_VERSION) {
    throw new HotReplError({
      kind: "unsupported_operation",
      code: "protocolVersionMismatch",
      message: `Expected protocol ${PROTOCOL_VERSION}, got ${handshake.protocolVersion}.`,
      retryable: false,
    });
  }

  return new Session(runtime, handshake);
}
