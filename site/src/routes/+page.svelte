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

<!-- ── Hero ────────────────────────────────────────────────────────── -->
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

<!-- ── Feature cards ──────────────────────────────────────────────────── -->
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

<!-- ── Quickstart ──────────────────────────────────────────────────── -->
<section class="section" id="quickstart">
  <h2 class="section-title">Quickstart</h2>
  <p class="section-lead">
    Prerequisite: the BepInEx plugin (Mono) or MelonLoader mod (IL2CPP) is loaded and listening on
    <code>ws://127.0.0.1:18590</code>. Outcome: connect once, evaluate a live runtime value, then
    call stable typed commands through the same session.
  </p>

  <div class="example">
    <div class="example-pane">
      <span class="example-tag">TypeScript SDK</span>
      <!-- eslint-disable-next-line svelte/no-at-html-tags -->
      {@html data.sdkHtml}
    </div>
    <div class="example-pane example-pane--result">
      <span class="example-tag example-tag--result">↳ Returned by the game</span>
      <!-- eslint-disable-next-line svelte/no-at-html-tags -->
      {@html data.sdkResultHtml}
    </div>
  </div>

  <p class="example-caption">
    Same operations, same wire protocol, from your shell:
  </p>

  <div class="example example--single">
    <div class="example-pane">
      <span class="example-tag">CLI</span>
      <!-- eslint-disable-next-line svelte/no-at-html-tags -->
      {@html data.cliHtml}
    </div>
  </div>

  <p class="example-caption">
    Or wire the same runtime into your MCP-enabled coding agent:
  </p>

  <div class="example">
    <div class="example-pane">
      <span class="example-tag">MCP server config</span>
      <!-- eslint-disable-next-line svelte/no-at-html-tags -->
      {@html data.mcpConfigHtml}
    </div>
    <div class="example-pane example-pane--result">
      <span class="example-tag example-tag--result">↳ Tools the agent sees</span>
      <!-- eslint-disable-next-line svelte/no-at-html-tags -->
      {@html data.mcpToolsHtml}
    </div>
  </div>
</section>

<!-- ── Choose your path ────────────────────────────────────────────────── -->
<section class="section" id="paths">
  <h2 class="section-title">Choose your path</h2>
  <p class="section-lead">
    Start from the surface that matches the job. All paths use the same loopback runtime and typed
    command catalog.
  </p>

  <div class="path-grid">
    <a
      class="path-card"
      href="https://github.com/glockyco/HotRepl/tree/main/packages/sdk"
      rel="noopener noreferrer"
    >
      <span class="path-card__label">TypeScript SDK</span>
      <strong>App and controller automation</strong>
      <code>@hotrepl/sdk</code>
    </a>
    <a
      class="path-card"
      href="https://github.com/glockyco/HotRepl/tree/main/src/HotRepl.Sdk"
      rel="noopener noreferrer"
    >
      <span class="path-card__label">C# SDK</span>
      <strong>.NET build tools and tests</strong>
      <code>HotRepl.Sdk</code>
    </a>
    <a
      class="path-card"
      href="https://github.com/glockyco/HotRepl/tree/main/packages/cli"
      rel="noopener noreferrer"
    >
      <span class="path-card__label">CLI</span>
      <strong>Shell scripts and local workflows</strong>
      <code>@hotrepl/cli</code>
    </a>
    <a
      class="path-card"
      href="https://github.com/glockyco/HotRepl/tree/main/packages/mcp"
      rel="noopener noreferrer"
    >
      <span class="path-card__label">MCP</span>
      <strong>Coding-agent tool catalog</strong>
      <code>@hotrepl/mcp</code>
    </a>
    <a
      class="path-card"
      href="https://github.com/glockyco/HotRepl/blob/main/docs/authoring-commands.md"
      rel="noopener noreferrer"
    >
      <span class="path-card__label">Author typed commands</span>
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

<!-- ── Artifacts ───────────────────────────────────────────────────────── -->
<section class="section">
  <div class="callout">
    <h2>Artifacts are references, not payloads</h2>
    <p>
      Typed commands return artifact refs with <code>uri</code>, <code>path</code>,
      <code>sha256</code>, <code>byteSize</code>, and <code>finalized</code>. Large files stay on
      disk; clients receive stable metadata they can verify, stream, or hand to build pipelines.
    </p>
  </div>
</section>

<!-- ── Real consumers ─────────────────────────────────────────────────── -->
<section class="section">
  <h2 class="section-title">Real consumers</h2>
  <div class="consumers">
    <a
      class="consumer-card"
      href="https://github.com/glockyco/ardenfall-compendium"
      rel="noopener noreferrer"
    >
      <strong>Ardenfall Compendium</strong>
      <span>BepInEx/Mono path — typed export commands with snapshot artifact references</span>
    </a>
    <a
      class="consumer-card"
      href="https://github.com/glockyco/ancient-kingdoms-mods"
      rel="noopener noreferrer"
    >
      <strong>Ancient Kingdoms Compendium</strong>
      <span>MelonLoader/IL2CPP path — job-style export orchestration and artifact-driven data capture</span>
    </a>
  </div>
</section>

