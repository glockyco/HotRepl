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
    expect(page).toContain("Clients and frame debugging");
    expect(page).toContain("Artifacts are references, not payloads");
    expect(page).toContain("All paths use the same runtime and command catalog.");
    expect(page).toContain(
      "BepInEx/Mono path — typed export commands with snapshot artifact references",
    );
    expect(page).toContain(
      "MelonLoader/IL2CPP path — job-style export orchestration and artifact-driven data capture",
    );
  });

  test("protocol reference orients without card chrome", async () => {
    const protocol = await readSiteFile("routes/protocol/+page.svelte");

    expect(protocol).toContain("HotRepl is a JSON-over-WebSocket protocol");
    expect(protocol).toContain(
      "Most consumers should start with SDKs, CLI, MCP, or typed command authoring",
    );
    expect(protocol).toContain(
      "Use this page when implementing a client, debugging frames, or verifying exact schema shapes",
    );
    expect(protocol).toContain("Artifact refs keep large outputs out of JSON frames");
    expect(protocol).not.toContain("orientation-card");
    expect(protocol).not.toContain("Start higher-level first");
  });
});
