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

  test("dedupes concurrent getSession calls so the backend sees one connect", async () => {
    // Race scenario: refreshAnnotations and a tool handler both call
    // getSession() before either completes. Without deduping, two
    // connect()s run and the backend (BepInEx single-client policy)
    // evicts the first session.
    const runtime = new FakeRuntime();
    const manager = new SessionManager({ runtime });

    const [a, b] = await Promise.all([
      manager.getSession(),
      manager.getSession(),
    ]);

    // Both calls must yield the same Session. If connect() ran twice
    // they would be distinct Session instances.
    expect(a).toBe(b);
  });
});
