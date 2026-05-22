import { PROTOCOL_VERSION } from "@hotrepl/protocol";
import type { RuntimeTransport } from "./session";
import { HotReplError } from "./errors";
import { Session } from "./session";

export interface ConnectOptions {
  runtime?: RuntimeTransport;
  url?: string;
  env?: Record<string, string | undefined>;
}

export function resolveHotReplUrl(options: ConnectOptions = {}): string {
  return options.url ?? options.env?.HOTREPL_URL ?? "ws://127.0.0.1:31337";
}

export async function connect(options: ConnectOptions = {}): Promise<Session> {
  const runtime = options.runtime;
  if (runtime === undefined) {
    throw new HotReplError({
      kind: "unsupported_operation",
      code: "webSocketTransportUnavailable",
      message: `No in-process runtime supplied for ${resolveHotReplUrl(options)}.`,
      retryable: false,
    });
  }

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
