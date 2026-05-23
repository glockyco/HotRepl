<script lang="ts">
  import MessageCard from "$lib/components/MessageCard.svelte";
  import ProtocolNav from "$lib/components/ProtocolNav.svelte";
  import type { PageServerData } from "./$types";

  let { data }: { data: PageServerData } = $props();
</script>

<svelte:head>
  <title>Protocol Reference — HotRepl</title>
  <meta
    name="description"
    content="Complete reference for the HotRepl v2 WebSocket protocol — all message types, JSON examples, and JSON Schemas."
  />
</svelte:head>

<div class="layout">
  <!-- Sidebar: hidden on mobile, shown as sticky sidebar on desktop -->
  <div class="sidebar-wrap">
    <ProtocolNav families={data.families} />
  </div>

  <!-- Mobile nav (collapsible) -->
  <details class="mobile-nav">
    <summary>Protocol navigation</summary>
    <ProtocolNav families={data.families} />
  </details>

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
  .layout {
    display: flex;
    gap: 0;
    /* Override site-main padding for full-bleed sidebar layout */
    margin: 0 -24px;
  }

  .sidebar-wrap {
    display: none;
  }

  @media (min-width: 768px) {
    .sidebar-wrap {
      display: block;
    }

    .mobile-nav {
      display: none;
    }
  }

  .mobile-nav {
    padding: 12px 0;
    border-bottom: 1px solid var(--border);
    margin-bottom: 24px;
  }

  .mobile-nav summary {
    font-size: 0.875rem;
    font-weight: 700;
    color: var(--accent);
    cursor: pointer;
    padding: 0 24px;
    list-style: none;
  }

  .mobile-nav :global(.proto-nav) {
    position: static;
    height: auto;
    width: 100%;
    border-right: none;
  }

  .content {
    flex: 1;
    padding: 32px 32px 64px;
    min-width: 0;
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

  /* Shared type cards reuse MessageCard styling inline */
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
