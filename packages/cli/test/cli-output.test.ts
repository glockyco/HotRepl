import { FakeRuntime } from "@hotrepl/testing";
import { describe, expect, test } from "bun:test";
import { runCli } from "../src/index";

const commandDescriptor = {
  name: "math.double",
  majorVersion: 1,
  kind: "sync" as const,
  mutatesState: false,
  inputSchema: { type: "object", properties: { value: { type: "number" } } },
  outputSchema: { type: "object", properties: { value: { type: "number" } } },
  artifactsSchema: { type: "object" },
};

async function configuredRuntime(): Promise<FakeRuntime> {
  const runtime = new FakeRuntime({ supportsCompletion: true });
  runtime.setEvalHandler((code) => ({ value: code.length, valueType: "System.Int32" }));
  runtime.registerCompletion("Con", ["Console", "Convert"]);
  runtime.setWatch("Health", [
    { value: 10, final: false },
    { value: 11, final: true },
  ]);
  runtime.registerCommand(commandDescriptor, (args) => ({
    output: { value: Number(args.value) * 2 },
  }));
  return runtime;
}

describe("hotrepl CLI output", () => {
  test("renders text output for the public command surface", async () => {
    // Each runCli call now closes its own session (so node can drain its
    // event loop on exit). Build a fresh runtime per invocation — that
    // mirrors how the CLI is actually used (one process per command).
    const artifactsCase = async () => {
      const runtime = await configuredRuntime();
      const artifact = await runtime.putArtifact(
        "manifest",
        new TextEncoder().encode("ok"),
        { contentType: "text/plain", path: "/tmp/manifest.txt" },
      );
      return runCli(["artifacts", "read", JSON.stringify(artifact)], { runtime });
    };
    const journalCase = async () => {
      const runtime = await configuredRuntime();
      // Pre-populate the journal with one eval + one command call before
      // invoking the CLI, because each runCli runs on a fresh runtime and
      // closes its session on exit.
      await runtime.request({ type: "eval", id: "pre-eval", code: "1+1" });
      await runtime.request({
        type: "command_call",
        id: "pre-cmd",
        name: "math.double",
        args: { value: 1 },
      });
      return runCli(["journal", "--limit", "2"], { runtime });
    };

    const outputs = [
      await runCli(["info"], { runtime: await configuredRuntime() }),
      await runCli(["wait"], { runtime: await configuredRuntime() }),
      await runCli(["doctor"], { runtime: await configuredRuntime() }),
      await runCli(["eval", "1 + 1"], { runtime: await configuredRuntime() }),
      await runCli(["reset"], { runtime: await configuredRuntime() }),
      await runCli(["complete", "Con", "3"], { runtime: await configuredRuntime() }),
      await runCli(["run", "math.double", "{\"value\":4}"], {
        runtime: await configuredRuntime(),
      }),
      await runCli(["describe", "math.double"], { runtime: await configuredRuntime() }),
      await artifactsCase(),
      await journalCase(),
    ];

    expect(outputs.every((output) => output.exitCode === 0)).toBe(true);
    expect(outputs.map((output) => output.stdout.trim()).join("\n---\n")).toMatchInlineSnapshot(`
      "HotRepl v2 on FakeRuntime 0.0.0 (test)
      evaluator: FakeEvaluator, completion: yes
      ---
      ready
      ---
      ok
      ---
      5
      ---
      reset
      ---
      Console
      Convert
      ---
      {\"value\":8}
      ---
      math.double v1 sync readonly
      ---
      ok
      ---
      eval ok
      command ok"
    `);
  });

  test("renders JSON output for structured commands", async () => {
    const runtime = await configuredRuntime();

    const output = await runCli(["run", "math.double", "{\"value\":5}", "--format", "json"], {
      runtime,
    });

    expect(output.exitCode).toBe(0);
    expect(output.stdout).toMatchInlineSnapshot(`
      "{\"output\":{\"value\":10},\"artifacts\":{}}
      "
    `);
  });

  test("renders JSONL output for watch", async () => {
    const runtime = await configuredRuntime();

    const output = await runCli(["watch", "Health", "--format", "jsonl"], { runtime });

    expect(output.exitCode).toBe(0);
    expect(output.stdout).toMatchInlineSnapshot(`
      "{\"seq\":1,\"hasValue\":true,\"final\":false,\"durationMs\":0,\"value\":10}
      {\"seq\":2,\"hasValue\":true,\"final\":true,\"durationMs\":0,\"value\":11}
      "
    `);
  });

  test("runCli closes its session so the event loop can drain", async () => {
    const runtime = await configuredRuntime();
    const result = await runCli(["info"], { runtime });
    expect(result.exitCode).toBe(0);

    // FakeRuntime.close() sets isClosed=true; subsequent request() rejects.
    // If runCli neglects to close, this assertion fails — that is the bug
    // we are guarding against (CLI process hanging after success).
    await expect(runtime.request({ type: "eval", id: "post-close", code: "1" }))
      .rejects.toThrow(/closed/i);
  });

  test("runCli closes its session on dispatch failure too", async () => {
    const runtime = await configuredRuntime();
    // Bad JSON argument forces dispatchCommand to throw.
    const result = await runCli(["run", "math.double", "not-json"], { runtime });
    expect(result.exitCode).not.toBe(0);

    await expect(runtime.request({ type: "eval", id: "post-close-error", code: "1" }))
      .rejects.toThrow(/closed/i);
  });
});
