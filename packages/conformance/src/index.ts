import { describe, expect, test } from "bun:test";
import { MESSAGE_TYPES, PROTOCOL_VERSION } from "@hotrepl/protocol";
import type { CommandDescriptor, RuntimeLimits } from "@hotrepl/protocol";
import { HotReplError, HotReplSessionEvicted } from "@hotrepl/sdk";
import type { Session } from "@hotrepl/sdk";
import type { FakeRuntime } from "@hotrepl/testing";

export interface ConformanceCreateOptions {
  configure?: (runtime: FakeRuntime) => void;
  limits?: Partial<RuntimeLimits>;
  supportsCompletion?: boolean;
}

export interface ConformanceContext {
  dispose(): void | Promise<void>;
  runtime?: FakeRuntime;
  session: Session;
}

export interface ConformanceTarget {
  configurable: boolean;
  create(options?: ConformanceCreateOptions): Promise<ConformanceContext>;
  name: string;
  skip?: boolean;
}

const syncDescriptor: CommandDescriptor = {
  name: "math.double",
  majorVersion: 1,
  kind: "sync",
  mutatesState: false,
  inputSchema: { type: "object" },
  outputSchema: { type: "object" },
  artifactsSchema: { type: "object" },
};

const jobDescriptor: CommandDescriptor = {
  name: "data.export",
  majorVersion: 1,
  kind: "job",
  mutatesState: false,
  inputSchema: { type: "object" },
  outputSchema: { type: "object" },
  artifactsSchema: { type: "object" },
};

export function runConformance(target: ConformanceTarget): void {
  const register = target.skip === true ? describe.skip : describe;
  register(`protocol conformance: ${target.name}`, () => {
    test("exposes a v2 handshake with enforced limits", async () => {
      const context = await target.create();
      try {
        expect(context.session.handshake.type).toBe(MESSAGE_TYPES.handshake);
        expect(context.session.handshake.protocolVersion).toBe(PROTOCOL_VERSION);
        expect(context.session.handshake.limits.maxMessageBytes).toBeGreaterThan(0);
        expect(context.session.handshake.enforces).toContain("maxMessageBytes");
      } finally {
        await context.dispose();
      }
    });

    test("evaluates success, eval errors, and reset", async () => {
      const context = await target.create({
        configure: (runtime) => {
          runtime.setEvalHandler((code) => {
            if (code === "fail") throw new Error("boom");
            return { value: code.length, valueType: "System.Int32" };
          });
        },
      });
      try {
        expect((await context.session.eval<number>("1 + 1")).value).toBe(5);
        await expect(context.session.eval("fail")).rejects.toMatchObject({
          kind: "internal",
          code: "handlerException",
        });
        await context.session.reset();
      } finally {
        await context.dispose();
      }
    });

    const configurableTest = target.configurable ? test : test.skip;

    configurableTest("lists, describes, runs, polls, and cancels commands", async () => {
      const context = await target.create({
        configure: (runtime) => {
          runtime.registerCommand(syncDescriptor, (args) => ({
            output: { value: Number(args.value) * 2 },
          }));
          runtime.registerCommand(
            jobDescriptor,
            () => ({ output: { exported: 2 } }),
            { completeAfterPolls: 2 },
          );
        },
      });
      try {
        const list = await context.session.request({ type: "commands_list", id: "list-1" });
        expect(list.type).toBe(MESSAGE_TYPES.commandsListResult);
        if (list.type !== MESSAGE_TYPES.commandsListResult) {
          throw new Error(`Expected commands_list_result, got ${list.type}.`);
        }
        expect(list.commands.map((command) => command.name)).toContain("math.double");

        const descriptor = await context.session.describeCommand("math.double");
        expect(descriptor.kind).toBe("sync");

        const sync = await context.session.run<{ value: number }>("math.double", { value: 4 });
        expect(sync.output.value).toBe(8);

        const job = await context.session.run<{ exported: number }>(
          "data.export",
          {},
          { pollIntervalMs: 0 },
        );
        expect(job.output.exported).toBe(2);

        const handle = await context.session.run("data.export", {}, { wait: false });
        expect((await handle.status()).state).toBe("running");
        expect((await handle.cancel()).state).toBe("cancelled");
        expect((await handle.status()).state).toBe("cancelled");
      } finally {
        await context.dispose();
      }
    });

    configurableTest("queries journal, rejects limits, and reports eviction", async () => {
      const context = await target.create({
        configure: (runtime) => {
          runtime.setEvalHandler((code) => ({ value: code.length }));
          runtime.registerCommand(syncDescriptor, () => ({ output: { ok: true } }));
        },
        limits: { maxMessageBytes: 512 },
      });
      try {
        await context.session.eval("1");
        await context.session.run("math.double", {});
        expect((await context.session.journal({ limit: 2 })).length).toBe(2);

        await expect(context.session.eval("x".repeat(1024))).rejects.toBeInstanceOf(HotReplError);

        let evictions = 0;
        context.session.onSessionEvicted(() => {
          evictions += 1;
        });
        const runtime = requireRuntime(context);
        runtime.evict("displaced");
        await expect(context.session.eval("1")).rejects.toBeInstanceOf(HotReplSessionEvicted);
        expect(evictions).toBe(1);
      } finally {
        await context.dispose();
      }
    });
  });
}

function requireRuntime(context: ConformanceContext): FakeRuntime {
  if (context.runtime === undefined) {
    throw new Error("Conformance target does not expose a configurable runtime.");
  }
  return context.runtime;
}
