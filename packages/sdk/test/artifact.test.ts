import { describe, expect, test } from "bun:test";
import { FakeRuntime, MockSession } from "@hotrepl/testing";
import { HotReplArtifactCorrupted } from "../src";

describe("Artifact", () => {
  test("reads text, json, bytes, and open metadata after hash verification", async () => {
    const runtime = new FakeRuntime();
    const bytes = new TextEncoder().encode('{"ok":true}');
    const ref = await runtime.putArtifact("manifest", bytes, {
      contentType: "application/json",
      path: "/tmp/manifest.json",
    });
    const session = await MockSession.create(runtime);
    const artifact = session.artifact(ref);

    expect(await artifact.text()).toBe('{"ok":true}');
    expect(await artifact.json<{ ok: boolean }>()).toEqual({ ok: true });
    expect(await artifact.bytes()).toEqual(bytes);
    expect(await artifact.open()).toEqual({ path: "/tmp/manifest.json", uri: ref.uri });
  });

  test("rejects corrupted artifact bytes", async () => {
    const runtime = new FakeRuntime();
    const ref = await runtime.putArtifact("manifest", new TextEncoder().encode("good"));
    const session = await MockSession.create(runtime);
    runtime.overwriteArtifact(ref.uri, new TextEncoder().encode("bad"));

    await expect(session.artifact(ref).bytes()).rejects.toBeInstanceOf(
      HotReplArtifactCorrupted,
    );
  });
});
