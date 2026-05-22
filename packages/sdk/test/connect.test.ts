import { describe, expect, test } from "bun:test";
import { resolveHotReplUrl } from "../src";

describe("resolveHotReplUrl", () => {
  test("uses the runtime default port unless env or explicit URL overrides it", () => {
    expect(resolveHotReplUrl()).toBe("ws://127.0.0.1:18590");
    expect(resolveHotReplUrl({ env: { HOTREPL_URL: "ws://game:19000" } })).toBe("ws://game:19000");
    expect(resolveHotReplUrl({ url: "ws://explicit:19001" })).toBe("ws://explicit:19001");
  });
});
