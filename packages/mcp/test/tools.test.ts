import { FakeRuntime } from "@hotrepl/testing";
import { describe, expect, test } from "bun:test";
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
  test("registers exactly the fixed v2 tools", () => {
    const runtime = new FakeRuntime({ supportsCompletion: true });
    runtime.registerCommand(descriptor, () => ({ output: { ok: true } }));
    const manager = new SessionManager({ runtime });

    const tools = createHotReplTools(manager);

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

  test("registers hotrepl_run with conservative MCP-spec defaults", () => {
    const runtime = new FakeRuntime();
    runtime.registerCommand(descriptor, () => ({ output: { ok: true } }));
    const manager = new SessionManager({ runtime });

    const tools = createHotReplTools(manager);
    const run = tools.find((tool) => tool.name === "hotrepl_run");

    // Conservative defaults match the MCP spec defaults:
    // destructiveHint: true, readOnlyHint: false. These are deliberately
    // independent of the backend's mutatesState — that refinement happens
    // later via refreshAnnotations (Task 5).

    expect(run?.annotations).toMatchObject({
      destructiveHint: true,
      readOnlyHint: false,
    });
  });

  test("hotrepl_run delegates to Session.run", async () => {
    const runtime = new FakeRuntime();
    runtime.registerCommand(descriptor, () => ({ output: { ok: true } }));
    const manager = new SessionManager({ runtime });
    const tools = createHotReplTools(manager);
    const run = tools.find((tool) => tool.name === "hotrepl_run");

    const result = await run?.handler({ name: "world.export", args: { scene: "main" } });

    expect(result?.structuredContent).toEqual({ output: { ok: true }, artifacts: {} });
    expect(result?.content).toEqual([{
      type: "text",
      text: "{\"output\":{\"ok\":true},\"artifacts\":{}}",
    }]);
  });
});