<style>
  /* ── Hero ── */
  .hero {
    padding: 72px 0 56px;
    max-width: 640px;
  }

  .hero-title {
    font-size: clamp(2.5rem, 6vw, 4rem);
    font-weight: 900;
    letter-spacing: -0.04em;
    color: var(--accent);
    line-height: 1;
    margin-bottom: 16px;
  }

  .hero-tagline {
    font-size: 1.125rem;
    font-weight: 600;
    color: var(--text);
    margin-bottom: 12px;
    line-height: 1.4;
  }

  .hero-desc {
    color: var(--muted);
    font-size: 1rem;
    line-height: 1.7;
    margin-bottom: 28px;
  }

  .hero-install {
    display: flex;
    align-items: center;
    flex-wrap: wrap;
    gap: 12px;
    padding: 10px 16px;
    border: 1px solid var(--border);
    border-radius: var(--radius);
    background: var(--surface);
    font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
    font-size: 0.875rem;
    margin-bottom: 24px;
  }

  .hero-install code {
    color: var(--text);
    font-weight: 600;
  }

  .hero-install-prompt {
    color: var(--muted);
    user-select: none;
  }

  .hero-install-aside {
    color: var(--muted);
    font-size: 0.8125rem;
    font-family: var(--font-sans, system-ui, sans-serif);
  }

  .hero-ctas {
    display: flex;
    gap: 12px;
    flex-wrap: wrap;
  }

  .btn {
    display: inline-flex;
    align-items: center;
    padding: 10px 22px;
    border-radius: var(--radius);
    font-size: 0.9375rem;
    font-weight: 700;
    text-decoration: none;
    transition: all 0.15s;
  }

  .btn-primary {
    background: var(--accent);
    color: oklch(0.1 0 0);
  }

  .btn-primary:hover {
    filter: brightness(1.1);
  }

  .btn-secondary {
    background: var(--surface);
    color: var(--text);
    border: 1px solid var(--border);
  }

  .btn-secondary:hover {
    border-color: var(--accent);
    color: var(--accent);
  }

  /* ── Features ── */
  .features {
    display: grid;
    grid-template-columns: repeat(3, 1fr);
    gap: 16px;
    padding: 0 0 48px;
  }

  @media (max-width: 640px) {
    .features {
      grid-template-columns: 1fr;
    }
  }

  .feature-card {
    background: var(--surface);
    border: 1px solid var(--border);
    border-radius: var(--radius);
    padding: 20px 24px;
  }

  .feature-card h3 {
    font-size: 0.9375rem;
    font-weight: 700;
    color: var(--accent);
    margin-bottom: 8px;
  }

  .feature-card p {
    font-size: 0.875rem;
    color: var(--muted);
    line-height: 1.6;
  }

  /* ── Sections ── */
  .section {
    padding: 40px 0;
    border-top: 1px solid var(--border);
  }

  .section-title {
    font-size: 1rem;
    font-weight: 700;
    letter-spacing: 0.06em;
    text-transform: uppercase;
    color: var(--accent);
    margin-bottom: 20px;
  }

  .section-lead {
    color: var(--muted);
    font-size: 0.9375rem;
    line-height: 1.7;
    max-width: 760px;
    margin-bottom: 24px;
  }

  .example-caption {
    color: var(--muted);
    font-size: 0.8125rem;
    font-weight: 600;
    letter-spacing: 0.04em;
    text-transform: uppercase;
    margin: 28px 0 12px;
  }

  /* ── Paired code / result example ── */
  .example {
    display: grid;
    grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
    gap: 12px;
    align-items: stretch;
  }

  .example--single {
    grid-template-columns: minmax(0, 1fr);
  }

  @media (max-width: 900px) {
    .example {
      grid-template-columns: 1fr;
    }
  }

  .example-pane {
    position: relative;
    min-width: 0;
  }

  .example-pane :global(.shiki) {
    height: 100%;
    margin: 0;
    padding-top: 32px;
    border: 1px solid var(--border);
  }

  .example-pane--result :global(.shiki) {
    background-color: oklch(0.135 0.012 240) !important;
    border-color: var(--accent-dim);
  }

  .example-tag {
    position: absolute;
    top: 8px;
    right: 12px;
    z-index: 1;
    font-family: ui-monospace, "Cascadia Code", "Fira Code", monospace;
    font-size: 0.6875rem;
    font-weight: 700;
    letter-spacing: 0.06em;
    text-transform: uppercase;
    color: var(--muted);
    background: var(--code-bg);
    padding: 2px 8px;
    border-radius: 4px;
    pointer-events: none;
  }

  .example-tag--result {
    color: var(--accent);
  }

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
    transition: border-color 0.15s;
  }

  .path-card:hover {
    border-color: var(--accent);
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

  code {
    font-family: ui-monospace, "Cascadia Code", "Fira Code", monospace;
    font-size: 0.8125rem;
    background: var(--code-bg);
    padding: 2px 6px;
    border-radius: 4px;
    color: var(--text);
  }

  /* ── Consumers ── */
  .consumers {
    display: grid;
    grid-template-columns: repeat(2, 1fr);
    gap: 12px;
  }

  @media (max-width: 640px) {
    .consumers {
      grid-template-columns: 1fr;
    }
  }

  .consumer-card {
    display: block;
    background: var(--surface);
    border: 1px solid var(--border);
    border-radius: var(--radius);
    padding: 16px 20px;
    text-decoration: none;
    transition: border-color 0.15s;
  }

  .consumer-card:hover {
    border-color: var(--accent);
  }

  .consumer-card strong {
    display: block;
    font-size: 0.9375rem;
    color: var(--accent);
    margin-bottom: 4px;
  }

  .consumer-card span {
    font-size: 0.8125rem;
    color: var(--muted);
    line-height: 1.5;
  }
</style>
