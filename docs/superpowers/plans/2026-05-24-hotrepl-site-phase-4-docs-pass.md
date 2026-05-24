# HotRepl Site Phase 4 Docs Pass Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development
> (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** Update the HotRepl website so the landing page and protocol reference communicate the
Phase 4 typed-command, SDK, MCP, artifact, and real-consumer story clearly.

**Architecture:** Keep the existing two-route SvelteKit site and existing dark visual language.
Replace redundant table-based integration copy with task-based cards on the homepage, add an
artifact invariant callout, and add a compact protocol-page orientation block. No new routes,
dependencies, generated assets, or protocol/schema changes.

**Tech Stack:** SvelteKit 2, Svelte 5, TypeScript, Shiki, Tailwind CSS v4 globals, dprint, Bun.

---

## File structure

**Modified:**

- `site/src/routes/+layout.svelte` — top navigation only. Keep the current layout shell and styles;
  add task-oriented links.
- `site/src/routes/+page.svelte` — homepage copy, feature cards, quickstart framing, choose-path
  cards, artifact callout, real consumer descriptions, and local CSS for the new card/callout
  sections.
- `site/src/routes/protocol/+page.svelte` — protocol reference intro/orientation block and local CSS
  for that block.

**Not modified:**

- `site/src/routes/+page.server.ts` — existing SDK/CLI/MCP snippets already match the design. Do not
  add a C# SDK snippet unless the exact API is verified from source during implementation.
- `site/src/lib/data/protocol.ts` — protocol data already includes the artifact schema wording from
  the previous pass.
- `site/src/lib/components/Seo.svelte` and `site/src/lib/seo/site.ts` — SEO infrastructure already
  exists.

---

## Task 1: Update task-oriented site navigation

**Files:**

- Modify: `site/src/routes/+layout.svelte:8-14`

- [ ] **Step 1: Replace the header nav links**

Replace the `<nav class="nav-inner">` block with:

```svelte
<nav class="nav-inner" aria-label="Main navigation">
  <a class="wordmark" href="/">HotRepl</a>
  <a class="nav-link" href="/#quickstart">Quickstart</a>
  <a
    class="nav-link"
    href="https://github.com/glockyco/HotRepl/blob/main/docs/authoring-commands.md"
    rel="noopener noreferrer"
  >
    Author commands
  </a>
  <a class="nav-link" href="/protocol/">Protocol</a>
  <a class="nav-link" href="https://github.com/glockyco/HotRepl" rel="noopener noreferrer">
    GitHub
  </a>
</nav>
```

- [ ] **Step 2: Verify the layout compiles**

Run:

```bash
bun run --cwd site check
```

Expected: `svelte-check` exits with code 0.

- [ ] **Step 3: Commit navigation update**

Run:

```bash
git add site/src/routes/+layout.svelte
git commit -m "docs(site): add task-oriented website navigation"
```

Expected: commit succeeds; pre-commit dprint and typos hooks pass.

---

## Task 2: Reposition homepage hero and feature cards

**Files:**

- Modify: `site/src/routes/+page.svelte:6-79`

- [ ] **Step 1: Update homepage SEO description data**

In the `jsonLd` object and `<Seo>` component, use this exact description text in both places:

```ts
"Live Unity automation for runtime C# eval, typed commands, artifact references, SDKs, CLI workflows, and MCP-enabled coding agents.";
```

The top of `site/src/routes/+page.svelte` should become:

```svelte
<script lang="ts">
  import type { PageServerData } from "./$types";
  import Seo from "$lib/components/Seo.svelte";
  let { data }: { data: PageServerData } = $props();

  const description =
    "Live Unity automation for runtime C# eval, typed commands, artifact references, SDKs, CLI workflows, and MCP-enabled coding agents.";

  const jsonLd = {
    "@context": "https://schema.org",
    "@type": "SoftwareApplication",
    name: "HotRepl",
    applicationCategory: "DeveloperApplication",
    operatingSystem: "Windows, macOS, Linux",
    url: "https://hotrepl.glockyco.com",
    codeRepository: "https://github.com/glockyco/HotRepl",
    description,
    author: { "@type": "Person", name: "glockyco" },
  };
</script>

<Seo
  title="HotRepl — Live Unity automation over WebSocket"
  {description}
  path="/"
/>
<svelte:head>
  <!-- eslint-disable-next-line svelte/no-at-html-tags -->
  {@html `<script type="application/ld+json">${JSON.stringify(jsonLd)}</script>`}
</svelte:head>
```

- [ ] **Step 2: Replace hero copy and keep existing CTA structure**

Replace the current `<section class="hero">` with:

```svelte
<section class="hero">
  <h1 class="hero-title">HotRepl</h1>
  <p class="hero-tagline">Live Unity automation over WebSocket</p>
  <p class="hero-desc">
    Embed in any Unity game via BepInEx or MelonLoader. Inspect the live runtime with C# eval,
    expose stable typed commands, attach artifact references, and drive everything from SDKs,
    shell scripts, or MCP-enabled agents.
  </p>
  <div class="hero-install">
    <span class="hero-install-prompt">$</span>
    <code>bun add @hotrepl/sdk</code>
    <span class="hero-install-aside">or <code>npx -y @hotrepl/mcp</code> for the agent server</span>
  </div>
  <div class="hero-ctas">
    <a class="btn btn-primary" href="#quickstart">See it work →</a>
    <a class="btn btn-secondary" href="/protocol/">Protocol</a>
    <a
      class="btn btn-secondary"
      href="https://github.com/glockyco/HotRepl"
      rel="noopener noreferrer"
    >
      GitHub
    </a>
  </div>
</section>
```

- [ ] **Step 3: Replace feature cards with intent-based cards**

Replace the current `<section class="features">` with:

```svelte
<section class="features" aria-label="HotRepl capabilities">
  <div class="feature-card">
    <h3>Inspect the live runtime</h3>
    <p>
      Run C# on the game's main thread to inspect objects, diagnose state, and apply one-off repair
      snippets without rebuilding.
    </p>
  </div>
  <div class="feature-card">
    <h3>Ship typed commands</h3>
    <p>
      Register schema-validated host operations for repeatable exports, tests, and automation that
      survive beyond an interactive eval session.
    </p>
  </div>
  <div class="feature-card">
    <h3>Automate from SDKs and agents</h3>
    <p>
      TypeScript SDK, C# SDK, CLI, and MCP all speak the same loopback WebSocket protocol and see the
      same command catalog.
    </p>
  </div>
</section>
```

- [ ] **Step 4: Verify homepage compiles**

Run:

```bash
bun run --cwd site check
```

Expected: `svelte-check` exits with code 0.

- [ ] **Step 5: Commit hero and feature update**

Run:

```bash
git add site/src/routes/+page.svelte
git commit -m "docs(site): reposition homepage around live Unity automation"
```

Expected: commit succeeds; pre-commit hooks pass.

---

## Task 3: Replace integration table with choose-path cards and artifact callout

**Files:**

- Modify: `site/src/routes/+page.svelte:82-205`
- Modify: `site/src/routes/+page.svelte:339-508`

- [ ] **Step 1: Reframe the quickstart lead**

Inside `<section class="section" id="quickstart">`, replace the existing `<p class="section-lead">`
with:

```svelte
<p class="section-lead">
  Prerequisite: the BepInEx plugin (Mono) or MelonLoader mod (IL2CPP) is loaded and listening on
  <code>ws://127.0.0.1:18590</code>. Outcome: connect once, evaluate a live runtime value, then call
  stable typed commands through the same session.
</p>
```

- [ ] **Step 2: Replace the integration table section with choose-path cards**

Delete the entire section from `<!-- ── Integration paths` through its closing `</section>`, and
insert:

```svelte
<!-- ── Choose your path ────────────────────────────────────────────────── -->
<section class="section" id="paths">
  <h2 class="section-title">Choose your path</h2>
  <p class="section-lead">
    Start from the surface that matches the job. All paths use the same loopback runtime and typed
    command catalog.
  </p>

  <div class="path-grid">
    <a class="path-card" href="https://github.com/glockyco/HotRepl/tree/main/packages/sdk" rel="noopener noreferrer">
      <span class="path-card__label">TypeScript SDK</span>
      <strong>App and controller automation</strong>
      <code>@hotrepl/sdk</code>
    </a>
    <a class="path-card" href="https://github.com/glockyco/HotRepl/tree/main/src/HotRepl.Sdk" rel="noopener noreferrer">
      <span class="path-card__label">C# SDK</span>
      <strong>.NET build tools and tests</strong>
      <code>HotRepl.Sdk</code>
    </a>
    <a class="path-card" href="https://github.com/glockyco/HotRepl/tree/main/packages/cli" rel="noopener noreferrer">
      <span class="path-card__label">CLI</span>
      <strong>Shell scripts and local workflows</strong>
      <code>@hotrepl/cli</code>
    </a>
    <a class="path-card" href="https://github.com/glockyco/HotRepl/tree/main/packages/mcp" rel="noopener noreferrer">
      <span class="path-card__label">MCP</span>
      <strong>Coding-agent tool catalog</strong>
      <code>@hotrepl/mcp</code>
    </a>
    <a class="path-card" href="https://github.com/glockyco/HotRepl/blob/main/docs/authoring-commands.md" rel="noopener noreferrer">
      <span class="path-card__label">Author commands</span>
      <strong>Host-side typed automation</strong>
      <code>docs/authoring-commands.md</code>
    </a>
    <a class="path-card" href="/protocol/">
      <span class="path-card__label">Protocol reference</span>
      <strong>Client implementation and frame debugging</strong>
      <code>command_call</code>
    </a>
  </div>
</section>
```

- [ ] **Step 3: Add artifact invariant callout after choose-path cards**

Immediately after the choose-path section, insert:

```svelte
<!-- ── Artifacts ───────────────────────────────────────────────────────── -->
<section class="section">
  <div class="callout">
    <h2>Artifacts are references, not payloads</h2>
    <p>
      Typed commands return artifact refs with <code>uri</code>, <code>path</code>, <code>sha256</code>,
      <code>byteSize</code>, and <code>finalized</code>. Large files stay on disk; clients receive
      stable metadata they can verify, stream, or hand to build pipelines.
    </p>
  </div>
</section>
```

- [ ] **Step 4: Update real consumer descriptions**

In the “Real consumers” section, change the two `<span>` descriptions to:

```svelte
<span>BepInEx/Mono path — typed export commands with snapshot artifact references</span>
```

and:

```svelte
<span>MelonLoader/IL2CPP path — job-style export orchestration and artifact-driven data capture</span>
```

- [ ] **Step 5: Add CSS for choose-path cards and callout**

In the `<style>` block, remove the table-specific CSS from `/* ── Table ── */` through
`td:first-child`, because the table is gone. Insert this CSS before `code {`:

```css
  /* ── Path cards ── */
  .path-grid {
    display: grid;
    grid-template-columns: repeat(3, minmax(0, 1fr));
    gap: 12px;
  }

  @media (max-width: 900px) {
    .path-grid {
      grid-template-columns: repeat(2, minmax(0, 1fr));
    }
  }

  @media (max-width: 640px) {
    .path-grid {
      grid-template-columns: 1fr;
    }
  }

  .path-card {
    display: flex;
    min-width: 0;
    flex-direction: column;
    gap: 8px;
    background: var(--surface);
    border: 1px solid var(--border);
    border-radius: var(--radius);
    padding: 16px 18px;
    text-decoration: none;
    transition: border-color 0.15s, transform 0.15s;
  }

  .path-card:hover {
    border-color: var(--accent);
    transform: translateY(-1px);
  }

  .path-card__label {
    font-size: 0.6875rem;
    font-weight: 800;
    letter-spacing: 0.08em;
    text-transform: uppercase;
    color: var(--accent);
  }

  .path-card strong {
    color: var(--text);
    font-size: 0.9375rem;
    line-height: 1.35;
  }

  .path-card code {
    align-self: flex-start;
  }

  /* ── Callout ── */
  .callout {
    background: linear-gradient(135deg, var(--accent-dim), transparent 60%), var(--surface);
    border: 1px solid var(--accent-dim);
    border-radius: var(--radius);
    padding: 20px 24px;
  }

  .callout h2 {
    font-size: 1rem;
    font-weight: 800;
    color: var(--accent);
    margin-bottom: 8px;
  }

  .callout p {
    color: var(--muted);
    font-size: 0.9375rem;
    line-height: 1.7;
    margin: 0;
  }
```

- [ ] **Step 6: Verify homepage compiles and builds**

Run:

```bash
bun run --cwd site check
bun run --cwd site build
```

Expected: both commands exit with code 0. `vite build` may print existing `node:async_hooks`
browser-compatibility warnings; the expected final line is `✔ done`.

- [ ] **Step 7: Commit homepage IA update**

Run:

```bash
git add site/src/routes/+page.svelte
git commit -m "docs(site): add choose-path cards and artifact callout"
```

Expected: commit succeeds; pre-commit hooks pass.

---

## Task 4: Add protocol-page orientation block

**Files:**

- Modify: `site/src/routes/protocol/+page.svelte:81-88`
- Modify: `site/src/routes/protocol/+page.svelte:330-430`

- [ ] **Step 1: Replace protocol page intro text with orientation copy**

Replace the existing `.content-header` block with:

```svelte
<div class="content-header">
  <h1>Protocol Reference</h1>
  <p class="content-desc">
    HotRepl v2 is a stable JSON-over-WebSocket protocol: one loopback connection, JSON frames, and
    shared message shapes for eval, typed commands, jobs, artifacts, and journal queries.
  </p>
  <div class="orientation-grid" aria-label="Protocol reference orientation">
    <div class="orientation-card">
      <strong>Start higher-level first</strong>
      <span>Most consumers should use the SDKs, CLI, MCP server, or C# command authoring APIs.</span>
    </div>
    <div class="orientation-card">
      <strong>Use this page for wire details</strong>
      <span>Implement clients, debug raw frames, or verify exact request, response, and shared schemas.</span>
    </div>
    <div class="orientation-card">
      <strong>Artifacts stay by reference</strong>
      <span>Command descriptors and results expose JSON-schema-described metadata and artifact refs.</span>
    </div>
  </div>
</div>
```

- [ ] **Step 2: Add protocol orientation CSS**

In the `<style>` block, immediately after the existing `.content-desc` rule, insert:

```css
  .orientation-grid {
    display: grid;
    grid-template-columns: repeat(3, minmax(0, 1fr));
    gap: 12px;
    margin-top: 20px;
  }

  .orientation-card {
    background: var(--surface);
    border: 1px solid var(--border);
    border-radius: var(--radius);
    padding: 14px 16px;
  }

  .orientation-card strong {
    display: block;
    color: var(--accent);
    font-size: 0.8125rem;
    margin-bottom: 6px;
  }

  .orientation-card span {
    display: block;
    color: var(--muted);
    font-size: 0.8125rem;
    line-height: 1.5;
  }

  @media (max-width: 900px) {
    .orientation-grid {
      grid-template-columns: 1fr;
    }
  }
```

- [ ] **Step 3: Verify protocol page compiles**

Run:

```bash
bun run --cwd site check
bun run --cwd site build
```

Expected: both commands exit with code 0. `vite build` may print existing `node:async_hooks`
browser-compatibility warnings; the expected final line is `✔ done`.

- [ ] **Step 4: Commit protocol orientation update**

Run:

```bash
git add site/src/routes/protocol/+page.svelte
git commit -m "docs(site): orient protocol reference around consumer paths"
```

Expected: commit succeeds; pre-commit hooks pass.

---

## Task 5: Final formatting, smoke checks, and branch gate

**Files:**

- Verify: `site/src/routes/+layout.svelte`
- Verify: `site/src/routes/+page.svelte`
- Verify: `site/src/routes/protocol/+page.svelte`

- [ ] **Step 1: Run targeted dprint check**

Run:

```bash
dprint check site/src/routes/+layout.svelte site/src/routes/+page.svelte site/src/routes/protocol/+page.svelte --allow-no-files
```

Expected: command exits with code 0. If it prints no files found, that is acceptable because Svelte
files are not dprint-managed in this repository.

- [ ] **Step 2: Run site build**

Run:

```bash
bun run --cwd site build
```

Expected: `vite build` exits with code 0. Existing `node:async_hooks` browser-compatibility warnings
are acceptable; build output ends with `✔ done`.

- [ ] **Step 3: Run local route smoke check**

Start the site dev server in one terminal:

```bash
bun run --cwd site dev -- --host 127.0.0.1 --port 5174
```

In another terminal, run:

```bash
python3 - <<'PY'
from urllib.request import urlopen
for path in ("/", "/protocol/"):
    with urlopen(f"http://127.0.0.1:5174{path}", timeout=10) as res:
        print(path, res.status)
        assert res.status == 200
PY
```

Expected output:

```text
/ 200
/protocol/ 200
```

Stop the dev server after the check.

- [ ] **Step 4: Run full pre-push gate**

Run:

```bash
lefthook run pre-push --force
```

Expected: all commands pass, including bun install, bun tests, TypeScript typecheck, schema export,
dprint check, typos, actionlint, dotnet build, and dotnet tests.

- [ ] **Step 5: Confirm clean worktree**

Run:

```bash
git status --short
```

Expected: no output.

- [ ] **Step 6: Resolve unexpected verification changes**

If `git status --short` produced any output in Step 5, do not make a generic cleanup commit. Inspect
the changed files and decide whether the verification command exposed a real source drift. Expected
for this plan: no tracked files change during verification. If files changed, fix the source of the
drift and rerun Steps 2-5 before completing the task.
