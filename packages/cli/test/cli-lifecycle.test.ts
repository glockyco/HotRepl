import { HotReplError, type RuntimeRequest, type WatchWireMessage } from "@hotrepl/sdk";
import { FakeRuntime } from "@hotrepl/testing";
import { describe, expect, test } from "bun:test";
import { resolve } from "node:path";
import { pathToFileURL } from "node:url";
import { runCli } from "../src/index";

function serveRuntime(options: { readyAfter?: number; handshake?: boolean } = {}) {
  const runtime = new FakeRuntime();
  let attempts = 0;
  const closed = Promise.withResolvers<void>();
  const server = Bun.serve({
    hostname: "127.0.0.1",
    port: 0,
    fetch(request, server) {
      attempts += 1;
      if (attempts < (options.readyAfter ?? 1)) {
        return new Response("Starting", { status: 503 });
      }
      if (server.upgrade(request)) return undefined;
      return new Response("Expected WebSocket upgrade.", { status: 426 });
    },
    websocket: {
      open(socket) {
        if (options.handshake !== false) socket.send(JSON.stringify(runtime.handshakeMessage));
      },
      message(socket, message) {
        const request = JSON.parse(String(message)) as RuntimeRequest;
        if (request.type !== "subscribe") return;
        socket.send(JSON.stringify({
          type: "subscribe_result",
          id: request.id,
          seq: 1,
          hasValue: true,
          value: 10,
          final: false,
          durationMs: 0,
        }));
      },
      close() {
        closed.resolve();
      },
    },
  });
  return {
    server,
    url: `ws://127.0.0.1:${server.port}`,
    attempts: () => attempts,
    closed: closed.promise,
  };
}

class PausedWatchRuntime extends FakeRuntime {
  readonly resume = Promise.withResolvers<void>();
  readonly paused = Promise.withResolvers<void>();

  override async *watch(
    request: Extract<RuntimeRequest, { type: "subscribe" }>,
  ): AsyncIterable<WatchWireMessage> {
    let first = true;
    for await (const event of super.watch(request)) {
      yield event;
      if (first) {
        first = false;
        this.paused.resolve();
        await this.resume.promise;
      }
    }
  }
}

describe("wait lifecycle", () => {
  test("retries connection failures by default until the host answers", async () => {
    const host = serveRuntime({ readyAfter: 3 });
    try {
      const result = await runCli(["wait"], { env: { HOTREPL_URL: host.url } });
      expect(result).toEqual({ exitCode: 0, stdout: "ready\n", stderr: "" });
      expect(host.attempts()).toBe(3);
      await host.closed;
    } finally {
      host.server.stop(true);
    }
  });

  test("bounds a stalled handshake and closes its socket at the deadline", async () => {
    const host = serveRuntime({ handshake: false });
    try {
      const result = await runCli(["wait", "--timeout-ms", "50", "--url", host.url]);
      expect(result.exitCode).toBe(6);
      expect(result.stdout).toBe("");
      expect(result.stderr).toContain("within 50 ms");
      await host.closed;
    } finally {
      host.server.stop(true);
    }
  });

  test("maps repeated connection refusal to timeout rather than server unreachable", async () => {
    const host = serveRuntime();
    host.server.stop(true);
    const result = await runCli(["--timeout-ms", "50", "wait", "--url", host.url]);
    expect(result.exitCode).toBe(6);
    expect(result.stdout).toBe("");
  });

  test("does not retry an incompatible protocol", async () => {
    const runtime = new FakeRuntime({ protocolVersion: 3 });
    const result = await runCli(["wait", "--timeout-ms", "50"], { runtime });
    expect(result.exitCode).toBe(9);
    expect(result.stderr).toContain("Expected protocol");
    await expect(runtime.request({ type: "reset", id: "closed" })).rejects.toThrow(/closed/i);
  });

  test("leaves commands other than wait as a single connection attempt", async () => {
    const host = serveRuntime({ readyAfter: 3 });
    try {
      const result = await runCli(["info", "--url", host.url]);
      expect(result.exitCode).toBe(69);
      expect(host.attempts()).toBe(1);
    } finally {
      host.server.stop(true);
    }
  });

  test.each(["0", "-1", "NaN", "Infinity", "1.5", "2147483648"])(
    "rejects invalid timeout %s before connecting",
    async (timeout) => {
      const result = await runCli(["wait", "--timeout-ms", timeout]);
      expect(result.exitCode).toBe(2);
      expect(result.stderr).toContain("--timeout-ms must be a positive integer");
    },
  );

  test("rejects a missing timeout value", async () => {
    const result = await runCli(["wait", "--timeout-ms"]);
    expect(result.exitCode).toBe(2);
    expect(result.stderr).toContain("Missing value for --timeout-ms");
  });
});

