import { connect, type ConnectOptions, HotReplError, type Session } from "@hotrepl/sdk";
import { setTimeout as delay } from "node:timers/promises";

export async function connectUntilReady(
  options: ConnectOptions,
  timeoutMs = 30_000,
): Promise<Session> {
  const deadline = new AbortController();
  const signal = options.signal === undefined
    ? deadline.signal
    : AbortSignal.any([options.signal, deadline.signal]);
  const timer = setTimeout(() => {
    deadline.abort(
      new HotReplError({
        kind: "timeout",
        code: "waitTimeout",
        message: `HotRepl did not become ready within ${timeoutMs} ms.`,
        retryable: true,
      }),
    );
  }, timeoutMs);

  try {
    while (true) {
      signal.throwIfAborted();
      try {
        return await connect({ ...options, signal });
      } catch (error) {
        signal.throwIfAborted();
        if (
          (error instanceof HotReplError && !error.retryable)
          || error instanceof SyntaxError
          || error instanceof TypeError
        ) throw error;
      }
      try {
        await delay(250, undefined, { signal });
      } catch {
        signal.throwIfAborted();
      }
    }
  } finally {
    clearTimeout(timer);
  }
}
