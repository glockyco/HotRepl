import type { RuntimeRequest } from "@hotrepl/sdk";
import { connect, HotReplError, HotReplSessionEvicted, WebSocketTransport } from "@hotrepl/sdk";
import { FakeRuntime } from "@hotrepl/testing";
import { describe, expect, test } from "bun:test";

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
        const parsed = JSON.parse(String(message)) as
          | RuntimeRequest
          | { type: "cancel"; targetId: string };
        if (parsed.type === "cancel") {
          runtime.cancel(parsed.targetId);
          return;
        }
        const request = parsed;
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

  test("routes protocol error frames to subscription iterators", async () => {
    const runtime = new FakeRuntime();
    const server = Bun.serve<{ closeEviction: () => void }>({
      port: 0,
      hostname: "127.0.0.1",
      fetch(request, server) {
        if (server.upgrade(request, { data: { closeEviction: () => {} } })) return undefined;
        return new Response("Expected WebSocket upgrade.", { status: 426 });
      },
      websocket: {
        open(socket) {
          socket.send(JSON.stringify(runtime.handshakeMessage));
        },
        message(socket, message) {
          const request = JSON.parse(String(message)) as RuntimeRequest;
          socket.send(JSON.stringify({
            type: "error",
            id: request.id,
            error: {
              kind: "invalid_request",
              code: "badSubscription",
              message: "Subscription rejected.",
              retryable: false,
            },
          }));
        },
      },
    });

    try {
      const session = await connect({ url: `ws://127.0.0.1:${server.port}` });
      const iterator = session.watch("Health")[Symbol.asyncIterator]();

      try {
        await iterator.next();
        throw new Error("Expected subscription to fail.");
      } catch (error) {
        expect(error).toBeInstanceOf(HotReplError);
        expect(error).toMatchObject({
          kind: "invalid_request",
          code: "badSubscription",
        });
      }
    } finally {
      server.stop(true);
    }
  });

  test("rejects request protocol error frames at the transport boundary", async () => {
    const runtime = new FakeRuntime();
    const server = Bun.serve({
      port: 0,
      hostname: "127.0.0.1",
      fetch(request, server) {
        if (server.upgrade(request)) return undefined;
        return new Response("Expected WebSocket upgrade.", { status: 426 });
      },
      websocket: {
        open(socket) {
          socket.send(JSON.stringify(runtime.handshakeMessage));
        },
        message(socket, message) {
          const request = JSON.parse(String(message)) as RuntimeRequest;
          socket.send(JSON.stringify({
            type: "error",
            id: request.id,
            error: {
              kind: "invalid_request",
              code: "badRequest",
              message: "Request rejected.",
              retryable: false,
            },
          }));
        },
      },
    });

    try {
      const transport = await WebSocketTransport.connect(`ws://127.0.0.1:${server.port}`);

      await expect(transport.request({ type: "eval", id: "eval-1", code: "1 + 1" }))
        .rejects.toMatchObject({
          kind: "invalid_request",
          code: "badRequest",
        });
    } finally {
      server.stop(true);
    }
  });
  test("reads a file-backed artifact from its uri when the ref carries no path", async () => {
    const runtime = new FakeRuntime();
    const server = serveRuntime(runtime);
    const path = `${import.meta.dir}/artifact-read.tmp`;
    try {
      await Bun.write(path, "payload");
      const transport = await WebSocketTransport.connect(server.url);

      const bytes = await transport.readArtifact({
        uri: Bun.pathToFileURL(path).href,
        sha256: "",
        byteSize: 7,
        contentType: "text/plain",
        finalized: true,
      });

      expect(new TextDecoder().decode(bytes)).toBe("payload");
    } finally {
      await Bun.file(path).delete();
      server.close();
    }
  });

  test("rejects an artifact whose scheme cannot be fetched", async () => {
    const runtime = new FakeRuntime();
    const server = serveRuntime(runtime);
    try {
      const transport = await WebSocketTransport.connect(server.url);

      await expect(transport.readArtifact({
        uri: "hotrepl-artifact://memory/screenshot",
        sha256: "",
        byteSize: 1,
        contentType: "image/png",
        finalized: true,
      })).rejects.toMatchObject({
        kind: "artifact_missing",
        code: "artifactNotReadable",
      });
    } finally {
      server.close();
    }
  });

  test("connect to an unreachable port rejects without uncaughtException", async () => {
    const uncaught: unknown[] = [];
    const onUncaught = (err: unknown) => uncaught.push(err);
    process.on("uncaughtException", onUncaught);
    process.on("unhandledRejection", onUncaught);

    try {
      await connect({ url: "ws://127.0.0.1:1" });
      expect.unreachable();
    } catch (error) {
      expect(error).toBeInstanceOf(Error);
      expect((error as Error).message).toContain("WebSocket connection failed");
    }

    // Let any deferred uncaught event surface before asserting.
    await new Promise((resolve) => setImmediate(resolve));

    process.off("uncaughtException", onUncaught);
    process.off("unhandledRejection", onUncaught);
    expect(uncaught).toEqual([]);
  });
  test("a resolver maps a foreign path onto one this process can open", async () => {
    const runtime = new FakeRuntime();
    const server = serveRuntime(runtime);
    const hostPath = `${import.meta.dir}/artifact-resolved.tmp`;
    try {
      await Bun.write(hostPath, "payload");
      const session = await connect({
        url: server.url,
        resolveArtifactPath: (ref) =>
          ref.path?.startsWith("Z:\\") === true
            ? ref.path.slice(2).replaceAll("\\", "/")
            : undefined,
      });

      const bytes = await session.artifact({
        uri: "file:///Z:/foreign/artifact",
        path: `Z:${hostPath.replaceAll("/", "\\")}`,
        sha256: "239f59ed55e737c77147cf55ad0c1b030b6d7ee748a7426952f9b852d5a935e5",
        byteSize: 7,
        contentType: "text/plain",
        finalized: true,
      }).bytes();

      expect(new TextDecoder().decode(bytes)).toBe("payload");
    } finally {
      await Bun.file(hostPath).delete();
      server.close();
    }
  });
});