describe("watch streaming", () => {
  test.each(["text", "json", "jsonl"])(
    "writes each %s tick before the next arrives",
    async (format) => {
      const runtime = new PausedWatchRuntime();
      runtime.setWatch("Health", [{ value: 10, final: false }, { value: 11, final: true }]);
      const chunks: string[] = [];
      let finished = false;
      const running = runCli(["watch", "Health", "--format", format], {
        runtime,
        writeStdout(chunk) {
          chunks.push(chunk);
        },
      }).then((result) => {
        finished = true;
        return result;
      });
      try {
        await runtime.paused.promise;
        expect(chunks.join("")).toContain("10");
        expect(finished).toBe(false);
        expect(chunks.join("")).not.toContain("11");
      } finally {
        runtime.resume.resolve();
      }
      const result = await running;
      expect(result).toEqual({ exitCode: 0, stderr: "", stdout: "" });
      const output = chunks.join("");
      if (format === "text") expect(output).toBe("10\n11\n");
      else {
        const ticks = format === "json"
          ? JSON.parse(output)
          : output.trim().split("\n").map((line) => JSON.parse(line));
        expect(ticks.map((tick: { value: number }) => tick.value)).toEqual([10, 11]);
      }
      await expect(runtime.request({ type: "reset", id: "closed" })).rejects.toThrow(/closed/i);
    },
  );

  test("retains emitted output and the mapped exit code after a watch error", async () => {
    const runtime = new FakeRuntime();
    runtime.setWatch("Health", [
      { value: 10, final: false },
      {
        error: { kind: "cancelled", code: "stopped", message: "Stopped.", retryable: false },
        final: true,
      },
    ]);
    const result = await runCli(["watch", "Health", "--json"], { runtime });
    expect(result.exitCode).toBe(7);
    expect(result.stderr).toBe("Stopped.\n");
    expect(JSON.parse(result.stdout).map((tick: { value: number }) => tick.value)).toEqual([10]);
    await expect(runtime.request({ type: "reset", id: "closed" })).rejects.toThrow(/closed/i);
  });

  test("closes an interrupted watch and completes its JSON output", async () => {
    const host = serveRuntime();
    const controller = new AbortController();
    let output = "";
    try {
      const result = await runCli(["watch", "Health", "--json", "--url", host.url], {
        signal: controller.signal,
        writeStdout(chunk) {
          output += chunk;
          if (chunk.includes("\"value\":10")) {
            controller.abort(
              new HotReplError({
                kind: "cancelled",
                code: "interrupted",
                message: "Interrupted.",
                retryable: false,
              }),
            );
          }
        },
      });
      expect(result.exitCode).toBe(7);
      expect(JSON.parse(output).map((tick: { value: number }) => tick.value)).toEqual([10]);
      await host.closed;
    } finally {
      host.server.stop(true);
    }
  });

  test("the executable streams stdout and drains it before exiting 130 on SIGINT", async () => {
    const host = serveRuntime();
    const bin = pathToFileURL(resolve(import.meta.dir, "../src/bin.ts")).href;
    const script = `
      process.argv = ["bun", "hotrepl", "watch", "Health", "--json", "--url", ${
      JSON.stringify(host.url)
    }];
      const write = process.stdout.write.bind(process.stdout);
      process.stdout.write = (chunk) => {
        const result = write(chunk);
        if (String(chunk).includes('"value":10')) process.emit("SIGINT");
        return result;
      };
      // Install the signal trigger before loading the executable's entry point.
      await import(${JSON.stringify(bin)});
    `;
    const child = Bun.spawn([process.execPath, "--eval", script], {
      stdout: "pipe",
      stderr: "pipe",
    });
    try {
      const stdout = new Response(child.stdout).text();
      const stderr = new Response(child.stderr).text();
      expect(await child.exited).toBe(130);
      expect(await stderr).toBe("");
      expect(JSON.parse(await stdout).map((tick: { value: number }) => tick.value)).toEqual([10]);
      await host.closed;
    } finally {
      child.kill();
      host.server.stop(true);
    }
  });

  test("a second SIGINT exits immediately while interruption cleanup is pending", async () => {
    const host = serveRuntime();
    const bin = pathToFileURL(resolve(import.meta.dir, "../src/bin.ts")).href;
    const script = `
      process.argv = ["bun", "hotrepl", "watch", "Health", "--url", ${JSON.stringify(host.url)}];
      const write = process.stdout.write.bind(process.stdout);
      process.stdout.write = (chunk) => {
        const result = write(chunk);
        if (String(chunk).includes("10")) {
          process.emit("SIGINT");
          process.emit("SIGINT");
          process.exit(99);
        }
        return result;
      };
      // Trigger both signals before the entry point can finish asynchronous cleanup.
      await import(${JSON.stringify(bin)});
    `;
    const child = Bun.spawn([process.execPath, "--eval", script], {
      stdout: "pipe",
      stderr: "pipe",
    });
    try {
      expect(await child.exited).toBe(130);
      expect(await new Response(child.stderr).text()).toBe("");
    } finally {
      child.kill();
      host.server.stop(true);
    }
  });
});
