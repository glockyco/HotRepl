import { describe, expect, test } from "bun:test";

async function readSiteFile(path: string): Promise<string> {
  return await Bun.file(new URL(`../../../site/src/${path}`, import.meta.url)).text();
}

describe("Phase 4 site content", () => {
  test("top navigation keeps only high-priority site paths", async () => {
    const layout = await readSiteFile("routes/+layout.svelte");

    expect(layout).toContain("aria-label=\"Main navigation\"");
    expect(layout).toContain("href=\"/#quickstart\"");
    expect(layout).toContain("Quickstart");
    expect(layout).toContain("href=\"/protocol/\"");
    expect(layout).toContain("GitHub");
    expect(layout).not.toContain("Authoring Commands");
    expect(layout).not.toContain("docs/authoring-commands.md");
  });

  test("homepage presents Phase 4 hero and capability cards", async () => {
    const page = await readSiteFile("routes/+page.svelte");

    expect(page).toContain("Live Unity automation over WebSocket");
    expect(page).toContain("Inspect the live runtime");
    expect(page).toContain("Ship typed commands");
    expect(page).toContain("Automate from SDKs and agents");
    expect(page).toContain(
      "Live Unity automation for runtime C# eval, typed commands, artifact references, SDKs, CLI workflows, and MCP-enabled coding agents.",
    );
  });

  test("homepage exposes integration paths and artifact invariant", async () => {
    const page = await readSiteFile("routes/+page.svelte");

    expect(page).toContain("Choose your path");
    expect(page).toContain("TypeScript SDK");
    expect(page).toContain("C# SDK");
    expect(page).toContain("@hotrepl/cli");
    expect(page).toContain("@hotrepl/mcp");
    expect(page).toContain("Author typed commands");
    expect(page).toContain("Protocol reference");
    expect(page).toContain("Artifacts are references, not payloads");
    expect(page).toContain(
      "BepInEx/Mono path — typed export commands with snapshot artifact references",
    );
    expect(page).toContain(
      "MelonLoader/IL2CPP path — job-style export orchestration and artifact-driven data capture",
    );
  });
});
