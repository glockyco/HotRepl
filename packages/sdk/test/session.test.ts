import { FakeRuntime, MockSession } from "@hotrepl/testing";
import { describe, expect, test } from "bun:test";
import { connect, HotReplError, type RuntimeTransport, Session } from "../src";

const syncDescriptor = {
  name: "math.double",
  majorVersion: 1,
  kind: "sync" as const,
  mutatesState: false,
  inputSchema: { type: "object" },
  outputSchema: { type: "object" },
  artifactsSchema: { type: "object" },
};

const jobDescriptor = {
  name: "data.export",
  majorVersion: 1,
  kind: "job" as const,
  mutatesState: false,
  inputSchema: { type: "object" },
  outputSchema: { type: "object" },
  artifactsSchema: { type: "object" },
};

describe("Session", () => {
  test("connect validates the handshake before returning a session", async () => {
    const runtime = new FakeRuntime();

    const session = await connect({ runtime });

    expect(session.handshake.protocolVersion).toBe(2);
  });

  test("connect rejects protocol mismatches", async () => {
    const runtime = new FakeRuntime({ protocolVersion: 1 });

    await expect(connect({ runtime })).rejects.toMatchObject({
      kind: "unsupported_operation",
      code: "protocolVersionMismatch",
    });
  });

  test("run caches descriptors and returns sync command output", async () => {
    const runtime = new FakeRuntime();
    runtime.registerCommand(syncDescriptor, async (args) => ({
      output: { value: args.value * 2 },
    }));
    const session = await MockSession.create(runtime);

    const first = await session.run<{ value: number }>("math.double", { value: 2 });
    const second = await session.run<{ value: number }>("math.double", { value: 3 });

    expect(first.output.value).toBe(4);
    expect(second.output.value).toBe(6);
    expect(runtime.requestCount("command_describe", "math.double")).toBe(1);
  });

  test("run waits for job commands by polling job_status", async () => {
    const runtime = new FakeRuntime();
    runtime.registerCommand(
      jobDescriptor,
      async () => ({ output: { exported: 12 } }),
      { completeAfterPolls: 3 },
    );
    const session = await MockSession.create(runtime);

    const result = await session.run<{ exported: number }>(
      "data.export",
      {},
      { pollIntervalMs: 0 },
    );

    expect(result.output.exported).toBe(12);
    expect(runtime.requestCount("job_status")).toBe(3);
  });

  test("run wait:false returns a job handle", async () => {
    const runtime = new FakeRuntime();
    runtime.registerCommand(jobDescriptor, async () => ({ output: { done: true } }));
    const session = await MockSession.create(runtime);

    const handle = await session.run("data.export", {}, { wait: false });
    const status = await handle.status();
    const result = await handle.result<{ done: boolean }>();

    expect(status.state).toBe("running");
    expect(result.output.done).toBe(true);
  });

  test("run surfaces job start failures as HotReplError", async () => {
    const runtime = new FakeRuntime({ limits: { maxJobConcurrency: 1 } });
    runtime.registerCommand(jobDescriptor, async () => ({ output: { done: true } }));
    const session = await MockSession.create(runtime);

    await session.run("data.export", {}, { wait: false });

    await expect(session.run("data.export", {}, { pollIntervalMs: 0 })).rejects.toMatchObject({
      kind: "busy",
      code: "jobConcurrencyLimit",
    });
  });

  test("eval, reset, complete, journal, and typed errors use SDK methods", async () => {
    const runtime = new FakeRuntime({ supportsCompletion: true });
    runtime.setEvalHandler((code) => ({ value: code.length, valueType: "System.Int32" }));
    runtime.registerCompletion("Con", ["Console"]);
    const session = await MockSession.create(runtime);

    expect((await session.eval<number>("1 + 1")).value).toBe(5);
    expect(await session.complete("Con", 3)).toEqual(["Console"]);
    await session.reset();
    expect((await session.journal({ kind: "eval" })).length).toBeGreaterThan(0);

    runtime.registerCommand(syncDescriptor, async () => {
      throw new HotReplError({
        kind: "precondition_failed",
        code: "notReady",
        message: "Not ready.",
        retryable: false,
      });
    });

    await expect(session.run("math.double", {})).rejects.toMatchObject({
      kind: "precondition_failed",
      code: "notReady",
    });
  });

  test("complete fails fast when the runtime does not support completion", async () => {
    const runtime = new FakeRuntime({ supportsCompletion: false });
    const session = await MockSession.create(runtime);

    await expect(session.complete("Con", 3)).rejects.toMatchObject({
      kind: "unsupported_operation",
      code: "completionUnsupported",
    });
    expect(runtime.requestCount("complete")).toBe(0);
  });

  test("watch yields present and absent values and throws the final error", async () => {
    const runtime = new FakeRuntime();
    runtime.setWatch("Health", [
      { hasValue: false, final: false },
      { value: 10, final: false },
      {
        error: {
          kind: "cancelled",
          code: "watchCancelled",
          message: "Watch cancelled.",
          retryable: false,
        },
        final: true,
      },
    ]);
    const session = await MockSession.create(runtime);
    const ticks = [];

    try {
      for await (const tick of session.watch<number>("Health")) {
        ticks.push(tick);
      }
    } catch (error) {
      expect(error).toBeInstanceOf(HotReplError);
      expect((error as HotReplError).kind).toBe("cancelled");
    }

    expect(ticks).toEqual([
      { seq: 1, hasValue: false, final: false, durationMs: 0 },
      { seq: 2, hasValue: true, value: 10, final: false, durationMs: 0 },
    ]);
  });

  test("protocol error frames reject the matching request as HotReplError", async () => {
    const runtime = new FakeRuntime();
    const transport: RuntimeTransport = {
      handshake: async () => runtime.handshakeMessage,
      request: async (request) =>
        ({
          type: "error",
          id: request.id,
          error: {
            kind: "invalid_request",
            code: "unknownMessageType",
            message: "Unknown message type.",
            retryable: false,
          },
        }) as never,
      watch: async function*() {},
      readArtifact: async () => new Uint8Array(),
      onSessionEvicted: () => () => undefined,
    };
    const session = new Session(transport, runtime.handshakeMessage);

    await expect(session.describeCommand("missing.command")).rejects.toMatchObject({
      kind: "invalid_request",
      code: "unknownMessageType",
    });
  });
  test("session eviction notifies listeners and blocks later calls", async () => {
    const runtime = new FakeRuntime();
    const session = await MockSession.create(runtime);
    let reason = "";
    session.onSessionEvicted((event) => {
      reason = event.reason;
    });

    runtime.evict("displaced");

    expect(reason).toBe("displaced");
    await expect(session.eval("1+1")).rejects.toMatchObject({
      code: "sessionEvicted",
    });
  });
});
