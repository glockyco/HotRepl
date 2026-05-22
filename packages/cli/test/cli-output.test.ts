import { describe, expect, test } from "bun:test";
import { FakeRuntime } from "@hotrepl/testing";
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
    const runtime = await configuredRuntime();
    const artifact = await runtime.putArtifact("manifest", new TextEncoder().encode("ok"), {
      contentType: "text/plain",
      path: "/tmp/manifest.txt",
    });

    const outputs = [
      await runCli(["info"], { runtime }),
      await runCli(["wait"], { runtime }),
      await runCli(["doctor"], { runtime }),
      await runCli(["eval", "1 + 1"], { runtime }),
      await runCli(["reset"], { runtime }),
      await runCli(["complete", "Con", "3"], { runtime }),
      await runCli(["run", "math.double", '{"value":4}'], { runtime }),
      await runCli(["describe", "math.double"], { runtime }),
      await runCli(["artifacts", "read", JSON.stringify(artifact)], { runtime }),
      await runCli(["journal", "--limit", "2"], { runtime }),
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

    const output = await runCli(["run", "math.double", '{"value":5}', "--format", "json"], {
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
});
