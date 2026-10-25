import { PROTOCOL_VERSION } from "@hotrepl/protocol";
import type { HandshakeMessage } from "@hotrepl/protocol";
import { HotReplError } from "./errors";
import type { RuntimeTransport } from "./session";
import { Session } from "./session";
import type { ArtifactPathResolver } from "./websocket-transport";
import { WebSocketTransport } from "./websocket-transport";

export interface ConnectOptions {
  runtime?: RuntimeTransport;
  url?: string;
  env?: Record<string, string | undefined>;
  /** Cancels connection and handshake establishment, closing the transport. */
  signal?: AbortSignal;
  /** Maps an artifact reference onto a path this process can open. */
  resolveArtifactPath?: ArtifactPathResolver;
}

export function resolveHotReplUrl(options: ConnectOptions = {}): string {
  return options.url ?? options.env?.HOTREPL_URL ?? "ws://127.0.0.1:18590";
}

export async function connect(options: ConnectOptions = {}): Promise<Session> {
  options.signal?.throwIfAborted();
  const runtime = options.runtime
    ?? (await WebSocketTransport.connect(
      resolveHotReplUrl(options),
      options.resolveArtifactPath,
      options.signal,
    ));

  const signal = options.signal;
  let onAbort: (() => void) | undefined;
  try {
    const handshake = signal === undefined
      ? await runtime.handshake()
      : await new Promise<HandshakeMessage>((resolve, reject) => {
        onAbort = () => reject(signal.reason);
        signal.addEventListener("abort", onAbort, { once: true });
        if (signal.aborted) {
          onAbort();
          return;
        }
        void runtime.handshake().then(resolve, reject);
      });
    if (handshake.protocolVersion !== PROTOCOL_VERSION) {
      throw new HotReplError({
        kind: "unsupported_operation",
        code: "protocolVersionMismatch",
        message: `Expected protocol ${PROTOCOL_VERSION}, got ${handshake.protocolVersion}.`,
        retryable: false,
      });
    }
    return new Session(runtime, handshake);
  } catch (error) {
    runtime.close();
    throw error;
  } finally {
    if (onAbort !== undefined) signal?.removeEventListener("abort", onAbort);
  }
}
