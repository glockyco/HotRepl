import { connect, resolveHotReplUrl } from "@hotrepl/sdk";
import { FakeRuntime } from "@hotrepl/testing";
import { describe, expect, test } from "bun:test";

describe("resolveHotReplUrl", () => {
  test("uses the runtime default port unless env or explicit URL overrides it", () => {
    expect(resolveHotReplUrl()).toBe("ws://127.0.0.1:18590");
    expect(resolveHotReplUrl({ env: { HOTREPL_URL: "ws://game:19000" } })).toBe("ws://game:19000");
    expect(resolveHotReplUrl({ url: "ws://explicit:19001" })).toBe("ws://explicit:19001");
  });
});

describe("connect cancellation", () => {
  test("rejects an already aborted signal before opening a transport", async () => {
    const controller = new AbortController();
    const reason = new Error("Stopped before connecting.");
    controller.abort(reason);
    await expect(connect({ signal: controller.signal })).rejects.toBe(reason);
  });

  test("closes an injected runtime when its handshake is interrupted", async () => {
    class StalledRuntime extends FakeRuntime {
      override handshake(): Promise<never> {
        return new Promise(() => {});
      }
    }
    const runtime = new StalledRuntime();
    const controller = new AbortController();
    const reason = new Error("Stopped during handshake.");
    const connecting = connect({ runtime, signal: controller.signal });
    controller.abort(reason);
    await expect(connecting).rejects.toBe(reason);
    await expect(runtime.request({ type: "reset", id: "closed" })).rejects.toThrow(/closed/i);
  });

  test("does not close an established session when the connection signal aborts", async () => {
    const runtime = new FakeRuntime();
    const controller = new AbortController();
    const session = await connect({ runtime, signal: controller.signal });
    try {
      controller.abort();
      await expect(session.reset()).resolves.toBeUndefined();
    } finally {
      session.close();
    }
  });
});
