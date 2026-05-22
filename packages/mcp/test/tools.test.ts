import { describe, expect, test } from "bun:test";
import { FakeRuntime } from "@hotrepl/testing";
import { SessionManager } from "../src/session-manager";
import { createHotReplTools } from "../src/tools";

const descriptor = {
  name: "world.export",
  majorVersion: 1,
  kind: "job" as const,
  mutatesState: true,
  inputSchema: { type: "object", properties: { scene: { type: "string" } } },
  outputSchema: { type: "object", properties: { ok: { type: "boolean" } } },
  artifactsSchema: { type: "object" },
};

describe("HotRepl MCP tools", () => {
  test("registers exactly the fixed v2 tools", async () => {
    const runtime = new FakeRuntime({ supportsCompletion: true });
    runtime.registerCommand(descriptor, () => ({ output: { ok: true } }));
    const manager = new SessionManager({ runtime });

    const tools = await createHotReplTools(manager);

    expect(tools.map((tool) => tool.name)).toEqual([
      "hotrepl_info",
      "hotrepl_eval",
      "hotrepl_reset",
      "hotrepl_complete",
      "hotrepl_list_commands",
      "hotrepl_describe_command",
      "hotrepl_run",
      "hotrepl_read_artifact",
      "hotrepl_journal",
    ]);
  });

  test("derives hotrepl_run annotations from command descriptors", async () => {
    const runtime = new FakeRuntime();
    runtime.registerCommand(descriptor, () => ({ output: { ok: true } }));
    const manager = new SessionManager({ runtime });

    const tools = await createHotReplTools(manager);
    const run = tools.find((tool) => tool.name === "hotrepl_run");

    expect(run?.annotations).toMatchObject({
      destructiveHint: true,
      readOnlyHint: false,
    });
  });

  test("hotrepl_run delegates to Session.run", async () => {
    const runtime = new FakeRuntime();
    runtime.registerCommand(descriptor, () => ({ output: { ok: true } }));
    const manager = new SessionManager({ runtime });
    const tools = await createHotReplTools(manager);
    const run = tools.find((tool) => tool.name === "hotrepl_run");

    const result = await run?.handler({ name: "world.export", args: { scene: "main" } });

    expect(result?.structuredContent).toEqual({ output: { ok: true }, artifacts: {} });
    expect(result?.content).toEqual([{ type: "text", text: '{"output":{"ok":true},"artifacts":{}}' }]);
  });
});
