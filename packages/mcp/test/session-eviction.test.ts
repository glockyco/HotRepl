import { FakeRuntime } from "@hotrepl/testing";
import { describe, expect, test } from "bun:test";
import { SessionManager } from "../src/session-manager";

describe("MCP SessionManager", () => {
  test("keeps one SDK session, reports eviction once, and reconnects lazily", async () => {
    const runtime = new FakeRuntime();
    const notifications: string[] = [];
    const manager = new SessionManager({
      onNotification: (message) => notifications.push(message),
      runtime,
    });

    const first = await manager.getSession();
    expect(await manager.getSession()).toBe(first);

    runtime.evict("displaced");
    runtime.evict("ignored duplicate");

    expect(notifications).toEqual(["HotRepl session evicted: displaced"]);
    const reconnected = await manager.getSession();
    expect(reconnected).not.toBe(first);
    expect(await manager.getSession()).toBe(reconnected);
  });
});
