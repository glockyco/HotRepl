<script lang="ts">
  import MessageCard from "$lib/components/MessageCard.svelte";
  import ProtocolNav from "$lib/components/ProtocolNav.svelte";
  import Seo from "$lib/components/Seo.svelte";
  import type { PageServerData } from "./$types";

  let { data }: { data: PageServerData } = $props();
  let sheetOpen = $state(false);

  // Lock body scroll while the sheet is open
  $effect(() => {
    document.body.style.overflow = sheetOpen ? "hidden" : "";
    return () => {
      document.body.style.overflow = "";
    };
  });
</script>

<Seo
  title="Protocol Reference — HotRepl"
  description="Complete reference for the HotRepl v2 WebSocket protocol — all message types, JSON examples, and JSON Schemas."
  path="/protocol/"
/>

<svelte:window
  onkeydown={(e) => {
    if (e.key === "Escape") sheetOpen = false;
  }}
/>

<!-- Backdrop -->
<div
  class="sheet-backdrop"
  class:visible={sheetOpen}
  role="presentation"
  onclick={() => (sheetOpen = false)}
></div>

<!-- Bottom sheet (mobile only) -->
<div
  id="proto-sheet"
  class="bottom-sheet"
  class:open={sheetOpen}
  role="dialog"
  aria-modal="true"
  aria-label="Protocol reference navigation"
  aria-hidden={!sheetOpen}
>
  <div class="sheet-header">
    <div class="sheet-drag-handle" role="presentation"></div>
    <button
      class="sheet-close"
      onclick={() => (sheetOpen = false)}
      aria-label="Close navigation"
    >×</button>
  </div>
  <div class="sheet-content">
    <ProtocolNav families={data.families} />
  </div>
</div>

<!-- FAB: persists while scrolling, mobile only -->
<button
  class="nav-fab"
  class:hidden={sheetOpen}
  onclick={() => (sheetOpen = true)}
  aria-label="Open protocol navigation"
  aria-expanded={sheetOpen}
  aria-controls="proto-sheet"
  aria-haspopup="dialog"
>
  ☰
</button>

<div class="layout">
  <!-- Sticky sidebar — desktop only -->
  <div class="sidebar-wrap">
    <ProtocolNav families={data.families} />
  </div>

  <div class="content">
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
  /* ── FAB ────────────────────────────────────────────────────────────────── */
  .nav-fab {
    /* hidden on desktop; flex on mobile */
    display: none;
    position: fixed;
    /* Respect iOS home indicator */
    bottom: max(24px, calc(env(safe-area-inset-bottom) + 12px));
    right: 20px;
    width: 52px;
    height: 52px;
    border-radius: 50%;
    background: var(--accent);
    color: oklch(0.1 0 0);
    border: none;
    font-size: 1.25rem;
    line-height: 1;
    cursor: pointer;
    z-index: 98;
    align-items: center;
    justify-content: center;
    box-shadow: 0 3px 12px oklch(0 0 0 / 45%);
    transition: opacity 0.2s, transform 0.15s;
  }

  .nav-fab:hover {
    transform: scale(1.08);
  }

  /* Fade out when sheet is open so it doesn't overlap the sheet */
  .nav-fab.hidden {
    opacity: 0;
    pointer-events: none;
  }

  /* ── Backdrop ───────────────────────────────────────────────────────────── */
  .sheet-backdrop {
    display: none;
    position: fixed;
    inset: 0;
    background: oklch(0 0 0 / 55%);
    z-index: 99;
  }

  .sheet-backdrop.visible {
    display: block;
  }

  /* ── Bottom sheet ───────────────────────────────────────────────────────── */
  .bottom-sheet {
    position: fixed;
    bottom: 0;
    left: 0;
    right: 0;
    max-height: 78vh;
    background: var(--bg);
    border-radius: 16px 16px 0 0;
    border-top: 1px solid var(--border);
    z-index: 100;
    display: flex;
    flex-direction: column;
    transform: translateY(100%);
    transition: transform 0.28s cubic-bezier(0.32, 0.72, 0, 1);
  }

  .bottom-sheet.open {
    transform: translateY(0);
  }

  .sheet-header {
    position: relative;
    padding: 10px 48px 8px;
    display: flex;
    align-items: center;
    justify-content: center;
    flex-shrink: 0;
    border-bottom: 1px solid var(--border);
  }

  .sheet-drag-handle {
    width: 36px;
    height: 4px;
    border-radius: 2px;
    background: var(--border);
  }

  .sheet-close {
    position: absolute;
    right: 12px;
    top: 50%;
    transform: translateY(-50%);
    width: 30px;
    height: 30px;
    display: flex;
    align-items: center;
    justify-content: center;
    background: none;
    border: none;
    border-radius: 4px;
    font-size: 1.125rem;
    line-height: 1;
    color: var(--muted);
    cursor: pointer;
    transition: color 0.15s, background-color 0.15s;
  }

  .sheet-close:hover {
    color: var(--text);
    background-color: oklch(1 0 0 / 8%);
  }


  .sheet-content {
    overflow-y: auto;
    overflow-x: hidden;
    flex: 1;
    /* Contain scrolling to the sheet — don't chain to the page behind */
    overscroll-behavior: contain;
    /* Reserve space for scrollbar so it never overlaps content */
    scrollbar-gutter: stable;
    scrollbar-width: thin;
    scrollbar-color: var(--border) transparent;
    padding-bottom: max(12px, env(safe-area-inset-bottom));
  }

  /* Override ProtocolNav's sticky-sidebar sizing when inside the sheet */
  .sheet-content :global(.proto-nav) {
    position: static;
    height: auto;
    width: 100%;
    border-right: none;
    padding: 8px 0 16px;
  }

  /* ── Responsive visibility ───────────────────────────────────────────────── */
  @media (min-width: 768px) {
    /* Desktop: hide everything mobile-specific */
    .nav-fab,
    .sheet-backdrop,
    .bottom-sheet {
      display: none !important;
    }
  }

  @media (max-width: 767px) {
    /* Mobile: show FAB */
    .nav-fab {
      display: flex;
    }
  }

  /* ── Layout ─────────────────────────────────────────────────────────────── */
  .layout {
    display: flex;
    /* Column by default (mobile) — no horizontal overflow */
    flex-direction: column;
    gap: 0;
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

  /* ── Content area ───────────────────────────────────────────────────────── */
  .content {
    flex: 1;
    padding: 32px 32px 64px;
    min-width: 0;
  }

  @media (max-width: 767px) {
    .content {
      /* Extra bottom padding so the FAB never obscures the last card */
      padding: 24px 24px 100px;
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

  /* ── Shared type cards ───────────────────────────────────────────────────── */
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
