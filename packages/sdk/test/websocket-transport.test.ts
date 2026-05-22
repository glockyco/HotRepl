import { FakeRuntime } from "@hotrepl/testing";
import { describe, expect, test } from "bun:test";
import type { RuntimeRequest } from "../src";
import { connect, HotReplSessionEvicted } from "../src";

function serveRuntime(runtime: FakeRuntime): { close: () => void; url: string } {
  const server = Bun.serve<{ closeEviction: () => void }>({
    port: 0,
    hostname: "127.0.0.1",
    fetch(request, server) {
      if (server.upgrade(request, { data: { closeEviction: () => {} } })) return undefined;
      return new Response("Expected WebSocket upgrade.", { status: 426 });
    },
    websocket: {
      open(socket) {
        const closeEviction = runtime.onSessionEvicted((event) => {
          socket.send(JSON.stringify(event));
        });
        socket.data = { closeEviction };
        socket.send(JSON.stringify(runtime.handshakeMessage));
      },
      close(socket) {
        socket.data.closeEviction();
      },
      async message(socket, message) {
        const request = JSON.parse(String(message)) as RuntimeRequest;
        if (request.type === "subscribe") {
          for await (const event of runtime.watch(request)) {
            socket.send(JSON.stringify(event));
          }
          return;
        }

        const response = await runtime.request(request);
        socket.send(JSON.stringify(response));
      },
    },
  });

  return {
    close: () => server.stop(true),
    url: `ws://127.0.0.1:${server.port}`,
  };
}

describe("WebSocket transport", () => {
  test("connects, receives the handshake, and correlates requests", async () => {
    const runtime = new FakeRuntime({ supportsCompletion: true });
    runtime.setEvalHandler((code) => ({ value: code.length, valueType: "System.Int32" }));
    const server = serveRuntime(runtime);
    try {
      const session = await connect({ url: server.url });

      expect(session.handshake.host.name).toBe("FakeRuntime");
      expect((await session.eval<number>("1 + 1")).value).toBe(5);
    } finally {
      server.close();
    }
  });

  test("streams subscription frames and reports session eviction once", async () => {
    const runtime = new FakeRuntime();
    runtime.setWatch("Health", [
      { value: 10, final: false },
      { value: 11, final: true },
    ]);
    const server = serveRuntime(runtime);
    try {
      const session = await connect({ url: server.url });
      const values: number[] = [];
      for await (const tick of session.watch<number>("Health")) {
        if (tick.hasValue) values.push(tick.value ?? 0);
      }
      expect(values).toEqual([10, 11]);

      let evictionReason = "";
      session.onSessionEvicted((event) => {
        evictionReason = event.reason;
      });
      runtime.evict("displaced");
      await expect(session.eval("1 + 1")).rejects.toBeInstanceOf(HotReplSessionEvicted);
      expect(evictionReason).toBe("displaced");
    } finally {
      server.close();
    }
  });
});
