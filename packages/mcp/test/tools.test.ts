import type { RuntimeTransport } from "@hotrepl/sdk";
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
    // later via refreshAnnotations once the backend is reachable.

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
  test("tool invocation surfaces backend-unreachable as isError envelope", async () => {
    // A runtime whose handshake() rejects — simulates a backend that is down.
    // connect() calls handshake() first, so this causes manager.getSession() to reject.
    const failingRuntime: RuntimeTransport = {
      handshake: () => Promise.reject(new Error("HotRepl WebSocket connection failed.")),
      request: () => Promise.reject(new Error("transport closed")),
      watch: async function*() {
        throw new Error("transport closed");
      },
      readArtifact: () => Promise.reject(new Error("transport closed")),
      onSessionEvicted: () => () => {},
      close: () => {},
    };
    const manager = new SessionManager({ runtime: failingRuntime });

    const tools = createHotReplTools(manager);
    const eval_ = tools.find((tool) => tool.name === "hotrepl_eval");
    expect(eval_).toBeDefined();

    const callResult = await eval_!.handler({ code: "1 + 1" });
    expect(callResult.isError).toBe(true);
    expect(callResult.content).toEqual([{
      type: "text",
      text: expect.stringContaining("HotRepl is not reachable"),
    }]);
  });
});
