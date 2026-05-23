<script lang="ts">
  import MessageCard from "$lib/components/MessageCard.svelte";
  import ProtocolNav from "$lib/components/ProtocolNav.svelte";
  import type { PageServerData } from "./$types";

  let { data }: { data: PageServerData } = $props();
  let drawerOpen = $state(false);
</script>

<svelte:head>
  <title>Protocol Reference — HotRepl</title>
  <meta
    name="description"
    content="Complete reference for the HotRepl v2 WebSocket protocol — all message types, JSON examples, and JSON Schemas."
  />
</svelte:head>

<svelte:window
  onkeydown={(e) => {
    if (e.key === "Escape") drawerOpen = false;
  }}
/>

<!-- Backdrop (mobile only) -->
<div
  class="drawer-backdrop"
  class:visible={drawerOpen}
  role="presentation"
  onclick={() => (drawerOpen = false)}
></div>

<!-- Drawer panel (mobile only) -->
<div class="drawer" class:open={drawerOpen} aria-hidden={!drawerOpen}>
  <button
    class="drawer-close"
    onclick={() => (drawerOpen = false)}
    aria-label="Close navigation"
  >
    ×
  </button>
  <ProtocolNav families={data.families} />
</div>

<div class="layout">
  <!-- Sticky sidebar — desktop only -->
  <div class="sidebar-wrap">
    <ProtocolNav families={data.families} />
  </div>

  <div class="content">
    <!-- Mobile menu trigger -->
    <div class="mobile-menu-bar">
      <button
        class="menu-btn"
        onclick={() => (drawerOpen = true)}
        aria-label="Open protocol navigation"
        aria-expanded={drawerOpen}
        aria-controls="proto-drawer"
      >
        <span aria-hidden="true">☰</span>
        Navigation
      </button>
    </div>

    <div class="content-header">
      <h1>Protocol Reference</h1>
      <p class="content-desc">
        HotRepl v2 — one WebSocket connection, JSON frames, all operations.
      </p>
    </div>

    {#each data.families as family (family.id)}
      <section class="family-section" id={family.id}>
        <h2 class="family-heading">{family.name}</h2>
        <p class="family-desc">{family.description}</p>

        {#each family.messages as msg (msg.type)}
          <MessageCard
            type={msg.type}
            direction={msg.direction}
            description={msg.description}
            exampleHtml={msg.exampleHtml}
            schemaHtml={msg.schemaHtml}
          />
        {/each}
      </section>
    {/each}

    <!-- Shared types -->
    <section class="family-section" id="shared-types">
      <h2 class="family-heading">Shared Types</h2>
      <p class="family-desc">
        Reusable structures referenced by multiple messages.
      </p>

      {#each data.sharedTypes as t (t.name)}
        <article class="card" id={t.name}>
          <div class="card-header">
            <code class="type-badge">{t.name}</code>
          </div>
          <p class="description">{t.description}</p>
          <div class="block-label">Example</div>
          <!-- eslint-disable-next-line svelte/no-at-html-tags -->
          {@html t.exampleHtml}
          <details class="schema-details">
            <summary>JSON Schema</summary>
            <!-- eslint-disable-next-line svelte/no-at-html-tags -->
            {@html t.schemaHtml}
          </details>
        </article>
      {/each}
    </section>
  </div>
</div>

<style>
  /* ── Drawer backdrop ── */
  .drawer-backdrop {
    display: none;
    position: fixed;
    inset: 0;
    background: oklch(0 0 0 / 55%);
    z-index: 99;
  }

  .drawer-backdrop.visible {
    display: block;
  }

  /* ── Drawer panel ── */
  .drawer {
    position: fixed;
    top: 52px; /* height of site header */
    left: 0;
    bottom: 0;
    width: 260px;
    background: var(--bg);
    border-right: 1px solid var(--border);
    z-index: 100;
    transform: translateX(-100%);
    transition: transform 0.22s ease;
    overflow-y: auto;
    overflow-x: hidden;
  }

  .drawer.open {
    transform: translateX(0);
  }

  .drawer-close {
    position: absolute;
    top: 10px;
    right: 10px;
    width: 28px;
    height: 28px;
    display: flex;
    align-items: center;
    justify-content: center;
    background: none;
    border: 1px solid var(--border);
    border-radius: 4px;
    font-size: 1.125rem;
    line-height: 1;
    color: var(--muted);
    cursor: pointer;
  }

  .drawer-close:hover {
    color: var(--text);
    border-color: var(--accent);
  }

  /* Hide drawer on desktop — sidebar takes over */
  @media (min-width: 768px) {
    .drawer,
    .drawer-backdrop,
    .mobile-menu-bar {
      display: none !important;
    }
  }

  /* ── Layout ── */
  .layout {
    display: flex;
    flex-direction: column;
    gap: 0;
    /* Override site-main padding for full-bleed sidebar layout */
    margin: 0 -24px;
  }

  @media (min-width: 768px) {
    .layout {
      flex-direction: row;
    }
  }

  .sidebar-wrap {
    display: none;
  }

  @media (min-width: 768px) {
    .sidebar-wrap {
      display: block;
    }
  }

  /* ── Mobile menu bar ── */
  .mobile-menu-bar {
    padding: 12px 24px;
    border-bottom: 1px solid var(--border);
    margin-bottom: 24px;
  }

  .menu-btn {
    display: inline-flex;
    align-items: center;
    gap: 8px;
    background: var(--surface);
    border: 1px solid var(--border);
    border-radius: var(--radius);
    color: var(--text);
    padding: 8px 14px;
    font-size: 0.875rem;
    font-weight: 600;
    cursor: pointer;
    transition: border-color 0.15s, color 0.15s;
  }

  .menu-btn:hover {
    border-color: var(--accent);
    color: var(--accent);
  }

  /* ── Content area ── */
  .content {
    flex: 1;
    padding: 32px 32px 64px;
    min-width: 0;
  }

  /* On mobile: restore horizontal padding to content area */
  @media (max-width: 767px) {
    .content {
      padding: 0 24px 64px;
    }
  }

  .content-header {
    margin-bottom: 40px;
  }

  h1 {
    font-size: 1.75rem;
    font-weight: 900;
    letter-spacing: -0.03em;
    color: var(--text);
    margin-bottom: 8px;
  }

  .content-desc {
    font-size: 0.9375rem;
    color: var(--muted);
  }

  .family-section {
    padding: 32px 0;
    border-top: 1px solid var(--border);
    scroll-margin-top: 72px;
  }

  .family-heading {
    font-size: 1.125rem;
    font-weight: 800;
    color: var(--accent);
    letter-spacing: -0.01em;
    margin-bottom: 6px;
  }

  .family-desc {
    font-size: 0.875rem;
    color: var(--muted);
    margin-bottom: 20px;
  }

  /* Shared type cards */
  .card {
    background: var(--surface);
    border: 1px solid var(--border);
    border-radius: var(--radius);
    padding: 20px 24px;
    margin-bottom: 16px;
    scroll-margin-top: 72px;
  }

  .card-header {
    display: flex;
    align-items: center;
    gap: 10px;
    margin-bottom: 12px;
  }

  .type-badge {
    font-family: ui-monospace, "Cascadia Code", "Fira Code", monospace;
    font-size: 0.9375rem;
    font-weight: 700;
    color: var(--accent);
    background: var(--accent-dim);
    padding: 3px 10px;
    border-radius: 4px;
  }

  .description {
    font-size: 0.875rem;
    color: var(--muted);
    line-height: 1.6;
    margin-bottom: 16px;
  }

  .block-label {
    font-size: 0.75rem;
    font-weight: 700;
    letter-spacing: 0.06em;
    text-transform: uppercase;
    color: var(--muted);
    margin-bottom: 8px;
  }

  .schema-details {
    margin-top: 12px;
  }

  .schema-details summary {
    font-size: 0.8125rem;
    font-weight: 600;
    color: var(--muted);
    cursor: pointer;
    padding: 6px 0;
    user-select: none;
    list-style: none;
    display: flex;
    align-items: center;
    gap: 6px;
  }

  .schema-details summary::before {
    content: "▶";
    font-size: 0.625rem;
    transition: transform 0.15s;
  }

  .schema-details[open] summary::before {
    transform: rotate(90deg);
  }
</style>
