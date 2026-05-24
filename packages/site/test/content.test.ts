import { describe, expect, test } from "bun:test";

async function readSiteFile(path: string): Promise<string> {
  return await Bun.file(new URL(`../../../site/src/${path}`, import.meta.url)).text();
}

describe("Phase 4 site content", () => {
  test("top navigation exposes task-oriented docs paths", async () => {
    const layout = await readSiteFile("routes/+layout.svelte");

    expect(layout).toContain("aria-label=\"Main navigation\"");
    expect(layout).toContain("href=\"/#quickstart\"");
    expect(layout).toContain("Quickstart");
    expect(layout).toContain("Authoring Commands");
    expect(layout).toContain("docs/authoring-commands.md");
    expect(layout).toContain("href=\"/protocol/\"");
  });
});
