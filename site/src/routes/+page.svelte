<script lang="ts">
  import type { PageServerData } from "./$types";
  let { data }: { data: PageServerData } = $props();
</script>

<svelte:head>
  <title>HotRepl — Runtime C# REPL for Unity games</title>
  <meta
    name="description"
    content="Runtime C# REPL and typed command bridge for Unity games. Embed via BepInEx or MelonLoader, inspect and automate a running game from your terminal, scripts, or AI agents."
  />
</svelte:head>

<!-- ── Hero ────────────────────────────────────────────────────────── -->
<section class="hero">
  <h1 class="hero-title">HotRepl</h1>
  <p class="hero-tagline">Runtime C# REPL and typed command bridge for Unity games</p>
  <p class="hero-desc">
    Embed in any Unity game via BepInEx or MelonLoader. Inspect and automate a running game from
    your terminal, scripts, or AI agents — without rebuilding.
  </p>
  <div class="hero-ctas">
    <a class="btn btn-primary" href="/protocol/">Protocol Reference →</a>
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
<section class="features">
  <div class="feature-card">
    <h3>Raw Eval</h3>
    <p>
      Write C# on the game's main thread. Inspect live objects, run one-off repair snippets,
      explore the runtime interactively.
    </p>
  </div>
  <div class="feature-card">
    <h3>Typed Commands</h3>
    <p>
      Schema-validated operations registered by the host. Stable contract for repeatable exports,
      tests, and agent workflows.
    </p>
  </div>
  <div class="feature-card">
    <h3>Agent &amp; MCP Ready</h3>
    <p>
      Connect via WebSocket SDK, CLI, or the nine-tool MCP stdio server. Plug into any coding
      agent, script, or automation pipeline.
    </p>
  </div>
</section>

<!-- ── Quickstart ──────────────────────────────────────────────────── -->
<section class="section">
  <h2 class="section-title">Quickstart</h2>
  <!-- eslint-disable-next-line svelte/no-at-html-tags -->
  {@html data.quickstartHtml}
</section>

<!-- ── Integration paths ────────────────────────────────────────────────── -->
<section class="section">
  <h2 class="section-title">Integration paths</h2>
  <div class="table-wrap">
    <table>
      <thead>
        <tr>
          <th>Path</th>
          <th>Use for</th>
          <th>Entry</th>
        </tr>
      </thead>
      <tbody>
        <tr>
          <td>Raw eval</td>
          <td>Interactive inspection, one-off repair</td>
          <td><code>session.eval()</code> / <code>hotrepl eval</code></td>
        </tr>
        <tr>
          <td>Typed commands</td>
          <td>Automation, exports, repeatable tests</td>
          <td><code>session.run()</code> / <code>command_call</code></td>
        </tr>
        <tr>
          <td>CLI</td>
          <td>Shell scripts, local workflows</td>
          <td><code>@hotrepl/cli</code></td>
        </tr>
        <tr>
          <td>MCP</td>
          <td>AI agent tool catalog</td>
          <td><code>@hotrepl/mcp</code></td>
        </tr>
        <tr>
          <td>Host embedding</td>
          <td>New loader adapters, test hosts</td>
          <td><code>IReplHost</code> + <code>ReplEngine.Tick()</code></td>
        </tr>
      </tbody>
    </table>
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
      <span>BepInEx/Mono path — reference consumer for typed commands and snapshot artifacts</span>
    </a>
    <a
      class="consumer-card"
      href="https://github.com/glockyco/ancient-kingdoms-mods"
      rel="noopener noreferrer"
    >
      <strong>Ancient Kingdoms Compendium</strong>
      <span>MelonLoader/IL2CPP path — reference consumer for data export orchestration</span>
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

  /* ── Table ── */
  .table-wrap {
    overflow-x: auto;
  }

  table {
    width: 100%;
    border-collapse: collapse;
    font-size: 0.875rem;
  }

  th {
    text-align: left;
    padding: 8px 12px;
    border-bottom: 1px solid var(--border);
    color: var(--muted);
    font-weight: 600;
    font-size: 0.8125rem;
  }

  td {
    padding: 10px 12px;
    border-bottom: 1px solid var(--border);
    color: var(--text);
    vertical-align: top;
  }

  td:first-child {
    font-weight: 600;
    color: var(--accent);
    white-space: nowrap;
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
